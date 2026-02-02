using System.Collections.Concurrent;
using System.Security.Cryptography;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.Primitives;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi.Services;

/// <summary>
/// [Issue #324] ROI領域変化監視サービス実装
/// 学習済みROI領域のみを監視し、テキスト送りを検知
/// </summary>
public sealed class RoiChangeMonitorService : IRoiChangeMonitorService
{
    private readonly IRoiManager _roiManager;
    private readonly ITranslationModeService _translationModeService;
    private readonly IOptionsMonitor<RoiManagerSettings> _settingsMonitor;
    private readonly ILogger<RoiChangeMonitorService> _logger;

    // 監視状態 (Note: _translationModeServiceは将来のLive翻訳連携用に保持)
    private volatile bool _isMonitoring;
    private CancellationTokenSource? _monitoringCts;
    private readonly Task? _monitoringTask;

    // ベースラインハッシュ（ROI領域ID → ハッシュ）
    private readonly ConcurrentDictionary<string, string> _baselineHashes = new();

    // 変化検知設定
    private const int DefaultPollingIntervalMs = 1000; // 1秒間隔
    private const float MinChangeRatioForTextAdvance = 0.1f; // 10%以上の変化でテキスト送りと判定
    private const int MinHashSampleSize = 512; // ハッシュ計算用サンプルサイズ

    // [Issue #370] デバウンス設定
    private const int DefaultDebounceIntervalMs = 500;
    private DateTime _lastChangeDetectionTime = DateTime.MinValue;

    private bool _disposed;

