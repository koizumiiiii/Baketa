using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.ResourceManagement;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.ResourceManagement;

/// <summary>
/// 動的VRAM容量検出システム
/// ハードコーディングされた固定値を排除し、実際のGPU環境に基づく容量を検出
/// </summary>
public sealed class VramCapacityDetector : IDisposable
{
    private readonly ILogger<VramCapacityDetector> _logger;
    private readonly IResourceMonitor _resourceMonitor;
    private readonly IGpuEnvironmentDetector? _gpuEnvironmentDetector;
    
    private long? _cachedVramCapacityMB;
    private DateTime _lastDetectionTime = DateTime.MinValue;
    private readonly TimeSpan _cacheValidityDuration = ResourceManagementConstants.Vram.CacheValidityDuration;
    private readonly object _detectionLock = new();
    private bool _disposed;

    // フォールバック設定
    private static readonly long[] CommonVramSizes = ResourceManagementConstants.Vram.CommonCapacityMB;

    public VramCapacityDetector(
        ILogger<VramCapacityDetector> logger,
        IResourceMonitor resourceMonitor,
        IGpuEnvironmentDetector? gpuEnvironmentDetector = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resourceMonitor = resourceMonitor ?? throw new ArgumentNullException(nameof(resourceMonitor));
        _gpuEnvironmentDetector = gpuEnvironmentDetector;

        _logger.LogInformation("🔍 [VRAM] 動的VRAM容量検出システム初期化完了");
    }

