using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Models.OCR;
using Baketa.Infrastructure.OCR.Clients;
using Baketa.Ocr.V1;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Engines;

/// <summary>
/// Surya OCR エンジン実装
/// Issue #189: gRPCベースのSurya OCR統合
///
/// 特徴:
/// - Python gRPCサーバー経由でSurya OCRを呼び出し
/// - 90+言語対応（日本語/英語高精度）
/// - ビジュアルノベルのダイアログテキスト検出に最適化
/// </summary>
public sealed class SuryaOcrEngine : IOcrEngine
{
    private readonly GrpcOcrClient _client;
    private readonly ILogger<SuryaOcrEngine> _logger;
    private readonly OcrEngineSettings _settings;
    private readonly ConcurrentDictionary<string, long> _performanceStats = new();
    private readonly object _initLock = new();
    private readonly DateTime _startTime = DateTime.UtcNow;

    private bool _isInitialized;
    private bool _disposed;
    private int _consecutiveFailures;
    private int _totalProcessed;
    private int _errorCount;
    private double _minTimeMs = double.MaxValue;
    private double _maxTimeMs;
    private CancellationTokenSource? _currentTimeoutCts;

    private static readonly IReadOnlyList<string> SupportedLanguages =
    [
        "ja", "en", "zh", "ko", "fr", "de", "es", "it", "pt", "ru",
        "ar", "hi", "th", "vi", "id", "ms", "nl", "pl", "tr", "uk"
    ];

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public SuryaOcrEngine(GrpcOcrClient client, ILogger<SuryaOcrEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _logger = logger;
        _settings = new OcrEngineSettings
        {
            Language = "ja",
            EnablePreprocessing = false, // Suryaは前処理不要
            DetectionThreshold = 0.5
        };

        _logger.LogInformation("SuryaOcrEngine created");
    }

    /// <inheritdoc/>
    public string EngineName => "Surya OCR";

    /// <inheritdoc/>
    public string EngineVersion => "0.17.0";

    /// <inheritdoc/>
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc/>
    public string? CurrentLanguage => _settings.Language;

    /// <inheritdoc/>
    public async Task<bool> InitializeAsync(
        OcrEngineSettings? settings = null,
        CancellationToken cancellationToken = default)
    {
        if (_isInitialized) return true;

        lock (_initLock)
        {
            if (_isInitialized) return true;

            if (settings != null)
            {
                _settings.Language = settings.Language;
                _settings.EnablePreprocessing = settings.EnablePreprocessing;
                _settings.DetectionThreshold = settings.DetectionThreshold;
            }
        }

        try
        {
            // サーバー接続確認
            var readyResponse = await _client.IsReadyAsync(cancellationToken).ConfigureAwait(false);

            if (readyResponse.IsReady)
            {
                _isInitialized = true;
                _logger.LogInformation("SuryaOcrEngine initialized successfully");
                return true;
            }

            _logger.LogWarning("Surya OCR server not ready: {Status}", readyResponse.Status);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SuryaOcrEngine");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> WarmupAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var healthResponse = await _client.HealthCheckAsync(cancellationToken).ConfigureAwait(false);
            return healthResponse.IsHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warmup failed");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        return await RecognizeAsync(image, null, progressCallback, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<OcrResults> RecognizeAsync(
        IImage image,
        Rectangle? regionOfInterest,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);

        var sw = Stopwatch.StartNew();
        progressCallback?.Report(new OcrProgress(0, "Starting OCR...") { Phase = OcrPhase.TextDetection });

        try
        {
            // 画像データ取得（PNGエンコード済み）- メモリ効率最適化
            var imageMemory = image.GetImageMemory();

            progressCallback?.Report(new OcrProgress(0.2, "Sending to Surya OCR...") { Phase = OcrPhase.TextDetection });

            // gRPC呼び出し（Span経由で中間配列を回避）
            var response = await _client.RecognizeAsync(
                imageMemory.ToArray(), // Note: GrpcOcrClient内でByteString.CopyFromを使用
                "png",
                [_settings.Language ?? "ja"],
                cancellationToken).ConfigureAwait(false);

            progressCallback?.Report(new OcrProgress(0.8, "Processing results...") { Phase = OcrPhase.TextRecognition });

            if (!response.IsSuccess)
            {
                Interlocked.Increment(ref _consecutiveFailures);
                Interlocked.Increment(ref _errorCount);
                var errorMessage = response.Error?.Message ?? "Unknown error";
                _logger.LogWarning("Surya OCR failed: {Error}", errorMessage);

                progressCallback?.Report(new OcrProgress(1.0, $"Error: {errorMessage}") { Phase = OcrPhase.Completed });

                return new OcrResults(
                    [],
                    image,
                    sw.Elapsed,
                    _settings.Language ?? "ja",
                    regionOfInterest);
            }

            // 成功時はカウンタリセット
            Interlocked.Exchange(ref _consecutiveFailures, 0);
            Interlocked.Increment(ref _totalProcessed);

            // gRPCレスポンスをOcrResultsに変換
            var regions = ConvertToOcrTextRegions(response, regionOfInterest);

            sw.Stop();
            UpdatePerformanceStats(sw.ElapsedMilliseconds);

            progressCallback?.Report(new OcrProgress(1.0, $"Detected {regions.Count} regions") { Phase = OcrPhase.Completed });

            _logger.LogInformation(
                "Surya OCR completed: {RegionCount} regions in {ElapsedMs}ms",
                regions.Count,
                sw.ElapsedMilliseconds);

            return new OcrResults(
                regions,
                image,
                sw.Elapsed,
                _settings.Language ?? "ja",
                regionOfInterest);
        }
        catch (OperationCanceledException)
        {
            progressCallback?.Report(new OcrProgress(1.0, "Cancelled") { Phase = OcrPhase.Completed });
            throw;
        }
        catch (Exception ex)
        {
            Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Increment(ref _errorCount);
            _logger.LogError(ex, "Surya OCR error");
            progressCallback?.Report(new OcrProgress(1.0, $"Error: {ex.Message}") { Phase = OcrPhase.Completed });

            return new OcrResults(
                [],
                image,
                sw.Elapsed,
                _settings.Language ?? "ja",
                regionOfInterest);
        }
    }

