using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;

/// <summary>
/// PP-OCRv5エンジンの診断サービス
/// パフォーマンス測定、タイムアウト検出、エラー分析を提供
/// </summary>
public sealed class PPOCRv5DiagnosticService : IDisposable
{
    private readonly ILogger<PPOCRv5DiagnosticService> _logger;
    private readonly ImageDiagnosticsSaver _imageSaver;
    
    // 診断統計
    private readonly DiagnosticStatistics _statistics = new();
    private readonly object _statisticsLock = new();
    
    // タイムアウト監視
    private readonly CancellationTokenSource _timeoutCts = new();
    private readonly List<ActiveOperation> _activeOperations = [];
    private readonly object _operationsLock = new();
    
    private bool _disposed;

    public PPOCRv5DiagnosticService(
        ILogger<PPOCRv5DiagnosticService> logger,
        ImageDiagnosticsSaver imageSaver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _imageSaver = imageSaver ?? throw new ArgumentNullException(nameof(imageSaver));
        
        // タイムアウト監視タスクを開始
        _ = Task.Run(MonitorTimeoutsAsync, _timeoutCts.Token);
    }

    /// <summary>
    /// OCR処理を診断付きで実行
    /// </summary>
    public async Task<OcrResults> ExecuteWithDiagnosticsAsync(
        IOcrEngine engine,
        IImage image,
        OcrEngineSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(image);
        
        var operationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogDebug("OCR診断開始: OperationId={OperationId}, ImageSize={Width}x{Height}", 
            operationId, image.Width, image.Height);

        var operation = new ActiveOperation
        {
            Id = operationId,
            StartTime = DateTime.UtcNow,
            ImageInfo = $"{image.Width}x{image.Height}",
            Settings = settings
        };

        try
        {
            // アクティブ操作に追加
            lock (_operationsLock)
            {
                _activeOperations.Add(operation);
            }

            // 診断情報付きで画像を保存
            var imageFileName = await _imageSaver.SaveDiagnosticImageAsync(
                image, $"ocr_input_{operationId}", 
                new Dictionary<string, object>
                {
                    ["OperationId"] = operationId,
                    ["ImageSize"] = $"{image.Width}x{image.Height}",
                    ["Settings"] = settings?.ToString() ?? "default"
                }).ConfigureAwait(false);

            operation.ImagePath = imageFileName;

            // OCR実行
            var result = await engine.RecognizeAsync(image, cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            stopwatch.Stop();
            operation.Duration = stopwatch.Elapsed;
            operation.Success = result.HasText;
            operation.TextRegionsCount = result.TextRegions.Count;
            operation.ProcessingTime = result.ProcessingTime;

            // 統計更新
            UpdateStatistics(operation, result);

            _logger.LogInformation("OCR診断完了: OperationId={OperationId}, Duration={Duration}ms, TextRegions={Count}, HasText={HasText}",
                operationId, stopwatch.ElapsedMilliseconds, result.TextRegions.Count, result.HasText);

            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            operation.Duration = stopwatch.Elapsed;
            operation.Success = false;
            operation.ErrorType = "Cancelled";
            
            UpdateStatistics(operation, null);
            
            _logger.LogWarning("OCR診断キャンセル: OperationId={OperationId}, Duration={Duration}ms",
                operationId, stopwatch.ElapsedMilliseconds);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            operation.Duration = stopwatch.Elapsed;
            operation.Success = false;
            operation.ErrorType = ex.GetType().Name;
            operation.ErrorMessage = ex.Message;
            
            UpdateStatistics(operation, null);
            
            _logger.LogError(ex, "OCR診断エラー: OperationId={OperationId}, Duration={Duration}ms",
                operationId, stopwatch.ElapsedMilliseconds);

            // 診断用にエラー画像を保存
            try
            {
                await _imageSaver.SaveErrorImageAsync(image, operationId, ex).ConfigureAwait(false);
            }
            catch (Exception saveEx)
            {
                _logger.LogWarning(saveEx, "エラー画像保存失敗: OperationId={OperationId}", operationId);
            }

            throw;
        }
        finally
        {
            // アクティブ操作から削除
            lock (_operationsLock)
            {
                _activeOperations.RemoveAll(op => op.Id == operationId);
            }
        }
    }

    /// <summary>
    /// 現在の診断統計を取得
    /// </summary>
    public DiagnosticReport GetDiagnosticReport()
    {
        lock (_statisticsLock)
        {
            return new DiagnosticReport
            {
                GeneratedAt = DateTime.UtcNow,
                Statistics = _statistics.Clone(),
                ActiveOperationsCount = GetActiveOperationsCount(),
                RecentOperations = GetRecentOperations(10)
            };
        }
    }

    /// <summary>
    /// タイムアウト操作をチェックして強制キャンセル
    /// </summary>
    public void CheckAndCancelTimeoutOperations(TimeSpan timeout = default)
    {
        if (timeout == default)
            timeout = TimeSpan.FromSeconds(45); // PP-OCRv5のデフォルトタイムアウト

        var timeoutThreshold = DateTime.UtcNow - timeout;
        var timeoutOperations = new List<ActiveOperation>();

        lock (_operationsLock)
        {
            timeoutOperations.AddRange(_activeOperations.Where(op => op.StartTime < timeoutThreshold));
        }

        foreach (var operation in timeoutOperations)
        {
            _logger.LogWarning("タイムアウト操作検出: OperationId={OperationId}, Duration={Duration}, ImageInfo={ImageInfo}",
                operation.Id, DateTime.UtcNow - operation.StartTime, operation.ImageInfo);

            // 統計に記録
            operation.Success = false;
            operation.ErrorType = "Timeout";
            operation.Duration = DateTime.UtcNow - operation.StartTime;
            UpdateStatistics(operation, null);
        }
    }

    private async Task MonitorTimeoutsAsync()
    {
        while (!_timeoutCts.Token.IsCancellationRequested)
        {
            try
            {
                CheckAndCancelTimeoutOperations();
                await Task.Delay(TimeSpan.FromSeconds(5), _timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "タイムアウト監視中にエラー");
            }
        }
    }

    private void UpdateStatistics(ActiveOperation operation, OcrResults? result)
    {
        lock (_statisticsLock)
        {
            _statistics.TotalOperations++;
            
            if (operation.Success)
            {
                _statistics.SuccessfulOperations++;
                _statistics.TotalProcessingTime += operation.ProcessingTime ?? TimeSpan.Zero;
                _statistics.TotalTextRegions += operation.TextRegionsCount;
                
                if (result != null)
                {
                    _statistics.UpdateAverageConfidence(
                        result.TextRegions.Count > 0 
                            ? result.TextRegions.Average(r => r.Confidence)
                            : 0.0);
                }
            }
            else
            {
                _statistics.FailedOperations++;
                _statistics.IncrementErrorType(operation.ErrorType ?? "Unknown");
            }

            _statistics.UpdateProcessingTimeStats(operation.Duration);
            _statistics.LastUpdated = DateTime.UtcNow;
        }
    }

    private int GetActiveOperationsCount()
    {
        lock (_operationsLock)
        {
            return _activeOperations.Count;
        }
    }

    private List<ActiveOperation> GetRecentOperations(int count)
    {
        lock (_operationsLock)
        {
            return [.. _activeOperations
                .OrderByDescending(op => op.StartTime)
                .Take(count)];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _timeoutCts.Cancel();
        _timeoutCts.Dispose();
        _imageSaver?.Dispose();
        
        _disposed = true;
    }
}

/// <summary>
/// アクティブなOCR操作の追跡情報
/// </summary>
public class ActiveOperation
{
    public required string Id { get; init; }
    public DateTime StartTime { get; init; }
    public TimeSpan Duration { get; set; }
    public bool Success { get; set; }
    public string? ErrorType { get; set; }
    public string? ErrorMessage { get; set; }
    public required string ImageInfo { get; init; }
    public string? ImagePath { get; set; }
    public int TextRegionsCount { get; set; }
    public TimeSpan? ProcessingTime { get; set; }
    public OcrEngineSettings? Settings { get; init; }
}

/// <summary>
/// 診断統計情報
/// </summary>
public class DiagnosticStatistics
{
    public int TotalOperations { get; set; }
    public int SuccessfulOperations { get; set; }
    public int FailedOperations { get; set; }
    public int TotalTextRegions { get; set; }
    public TimeSpan TotalProcessingTime { get; set; }
    public DateTime LastUpdated { get; set; }
    
    // エラー分類
    private readonly Dictionary<string, int> _errorCounts = [];
    
    // 処理時間統計
    public TimeSpan MinProcessingTime { get; private set; } = TimeSpan.MaxValue;
    public TimeSpan MaxProcessingTime { get; private set; } = TimeSpan.Zero;
    public double AverageConfidence { get; private set; }
    private double _totalConfidence;
    private int _confidenceCount;

    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations * 100 : 0;
    public TimeSpan AverageProcessingTime => SuccessfulOperations > 0 ? 
        TimeSpan.FromTicks(TotalProcessingTime.Ticks / SuccessfulOperations) : TimeSpan.Zero;

    public IReadOnlyDictionary<string, int> ErrorCounts => _errorCounts.AsReadOnly();

    public void IncrementErrorType(string errorType)
    {
        _errorCounts[errorType] = _errorCounts.GetValueOrDefault(errorType, 0) + 1;
    }

    public void UpdateProcessingTimeStats(TimeSpan duration)
    {
        if (duration < MinProcessingTime)
            MinProcessingTime = duration;
        if (duration > MaxProcessingTime)
            MaxProcessingTime = duration;
    }

    public void UpdateAverageConfidence(double confidence)
    {
        _totalConfidence += confidence;
        _confidenceCount++;
        AverageConfidence = _totalConfidence / _confidenceCount;
    }

    public DiagnosticStatistics Clone()
    {
        var clone = new DiagnosticStatistics
        {
            TotalOperations = TotalOperations,
            SuccessfulOperations = SuccessfulOperations,
            FailedOperations = FailedOperations,
            TotalTextRegions = TotalTextRegions,
            TotalProcessingTime = TotalProcessingTime,
            LastUpdated = LastUpdated,
            MinProcessingTime = MinProcessingTime,
            MaxProcessingTime = MaxProcessingTime,
            AverageConfidence = AverageConfidence,
            _totalConfidence = _totalConfidence,
            _confidenceCount = _confidenceCount
        };

        foreach (var kvp in _errorCounts)
        {
            clone._errorCounts[kvp.Key] = kvp.Value;
        }

        return clone;
    }
}

/// <summary>
/// 診断レポート
/// </summary>
public class DiagnosticReport
{
    public DateTime GeneratedAt { get; init; }
    public required DiagnosticStatistics Statistics { get; init; }
    public int ActiveOperationsCount { get; init; }
    public required List<ActiveOperation> RecentOperations { get; init; }
}

/// <summary>
/// 診断サービスファクトリー
/// </summary>
public static class PPOCRv5DiagnosticServiceFactory
{
    public static PPOCRv5DiagnosticService Create(
        ILogger<PPOCRv5DiagnosticService> logger,
        string? diagnosticsOutputPath = null)
    {
        var imageSaver = new ImageDiagnosticsSaver(
            diagnosticsOutputPath ?? Path.Combine(Environment.CurrentDirectory, "ocr_diagnostics"));
        
        return new PPOCRv5DiagnosticService(logger, imageSaver);
    }
}