using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Memory.Pools;

/// <summary>
/// 汎用OCR結果用の高性能オブジェクトプール実装
/// </summary>
public sealed class OcrResultPool<T> : IOcrResultPool<T> where T : class
{
    private readonly ILogger<OcrResultPool<T>> _logger;
    private readonly ConcurrentQueue<T> _pool = new();
    private readonly object _statsLock = new();
    private readonly int _maxCapacity;
    private volatile bool _disposed;

    // パフォーマンス測定用
    private readonly Stopwatch _getTimeWatch = new();
    private readonly Stopwatch _returnTimeWatch = new();
    private long _totalGetTime;
    private long _totalReturnTime;

    public OcrResultPool(ILogger<OcrResultPool<T>> logger, int maxCapacity = 100)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxCapacity = maxCapacity;
        Statistics.MaxCapacity = maxCapacity;
        
        _logger.LogInformation("📄 OcrResultPool initialized with capacity: {MaxCapacity}", maxCapacity);
    }

    public ObjectPoolStatistics Statistics { get; } = new();

    public T Acquire()
    {
        return AcquireWithCapacity(10); // デフォルトで10個のテキスト領域を想定
    }

    public T AcquireWithCapacity(int estimatedRegionCount)
    {
        ThrowIfDisposed();
        
        _getTimeWatch.Restart();
        
        lock (_statsLock)
        {
            Statistics.TotalGets++;
        }

        if (_pool.TryDequeue(out var pooledResult))
        {
            // プールされた結果をリセット（型依存処理は呼び出し側で）
            
            _getTimeWatch.Stop();
            Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);
            
            lock (_statsLock)
            {
                Statistics.PooledCount--;
            }
            
            _logger.LogDebug("📤 OcrResult retrieved from pool: EstimatedRegions={EstimatedRegions}, PoolHit=true", 
                estimatedRegionCount);
            
            return pooledResult;
        }

        // プールにない場合は新規作成（ファクトリーが必要）
        var newResult = CreateNewResult(estimatedRegionCount);
        
        _getTimeWatch.Stop();
        Interlocked.Add(ref _totalGetTime, _getTimeWatch.ElapsedTicks);
        
        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }
        
        _logger.LogDebug("🆕 New OcrResult created: EstimatedRegions={EstimatedRegions}, PoolHit=false", 
            estimatedRegionCount);
        
        return newResult;
    }

    public void Release(T item)
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
                _logger.LogDebug("🗑️ OcrResultPool at capacity, disposing returned item");
                
                // アイテムがIDisposableを実装している場合のみDispose
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                
                _returnTimeWatch.Stop();
                Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);
                return;
            }
            
            Statistics.PooledCount++;
        }

        // オブジェクトをプールに返却
        _pool.Enqueue(item);
        
        _returnTimeWatch.Stop();
        Interlocked.Add(ref _totalReturnTime, _returnTimeWatch.ElapsedTicks);
        
        _logger.LogDebug("📥 OcrResult returned to pool: PoolSize={PoolSize}", Statistics.PooledCount);
    }

    public void Clear()
    {
        _logger.LogInformation("🧹 Clearing OcrResultPool");
        
        var clearedCount = 0;
        while (_pool.TryDequeue(out var item))
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
            clearedCount++;
        }
        
        lock (_statsLock)
        {
            Statistics.PooledCount = 0;
            Statistics.Clear();
        }
        
        _totalGetTime = 0;
        _totalReturnTime = 0;
        
        _logger.LogInformation("✅ OcrResultPool cleared: {ClearedCount} results disposed", clearedCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        LogFinalStatistics();
        Clear();
        
        _logger.LogInformation("🏁 OcrResultPool disposed");
    }

    private void LogFinalStatistics()
    {
        var avgGetTime = Statistics.TotalGets > 0 ? 
            new TimeSpan(_totalGetTime / Statistics.TotalGets).TotalMicroseconds : 0;
        
        var avgReturnTime = Statistics.TotalReturns > 0 ? 
            new TimeSpan(_totalReturnTime / Statistics.TotalReturns).TotalMicroseconds : 0;

        _logger.LogInformation("📊 OcrResultPool Final Statistics:\n" +
            "  📈 Pool Efficiency: HitRate={HitRate:P1}, ReturnRate={ReturnRate:P1}\n" +
            "  🔢 Operations: Gets={Gets}, Returns={Returns}, Creates={Creates}\n" +
            "  ⚡ Performance: AvgGetTime={AvgGetTime:F1}μs, AvgReturnTime={AvgReturnTime:F1}μs\n" +
            "  💾 Memory Savings: {MemorySavings} object creations avoided",
            Statistics.HitRate, Statistics.ReturnRate,
            Statistics.TotalGets, Statistics.TotalReturns, Statistics.TotalCreations,
            avgGetTime, avgReturnTime,
            Statistics.TotalGets - Statistics.TotalCreations);
    }

    private static T CreateNewResult(int estimatedRegionCount)
    {
        // 実際の実装では、適切なTインスタンスを作成する必要があります
        // ここではスタブ実装を返します
        throw new NotImplementedException("CreateNewResult method needs actual factory implementation");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}

