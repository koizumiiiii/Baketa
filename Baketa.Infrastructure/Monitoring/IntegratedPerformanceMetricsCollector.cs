using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.Logging;
using Baketa.Infrastructure.Translation.Metrics;

namespace Baketa.Infrastructure.Monitoring;

/// <summary>
/// 統合パフォーマンスメトリクス収集サービス
/// 既存のTranslationMetricsCollector、IBaketaLoggerと統合し、
/// OCR・リソース調整メトリクスも収集する統合レイヤー
/// </summary>
public sealed class IntegratedPerformanceMetricsCollector : IPerformanceMetricsCollector
{
    private readonly ILogger<IntegratedPerformanceMetricsCollector> _logger;
    private readonly IBaketaLogger _baketaLogger;
    private readonly TranslationMetricsCollector _translationMetricsCollector;
    private readonly PerformanceMetricsSettings _settings;
    
    // OCRメトリクス専用キューイング
    private readonly ConcurrentQueue<OcrPerformanceMetrics> _ocrMetricsQueue;
    private readonly ConcurrentQueue<ResourceAdjustmentMetrics> _resourceMetricsQueue;
    private readonly System.Threading.Timer _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly string _metricsLogPath;
    private readonly string _reportsPath;
    private bool _disposed;
    
    // 統計集計（スレッドセーフ）
    private long _totalOcrOperations;
    private long _successfulOcrOperations;
    private long _totalResourceAdjustments;
    private double _totalOcrConfidenceScore;
    private readonly object _statsLock = new();

    public IntegratedPerformanceMetricsCollector(
        ILogger<IntegratedPerformanceMetricsCollector> logger,
        IBaketaLogger baketaLogger,
        TranslationMetricsCollector translationMetricsCollector,
        IOptions<PerformanceMetricsSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _baketaLogger = baketaLogger ?? throw new ArgumentNullException(nameof(baketaLogger));
        _translationMetricsCollector = translationMetricsCollector ?? throw new ArgumentNullException(nameof(translationMetricsCollector));
        _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
        
        _ocrMetricsQueue = new ConcurrentQueue<OcrPerformanceMetrics>();
        _resourceMetricsQueue = new ConcurrentQueue<ResourceAdjustmentMetrics>();
        _flushSemaphore = new SemaphoreSlim(1, 1);
        _cancellationTokenSource = new CancellationTokenSource();
        
        // ログファイルパス設定
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baketaMetricsPath = Path.Combine(appDataPath, "Baketa", "Metrics");
        var baketaReportsPath = Path.Combine(appDataPath, "Baketa", "Reports");
        
        Directory.CreateDirectory(baketaMetricsPath);
        Directory.CreateDirectory(baketaReportsPath);
        
        _metricsLogPath = Path.Combine(baketaMetricsPath, $"performance-{DateTime.Now:yyyy-MM-dd}.log");
        _reportsPath = baketaReportsPath;
        
        // フラッシュタイマー設定（設定可能）
        var flushInterval = TimeSpan.FromSeconds(_settings.FlushIntervalSeconds);
        _flushTimer = new System.Threading.Timer(
            FlushMetricsCallback, 
            null, 
            flushInterval, 
            flushInterval);
        
        _logger.LogInformation("🎯 統合パフォーマンスメトリクスコレクター初期化完了 - ログ: {Path}", _metricsLogPath);
    }
    