    public RoiChangeMonitorService(
        IRoiManager roiManager,
        ITranslationModeService translationModeService,
        IOptionsMonitor<RoiManagerSettings> settingsMonitor,
        ILogger<RoiChangeMonitorService> logger)
    {
        _roiManager = roiManager ?? throw new ArgumentNullException(nameof(roiManager));
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));
        _settingsMonitor = settingsMonitor ?? throw new ArgumentNullException(nameof(settingsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("[Issue #324] RoiChangeMonitorService initialized");
    }

    private RoiManagerSettings Settings => _settingsMonitor.CurrentValue;

    #region IRoiChangeMonitorService Implementation

    /// <inheritdoc/>
    public bool IsEnabled => Settings.Enabled;

    /// <inheritdoc/>
    public bool IsMonitoring => _isMonitoring;

    /// <inheritdoc/>
    public bool IsLearningComplete => _roiManager.IsLearningComplete;

    /// <inheritdoc/>
    public int MonitoredRegionCount => _roiManager.GetHighConfidenceRegions().Count;

    /// <inheritdoc/>
    public async Task StartMonitoringAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isMonitoring)
        {
            _logger.LogDebug("[Issue #324] Already monitoring, ignoring start request");
            return;
        }

        if (!IsEnabled)
        {
            _logger.LogDebug("[Issue #324] ROI monitoring disabled in settings");
            return;
        }

        if (!IsLearningComplete)
        {
            _logger.LogDebug("[Issue #324] Learning not complete, cannot start ROI monitoring");
            return;
        }

        // Live翻訳中は監視しない
        if (_translationModeService.CurrentMode == TranslationMode.Live)
        {
            _logger.LogDebug("[Issue #324] Live translation active, skipping ROI monitoring");
            OnMonitoringStateChanged(false, 0, RoiMonitoringStateChangeReason.LiveTranslationStarted);
            return;
        }

        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();
        _monitoringCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        _isMonitoring = true;
        var highConfidenceRegions = _roiManager.GetHighConfidenceRegions();

        _logger.LogInformation(
            "[Issue #324] Starting ROI monitoring: {RegionCount} high-confidence regions",
            highConfidenceRegions.Count);

        OnMonitoringStateChanged(true, highConfidenceRegions.Count, RoiMonitoringStateChangeReason.ManualStart);

        // Note: 実際のポーリングループはキャプチャサービスとの統合が必要
        // ここではイベント駆動型の実装に必要なメソッドのみ提供
    }

    /// <inheritdoc/>
    public async Task StopMonitoringAsync()
    {
        if (!_isMonitoring)
        {
            return;
        }

        _logger.LogInformation("[Issue #324] Stopping ROI monitoring");

        _monitoringCts?.Cancel();
        _isMonitoring = false;

        if (_monitoringTask != null)
        {
            try
            {
                await _monitoringTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _baselineHashes.Clear();
        OnMonitoringStateChanged(false, 0, RoiMonitoringStateChangeReason.ManualStop);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<RoiRegion>> CheckForChangesAsync(
        IImage currentImage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsEnabled || !IsLearningComplete)
        {
            return [];
        }

        var highConfidenceRegions = _roiManager.GetHighConfidenceRegions();
        if (highConfidenceRegions.Count == 0)
        {
            return [];
        }

        var changedRegions = new List<RoiRegion>();
        var imageWidth = currentImage.Width;
        var imageHeight = currentImage.Height;

        foreach (var region in highConfidenceRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                // ROI領域を絶対座標に変換
                var absoluteRect = region.ToAbsoluteRect(imageWidth, imageHeight);

                // 領域のハッシュを計算
                var currentHash = await ComputeRegionHashAsync(currentImage, absoluteRect, cancellationToken)
                    .ConfigureAwait(false);

                // ベースラインと比較
                if (_baselineHashes.TryGetValue(region.Id, out var baselineHash))
                {
                    if (currentHash != baselineHash)
                    {
                        changedRegions.Add(region);
                        _logger.LogDebug(
                            "[Issue #324] Change detected in ROI '{RegionId}': {OldHash} -> {NewHash}",
                            region.Id, baselineHash, currentHash);
                    }
                }
                else
                {
                    // ベースラインがない場合は初回として記録
                    _baselineHashes[region.Id] = currentHash;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #324] Error checking region '{RegionId}'", region.Id);
            }
        }

        if (changedRegions.Count > 0)
        {
            var changeRatio = (float)changedRegions.Count / highConfidenceRegions.Count;
            var isLikelyTextAdvance = changeRatio >= MinChangeRatioForTextAdvance;

            // [Issue #370] デバウンス処理: 短時間での重複イベント発火を抑制
            var debounceMs = Settings.ChangeDetectionDebounceMs > 0
                ? Settings.ChangeDetectionDebounceMs
                : DefaultDebounceIntervalMs;
            var debounceInterval = TimeSpan.FromMilliseconds(debounceMs);
            var now = DateTime.UtcNow;
            if (now - _lastChangeDetectionTime < debounceInterval)
            {
                _logger.LogDebug(
                    "ROI change debounced: {ChangedCount} regions detected within {DebounceMs}ms, skipping event",
                    changedRegions.Count, debounceMs);
                return changedRegions;
            }
            _lastChangeDetectionTime = now;

            _logger.LogInformation(
                "ROI changes detected: {ChangedCount}/{TotalCount} regions, LikelyTextAdvance={IsTextAdvance}",
                changedRegions.Count, highConfidenceRegions.Count, isLikelyTextAdvance);

            OnRoiChangeDetected(changedRegions, changeRatio, isLikelyTextAdvance);
        }

        return changedRegions;
    }

    /// <inheritdoc/>
    public async Task UpdateBaselineAsync(
        IImage currentImage,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!IsEnabled)
        {
            return;
        }

        var highConfidenceRegions = _roiManager.GetHighConfidenceRegions();
        if (highConfidenceRegions.Count == 0)
        {
            return;
        }

        var imageWidth = currentImage.Width;
        var imageHeight = currentImage.Height;

        foreach (var region in highConfidenceRegions)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var absoluteRect = region.ToAbsoluteRect(imageWidth, imageHeight);
                var hash = await ComputeRegionHashAsync(currentImage, absoluteRect, cancellationToken)
                    .ConfigureAwait(false);

                _baselineHashes[region.Id] = hash;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #324] Error updating baseline for region '{RegionId}'", region.Id);
            }
        }

        _logger.LogDebug(
            "[Issue #324] Updated baseline for {RegionCount} ROI regions",
            highConfidenceRegions.Count);
    }

    /// <inheritdoc/>
    public event EventHandler<RoiChangeDetectedEventArgs>? RoiChangeDetected;

    /// <inheritdoc/>
    public event EventHandler<RoiMonitoringStateChangedEventArgs>? MonitoringStateChanged;

    #endregion

    #region Private Methods

    /// <summary>
    /// 指定した領域の画像ハッシュを計算
    /// </summary>
    private async Task<string> ComputeRegionHashAsync(
        IImage image,
        Rect region,
        CancellationToken cancellationToken)
    {
        // 領域が画像範囲内かチェック
        var clampedX = Math.Max(0, Math.Min(region.X, image.Width - 1));
        var clampedY = Math.Max(0, Math.Min(region.Y, image.Height - 1));
        var clampedWidth = Math.Min(region.Width, image.Width - clampedX);
        var clampedHeight = Math.Min(region.Height, image.Height - clampedY);

        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return Guid.NewGuid().ToString("N")[..16];
        }

        // 画像データを取得
        var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);
        if (imageData == null || imageData.Length == 0)
        {
            return Guid.NewGuid().ToString("N")[..16];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // 領域のサンプリングハッシュを計算
        // 注: IImageインターフェースには領域切り出しメソッドがないため、
        // 画像全体のデータから領域位置を基にサンプリング
        var sampleSize = Math.Min(MinHashSampleSize, imageData.Length);
        var sample = new byte[sampleSize];

        // 領域の中心位置を基点にサンプリング
        var regionStartOffset = (clampedY * image.Width + clampedX) * 4; // BGRA
        var step = Math.Max(1, imageData.Length / sampleSize);

        for (int i = 0, offset = regionStartOffset % imageData.Length;
             i < sampleSize && offset < imageData.Length;
             i++, offset = (offset + step) % imageData.Length)
        {
            sample[i] = imageData[offset];
        }

        var hash = SHA256.HashData(sample);
        return Convert.ToHexString(hash)[..16];
    }

    private void OnRoiChangeDetected(
        IReadOnlyList<RoiRegion> changedRegions,
        float changeRatio,
        bool isLikelyTextAdvance)
    {
        try
        {
            RoiChangeDetected?.Invoke(this, new RoiChangeDetectedEventArgs
            {
                ChangedRegions = changedRegions,
                ChangeRatio = changeRatio,
                IsLikelyTextAdvance = isLikelyTextAdvance
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #324] Error invoking RoiChangeDetected event");
        }
    }

    private void OnMonitoringStateChanged(bool isMonitoring, int regionCount, RoiMonitoringStateChangeReason reason)
    {
        try
        {
            MonitoringStateChanged?.Invoke(this, new RoiMonitoringStateChangedEventArgs
            {
                IsMonitoring = isMonitoring,
                MonitoredRegionCount = regionCount,
                Reason = reason
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #324] Error invoking MonitoringStateChanged event");
        }
    }

    #endregion

    #region IDisposable

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _isMonitoring = false;
        _monitoringCts?.Cancel();
        _monitoringCts?.Dispose();

        OnMonitoringStateChanged(false, 0, RoiMonitoringStateChangeReason.Disposing);

        _logger.LogDebug("[Issue #324] RoiChangeMonitorService disposed");
    }

    #endregion
}
