using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Baketa.Core.Abstractions.Memory;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Memory.Pools;

/// <summary>
/// OpenCV Mat用のオブジェクトプール（OpenCvSharp.Mat対応）
/// </summary>
public sealed class OpenCvMatPool : IObjectPool<IMatWrapper>
{
    private readonly ILogger<OpenCvMatPool> _logger;
    private readonly ConcurrentQueue<PooledMatItem> _pool = new();
    private readonly object _statsLock = new();
    private readonly int _maxCapacity;
    private volatile bool _disposed;

    // パフォーマンス測定用
    private readonly Stopwatch _getTimeWatch = new();
    private readonly Stopwatch _returnTimeWatch = new();
    private long _totalGetTime;
    private long _totalReturnTime;

    public OpenCvMatPool(ILogger<OpenCvMatPool> logger, int maxCapacity = 30)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxCapacity = maxCapacity;
        Statistics.MaxCapacity = maxCapacity;

        _logger.LogInformation("🖼️ OpenCvMatPool initialized with capacity: {MaxCapacity}", maxCapacity);
    }

    public ObjectPoolStatistics Statistics { get; } = new();

    public IMatWrapper Acquire()
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

            _logger.LogDebug("📤 Mat retrieved from pool: Size={Width}x{Height}, Type={MatType}, PoolHit=true",
                pooledItem.Mat.Width, pooledItem.Mat.Height, pooledItem.MatType);

            return pooledItem.Mat;
        }

        // プールにない場合は新規作成
        var newMat = CreateNewMat(640, 480, MatType.Cv8UC3); // デフォルトサイズ

        _getTimeWatch.Stop();
        Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }

        _logger.LogDebug("🆕 New Mat created: Size={Width}x{Height}, Type={Type}, PoolHit=false",
            newMat.Width, newMat.Height, MatType.Cv8UC3);

        return newMat;
    }

    /// <summary>
    /// 指定されたサイズと型のMatを取得
    /// </summary>
    public IMatWrapper AcquireWithSize(int width, int height, MatType matType)
    {
        ThrowIfDisposed();

        _getTimeWatch.Restart();

        lock (_statsLock)
        {
            Statistics.TotalGets++;
        }

        // 互換性のあるMatをプールから探す
        var incompatibleMats = new System.Collections.Generic.List<PooledMatItem>();
        while (_pool.TryDequeue(out var item))
        {
            if (IsCompatible(item, width, height, matType))
            {
                _getTimeWatch.Stop();
                Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

                lock (_statsLock)
                {
                    Statistics.PooledCount--;
                }

                // 互換性のないMatはプールに戻す
                foreach (var incompatibleItem in incompatibleMats)
                {
                    _pool.Enqueue(incompatibleItem);
                }

                _logger.LogDebug("📤 Compatible Mat retrieved from pool: Requested={Width}x{Height}:{Type}, Found={ActualWidth}x{ActualHeight}:{ActualType}",
                    width, height, matType, item.Mat.Width, item.Mat.Height, item.MatType);

                return item.Mat;
            }
            incompatibleMats.Add(item);
        }

        // 互換性のないMatをプールに戻す
        foreach (var item in incompatibleMats)
        {
            _pool.Enqueue(item);
        }

        // 互換性のあるMatがない場合は新規作成
        var newMat = CreateNewMat(width, height, matType);

        _getTimeWatch.Stop();
        Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);

        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }

        _logger.LogDebug("🆕 New compatible Mat created: Size={Width}x{Height}, Type={Type}",
            width, height, matType);

        return newMat;
    }

    public void Release(IMatWrapper item)
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
                _logger.LogDebug("🗑️ OpenCvMatPool at capacity, disposing returned Mat: Size={Width}x{Height}",
                    item.Width, item.Height);

                item.Dispose();
                _returnTimeWatch.Stop();
                Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);
                return;
            }

            Statistics.PooledCount++;
        }

        var pooledItem = new PooledMatItem
        {
            Mat = item,
            MatType = EstimateMatType(item),
            ReturnedAt = DateTime.UtcNow
        };

        // Matをクリーンな状態にリセット
        item.SetTo(0); // すべてのピクセルを0で初期化

        _pool.Enqueue(pooledItem);

        _returnTimeWatch.Stop();
        Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);

        _logger.LogDebug("📥 Mat returned to pool: Size={Width}x{Height}, Type={Type}, PoolSize={PoolSize}",
            item.Width, item.Height, pooledItem.MatType, Statistics.PooledCount);
    }

    public void Clear()
    {
        _logger.LogInformation("🧹 Clearing OpenCvMatPool");

        var clearedCount = 0;
        while (_pool.TryDequeue(out var item))
        {
            item.Mat.Dispose();
            clearedCount++;
        }

        lock (_statsLock)
        {
            Statistics.PooledCount = 0;
            Statistics.Clear();
        }

        _totalGetTime = 0;
        _totalReturnTime = 0;

        _logger.LogInformation("✅ OpenCvMatPool cleared: {ClearedCount} Mats disposed", clearedCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        LogFinalStatistics();
        Clear();

        _logger.LogInformation("🏁 OpenCvMatPool disposed");
    }

    private void LogFinalStatistics()
    {
        var avgGetTime = Statistics.TotalGets > 0 ?
            new TimeSpan(_totalGetTime / Statistics.TotalGets).TotalMicroseconds : 0;

        var avgReturnTime = Statistics.TotalReturns > 0 ?
            new TimeSpan(_totalReturnTime / Statistics.TotalReturns).TotalMicroseconds : 0;

        _logger.LogInformation("📊 OpenCvMatPool Final Statistics:\n" +
            "  📈 Pool Efficiency: HitRate={HitRate:P1}, ReturnRate={ReturnRate:P1}\n" +
            "  🔢 Operations: Gets={Gets}, Returns={Returns}, Creates={Creates}\n" +
            "  ⚡ Performance: AvgGetTime={AvgGetTime:F1}μs, AvgReturnTime={AvgReturnTime:F1}μs\n" +
            "  💾 Memory Savings: {MemorySavings} Mat creations avoided\n" +
            "  🎯 Typical Mat Size: Large Mats (>500KB) benefit most from pooling",
            Statistics.HitRate, Statistics.ReturnRate,
            Statistics.TotalGets, Statistics.TotalReturns, Statistics.TotalCreations,
            avgGetTime, avgReturnTime,
            Statistics.TotalGets - Statistics.TotalCreations);
    }

    private static bool IsCompatible(PooledMatItem item, int width, int height, MatType matType)
    {
        return item.Mat.Width == width &&
               item.Mat.Height == height &&
               item.MatType == matType;
    }

    private static MatType EstimateMatType(IMatWrapper _)
    {
        // 実際の実装では、IMatWrapperから適切にMatTypeを取得する必要があります
        return MatType.Cv8UC3; // デフォルト値
    }

    private static IMatWrapper CreateNewMat(int width, int height, MatType matType)
    {
        // 実際の実装では、指定されたパラメータでIMatWrapperを作成する必要があります
        throw new NotImplementedException("CreateNewMat method needs actual IMatWrapper factory implementation");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class PooledMatItem
    {
        public required IMatWrapper Mat { get; init; }
        public required MatType MatType { get; init; }
        public DateTime ReturnedAt { get; init; }
    }
}

/// <summary>
/// OpenCV Matのラッパーインターフェース（OpenCvSharp依存を抽象化）
/// </summary>
public interface IMatWrapper : IDisposable
{
    int Width { get; }
    int Height { get; }
    void SetTo(double value);
}

/// <summary>
/// OpenCV Mat型の定義（OpenCvSharpのMatType相当）
/// </summary>
public enum MatType
{
    Cv8UC1,   // 8-bit unsigned single channel
    Cv8UC3,   // 8-bit unsigned 3 channels (BGR)
    Cv8UC4,   // 8-bit unsigned 4 channels (BGRA)
    Cv32FC1,  // 32-bit float single channel
    Cv32FC3   // 32-bit float 3 channels
}