/// <summary>
/// TextRegion用の高性能オブジェクトプール実装
/// </summary>
public sealed class TextRegionPool : ITextRegionPool
{
    private readonly ILogger<TextRegionPool> _logger;
    private readonly ConcurrentQueue<TextRegion> _pool = new();
    private readonly object _statsLock = new();
    private readonly int _maxCapacity;
    private volatile bool _disposed;

    public TextRegionPool(ILogger<TextRegionPool> logger, int maxCapacity = 200)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _maxCapacity = maxCapacity;
        Statistics.MaxCapacity = maxCapacity;
        
        _logger.LogInformation("📝 TextRegionPool initialized with capacity: {MaxCapacity}", maxCapacity);
    }

    public ObjectPoolStatistics Statistics { get; } = new();

    public TextRegion Acquire()
    {
        return AcquireInitialized(new Baketa.Core.Abstractions.Memory.Rectangle(0, 0, 0, 0), string.Empty, 0.0);
    }

    public TextRegion AcquireInitialized(Baketa.Core.Abstractions.Memory.Rectangle boundingBox, string text, double confidence)
    {
        ThrowIfDisposed();
        
        lock (_statsLock)
        {
            Statistics.TotalGets++;
        }

        if (_pool.TryDequeue(out var pooledRegion))
        {
            // プールされたTextRegionを再初期化
            pooledRegion.Reset(boundingBox, text, confidence);
            
            lock (_statsLock)
            {
                Statistics.PooledCount--;
            }
            
            _logger.LogDebug("📤 TextRegion retrieved from pool: Text='{Text}', Confidence={Confidence:F2}", 
                text, confidence);
            
            return pooledRegion;
        }

        // プールにない場合は新規作成
        var newRegion = CreateNewTextRegion(boundingBox, text, confidence);
        
        lock (_statsLock)
        {
            Statistics.TotalCreations++;
        }
        
        _logger.LogDebug("🆕 New TextRegion created: Text='{Text}', Confidence={Confidence:F2}", 
            text, confidence);
        
        return newRegion;
    }

    public void Release(TextRegion item)
    {
        if (item == null || _disposed)
            return;

        lock (_statsLock)
        {
            Statistics.TotalReturns++;
            
            if (Statistics.PooledCount >= _maxCapacity)
            {
                _logger.LogDebug("🗑️ TextRegionPool at capacity, disposing returned item");
                
                if (item is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                return;
            }
            
            Statistics.PooledCount++;
        }

        _pool.Enqueue(item);
        
        _logger.LogDebug("📥 TextRegion returned to pool: PoolSize={PoolSize}", Statistics.PooledCount);
    }

    public void Clear()
    {
        _logger.LogInformation("🧹 Clearing TextRegionPool");
        
        var clearedCount = 0;
        while (_pool.TryDequeue(out var item))
        {
            if (item is IDisposable disposable)
            {
                disposable.Dispose();
            }
            clearedCount++;
        }
        
        lock (_statsLock)
        {
            Statistics.PooledCount = 0;
            Statistics.Clear();
        }
        
        _logger.LogInformation("✅ TextRegionPool cleared: {ClearedCount} regions disposed", clearedCount);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        
        Clear();
        _logger.LogInformation("🏁 TextRegionPool disposed");
    }

    private static TextRegion CreateNewTextRegion(Baketa.Core.Abstractions.Memory.Rectangle boundingBox, string _, double confidence)
    {
        // System.Drawing.Rectangle への変換を行って TextRegion を作成
        var drawingRect = new System.Drawing.Rectangle(boundingBox.X, boundingBox.Y, boundingBox.Width, boundingBox.Height);
        var textRegion = new TextRegion(drawingRect, (float)confidence);
        return textRegion;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
