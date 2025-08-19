using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Infrastructure.Translation.Metrics;

/// <summary>
/// 翻訳メトリクス収集
/// Issue #147 Phase 3.2: パフォーマンス監視
/// </summary>
public class TranslationMetricsCollector : IDisposable
{
    private readonly ILogger<TranslationMetricsCollector> _logger;
    private readonly ConcurrentQueue<TranslationMetrics> _metricsQueue;
    private readonly System.Threading.Timer _flushTimer;
    private readonly object _statsLock = new();
    private bool _disposed;
    
    // 集計統計
    private long _totalTranslations;
    private long _successfulTranslations;
    private long _failedTranslations;
    private double _totalProcessingTimeMs;
    private readonly Dictionary<string, StrategyStats> _strategyStats;

    public TranslationMetricsCollector(ILogger<TranslationMetricsCollector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsQueue = new ConcurrentQueue<TranslationMetrics>();
        _strategyStats = new Dictionary<string, StrategyStats>();
        
        // 60秒ごとに統計をログ出力
        _flushTimer = new System.Threading.Timer(FlushMetrics, null, TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60));
        
        _logger.LogInformation("📊 TranslationMetricsCollector初期化完了");
    }

    /// <summary>
    /// 翻訳メトリクスを記録
    /// </summary>
    public void RecordTranslation(TranslationMetrics metrics)
    {
        if (metrics == null) return;
        
        _metricsQueue.Enqueue(metrics);
        
        lock (_statsLock)
        {
            _totalTranslations++;
            
            if (metrics.Success)
                _successfulTranslations++;
            else
                _failedTranslations++;
            
            _totalProcessingTimeMs += metrics.ProcessingTime.TotalMilliseconds;
            
            // 戦略別統計を更新
            if (!_strategyStats.TryGetValue(metrics.Strategy, out var strategyStats))
            {
                strategyStats = new StrategyStats();
                _strategyStats[metrics.Strategy] = strategyStats;
            }
            
            strategyStats.TotalCount++;
            strategyStats.TotalProcessingTimeMs += metrics.ProcessingTime.TotalMilliseconds;
            strategyStats.TotalCharacters += metrics.TextLength;
            
            if (metrics.Success)
                strategyStats.SuccessCount++;
            else
                strategyStats.FailureCount++;
        }
        
        // 100件ごとに簡易ログ出力
        if (_totalTranslations % 100 == 0)
        {
            LogQuickStats();
        }
    }

    /// <summary>
    /// バッチ翻訳メトリクスを記録
    /// </summary>
    public void RecordBatchTranslation(BatchTranslationMetrics metrics)
    {
        if (metrics == null) return;
        
        _logger.LogInformation("📊 バッチ翻訳完了 - 戦略: {Strategy}, 件数: {Count}, 成功: {Success}, 失敗: {Failed}, 処理時間: {Time:F2}ms",
            metrics.Strategy,
            metrics.TextCount,
            metrics.SuccessCount,
            metrics.FailureCount,
            metrics.ProcessingTime.TotalMilliseconds);
        
        lock (_statsLock)
        {
            _totalTranslations += metrics.TextCount;
            _successfulTranslations += metrics.SuccessCount;
            _failedTranslations += metrics.FailureCount;
            _totalProcessingTimeMs += metrics.ProcessingTime.TotalMilliseconds;
            
            // 戦略別統計を更新
            if (!_strategyStats.TryGetValue(metrics.Strategy, out var strategyStats))
            {
                strategyStats = new StrategyStats();
                _strategyStats[metrics.Strategy] = strategyStats;
            }
            
            var stats = _strategyStats[metrics.Strategy];
            stats.TotalCount += metrics.TextCount;
            stats.BatchCount++;
            stats.SuccessCount += metrics.SuccessCount;
            stats.FailureCount += metrics.FailureCount;
            stats.TotalProcessingTimeMs += metrics.ProcessingTime.TotalMilliseconds;
            stats.TotalCharacters += metrics.TotalCharacterCount;
        }
    }

    /// <summary>
    /// エラーを記録
    /// </summary>
    public void RecordError(Exception exception, TranslationStrategyContext context)
    {
        _logger.LogError(exception, "翻訳エラー - TextCount: {Count}, TotalChars: {Chars}",
            context.TextCount, context.TotalCharacterCount);
        
        lock (_statsLock)
        {
            _failedTranslations += context.TextCount;
        }
    }

    /// <summary>
    /// 現在の統計を取得
    /// </summary>
    public TranslationStatistics GetStatistics()
    {
        lock (_statsLock)
        {
            var avgProcessingTime = _totalTranslations > 0 
                ? _totalProcessingTimeMs / _totalTranslations 
                : 0;
            
            var successRate = _totalTranslations > 0 
                ? (double)_successfulTranslations / _totalTranslations * 100 
                : 0;
            
            return new TranslationStatistics
            {
                TotalTranslations = _totalTranslations,
                SuccessfulTranslations = _successfulTranslations,
                FailedTranslations = _failedTranslations,
                SuccessRate = successRate,
                AverageProcessingTimeMs = avgProcessingTime,
                StrategyStatistics = new Dictionary<string, StrategyStats>(_strategyStats)
            };
        }
    }

    /// <summary>
    /// 簡易統計をログ出力
    /// </summary>
    private void LogQuickStats()
    {
        lock (_statsLock)
        {
            var successRate = _totalTranslations > 0 
                ? (double)_successfulTranslations / _totalTranslations * 100 
                : 0;
            
            _logger.LogInformation("📊 [Quick Stats] 総翻訳: {Total}, 成功率: {Rate:F1}%, 平均処理時間: {Avg:F2}ms",
                _totalTranslations,
                successRate,
                _totalTranslations > 0 ? _totalProcessingTimeMs / _totalTranslations : 0);
        }
    }

    /// <summary>
    /// 詳細統計をフラッシュ
    /// </summary>
    private void FlushMetrics(object? state)
    {
        try
        {
            var stats = GetStatistics();
            
            _logger.LogInformation("📊 === 翻訳統計レポート ===");
            _logger.LogInformation("総翻訳数: {Total} (成功: {Success}, 失敗: {Failed})",
                stats.TotalTranslations, stats.SuccessfulTranslations, stats.FailedTranslations);
            _logger.LogInformation("成功率: {Rate:F2}%, 平均処理時間: {Avg:F2}ms",
                stats.SuccessRate, stats.AverageProcessingTimeMs);
            
            foreach (var (strategy, stratStats) in stats.StrategyStatistics)
            {
                var stratSuccessRate = stratStats.TotalCount > 0 
                    ? (double)stratStats.SuccessCount / stratStats.TotalCount * 100 
                    : 0;
                var stratAvgTime = stratStats.TotalCount > 0 
                    ? stratStats.TotalProcessingTimeMs / stratStats.TotalCount 
                    : 0;
                
                _logger.LogInformation("  [{Strategy}] 件数: {Count}, 成功率: {Rate:F1}%, 平均: {Avg:F2}ms, バッチ: {Batch}",
                    strategy, stratStats.TotalCount, stratSuccessRate, stratAvgTime, stratStats.BatchCount);
            }
            
            // キューをクリア
            while (_metricsQueue.TryDequeue(out _)) { }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "メトリクスフラッシュ中にエラーが発生しました");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _flushTimer?.Dispose();
        FlushMetrics(null); // 最終フラッシュ
        _disposed = true;
    }
}

/// <summary>
/// 翻訳メトリクス
/// </summary>
public class TranslationMetrics
{
    public required string Strategy { get; init; }
    public required int TextLength { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required bool Success { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// バッチ翻訳メトリクス
/// </summary>
public class BatchTranslationMetrics
{
    public required string Strategy { get; init; }
    public required int TextCount { get; init; }
    public required int TotalCharacterCount { get; init; }
    public required TimeSpan ProcessingTime { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 戦略別統計
/// </summary>
public class StrategyStats
{
    public long TotalCount { get; set; }
    public long SuccessCount { get; set; }
    public long FailureCount { get; set; }
    public long BatchCount { get; set; }
    public double TotalProcessingTimeMs { get; set; }
    public long TotalCharacters { get; set; }
}

/// <summary>
/// 翻訳統計
/// </summary>
public class TranslationStatistics
{
    public required long TotalTranslations { get; init; }
    public required long SuccessfulTranslations { get; init; }
    public required long FailedTranslations { get; init; }
    public required double SuccessRate { get; init; }
    public required double AverageProcessingTimeMs { get; init; }
    public required Dictionary<string, StrategyStats> StrategyStatistics { get; init; }
}