    /// <inheritdoc/>
    public async Task<OcrResults> RecognizeAsync(
        OcrContext context,
        IProgress<OcrProgress>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Image);

        var roi = context.CaptureRegion != Rectangle.Empty ? context.CaptureRegion : (Rectangle?)null;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _currentTimeoutCts = cts;

        try
        {
            return await RecognizeAsync(context.Image, roi, progressCallback, cts.Token).ConfigureAwait(false);
        }
        finally
        {
            _currentTimeoutCts = null;
        }
    }

    /// <inheritdoc/>
    public async Task<OcrResults> DetectTextRegionsAsync(
        IImage image,
        CancellationToken cancellationToken = default)
    {
        // Suryaは検出と認識を同時に行うため、RecognizeAsyncを呼び出す
        return await RecognizeAsync(image, null, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public OcrEngineSettings GetSettings() => _settings;

    /// <inheritdoc/>
    public Task ApplySettingsAsync(OcrEngineSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        _settings.Language = settings.Language;
        _settings.EnablePreprocessing = settings.EnablePreprocessing;
        _settings.DetectionThreshold = settings.DetectionThreshold;

        _logger.LogDebug("Settings applied: Language={Language}", _settings.Language);

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableLanguages() => SupportedLanguages;

    /// <inheritdoc/>
    public IReadOnlyList<string> GetAvailableModels() => ["surya-0.17.0"];

    /// <inheritdoc/>
    public Task<bool> IsLanguageAvailableAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        var isAvailable = SupportedLanguages.Contains(languageCode, StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(isAvailable);
    }

    /// <inheritdoc/>
    public OcrPerformanceStats GetPerformanceStats()
    {
        _performanceStats.TryGetValue("total_time_ms", out var totalTimeMs);

        var avgTimeMs = _totalProcessed > 0 ? (double)totalTimeMs / _totalProcessed : 0;
        var successRate = (_totalProcessed + _errorCount) > 0
            ? (double)_totalProcessed / (_totalProcessed + _errorCount)
            : 1.0;

        return new OcrPerformanceStats
        {
            TotalProcessedImages = _totalProcessed,
            AverageProcessingTimeMs = avgTimeMs,
            MinProcessingTimeMs = _minTimeMs == double.MaxValue ? 0 : _minTimeMs,
            MaxProcessingTimeMs = _maxTimeMs,
            ErrorCount = _errorCount,
            SuccessRate = successRate,
            StartTime = _startTime,
            LastUpdateTime = DateTime.UtcNow
        };
    }

    /// <inheritdoc/>
    public void CancelCurrentOcrTimeout()
    {
        try
        {
            _currentTimeoutCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 既にDisposeされている場合は無視
        }
    }

    /// <inheritdoc/>
    public int GetConsecutiveFailureCount() => _consecutiveFailures;

    /// <inheritdoc/>
    public void ResetFailureCounter()
    {
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        _logger.LogDebug("Failure counter reset");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _currentTimeoutCts?.Dispose();
        _client.Dispose();

        _logger.LogDebug("SuryaOcrEngine disposed");
    }

    /// <summary>
    /// gRPCレスポンスをOcrTextRegionリストに変換
    /// </summary>
    private static List<OcrTextRegion> ConvertToOcrTextRegions(
        OcrResponse response,
        Rectangle? regionOfInterest)
    {
        var regions = new List<OcrTextRegion>();

        foreach (var region in response.Regions)
        {
            var bbox = region.BoundingBox;
            var bounds = new Rectangle(
                bbox.X,
                bbox.Y,
                bbox.Width,
                bbox.Height);

            // ROIがある場合は座標を調整
            if (regionOfInterest.HasValue)
            {
                var roi = regionOfInterest.Value;
                bounds = new Rectangle(
                    bounds.X + roi.X,
                    bounds.Y + roi.Y,
                    bounds.Width,
                    bounds.Height);
            }

            regions.Add(new OcrTextRegion(
                region.Text,
                bounds,
                region.Confidence));
        }

        return regions;
    }

    /// <summary>
    /// パフォーマンス統計を更新
    /// </summary>
    private void UpdatePerformanceStats(long elapsedMs)
    {
        _performanceStats.AddOrUpdate("total_time_ms", elapsedMs, (_, v) => v + elapsedMs);
        _performanceStats["last_time_ms"] = elapsedMs;

        // Min/Max更新
        if (elapsedMs < _minTimeMs) _minTimeMs = elapsedMs;
        if (elapsedMs > _maxTimeMs) _maxTimeMs = elapsedMs;
    }
}
