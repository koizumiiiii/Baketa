using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// 翻訳パフォーマンス監視サービス
/// </summary>
public class TranslationPerformanceMonitor : ITranslationPerformanceMonitor
{
    private readonly ILogger<TranslationPerformanceMonitor> _logger;
    private readonly ConcurrentDictionary<string, PerformanceMetrics> _engineMetrics = new();
    private readonly ConcurrentQueue<TranslationTimingData> _recentTimings = new();
    private readonly System.Threading.Timer _reportTimer;
    private readonly int _targetLatencyMs;
    private readonly int _maxTimingRecords;
    
    // 統計情報
    private long _totalTranslations;
    private long _successfulTranslations;
    private long _failedTranslations;
    private long _cacheHits;
    private long _targetExceeded;
    
    public TranslationPerformanceMonitor(
        ILogger<TranslationPerformanceMonitor> logger,
        int targetLatencyMs = 500,
        int maxTimingRecords = 1000)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _targetLatencyMs = targetLatencyMs;
        _maxTimingRecords = maxTimingRecords;
        
        // 定期レポート（1分ごと）
        _reportTimer = new System.Threading.Timer(
            callback: _ => GeneratePerformanceReport(),
            state: null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
        
        _logger.LogInformation("TranslationPerformanceMonitor初期化 - 目標レイテンシ: {TargetMs}ms", _targetLatencyMs);
    }

