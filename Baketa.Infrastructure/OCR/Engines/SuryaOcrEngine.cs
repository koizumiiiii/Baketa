using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Events;
using Baketa.Core.Models.OCR;
using Baketa.Infrastructure.OCR.Clients;
using Baketa.Infrastructure.OCR.Services;
using Baketa.Ocr.V1;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Engines;

/// <summary>
/// Surya OCR エンジン実装
/// Issue #189: gRPCベースのSurya OCR統合
/// Issue #300: 連続失敗時の自動復旧機能
///
/// 特徴:
/// - Python gRPCサーバー経由でSurya OCRを呼び出し
/// - 90+言語対応（日本語/英語高精度）
/// - ビジュアルノベルのダイアログテキスト検出に最適化
/// - 連続失敗時の自動サーバー再起動
/// </summary>
public sealed class SuryaOcrEngine : IOcrEngine
{
    private readonly GrpcOcrClient _client;
    private readonly SuryaServerManager? _serverManager;
    private readonly IEventAggregator? _eventAggregator;
    private readonly ILogger<SuryaOcrEngine> _logger;
    private readonly OcrEngineSettings _settings;
    private readonly ConcurrentDictionary<string, long> _performanceStats = new();
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly SemaphoreSlim _recoveryLock = new(1, 1);
    private readonly DateTime _startTime = DateTime.UtcNow;

    private bool _isInitialized;
    private bool _disposed;
    private int _consecutiveFailures;
    private int _totalProcessed;
    private int _errorCount;
    private double _minTimeMs = double.MaxValue;
    private double _maxTimeMs;
    private CancellationTokenSource? _currentTimeoutCts;
    private bool _isRecovering;
    private int _recoveryAttempts;
    private DateTime _lastRecoveryTime = DateTime.MinValue;

    // Issue #300: 復旧設定
    // リトライ使い切り後に即座に再起動（閾値1）
    private const int ConsecutiveFailuresThreshold = 1;
    private const int MaxRecoveryAttempts = 3;
    private static readonly TimeSpan RecoveryCooldown = TimeSpan.FromMinutes(5);

    private static readonly IReadOnlyList<string> SupportedLanguages =
    [
        "ja", "en", "zh", "ko", "fr", "de", "es", "it", "pt", "ru",
        "ar", "hi", "th", "vi", "id", "ms", "nl", "pl", "tr", "uk"
    ];

    /// <summary>
    /// コンストラクタ（サーバー自動起動対応 + イベント発行対応）
    /// Issue #300: IEventAggregator追加
    /// </summary>
    public SuryaOcrEngine(
        GrpcOcrClient client,
        SuryaServerManager serverManager,
        IEventAggregator? eventAggregator,
        ILogger<SuryaOcrEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(logger);

        _client = client;
        _serverManager = serverManager;
        _eventAggregator = eventAggregator;
        _logger = logger;
        _settings = new OcrEngineSettings
        {
            Language = "ja",
            EnablePreprocessing = false, // Suryaは前処理不要
            DetectionThreshold = 0.5
        };

        _logger.LogInformation("SuryaOcrEngine created (with auto-start and recovery support)");

        // Issue #300: デバッグログ追加
        if (_eventAggregator != null)
        {
            _logger.LogInformation("[Issue #300] EventAggregator is available - recovery notifications enabled");
        }
        else
        {
            _logger.LogWarning("[Issue #300] EventAggregator is NULL - recovery notifications will NOT work");
        }
    }

    /// <summary>
    /// コンストラクタ（サーバー自動起動対応）
    /// </summary>
    public SuryaOcrEngine(GrpcOcrClient client, SuryaServerManager serverManager, ILogger<SuryaOcrEngine> logger)
        : this(client, serverManager, null, logger)
    {
    }

    /// <summary>
    /// コンストラクタ（後方互換性用）
    /// </summary>
    public SuryaOcrEngine(GrpcOcrClient client, ILogger<SuryaOcrEngine> logger)
        : this(client, null!, null, logger)
    {
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

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized) return true;

            if (settings != null)
            {
                _settings.Language = settings.Language;
                _settings.EnablePreprocessing = settings.EnablePreprocessing;
                _settings.DetectionThreshold = settings.DetectionThreshold;
            }

