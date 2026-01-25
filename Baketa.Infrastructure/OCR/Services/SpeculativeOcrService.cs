using System.Diagnostics;
using System.Security.Cryptography;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.OCR;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.OCR.Services;

/// <summary>
/// 投機的OCRサービス実装
/// </summary>
/// <remarks>
/// Issue #293: 投機的実行とリソース適応
/// - GPU余裕時にバックグラウンドでOCRを先行実行
/// - 競合状態対策（SemaphoreSlim）
/// - 画面変化検知によるキャッシュ無効化
/// </remarks>
public sealed class SpeculativeOcrService : ISpeculativeOcrService
{
    private readonly IOcrEngine _ocrEngine;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly ITranslationModeService _translationModeService;
    private readonly IOptionsMonitor<SpeculativeOcrSettings> _settingsMonitor;
    private readonly ILogger<SpeculativeOcrService> _logger;

    // キャッシュ管理
    private SpeculativeOcrResult? _cachedResult;
    private readonly object _cacheLock = new();

    // 実行制御
    private readonly SemaphoreSlim _executionSemaphore = new(1, 1);
    private CancellationTokenSource? _currentExecutionCts;
    private DateTime _lastExecutionTime = DateTime.MinValue;
    private volatile bool _isExecuting;

    // メトリクス
    private int _executionCount;
    private int _cacheHitCount;
    private int _cacheMissCount;
    private int _wastedExecutionCount;
    private int _skippedDueToResourceCount;
    private long _totalExecutionTimeMs;
    private readonly DateTime _metricsStartTime = DateTime.UtcNow;

    // Dispose管理
    private bool _disposed;

    public SpeculativeOcrService(
        IOcrEngine ocrEngine,
        IResourceMonitor resourceMonitor,
        ITranslationModeService translationModeService,
        IOptionsMonitor<SpeculativeOcrSettings> settingsMonitor,
        ILogger<SpeculativeOcrService> logger)
    {
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("📊 [SpeculativeOcr] 投機的OCRサービス初期化完了");
    }

    private SpeculativeOcrSettings Settings => _settingsMonitor.CurrentValue;

    #region ISpeculativeOcrService実装

    /// <inheritdoc/>
    public bool IsEnabled => Settings.IsEnabled && !Settings.EnablePowerSavingMode;

    /// <inheritdoc/>
    public bool IsExecuting => _isExecuting;