    /// <summary>
    /// 翻訳処理を監視付きで実行
    /// </summary>
    public async Task<MonitoredTranslationResult> MonitorTranslationAsync(
        ITranslationEngine engine,
        TranslationRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var engineName = engine.Name;
        
        try
        {
            // エンジンメトリクスを取得または作成
            var metrics = _engineMetrics.GetOrAdd(engineName, _ => new PerformanceMetrics
            {
                EngineName = engineName,
                FirstUsed = DateTime.UtcNow
            });
            
            // 翻訳実行
            var response = await engine.TranslateAsync(request, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // メトリクス更新
            UpdateMetrics(metrics, elapsedMs, response.IsSuccess);
            
            // タイミングデータ記録
            RecordTiming(engineName, request.SourceText, elapsedMs, response.IsSuccess);
            
            // 目標超過チェック
            if (elapsedMs > _targetLatencyMs)
            {
                Interlocked.Increment(ref _targetExceeded);
                _logger.LogWarning(
                    "翻訳処理が目標を超過 - エンジン: {Engine}, 処理時間: {ElapsedMs}ms > {TargetMs}ms, テキスト: '{Text}'",
                    engineName, elapsedMs, _targetLatencyMs, request.SourceText.Substring(0, Math.Min(50, request.SourceText.Length)));
                
                // アラート発行（将来的には外部監視システムと連携）
                await RaisePerformanceAlertAsync(engineName, elapsedMs).ConfigureAwait(false);
            }
            else if (elapsedMs < 100)
            {
                // 高速処理の場合はキャッシュヒットの可能性
                Interlocked.Increment(ref _cacheHits);
            }
            
            // 結果を返す
            return new MonitoredTranslationResult
            {
                Response = response,
                ProcessingTimeMs = elapsedMs,
                IsWithinTarget = elapsedMs <= _targetLatencyMs,
                EngineName = engineName,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var elapsedMs = stopwatch.ElapsedMilliseconds;
            
            // エラーメトリクス更新
            var metrics = _engineMetrics.GetOrAdd(engineName, _ => new PerformanceMetrics
            {
                EngineName = engineName,
                FirstUsed = DateTime.UtcNow
            });
            UpdateMetrics(metrics, elapsedMs, false);
            
            _logger.LogError(ex, "翻訳処理エラー - エンジン: {Engine}, 処理時間: {ElapsedMs}ms", 
                engineName, elapsedMs);
            
            throw;
        }
    }

    /// <summary>
    /// バッチ翻訳を監視
    /// </summary>
    public async Task<BatchMonitoredTranslationResult> MonitorBatchTranslationAsync(
        ITranslationEngine engine,
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var engineName = engine.Name;
        
        try
        {
            var responses = await engine.TranslateBatchAsync(requests, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            var totalMs = stopwatch.ElapsedMilliseconds;
            var avgMs = requests.Count > 0 ? totalMs / requests.Count : 0;
            
            // メトリクス更新
            var metrics = _engineMetrics.GetOrAdd(engineName, _ => new PerformanceMetrics
            {
                EngineName = engineName,
                FirstUsed = DateTime.UtcNow
            });
            
            var successCount = responses.Count(r => r.IsSuccess);
            metrics.TotalRequests += requests.Count;
            metrics.SuccessfulRequests += successCount;
            metrics.FailedRequests += requests.Count - successCount;
            metrics.TotalProcessingTimeMs += totalMs;
            
            _logger.LogInformation(
                "バッチ翻訳完了 - エンジン: {Engine}, 件数: {Count}, 総時間: {TotalMs}ms, 平均: {AvgMs}ms/件",
                engineName, requests.Count, totalMs, avgMs);
            
            return new BatchMonitoredTranslationResult
            {
                Responses = responses,
                TotalProcessingTimeMs = totalMs,
                AverageProcessingTimeMs = avgMs,
                IsWithinTarget = avgMs <= _targetLatencyMs,
                EngineName = engineName,
                BatchSize = requests.Count,
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "バッチ翻訳エラー - エンジン: {Engine}, 件数: {Count}", 
                engineName, requests.Count);
            throw;
        }
    }

    private void UpdateMetrics(PerformanceMetrics metrics, long elapsedMs, bool success)
    {
        lock (metrics)
        {
            metrics.TotalRequests++;
            metrics.TotalProcessingTimeMs += elapsedMs;
            
            if (success)
            {
                metrics.SuccessfulRequests++;
                Interlocked.Increment(ref _successfulTranslations);
            }
            else
            {
                metrics.FailedRequests++;
                Interlocked.Increment(ref _failedTranslations);
            }
            
            // 最小/最大/平均を更新
            metrics.MinLatencyMs = Math.Min(metrics.MinLatencyMs, elapsedMs);
            metrics.MaxLatencyMs = Math.Max(metrics.MaxLatencyMs, elapsedMs);
            metrics.LastUsed = DateTime.UtcNow;
        }
        
        Interlocked.Increment(ref _totalTranslations);
    }

    private void RecordTiming(string engineName, string text, long elapsedMs, bool success)
    {
        var timing = new TranslationTimingData
        {
            EngineName = engineName,
            TextPreview = text.Length > 50 ? string.Concat(text.AsSpan(0, 50), "...") : text,
            ProcessingTimeMs = elapsedMs,
            Success = success,
            Timestamp = DateTime.UtcNow
        };
        
        _recentTimings.Enqueue(timing);
        
        // 最大レコード数を超えたら古いものを削除
        while (_recentTimings.Count > _maxTimingRecords)
        {
            _recentTimings.TryDequeue(out _);
        }
    }

    private async Task RaisePerformanceAlertAsync(string engineName, long elapsedMs)
    {
        // 将来的には外部監視システム（Prometheus、Application Insights等）と連携
        await Task.CompletedTask;
        
        _logger.LogWarning(
            "⚠️ パフォーマンスアラート - エンジン: {Engine}, レイテンシ: {ElapsedMs}ms (目標: {TargetMs}ms)",
            engineName, elapsedMs, _targetLatencyMs);
    }

    private void GeneratePerformanceReport()
    {
        try
        {
            if (_totalTranslations == 0) return;
            
            var report = new System.Text.StringBuilder();
            report.AppendLine("\n📊 === 翻訳パフォーマンスレポート ===");
            report.AppendLine($"期間: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            report.AppendLine($"総翻訳数: {_totalTranslations:N0}");
            report.AppendLine($"成功: {_successfulTranslations:N0} ({100.0 * _successfulTranslations / _totalTranslations:F1}%)");
            report.AppendLine($"失敗: {_failedTranslations:N0} ({100.0 * _failedTranslations / _totalTranslations:F1}%)");
            report.AppendLine($"キャッシュヒット推定: {_cacheHits:N0} ({100.0 * _cacheHits / _totalTranslations:F1}%)");
            report.AppendLine($"目標超過: {_targetExceeded:N0} ({100.0 * _targetExceeded / _totalTranslations:F1}%)");
            
            // エンジン別統計
            report.AppendLine("\n🔧 エンジン別統計:");
            foreach (var kvp in _engineMetrics.OrderBy(x => x.Key))
            {
                var metrics = kvp.Value;
                if (metrics.TotalRequests == 0) continue;
                
                var avgMs = metrics.TotalProcessingTimeMs / metrics.TotalRequests;
                var successRate = 100.0 * metrics.SuccessfulRequests / metrics.TotalRequests;
                
                report.AppendLine($"  [{metrics.EngineName}]");
                report.AppendLine($"    リクエスト: {metrics.TotalRequests:N0}");
                report.AppendLine($"    平均: {avgMs:F1}ms (最小: {metrics.MinLatencyMs}ms, 最大: {metrics.MaxLatencyMs}ms)");
                report.AppendLine($"    成功率: {successRate:F1}%");
                report.AppendLine($"    最終使用: {metrics.LastUsed:HH:mm:ss}");
            }
            
            // 最近の遅延トップ5
            var slowest = _recentTimings
                .OrderByDescending(t => t.ProcessingTimeMs)
                .Take(5)
                .ToList();
                
            if (slowest.Any())
            {
                report.AppendLine("\n⚠️ 最近の遅延トップ5:");
                foreach (var timing in slowest)
                {
                    report.AppendLine($"  {timing.ProcessingTimeMs}ms - {timing.EngineName} - \"{timing.TextPreview}\"");
                }
            }
            
            // SLA達成率
            var slaAchievement = 100.0 * (_totalTranslations - _targetExceeded) / _totalTranslations;
            report.AppendLine($"\n✅ SLA達成率 ({_targetLatencyMs}ms以下): {slaAchievement:F1}%");
            
            if (slaAchievement < 90)
            {
                report.AppendLine("⚠️ 警告: SLA達成率が90%を下回っています！");
            }
            
            _logger.LogInformation(report.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "パフォーマンスレポート生成エラー");
        }
    }

    /// <summary>
    /// 現在のパフォーマンス統計を取得
    /// </summary>
    public PerformanceStatistics GetStatistics()
    {
        var stats = new PerformanceStatistics
        {
            TotalTranslations = _totalTranslations,
            SuccessfulTranslations = _successfulTranslations,
            FailedTranslations = _failedTranslations,
            CacheHits = _cacheHits,
            TargetExceeded = _targetExceeded,
            TargetLatencyMs = _targetLatencyMs,
            Timestamp = DateTime.UtcNow
        };
        
        // エンジン別統計を追加
        foreach (var kvp in _engineMetrics)
        {
            var metrics = kvp.Value;
            if (metrics.TotalRequests > 0)
            {
                stats.EngineStatistics[kvp.Key] = new EngineStatistics
                {
                    EngineName = metrics.EngineName,
                    TotalRequests = metrics.TotalRequests,
                    AverageLatencyMs = metrics.TotalProcessingTimeMs / metrics.TotalRequests,
                    MinLatencyMs = metrics.MinLatencyMs,
                    MaxLatencyMs = metrics.MaxLatencyMs,
                    SuccessRate = 100.0 * metrics.SuccessfulRequests / metrics.TotalRequests
                };
            }
        }
        
        return stats;
    }

    public void Dispose()
    {
        _reportTimer?.Dispose();
        GeneratePerformanceReport(); // 最終レポート
    }

    // 内部クラス
    private class PerformanceMetrics
    {
        public string EngineName { get; set; } = string.Empty;
        public long TotalRequests { get; set; }
        public long SuccessfulRequests { get; set; }
        public long FailedRequests { get; set; }
        public long TotalProcessingTimeMs { get; set; }
        public long MinLatencyMs { get; set; } = long.MaxValue;
        public long MaxLatencyMs { get; set; }
        public DateTime FirstUsed { get; set; }
        public DateTime LastUsed { get; set; }
    }

    private class TranslationTimingData
    {
        public string EngineName { get; set; } = string.Empty;
        public string TextPreview { get; set; } = string.Empty;
        public long ProcessingTimeMs { get; set; }
        public bool Success { get; set; }
        public DateTime Timestamp { get; set; }
    }
}

// インターフェース定義
public interface ITranslationPerformanceMonitor : IDisposable
{
    Task<MonitoredTranslationResult> MonitorTranslationAsync(
        ITranslationEngine engine,
        TranslationRequest request,
        CancellationToken cancellationToken = default);
        
    Task<BatchMonitoredTranslationResult> MonitorBatchTranslationAsync(
        ITranslationEngine engine,
        IReadOnlyList<TranslationRequest> requests,
        CancellationToken cancellationToken = default);
        
    PerformanceStatistics GetStatistics();
}

// 結果モデル
public class MonitoredTranslationResult
{
    public TranslationResponse Response { get; set; } = null!;
    public long ProcessingTimeMs { get; set; }
    public bool IsWithinTarget { get; set; }
    public string EngineName { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
}

public class BatchMonitoredTranslationResult
{
    public IReadOnlyList<TranslationResponse> Responses { get; set; } = null!;
    public long TotalProcessingTimeMs { get; set; }
    public long AverageProcessingTimeMs { get; set; }
    public bool IsWithinTarget { get; set; }
    public string EngineName { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public DateTime Timestamp { get; set; }
}

public class PerformanceStatistics
{
    public long TotalTranslations { get; set; }
    public long SuccessfulTranslations { get; set; }
    public long FailedTranslations { get; set; }
    public long CacheHits { get; set; }
    public long TargetExceeded { get; set; }
    public int TargetLatencyMs { get; set; }
    public DateTime Timestamp { get; set; }
    public Dictionary<string, EngineStatistics> EngineStatistics { get; set; } = new();
}

public class EngineStatistics
{
    public string EngineName { get; set; } = string.Empty;
    public long TotalRequests { get; set; }
    public long AverageLatencyMs { get; set; }
    public long MinLatencyMs { get; set; }
    public long MaxLatencyMs { get; set; }
    public double SuccessRate { get; set; }
}