            // サーバー自動起動（SuryaServerManagerが設定されている場合）
            if (_serverManager != null)
            {
                _logger.LogInformation("🚀 [Surya] サーバー自動起動開始...");
                Console.WriteLine("🚀 [Surya] サーバー自動起動開始...");

                var serverStarted = await _serverManager.StartServerAsync(cancellationToken).ConfigureAwait(false);

                if (!serverStarted)
                {
                    _logger.LogError("❌ [Surya] サーバー自動起動失敗");
                    Console.WriteLine("❌ [Surya] サーバー自動起動失敗");
                    return false;
                }

                _logger.LogInformation("✅ [Surya] サーバー起動完了");
                Console.WriteLine("✅ [Surya] サーバー起動完了");
            }

            // サーバー接続確認
            var readyResponse = await _client.IsReadyAsync(cancellationToken).ConfigureAwait(false);

            if (readyResponse.IsReady)
            {
                _isInitialized = true;
                _logger.LogInformation("✅ SuryaOcrEngine initialized successfully");
                Console.WriteLine("✅ SuryaOcrEngine initialized successfully");
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
        finally
        {
            _initLock.Release();
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

        // 診断ログ
        Console.WriteLine($"🔍 [SuryaOCR] RecognizeAsync呼び出し - IsInitialized: {_isInitialized}, ImageSize: {image.Width}x{image.Height}");
        _logger.LogInformation("🔍 [SuryaOCR] RecognizeAsync呼び出し - IsInitialized: {IsInit}, ImageSize: {W}x{H}",
            _isInitialized, image.Width, image.Height);

        var sw = Stopwatch.StartNew();
        progressCallback?.Report(new OcrProgress(0, "Starting OCR...") { Phase = OcrPhase.TextDetection });

        try
        {
            // 画像データ取得（PNGエンコード済み）- メモリ効率最適化
            var imageMemory = image.GetImageMemory();
            Console.WriteLine($"🔍 [SuryaOCR] 画像データ取得完了: {imageMemory.Length} bytes");

            progressCallback?.Report(new OcrProgress(0.2, "Sending to Surya OCR...") { Phase = OcrPhase.TextDetection });

            // gRPC呼び出し（Span経由で中間配列を回避）
            Console.WriteLine($"🔍 [SuryaOCR] gRPC呼び出し開始...");
            var response = await _client.RecognizeAsync(
                imageMemory.ToArray(), // Note: GrpcOcrClient内でByteString.CopyFromを使用
                "png",
                [_settings.Language ?? "ja"],
                cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"🔍 [SuryaOCR] gRPC呼び出し完了: IsSuccess={response.IsSuccess}, RegionCount={response.Regions?.Count ?? 0}");

            progressCallback?.Report(new OcrProgress(0.8, "Processing results...") { Phase = OcrPhase.TextRecognition });

            if (!response.IsSuccess)
            {
                var failures = Interlocked.Increment(ref _consecutiveFailures);
                Interlocked.Increment(ref _errorCount);
                var errorMessage = response.Error?.Message ?? "Unknown error";
                _logger.LogWarning("Surya OCR failed: {Error} (consecutive failures: {Failures})", errorMessage, failures);

                // Issue #300: 連続失敗時の自動復旧
                if (ShouldTriggerRecovery(failures))
                {
                    _ = TriggerServerRecoveryAsync(failures, cancellationToken);
                }

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

            // 各テキスト領域の信頼度をログ出力
            foreach (var region in regions)
            {
                _logger.LogInformation(
                    "📝 [SuryaOCR] Text='{Text}' Confidence={Confidence:F3} Bounds=({X},{Y},{W}x{H})",
                    region.Text.Length > 50 ? region.Text[..50] + "..." : region.Text,
                    region.Confidence,
                    region.Bounds.X,
                    region.Bounds.Y,
                    region.Bounds.Width,
                    region.Bounds.Height);
            }

            // 平均信頼度を計算してサマリーログ
            var avgConfidence = regions.Count > 0 ? regions.Average(r => r.Confidence) : 0.0f;
            _logger.LogInformation(
                "Surya OCR completed: {RegionCount} regions in {ElapsedMs}ms (AvgConfidence={AvgConfidence:F3})",
                regions.Count,
                sw.ElapsedMilliseconds,
                avgConfidence);

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
            var failures = Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Increment(ref _errorCount);
            _logger.LogError(ex, "Surya OCR error (consecutive failures: {Failures})", failures);
            progressCallback?.Report(new OcrProgress(1.0, $"Error: {ex.Message}") { Phase = OcrPhase.Completed });

            // Issue #300: 連続失敗時の自動復旧
            if (ShouldTriggerRecovery(failures))
            {
                _ = TriggerServerRecoveryAsync(failures, cancellationToken);
            }

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
    /// <remarks>
    /// [Issue #320] Detection-Only APIを使用してテキスト領域の位置のみを検出。
    /// Recognition（テキスト認識）をスキップするため、約10倍高速（~100ms vs ~1000ms）。
    /// ROI学習用の高速検出に最適。
    /// 戻り値のOcrTextRegion.Textは空文字列（位置情報のみ）。
    /// </remarks>
    public async Task<OcrResults> DetectTextRegionsAsync(
        IImage image,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(image);

        var sw = Stopwatch.StartNew();

        try
        {
            // 画像データ取得（PNGエンコード済み）
            var imageMemory = image.GetImageMemory();

            _logger.LogDebug(
                "[Issue #320] Detection-Only: ImageSize={Width}x{Height}, DataSize={DataSize}KB",
                image.Width, image.Height, imageMemory.Length / 1024);

            // [Issue #320] Detection-Only gRPC呼び出し（Recognition をスキップ）
            var response = await _client.DetectAsync(
                imageMemory.ToArray(),
                "png",
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccess)
            {
                var errorMessage = response.Error?.Message ?? "Unknown error";
                _logger.LogWarning("[Issue #320] Detection-Only failed: {Error}", errorMessage);

                return new OcrResults(
                    [],
                    image,
                    sw.Elapsed,
                    _settings.Language ?? "ja",
                    null);
            }

            // DetectedRegion → OcrTextRegion 変換（テキストは空）
            var regions = ConvertDetectedRegionsToOcrTextRegions(response);

            sw.Stop();

            _logger.LogInformation(
                "[Issue #320] Detection-Only completed: {RegionCount} regions in {ElapsedMs}ms (Server: {ServerMs}ms)",
                regions.Count,
                sw.ElapsedMilliseconds,
                response.ProcessingTimeMs);

            return new OcrResults(
                regions,
                image,
                sw.Elapsed,
                _settings.Language ?? "ja",
                null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #320] Detection-Only error");

            return new OcrResults(
                [],
                image,
                sw.Elapsed,
                _settings.Language ?? "ja",
                null);
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// [Issue #330] バッチOCR実装。
    /// gRPC呼び出しを N回→1回に削減することで部分OCRを高速化。
    /// </remarks>
    public async Task<IReadOnlyList<OcrResults>> RecognizeBatchAsync(
        IReadOnlyList<IImage> images,
        IProgress<OcrProgress>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(images);

        if (images.Count == 0)
        {
            return [];
        }

        var sw = Stopwatch.StartNew();
        progressCallback?.Report(new OcrProgress(0, $"Starting batch OCR ({images.Count} images)...") { Phase = OcrPhase.TextDetection });

        try
        {
            // 画像データをバイト配列に変換
            var imageDataList = new List<byte[]>(images.Count);
            foreach (var image in images)
            {
                var imageMemory = image.GetImageMemory();
                imageDataList.Add(imageMemory.ToArray());
            }

            progressCallback?.Report(new OcrProgress(0.2, "Sending batch to Surya OCR...") { Phase = OcrPhase.TextDetection });

            _logger.LogInformation("[Issue #330] RecognizeBatchAsync: {Count} images, total size {TotalSizeKB}KB",
                images.Count, imageDataList.Sum(d => d.Length) / 1024);

            // gRPCバッチ呼び出し
            var response = await _client.RecognizeBatchAsync(
                imageDataList,
                "png",
                [_settings.Language ?? "ja"],
                cancellationToken).ConfigureAwait(false);

            progressCallback?.Report(new OcrProgress(0.8, "Processing batch results...") { Phase = OcrPhase.TextRecognition });

            if (!response.IsSuccess || response.Responses.Count == 0)
            {
                var errorMessage = response.Error?.Message ?? "Batch OCR failed";

                // [Issue #402] キャンセルによる失敗はDEBUGレベル
                if (cancellationToken.IsCancellationRequested || errorMessage.Contains("Cancelled"))
                {
                    _logger.LogDebug("[Issue #330] RecognizeBatchAsync cancelled: {Error}", errorMessage);
                }
                else
                {
                    _logger.LogWarning("[Issue #330] RecognizeBatchAsync failed: {Error}", errorMessage);
                }

                // エラー時は空の結果リストを返す
                var emptyResults = new List<OcrResults>();
                for (var i = 0; i < images.Count; i++)
                {
                    emptyResults.Add(new OcrResults(
                        [],
                        images[i],
                        TimeSpan.Zero,
                        _settings.Language ?? "ja",
                        null));
                }
                return emptyResults;
            }

            // 各レスポンスをOcrResultsに変換
            var results = new List<OcrResults>();
            for (var i = 0; i < response.Responses.Count && i < images.Count; i++)
            {
                var ocrResponse = response.Responses[i];
                var image = images[i];

                var regions = ocrResponse.IsSuccess
                    ? ConvertToOcrTextRegions(ocrResponse, null)
                    : [];

                results.Add(new OcrResults(
                    regions,
                    image,
                    TimeSpan.FromMilliseconds(ocrResponse.ProcessingTimeMs),
                    _settings.Language ?? "ja",
                    null));
            }

            // 足りない分は空の結果で埋める
            while (results.Count < images.Count)
            {
                results.Add(new OcrResults(
                    [],
                    images[results.Count],
                    TimeSpan.Zero,
                    _settings.Language ?? "ja",
                    null));
            }

            sw.Stop();

            _logger.LogInformation(
                "[Issue #330] RecognizeBatchAsync completed: {SuccessCount}/{TotalCount} success in {ElapsedMs}ms (Server: {ServerMs}ms)",
                response.SuccessCount,
                response.TotalCount,
                sw.ElapsedMilliseconds,
                response.TotalProcessingTimeMs);

            progressCallback?.Report(new OcrProgress(1.0, $"Batch OCR completed: {response.SuccessCount} success") { Phase = OcrPhase.Completed });

            return results;
        }
        catch (OperationCanceledException)
        {
            progressCallback?.Report(new OcrProgress(1.0, "Cancelled") { Phase = OcrPhase.Completed });
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #330] RecognizeBatchAsync error");
            progressCallback?.Report(new OcrProgress(1.0, $"Error: {ex.Message}") { Phase = OcrPhase.Completed });

            // エラー時は空の結果リストを返す
            var emptyResults = new List<OcrResults>();
            for (var i = 0; i < images.Count; i++)
            {
                emptyResults.Add(new OcrResults(
                    [],
                    images[i],
                    TimeSpan.Zero,
                    _settings.Language ?? "ja",
                    null));
            }
            return emptyResults;
        }
    }

    /// <summary>
    /// [Issue #320] DetectedRegion（Detection-Only）をOcrTextRegionリストに変換
    /// テキスト内容は空文字列（位置情報のみ）
    /// </summary>
    private static List<OcrTextRegion> ConvertDetectedRegionsToOcrTextRegions(DetectResponse response)
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

            // Detection-Only: テキストは空、位置情報と検出信頼度のみ
            regions.Add(new OcrTextRegion(
                string.Empty,  // テキストなし（Detection-Only）
                bounds,
                region.Confidence));
        }

        return regions;
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
        _initLock.Dispose();
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

    #region Issue #300: 自動復旧機能

    /// <summary>
    /// 復旧をトリガーすべきかどうかを判定
    /// </summary>
    private bool ShouldTriggerRecovery(int consecutiveFailures)
    {
        _logger.LogDebug("[Issue #300] ShouldTriggerRecovery check: failures={Failures}, threshold={Threshold}, recovering={Recovering}, attempts={Attempts}/{Max}",
            consecutiveFailures, ConsecutiveFailuresThreshold, _isRecovering, _recoveryAttempts, MaxRecoveryAttempts);

        // サーバーマネージャーがない場合は復旧不可
        if (_serverManager == null)
        {
            _logger.LogDebug("[Issue #300] Recovery skipped: ServerManager is null");
            return false;
        }

        // 既に復旧中の場合はスキップ
        if (_isRecovering)
        {
            _logger.LogDebug("[Issue #300] Recovery skipped: Already recovering");
            return false;
        }

        // 連続失敗が閾値未満の場合はスキップ
        if (consecutiveFailures < ConsecutiveFailuresThreshold)
        {
            _logger.LogDebug("[Issue #300] Recovery skipped: Failures below threshold");
            return false;
        }

        // 復旧試行回数が上限に達している場合はスキップ
        if (_recoveryAttempts >= MaxRecoveryAttempts)
        {
            _logger.LogDebug("[Issue #300] Recovery skipped: Max attempts reached");
            return false;
        }

        // クールダウン期間中の場合はスキップ
        if (DateTime.UtcNow - _lastRecoveryTime < RecoveryCooldown)
        {
            _logger.LogDebug("[Issue #300] Recovery skipped: In cooldown period");
            return false;
        }

        _logger.LogInformation("[Issue #300] Recovery WILL be triggered!");
        return true;
    }

    /// <summary>
    /// サーバー復旧を非同期でトリガー
    /// Issue #300: ユーザーに通知しながら自動再起動
    /// </summary>
    private async Task TriggerServerRecoveryAsync(int consecutiveFailures, CancellationToken cancellationToken)
    {
        // 復旧ロックを取得（二重復旧防止）
        if (!await _recoveryLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogDebug("[Issue #300] Recovery already in progress, skipping");
            return;
        }

        try
        {
            _isRecovering = true;
            _recoveryAttempts++;
            _lastRecoveryTime = DateTime.UtcNow;

            _logger.LogWarning(
                "[Issue #300] Triggering OCR server recovery (failures: {Failures}, attempt: {Attempt}/{MaxAttempts})",
                consecutiveFailures, _recoveryAttempts, MaxRecoveryAttempts);

            // 復旧開始イベントを発行
            if (_eventAggregator != null)
            {
                var startEvent = OcrServerRecoveryEvent.CreateRestartStarted(consecutiveFailures, _recoveryAttempts);
                await _eventAggregator.PublishAsync(startEvent, cancellationToken).ConfigureAwait(false);
            }

            // サーバー停止
            _logger.LogInformation("[Issue #300] Stopping OCR server...");
            await _serverManager!.StopServerAsync().ConfigureAwait(false);

            // 少し待機（ソケット解放のため）
            await Task.Delay(1000, cancellationToken).ConfigureAwait(false);

            // サーバー再起動
            _logger.LogInformation("[Issue #300] Restarting OCR server...");
            var restarted = await _serverManager.StartServerAsync(cancellationToken).ConfigureAwait(false);

            if (restarted)
            {
                _logger.LogInformation("[Issue #300] OCR server recovery successful");
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                _isInitialized = true;

                // 復旧成功イベントを発行
                if (_eventAggregator != null)
                {
                    var completeEvent = OcrServerRecoveryEvent.CreateRestartCompleted(_recoveryAttempts);
                    await _eventAggregator.PublishAsync(completeEvent, cancellationToken).ConfigureAwait(false);
                }

                // 復旧成功したら試行回数をリセット
                _recoveryAttempts = 0;
            }
            else
            {
                _logger.LogError("[Issue #300] OCR server recovery failed");
                _isInitialized = false;

                // 復旧失敗イベントを発行
                if (_eventAggregator != null)
                {
                    var failEvent = OcrServerRecoveryEvent.CreateRestartFailed(_recoveryAttempts);
                    await _eventAggregator.PublishAsync(failEvent, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[Issue #300] Recovery cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #300] Error during OCR server recovery");

            // 復旧失敗イベントを発行
            if (_eventAggregator != null)
            {
                try
                {
                    var failEvent = OcrServerRecoveryEvent.CreateRestartFailed(_recoveryAttempts);
                    await _eventAggregator.PublishAsync(failEvent, CancellationToken.None).ConfigureAwait(false);
                }
                catch
                {
                    // イベント発行失敗は無視
                }
            }
        }
        finally
        {
            _isRecovering = false;
            _recoveryLock.Release();
        }
    }

    #endregion
}