    /// <summary>
    /// 動的VRAM容量検出
    /// </summary>
    public async Task<long> DetectVramCapacityAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) 
            throw new ObjectDisposedException(nameof(VramCapacityDetector));

        lock (_detectionLock)
        {
            // キャッシュが有効な場合は再利用
            if (_cachedVramCapacityMB.HasValue && 
                DateTime.UtcNow - _lastDetectionTime < _cacheValidityDuration)
            {
                return _cachedVramCapacityMB.Value;
            }
        }

        try
        {
            // 検出実行
            var detectedCapacity = await PerformVramDetectionAsync(cancellationToken).ConfigureAwait(false);
            
            lock (_detectionLock)
            {
                _cachedVramCapacityMB = detectedCapacity;
                _lastDetectionTime = DateTime.UtcNow;
            }

            _logger.LogInformation("✅ [VRAM] 動的容量検出完了: {CapacityMB}MB", detectedCapacity);
            return detectedCapacity;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [VRAM] 容量検出エラー、フォールバック値使用");
            return GetFallbackVramCapacity();
        }
    }

    /// <summary>
    /// 現在のVRAM使用率を正確に計算
    /// </summary>
    public async Task<double> CalculateVramUsagePercentAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
            
            if (!metrics.GpuMemoryUsageMB.HasValue)
            {
                return 0.0;
            }

            var totalCapacity = await DetectVramCapacityAsync(cancellationToken).ConfigureAwait(false);
            var usagePercent = (double)metrics.GpuMemoryUsageMB.Value / totalCapacity * 100.0;
            
            return Math.Min(100.0, Math.Max(0.0, usagePercent));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [VRAM] 使用率計算エラー、デフォルト値使用");
            return 0.0;
        }
    }

    /// <summary>
    /// VRAM容量情報の詳細取得
    /// </summary>
    public async Task<VramCapacityInfo> GetVramCapacityInfoAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var totalCapacity = await DetectVramCapacityAsync(cancellationToken).ConfigureAwait(false);
            var usagePercent = await CalculateVramUsagePercentAsync(cancellationToken).ConfigureAwait(false);
            var usedCapacity = (long)(totalCapacity * usagePercent / 100.0);
            var availableCapacity = totalCapacity - usedCapacity;

            return new VramCapacityInfo(
                TotalCapacityMB: totalCapacity,
                UsedCapacityMB: usedCapacity,
                AvailableCapacityMB: availableCapacity,
                UsagePercent: usagePercent,
                DetectionMethod: _cachedVramCapacityMB.HasValue ? "Cached" : "Live",
                LastDetectionTime: _lastDetectionTime,
                IsCacheValid: DateTime.UtcNow - _lastDetectionTime < _cacheValidityDuration
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [VRAM] 容量情報取得エラー");
            var fallback = ResourceManagementConstants.Fallback.DefaultVramInfo;
            return new VramCapacityInfo(fallback.Total, fallback.Used, fallback.Available, fallback.UsagePercent, "Fallback", DateTime.MinValue, false);
        }
    }

    private async Task<long> PerformVramDetectionAsync(CancellationToken cancellationToken)
    {
        // 方法1: IGpuEnvironmentDetectorを使用
        if (_gpuEnvironmentDetector != null)
        {
            var gpuInfo = await _gpuEnvironmentDetector.DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
            if (gpuInfo?.AvailableMemoryMB > 0)
            {
                _logger.LogDebug("🎯 [VRAM] IGpuEnvironmentDetector検出成功: {CapacityMB}MB", 
                    gpuInfo.AvailableMemoryMB);
                return gpuInfo.AvailableMemoryMB;
            }
        }

        // 方法2: リソースモニターからの推定
        var estimatedCapacity = await EstimateVramFromResourceMonitorAsync(cancellationToken).ConfigureAwait(false);
        if (estimatedCapacity > 0)
        {
            _logger.LogDebug("📊 [VRAM] リソース監視による推定成功: {CapacityMB}MB", estimatedCapacity);
            return estimatedCapacity;
        }

        // 方法3: フォールバック
        _logger.LogWarning("⚠️ [VRAM] 動的検出失敗、フォールバック値使用");
        return GetFallbackVramCapacity();
    }

    private async Task<long> EstimateVramFromResourceMonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            // 複数回測定して最大値を取得（VRAM総量推定）
            long maxObservedUsage = 0;
            for (int i = 0; i < 3; i++)
            {
                var metrics = await _resourceMonitor.GetCurrentMetricsAsync(cancellationToken).ConfigureAwait(false);
                if (metrics.GpuMemoryUsageMB.HasValue && metrics.GpuMemoryUsageMB.Value > maxObservedUsage)
                {
                    maxObservedUsage = metrics.GpuMemoryUsageMB.Value;
                }
                
                if (i < 2) // 最後の繰り返し以外で待機
                    await Task.Delay(ResourceManagementConstants.Timing.DefaultDelayMs, cancellationToken).ConfigureAwait(false);
            }

            if (maxObservedUsage > 0)
            {
                // 観測した使用量から総容量を推定
                // 使用量が総容量の10-90%の範囲内と仮定
                return EstimateTotalFromUsage(maxObservedUsage);
            }

            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "📊 [VRAM] リソース監視による推定失敗");
            return 0;
        }
    }

    private static long EstimateTotalFromUsage(long observedUsageMB)
    {
        // 一般的なVRAMサイズに基づいて推定
        foreach (var commonSize in CommonVramSizes)
        {
            // 使用量が総容量の10%-90%の範囲にある場合、その容量を採用
            var minThreshold = commonSize * ResourceManagementConstants.Vram.MinUsagePercentForEstimation / 100.0;
            var maxThreshold = commonSize * ResourceManagementConstants.Vram.MaxUsagePercentForEstimation / 100.0;
            if (observedUsageMB >= minThreshold && observedUsageMB <= maxThreshold)
            {
                return commonSize;
            }
        }

        // 適切なサイズが見つからない場合、使用量から推定
        // 使用量が総容量の50%程度と仮定
        var estimatedTotal = observedUsageMB * 2;
        
        // 最も近い一般的なサイズに丸める
        foreach (var commonSize in CommonVramSizes)
        {
            if (estimatedTotal <= commonSize)
            {
                return commonSize;
            }
        }

        return CommonVramSizes[^1]; // 最大サイズを返す
    }

    private static long GetFallbackVramCapacity()
    {
        // 現代的な最も一般的な容量（8GB）をフォールバックとする
        return ResourceManagementConstants.Vram.DefaultCapacityMB;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _logger.LogDebug("🔄 [VRAM] 動的VRAM容量検出システム終了");
    }
}

/// <summary>
/// VRAM容量情報
/// </summary>
public sealed record VramCapacityInfo(
    long TotalCapacityMB,
    long UsedCapacityMB,
    long AvailableCapacityMB,
    double UsagePercent,
    string DetectionMethod,
    DateTime LastDetectionTime,
    bool IsCacheValid
)
{
    /// <summary>
    /// VRAM圧迫度レベル計算
    /// </summary>
    public VramPressureLevel GetPressureLevel() => UsagePercent switch
    {
        < 40 => VramPressureLevel.Low,
        < 60 => VramPressureLevel.Moderate,
        < 75 => VramPressureLevel.High,
        < 90 => VramPressureLevel.Critical,
        _ => VramPressureLevel.Emergency
    };
}