    /// <inheritdoc/>
    public SpeculativeOcrResult? CachedResult
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedResult;
            }
        }
    }

    /// <inheritdoc/>
    public bool IsCacheValid
    {
        get
        {
            lock (_cacheLock)
            {
                return _cachedResult != null && !_cachedResult.IsExpired;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryExecuteSpeculativeOcrAsync(
        IImage image,
        string? imageHash = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsEnabled)
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("📊 [SpeculativeOcr] 無効化されているためスキップ");
            return false;
        }

        // Live翻訳中は実行しない
        if (Settings.DisableDuringLiveTranslation &&
            _translationModeService.CurrentMode == TranslationMode.Live)
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("📊 [SpeculativeOcr] Live翻訳中のためスキップ");
            return false;
        }

        // 最小実行間隔チェック
        var timeSinceLastExecution = DateTime.UtcNow - _lastExecutionTime;
        if (timeSinceLastExecution < Settings.MinExecutionInterval)
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("📊 [SpeculativeOcr] 最小実行間隔未満のためスキップ: {Elapsed}ms < {Min}ms",
                    timeSinceLastExecution.TotalMilliseconds, Settings.MinExecutionInterval.TotalMilliseconds);
            return false;
        }

        // リソース状況チェック
        if (!await CheckResourceAvailabilityAsync(cancellationToken).ConfigureAwait(false))
        {
            Interlocked.Increment(ref _skippedDueToResourceCount);
            return false;
        }

        // 排他制御（既に実行中の場合はスキップ）
        if (!await _executionSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            if (Settings.EnableDetailedLogging)
                _logger.LogDebug("📊 [SpeculativeOcr] 既に実行中のためスキップ");
            return false;
        }

        try
        {
            _isExecuting = true;

            // 画像ハッシュ計算（未指定の場合）
            var hash = imageHash ?? await ComputeImageHashAsync(image, cancellationToken).ConfigureAwait(false);

            // 同じ画面のキャッシュが有効な場合はスキップ
            lock (_cacheLock)
            {
                if (_cachedResult != null && !_cachedResult.IsExpired && _cachedResult.MatchesHash(hash))
                {
                    if (Settings.EnableDetailedLogging)
                        _logger.LogDebug("📊 [SpeculativeOcr] 同一画面のキャッシュが有効のためスキップ");
                    return false;
                }
            }

            // キャンセレーショントークン準備
            _currentExecutionCts?.Cancel();
            _currentExecutionCts?.Dispose();
            _currentExecutionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _currentExecutionCts.CancelAfter(Settings.ExecutionTimeout);

            var linkedToken = _currentExecutionCts.Token;

            _logger.LogInformation("📊 [SpeculativeOcr] 投機的OCR実行開始");
            var stopwatch = Stopwatch.StartNew();

            // 既存キャッシュを無効化
            InvalidateCacheInternal(CacheInvalidationReason.NewExecutionStarted);

            // OCR実行
            var ocrResults = await _ocrEngine.RecognizeAsync(image, cancellationToken: linkedToken).ConfigureAwait(false);

            stopwatch.Stop();
            _lastExecutionTime = DateTime.UtcNow;

            // キャッシュ作成
            var result = new SpeculativeOcrResult
            {
                OcrResults = ocrResults,
                ImageHash = hash,
                CapturedAt = DateTime.UtcNow - stopwatch.Elapsed,
                CompletedAt = DateTime.UtcNow,
                ExecutionTime = stopwatch.Elapsed,
                ImageSize = new ImageSize(image.Width, image.Height),
                ExpiresAt = DateTime.UtcNow + Settings.CacheTtl
            };

            lock (_cacheLock)
            {
                _cachedResult = result;
            }

            // メトリクス更新
            Interlocked.Increment(ref _executionCount);
            Interlocked.Add(ref _totalExecutionTimeMs, (long)stopwatch.ElapsedMilliseconds);

            _logger.LogInformation("📊 [SpeculativeOcr] 投機的OCR完了: {ExecutionTime}ms, {RegionCount}領域検出",
                stopwatch.ElapsedMilliseconds, result.DetectedRegionCount);

            // イベント発行
            OnSpeculativeOcrCompleted(result, stopwatch.Elapsed);

            return true;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("📊 [SpeculativeOcr] 投機的OCRがキャンセルされました");
            InvalidateCacheInternal(CacheInvalidationReason.ExecutionCancelled);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📊 [SpeculativeOcr] 投機的OCR実行エラー");
            return false;
        }
        finally
        {
            _isExecuting = false;
            _executionSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public SpeculativeOcrResult? ConsumeCache(string? currentImageHash = null)
    {
        lock (_cacheLock)
        {
            if (_cachedResult == null)
            {
                Interlocked.Increment(ref _cacheMissCount);
                if (Settings.EnableDetailedLogging)
                    _logger.LogDebug("📊 [SpeculativeOcr] キャッシュミス: キャッシュなし");
                return null;
            }

            if (_cachedResult.IsExpired)
            {
                Interlocked.Increment(ref _cacheMissCount);
                if (Settings.EnableDetailedLogging)
                    _logger.LogDebug("📊 [SpeculativeOcr] キャッシュミス: TTL期限切れ (Age={Age}ms)",
                        _cachedResult.Age.TotalMilliseconds);
                InvalidateCacheInternal(CacheInvalidationReason.Expired);
                return null;
            }

            // 画面変化検知が有効で、ハッシュが指定されている場合は検証
            if (Settings.EnableScreenChangeDetection &&
                !string.IsNullOrEmpty(currentImageHash) &&
                !_cachedResult.MatchesHash(currentImageHash))
            {
                Interlocked.Increment(ref _cacheMissCount);
                if (Settings.EnableDetailedLogging)
                    _logger.LogDebug("📊 [SpeculativeOcr] キャッシュミス: 画面変化検出");
                InvalidateCacheInternal(CacheInvalidationReason.ScreenChanged);
                return null;
            }

            // キャッシュヒット
            var result = _cachedResult;
            _cachedResult = null;

            Interlocked.Increment(ref _cacheHitCount);
            _logger.LogInformation("📊 [SpeculativeOcr] キャッシュヒット！ OCRスキップ (Age={Age}ms, Regions={Regions})",
                result.Age.TotalMilliseconds, result.DetectedRegionCount);

            OnCacheInvalidated(CacheInvalidationReason.Consumed, result.Age);

            return result;
        }
    }

    /// <inheritdoc/>
    public void InvalidateCache()
    {
        InvalidateCacheInternal(CacheInvalidationReason.ManualInvalidation);
    }

    /// <inheritdoc/>
    public void CancelCurrentExecution()
    {
        if (_isExecuting)
        {
            _logger.LogDebug("📊 [SpeculativeOcr] 現在の実行をキャンセル");
            _currentExecutionCts?.Cancel();
        }
    }

    /// <inheritdoc/>
    public event EventHandler<SpeculativeOcrCompletedEventArgs>? SpeculativeOcrCompleted;

    /// <inheritdoc/>
    public event EventHandler<SpeculativeOcrCacheInvalidatedEventArgs>? CacheInvalidated;

    #endregion

    #region Private Methods

    private async Task<bool> CheckResourceAvailabilityAsync(CancellationToken cancellationToken)
    {
        try
        {
            var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);

            var gpuUsage = metrics.GpuUsagePercent ?? 0;
            var cpuUsage = metrics.CpuUsagePercent;

            // VRAM使用量からVRAM使用率と残りVRAMを推定
            // 注意: ResourceMetricsにはTotalGpuMemoryMBがないため、一般的なGPU容量（8GB）を仮定
            const long EstimatedTotalVramMB = 8192;
            var usedVramMB = metrics.GpuMemoryUsageMB ?? 0;
            var vramUsage = usedVramMB > 0 ? (double)usedVramMB / EstimatedTotalVramMB * 100 : 0;
            var availableVram = EstimatedTotalVramMB - usedVramMB;

            var canExecute = Settings.CanExecute(gpuUsage, vramUsage, availableVram, cpuUsage);

            if (!canExecute && Settings.EnableDetailedLogging)
            {
                _logger.LogDebug("📊 [SpeculativeOcr] リソース不足でスキップ: " +
                    "GPU={Gpu:F1}%/{GpuThreshold}%, VRAM={Vram:F1}%/{VramThreshold}%, " +
                    "AvailVRAM={AvailVram}MB/{MinVram}MB, CPU={Cpu:F1}%/{CpuThreshold}%",
                    gpuUsage, Settings.GpuUsageThreshold,
                    vramUsage, Settings.VramUsageThreshold,
                    availableVram, Settings.MinAvailableVramMB,
                    cpuUsage, Settings.CpuUsageThreshold);
            }

            return canExecute;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📊 [SpeculativeOcr] リソース状況取得エラー - 安全のためスキップ");
            return false;
        }
    }

    private static async Task<string> ComputeImageHashAsync(IImage image, CancellationToken cancellationToken)
    {
        try
        {
            // 画像をバイト配列に変換してサンプリングハッシュ
            var data = await image.ToByteArrayAsync().ConfigureAwait(false);

            if (data == null || data.Length == 0)
                return Guid.NewGuid().ToString("N")[..16];

            cancellationToken.ThrowIfCancellationRequested();

            // サンプリング（1024バイトを等間隔で取得）
            var sampleSize = Math.Min(1024, data.Length);
            var sample = new byte[sampleSize];
            var step = Math.Max(1, data.Length / sampleSize);

            for (int i = 0, j = 0; i < sampleSize && j < data.Length; i++, j += step)
            {
                sample[i] = data[j];
            }

            // SHA256ハッシュ（先頭16文字のみ使用）
            var hash = SHA256.HashData(sample);
            return Convert.ToHexString(hash)[..16];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return Guid.NewGuid().ToString("N")[..16];
        }
    }

    private void InvalidateCacheInternal(CacheInvalidationReason reason)
    {
        TimeSpan? cacheAge = null;

        lock (_cacheLock)
        {
            if (_cachedResult != null)
            {
                cacheAge = _cachedResult.Age;

                // 消費されずに無効化された場合は無駄カウント
                if (reason != CacheInvalidationReason.Consumed)
                {
                    Interlocked.Increment(ref _wastedExecutionCount);
                }

                _cachedResult = null;

                if (Settings.EnableDetailedLogging)
                    _logger.LogDebug("📊 [SpeculativeOcr] キャッシュ無効化: {Reason}, Age={Age}ms",
                        reason, cacheAge?.TotalMilliseconds ?? 0);
            }
        }

        if (cacheAge.HasValue)
        {
            OnCacheInvalidated(reason, cacheAge);
        }
    }

    private void OnSpeculativeOcrCompleted(SpeculativeOcrResult result, TimeSpan executionTime)
    {
        try
        {
            SpeculativeOcrCompleted?.Invoke(this, new SpeculativeOcrCompletedEventArgs
            {
                Result = result,
                ExecutionTime = executionTime
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📊 [SpeculativeOcr] 完了イベント発行エラー");
        }
    }

    private void OnCacheInvalidated(CacheInvalidationReason reason, TimeSpan? cacheAge)
    {
        try
        {
            CacheInvalidated?.Invoke(this, new SpeculativeOcrCacheInvalidatedEventArgs
            {
                Reason = reason,
                CacheAge = cacheAge
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "📊 [SpeculativeOcr] キャッシュ無効化イベント発行エラー");
        }
    }

    #endregion

    #region Metrics

    /// <summary>
    /// 投機的OCRのメトリクスを取得
    /// </summary>
    public SpeculativeOcrMetrics GetMetrics()
    {
        var avgExecutionTime = _executionCount > 0
            ? TimeSpan.FromMilliseconds((double)_totalExecutionTimeMs / _executionCount)
            : TimeSpan.Zero;

        return new SpeculativeOcrMetrics
        {
            ExecutionCount = _executionCount,
            CacheHitCount = _cacheHitCount,
            CacheMissCount = _cacheMissCount,
            WastedExecutionCount = _wastedExecutionCount,
            SkippedDueToResourceCount = _skippedDueToResourceCount,
            AverageExecutionTime = avgExecutionTime,
            CollectionStartedAt = _metricsStartTime,
            LastUpdatedAt = DateTime.UtcNow
        };
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _currentExecutionCts?.Cancel();
        _currentExecutionCts?.Dispose();
        _executionSemaphore.Dispose();

        // メトリクスログ出力
        if (Settings.EnableMetricsCollection)
        {
            var metrics = GetMetrics();
            _logger.LogInformation("📊 [SpeculativeOcr] 終了時メトリクス: " +
                "実行={Exec}, ヒット={Hit} ({HitRate:F1}%), ミス={Miss}, 無駄={Wasted}, スキップ={Skip}",
                metrics.ExecutionCount, metrics.CacheHitCount, metrics.CacheHitRate,
                metrics.CacheMissCount, metrics.WastedExecutionCount, metrics.SkippedDueToResourceCount);
        }
    }

    #endregion
}