    /// <summary>
    /// 翻訳メトリクスを記録（既存TranslationMetricsCollectorに委譲）
    /// </summary>
    public void RecordTranslationMetrics(TranslationPerformanceMetrics metrics)
    {
        if (metrics == null || !_settings.Enabled) return;
        
        try
        {
            // 既存TranslationMetricsCollectorに翻訳メトリクスとして委譲
            var translationMetrics = new TranslationMetrics
            {
                Strategy = metrics.Engine,
                TextLength = metrics.InputTextLength,
                ProcessingTime = metrics.TranslationDuration,
                Success = metrics.IsSuccess,
                Timestamp = metrics.Timestamp
            };
            
            _translationMetricsCollector.RecordTranslation(translationMetrics);
            
            // 追加で統合ログとしても記録
            _baketaLogger.LogPerformanceMetrics(
                "Translation", 
                metrics.TotalDuration, 
                metrics.IsSuccess,
                new Dictionary<string, object>
                {
                    ["Engine"] = metrics.Engine,
                    ["InputLength"] = metrics.InputTextLength,
                    ["OutputLength"] = metrics.OutputTextLength,
                    ["MemoryMB"] = metrics.MemoryUsageMB,
                    ["GpuUtilization"] = metrics.GpuUtilization
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 翻訳メトリクス記録失敗 - 処理続行");
        }
    }
    
    /// <summary>
    /// OCRメトリクスを記録（高速・ノンブロッキング）
    /// </summary>
    public void RecordOcrMetrics(OcrPerformanceMetrics metrics)
    {
        if (metrics == null || !_settings.Enabled) return;
        
        try
        {
            // キューサイズ制限チェック（メモリ保護）
            if (_ocrMetricsQueue.Count >= _settings.MaxQueueSize)
            {
                _logger.LogWarning("⚠️ OCRメトリクスキューが満杯です - 最新のメトリクスを破棄");
                return;
            }
            
            _ocrMetricsQueue.Enqueue(metrics);
            
            // 統計更新（スレッドセーフ）
            lock (_statsLock)
            {
                _totalOcrOperations++;
                if (metrics.IsSuccess)
                {
                    _successfulOcrOperations++;
                    _totalOcrConfidenceScore += metrics.ConfidenceScore;
                }
            }
            
            // IBaketaLogger経由でもログ記録
            _baketaLogger.LogPerformanceMetrics(
                "OCR", 
                metrics.ProcessingDuration, 
                metrics.IsSuccess,
                new Dictionary<string, object>
                {
                    ["Engine"] = metrics.OcrEngine,
                    ["ImageSize"] = $"{metrics.ImageWidth}x{metrics.ImageHeight}",
                    ["DetectedRegions"] = metrics.DetectedRegions,
                    ["ConfidenceScore"] = metrics.ConfidenceScore,
                    ["MemoryMB"] = metrics.MemoryUsageMB
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ OCRメトリクス記録失敗 - 処理続行");
        }
    }
    
    /// <summary>
    /// リソース調整メトリクスを記録
    /// </summary>
    public void RecordResourceAdjustment(ResourceAdjustmentMetrics metrics)
    {
        if (metrics == null || !_settings.Enabled) return;
        
        try
        {
            _resourceMetricsQueue.Enqueue(metrics);
            
            lock (_statsLock)
            {
                _totalResourceAdjustments++;
            }
            
            // 重要なリソース調整は即座にログ出力
            _logger.LogInformation("🔧 リソース調整: {Component} {Type} {Old}→{New} (理由: {Reason})",
                metrics.ComponentName,
                metrics.AdjustmentType,
                metrics.OldValue,
                metrics.NewValue,
                metrics.Reason);
                
            _baketaLogger.LogPerformanceMetrics(
                "ResourceAdjustment", 
                TimeSpan.Zero, 
                true,
                new Dictionary<string, object>
                {
                    ["Component"] = metrics.ComponentName,
                    ["Type"] = metrics.AdjustmentType,
                    ["OldValue"] = metrics.OldValue,
                    ["NewValue"] = metrics.NewValue,
                    ["Reason"] = metrics.Reason,
                    ["CpuUsage"] = metrics.CpuUsage,
                    ["MemoryUsage"] = metrics.MemoryUsage,
                    ["GpuUtilization"] = metrics.GpuUtilization
                });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ リソース調整メトリクス記録失敗 - 処理続行");
        }
    }
    
    /// <summary>
    /// 統合パフォーマンスレポートを生成（非同期）
    /// </summary>
    public async Task<IntegratedPerformanceReport> GenerateReportAsync()
    {
        try
        {
            // 既存TranslationMetricsCollectorから翻訳統計取得
            var translationStats = _translationMetricsCollector.GetStatistics();
            
            IntegratedPerformanceReport report;
            lock (_statsLock)
            {
                var ocrSuccessRate = _totalOcrOperations > 0 
                    ? (double)_successfulOcrOperations / _totalOcrOperations * 100 
                    : 0;
                    
                var avgConfidenceScore = _successfulOcrOperations > 0 
                    ? _totalOcrConfidenceScore / _successfulOcrOperations 
                    : 0;
                
                report = new IntegratedPerformanceReport
                {
                    GeneratedAt = DateTime.UtcNow,
                    ReportPeriod = TimeSpan.FromHours(24), // デフォルト24時間
                    
                    // 翻訳統計（既存システムから）
                    TotalTranslations = translationStats.TotalTranslations,
                    SuccessfulTranslations = translationStats.SuccessfulTranslations,
                    TranslationSuccessRate = translationStats.SuccessRate,
                    AverageTranslationTime = TimeSpan.FromMilliseconds(translationStats.AverageProcessingTimeMs),
                    
                    // OCR統計
                    TotalOcrOperations = _totalOcrOperations,
                    SuccessfulOcrOperations = _successfulOcrOperations,
                    OcrSuccessRate = ocrSuccessRate,
                    AverageConfidenceScore = avgConfidenceScore,
                    
                    // リソース調整統計
                    ResourceAdjustmentCount = (int)_totalResourceAdjustments,
                    
                    // ファイル情報
                    LogFilePath = _metricsLogPath,
                    LogFileSizeBytes = File.Exists(_metricsLogPath) ? new FileInfo(_metricsLogPath).Length : 0
                };
            }
            
            // JSON形式でレポートを保存（lockの外で実行）
            if (_settings.EnableStructuredReports)
            {
                var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
                var reportPath = Path.Combine(_reportsPath, $"performance_report_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                await File.WriteAllTextAsync(reportPath, reportJson);
                
                _logger.LogInformation("📊 統合パフォーマンスレポート生成完了: {Path}", reportPath);
            }
            
            return report;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 統合パフォーマンスレポート生成失敗");
            return new IntegratedPerformanceReport(); // エラー時は空のレポートを返す
        }
    }
    
    /// <summary>
    /// メトリクスファイルを手動フラッシュ
    /// </summary>
    public async Task FlushAsync()
    {
        await FlushMetricsAsync();
    }
    
    /// <summary>
    /// フラッシュタイマーコールバック
    /// </summary>
    private void FlushMetricsCallback(object? state)
    {
        _ = Task.Run(async () => await FlushMetricsAsync());
    }
    
    /// <summary>
    /// バッチ処理によるメトリクスフラッシュ（非同期I/O）
    /// </summary>
    private async Task FlushMetricsAsync()
    {
        await _flushSemaphore.WaitAsync();
        try
        {
            var logEntries = new List<string>();
            
            // OCRメトリクスをバッチ処理
            var ocrMetrics = new List<OcrPerformanceMetrics>();
            while (ocrMetrics.Count < _settings.BatchSize && _ocrMetricsQueue.TryDequeue(out var ocrMetric))
            {
                ocrMetrics.Add(ocrMetric);
            }
            
            foreach (var metric in ocrMetrics)
            {
                var logEntry = $"{metric.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [OCR] " +
                              $"Engine={metric.OcrEngine} " +
                              $"Duration={metric.ProcessingDuration.TotalMilliseconds:F2}ms " +
                              $"Size={metric.ImageWidth}x{metric.ImageHeight} " +
                              $"Regions={metric.DetectedRegions} " +
                              $"Confidence={metric.ConfidenceScore:F3} " +
                              $"Memory={metric.MemoryUsageMB}MB " +
                              $"Success={metric.IsSuccess}";
                logEntries.Add(logEntry);
            }
            
            // リソース調整メトリクスをバッチ処理
            var resourceMetrics = new List<ResourceAdjustmentMetrics>();
            while (resourceMetrics.Count < _settings.BatchSize && _resourceMetricsQueue.TryDequeue(out var resourceMetric))
            {
                resourceMetrics.Add(resourceMetric);
            }
            
            foreach (var metric in resourceMetrics)
            {
                var logEntry = $"{metric.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [RESOURCE] " +
                              $"Component={metric.ComponentName} " +
                              $"Type={metric.AdjustmentType} " +
                              $"Change={metric.OldValue}→{metric.NewValue} " +
                              $"Reason={metric.Reason} " +
                              $"CPU={metric.CpuUsage:F1}% " +
                              $"Memory={metric.MemoryUsage:F1}% " +
                              $"GPU={metric.GpuUtilization:F1}%";
                logEntries.Add(logEntry);
            }
            
            // バッチファイル書き込み（非同期）
            if (logEntries.Count > 0)
            {
                var content = string.Join(Environment.NewLine, logEntries) + Environment.NewLine;
                await File.AppendAllTextAsync(_metricsLogPath, content);
                
                _logger.LogDebug("📝 統合メトリクスバッチフラッシュ完了: {Count}件", logEntries.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 統合メトリクスファイル書き込み失敗");
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _flushTimer?.Dispose();
        _cancellationTokenSource.Cancel();
        
        // 最終フラッシュ（同期）
        FlushMetricsAsync().GetAwaiter().GetResult();
        
        _flushSemaphore?.Dispose();
        _cancellationTokenSource?.Dispose();
        
        _disposed = true;
        
        _logger.LogInformation("🎯 統合パフォーマンスメトリクスコレクター終了");
    }
}

/// <summary>
/// パフォーマンスメトリクス設定
/// </summary>
public class PerformanceMetricsSettings
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 50;
    public int FlushIntervalSeconds { get; set; } = 5;
    public int MaxQueueSize { get; set; } = 1000;
    public int LogRetentionDays { get; set; } = 30;
    public bool EnableStructuredReports { get; set; } = true;
    public string LogLevel { get; set; } = "Information";
}