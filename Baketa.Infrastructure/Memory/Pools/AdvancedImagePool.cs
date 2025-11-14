using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Memory.Pools;

/// <summary>
/// IAdvancedImage用の高性能オブジェクトプール実装
/// </summary>
public sealed class AdvancedImagePool : IAdvancedImagePool
{
    private readonly ILogger<AdvancedImagePool> _logger;
    private readonly ConcurrentQueue<PooledImageItem> _pool = new();
    private readonly object _statsLock = new();
    private readonly int _maxCapacity;
    private volatile bool _disposed;

    // パフォーマンス測定用
    private readonly Stopwatch _getTimeWatch = new();
    private readonly Stopwatch _returnTimeWatch = new();
    private long _totalGetTime;
    private long _totalReturnTime;

    public AdvancedImagePool(ILogger<AdvancedImagePool> logger, int maxCapacity = 50)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxCapacity = maxCapacity;
        Statistics.MaxCapacity = maxCapacity;

        _logger.LogInformation("🏊‍♂️ AdvancedImagePool initialized with capacity: {MaxCapacity}", maxCapacity);
    }

    public ObjectPoolStatistics Statistics { get; } = new();

    public IAdvancedImage Acquire()
    {
        ThrowIfDisposed();

        _getTimeWatch.Restart();

        lock (_statsLock)
        {
            Statistics.TotalGets++;
        }

        if (_pool.TryDequeue(out var pooledItem))
        {
            _getTimeWatch.Stop();
            Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

            lock (_statsLock)
            {
                Statistics.PooledCount--;
            }

            _logger.LogDebug("📤 Image retrieved from pool: Size={Width}x{Height}, Format={Format}, PoolHit=true",
                pooledItem.Image.Width, pooledItem.Image.Height, pooledItem.PixelFormat);

            return pooledItem.Image;
        }

        // プールにない場合は新規作成
        var newImage = CreateNewImage(800, 600, PixelFormat.Bgra32); // デフォルトサイズ

        _getTimeWatch.Stop();
        Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }

        _logger.LogDebug("🆕 New image created: Size={Width}x{Height}, Format={Format}, PoolHit=false",
            newImage.Width, newImage.Height, PixelFormat.Bgra32);

        return newImage;
    }

    public IAdvancedImage AcquireImage(int width, int height, PixelFormat pixelFormat)
    {
        ThrowIfDisposed();

        _getTimeWatch.Restart();

        lock (_statsLock)
        {
            Statistics.TotalGets++;
        }

        // 互換性のある画像をプールから探す
        var compatibleImages = new List<PooledImageItem>();
        while (_pool.TryDequeue(out var item))
        {
            if (IsCompatible(item, width, height, pixelFormat))
            {
                _getTimeWatch.Stop();
                Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

                lock (_statsLock)
                {
                    Statistics.PooledCount--;
                }

                // 互換性のない画像はプールに戻す
                foreach (var incompatibleItem in compatibleImages)
                {
                    _pool.Enqueue(incompatibleItem);
                }

                _logger.LogDebug("📤 Compatible image retrieved from pool: Requested={Width}x{Height}:{Format}, Found={ActualWidth}x{ActualHeight}:{ActualFormat}",
                    width, height, pixelFormat, item.Image.Width, item.Image.Height, item.PixelFormat);

                return item.Image;
            }
            compatibleImages.Add(item);
        }

        // 互換性のない画像をプールに戻す
        foreach (var item in compatibleImages)
        {
            _pool.Enqueue(item);
        }

        // 互換性のある画像がない場合は新規作成
        var newImage = CreateNewImage(width, height, pixelFormat);

        _getTimeWatch.Stop();
        Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }

        _logger.LogDebug("🆕 New compatible image created: Size={Width}x{Height}, Format={Format}",
            width, height, pixelFormat);

        return newImage;
    }

    public IAdvancedImage GetCompatible(IAdvancedImage templateImage)
    {
        ArgumentNullException.ThrowIfNull(templateImage);

        // PixelFormatの推定（実際の実装では適切なマッピングが必要）
        var estimatedFormat = EstimatePixelFormat(templateImage);
        return AcquireImage(templateImage.Width, templateImage.Height, estimatedFormat);
    }

    public void Release(IAdvancedImage item)
    {
        if (item == null || _disposed)
            return;

        _returnTimeWatch.Restart();

        lock (_statsLock)
        {
            Statistics.TotalReturns++;

            // 容量チェック
            if (Statistics.PooledCount >= _maxCapacity)
            {
                _logger.LogDebug("🗑️ Pool at capacity, disposing returned image: Size={Width}x{Height}",
                    item.Width, item.Height);

                item.Dispose();
                _returnTimeWatch.Stop();
                Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);
                return;
            }

            Statistics.PooledCount++;
        }

        var pooledItem = new PooledImageItem
        {
            Image = item,
            PixelFormat = EstimatePixelFormat(item),
            ReturnedAt = DateTime.UtcNow
        };

        _pool.Enqueue(pooledItem);

        _returnTimeWatch.Stop();
        Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);

        _logger.LogDebug("📥 Image returned to pool: Size={Width}x{Height}, Format={Format}, PoolSize={PoolSize}",
            item.Width, item.Height, pooledItem.PixelFormat, Statistics.PooledCount);
    }

    public void Clear()
    {
        _logger.LogInformation("🧹 Clearing AdvancedImagePool");

        var clearedCount = 0;
        while (_pool.TryDequeue(out var item))
        {
            item.Image.Dispose();
            clearedCount++;
        }

        lock (_statsLock)
        {
            Statistics.PooledCount = 0;
            Statistics.Clear();
        }

        _totalGetTime = 0;
        _totalReturnTime = 0;

        _logger.LogInformation("✅ AdvancedImagePool cleared: {ClearedCount} images disposed", clearedCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        LogFinalStatistics();
        Clear();

        _logger.LogInformation("🏁 AdvancedImagePool disposed");
    }

    private void LogFinalStatistics()
    {
        var avgGetTime = Statistics.TotalGets > 0 ?
            new TimeSpan(_totalGetTime / Statistics.TotalGets).TotalMicroseconds : 0;

        var avgReturnTime = Statistics.TotalReturns > 0 ?
            new TimeSpan(_totalReturnTime / Statistics.TotalReturns).TotalMicroseconds : 0;

        _logger.LogInformation("📊 AdvancedImagePool Final Statistics:\n" +
            "  📈 Pool Efficiency: HitRate={HitRate:P1}, ReturnRate={ReturnRate:P1}\n" +
            "  🔢 Operations: Gets={Gets}, Returns={Returns}, Creates={Creates}\n" +
            "  ⚡ Performance: AvgGetTime={AvgGetTime:F1}μs, AvgReturnTime={AvgReturnTime:F1}μs\n" +
            "  💾 Memory Savings: {MemorySavings} object creations avoided",
            Statistics.HitRate, Statistics.ReturnRate,
            Statistics.TotalGets, Statistics.TotalReturns, Statistics.TotalCreations,
            avgGetTime, avgReturnTime,
            Statistics.TotalGets - Statistics.TotalCreations);
    }

    private static bool IsCompatible(PooledImageItem item, int width, int height, PixelFormat pixelFormat)
    {
        return item.Image.Width == width &&
               item.Image.Height == height &&
               item.PixelFormat == pixelFormat;
    }

    private static PixelFormat EstimatePixelFormat(IAdvancedImage _)
    {
        // 実際の実装では、IAdvancedImageから適切にピクセル形式を取得する必要があります
        // ここではデフォルト値を返します
        return PixelFormat.Bgra32;
    }

    private static IAdvancedImage CreateNewImage(int width, int height, PixelFormat pixelFormat)
    {
        // 実際の実装では、指定されたパラメータでIAdvancedImageを作成する必要があります
        // ここではスタブ実装を返します
        throw new NotImplementedException("CreateNewImage method needs actual IAdvancedImage factory implementation");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class PooledImageItem
    {
        public required IAdvancedImage Image { get; init; }
        public required PixelFormat PixelFormat { get; init; }
        public DateTime ReturnedAt { get; init; }
    }
}
