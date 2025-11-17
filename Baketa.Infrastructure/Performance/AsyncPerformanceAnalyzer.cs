using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Performance;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Performance;

/// <summary>
/// 非同期処理のパフォーマンス分析実装
/// </summary>
public sealed class AsyncPerformanceAnalyzer : IAsyncPerformanceAnalyzer
{
    private readonly ILogger<AsyncPerformanceAnalyzer> _logger;
    private readonly ConcurrentBag<PerformanceMeasurement> _measurements = [];
    private readonly ConcurrentDictionary<string, OperationCounter> _operationCounters = new();
    private volatile bool _disposed;

    public AsyncPerformanceAnalyzer(ILogger<AsyncPerformanceAnalyzer> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _logger.LogInformation("⚡ AsyncPerformanceAnalyzer initialized for performance monitoring");
    }

    public async Task<PerformanceMeasurement> MeasureAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrEmpty(operationName);

        var stopwatch = Stopwatch.StartNew();
        var startTime = DateTime.UtcNow;
        var measurement = new PerformanceMeasurement
        {
            OperationName = operationName,
            StartTime = startTime
        };

        try
        {
            _logger.LogDebug("⏱️ Starting measurement for operation: {OperationName}", operationName);

            await operation(cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();
            var endTime = DateTime.UtcNow;

            measurement = measurement with
            {
                ExecutionTime = stopwatch.Elapsed,
                EndTime = endTime,
                IsSuccessful = true
            };

            // カウンターを更新
            UpdateOperationCounter(operationName, stopwatch.Elapsed, true);

            _logger.LogDebug("✅ Operation completed successfully: {OperationName} in {ExecutionTime:F2}ms",
                operationName, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException oce)
        {
            stopwatch.Stop();
            var endTime = DateTime.UtcNow;

            measurement = measurement with
            {
                ExecutionTime = stopwatch.Elapsed,
                EndTime = endTime,
                IsSuccessful = false,
                ErrorMessage = "Operation Canceled"
            };

            // エラーカウンターを更新
            UpdateOperationCounter(operationName, stopwatch.Elapsed, false);

            // 🔥 UltraThink Phase 9.1: 確実なログ出力（Console + ファイル + Logger）
            var cancelMessage = $"⏸️ [PERF_CANCEL] Operation '{operationName}' was canceled after {stopwatch.Elapsed.TotalMilliseconds:F2}ms";
            Console.WriteLine($"🚨🚨🚨 {cancelMessage}");

#if DEBUG
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_performance.txt");
                System.IO.File.AppendAllText(debugPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {cancelMessage}{Environment.NewLine}" +
                    $"  Exception: {oce.GetType().Name}{Environment.NewLine}" +
                    $"  Message: {oce.Message}{Environment.NewLine}" +
                    $"  StackTrace: {oce.StackTrace}{Environment.NewLine}");
            }
            catch { /* ファイル書き込み失敗を無視 */ }
#endif

            _logger.LogInformation(oce, "⏸️ Operation '{OperationName}' was canceled after {ExecutionTime:F2}ms",
                operationName, stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            var endTime = DateTime.UtcNow;

            measurement = measurement with
            {
                ExecutionTime = stopwatch.Elapsed,
                EndTime = endTime,
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };

            // エラーカウンターを更新
            UpdateOperationCounter(operationName, stopwatch.Elapsed, false);

            // 🔥 UltraThink Phase 9.1: 確実なログ出力（Console + ファイル + Logger）
            var errorMessage = $"❌ [PERF_ERROR] Operation '{operationName}' failed after {stopwatch.Elapsed.TotalMilliseconds:F2}ms - {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine($"🚨🚨🚨 {errorMessage}");

#if DEBUG
            try
            {
                var debugPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_performance.txt");
                System.IO.File.AppendAllText(debugPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {errorMessage}{Environment.NewLine}" +
                    $"  Exception: {ex.GetType().FullName}{Environment.NewLine}" +
                    $"  Message: {ex.Message}{Environment.NewLine}" +
                    $"  StackTrace: {ex.StackTrace}{Environment.NewLine}");
            }
            catch { /* ファイル書き込み失敗を無視 */ }
#endif

            _logger.LogWarning(ex, "❌ Operation failed: {OperationName} after {ExecutionTime:F2}ms",
                operationName, stopwatch.Elapsed.TotalMilliseconds);
        }

        _measurements.Add(measurement);
        return measurement;
    }

    public async Task<ParallelPerformanceMeasurement> MeasureParallelAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        string operationGroupName,
        int maxDegreeOfParallelism = -1,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentException.ThrowIfNullOrEmpty(operationGroupName);

        var operationList = operations.ToList();
        if (operationList.Count == 0)
        {
            return new ParallelPerformanceMeasurement
            {
                OperationGroupName = operationGroupName
            };
        }

        var totalStopwatch = Stopwatch.StartNew();
        var measurements = new List<PerformanceMeasurement>();
        var semaphore = maxDegreeOfParallelism > 0
            ? new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism)
            : null;

        _logger.LogInformation("🚀 Starting parallel operations: {OperationGroup} with {Count} operations, MaxParallelism={MaxParallelism}",
            operationGroupName, operationList.Count, maxDegreeOfParallelism);

        try
        {
            var tasks = operationList.Select(async (operation, index) =>
            {
                if (semaphore != null)
                {
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                }

                try
                {
                    return await MeasureAsync(async ct =>
                    {
                        var result = await operation(ct).ConfigureAwait(false);
                        return result;
                    }, $"{operationGroupName}[{index}]", cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    semaphore?.Release();
                }
            });

            measurements.AddRange(await Task.WhenAll(tasks).ConfigureAwait(false));
        }
        finally
        {
            semaphore?.Dispose();
            totalStopwatch.Stop();
        }

        // 統計計算
        var successfulMeasurements = measurements.Where(m => m.IsSuccessful).ToList();
        var executionTimes = successfulMeasurements.Select(m => m.ExecutionTime).ToList();

        var parallelMeasurement = new ParallelPerformanceMeasurement
        {
            OperationGroupName = operationGroupName,
            TotalExecutionTime = totalStopwatch.Elapsed,
            AverageExecutionTime = executionTimes.Count > 0
                ? TimeSpan.FromTicks((long)executionTimes.Average(t => t.Ticks))
                : TimeSpan.Zero,
            MaxExecutionTime = executionTimes.Count > 0 ? executionTimes.Max() : TimeSpan.Zero,
            MinExecutionTime = executionTimes.Count > 0 ? executionTimes.Min() : TimeSpan.Zero,
            TotalOperations = measurements.Count,
            SuccessfulOperations = successfulMeasurements.Count,
            FailedOperations = measurements.Count - successfulMeasurements.Count,
            ActualDegreeOfParallelism = CalculateActualParallelism(measurements),
            Throughput = totalStopwatch.Elapsed.TotalSeconds > 0
                ? successfulMeasurements.Count / totalStopwatch.Elapsed.TotalSeconds
                : 0,
            IndividualMeasurements = measurements
        };

        _logger.LogInformation("📊 Parallel operations completed: {OperationGroup} - " +
            "Total={Total}, Successful={Successful}, Failed={Failed}, " +
            "TotalTime={TotalTime:F2}ms, AvgTime={AvgTime:F2}ms, Throughput={Throughput:F1}ops/s",
            operationGroupName, parallelMeasurement.TotalOperations,
            parallelMeasurement.SuccessfulOperations, parallelMeasurement.FailedOperations,
            parallelMeasurement.TotalExecutionTime.TotalMilliseconds,
            parallelMeasurement.AverageExecutionTime.TotalMilliseconds,
            parallelMeasurement.Throughput);

        return parallelMeasurement;
    }

    public AsyncPerformanceStatistics GetStatistics()
    {
        ThrowIfDisposed();

        var measurementsList = _measurements.ToList();
        var operationGroups = measurementsList.GroupBy(m => m.OperationName);

        var operationStats = operationGroups.ToDictionary(
            group => group.Key,
            group =>
            {
                var measurements = group.ToList();
                var executionTimes = measurements.Select(m => m.ExecutionTime).ToList();
                var successfulMeasurements = measurements.Where(m => m.IsSuccessful).ToList();

                return new OperationStatistics
                {
                    OperationName = group.Key,
                    Count = measurements.Count,
                    TotalTime = TimeSpan.FromTicks(executionTimes.Sum(t => t.Ticks)),
                    AverageTime = executionTimes.Count > 0
                        ? TimeSpan.FromTicks((long)executionTimes.Average(t => t.Ticks))
                        : TimeSpan.Zero,
                    MinTime = executionTimes.Count > 0 ? executionTimes.Min() : TimeSpan.Zero,
                    MaxTime = executionTimes.Count > 0 ? executionTimes.Max() : TimeSpan.Zero,
                    SuccessCount = successfulMeasurements.Count,
                    FailureCount = measurements.Count - successfulMeasurements.Count
                };
            });

        var statistics = new AsyncPerformanceStatistics
        {
            TotalOperations = measurementsList.Count,
            SuccessfulOperations = measurementsList.Count(m => m.IsSuccessful),
            FailedOperations = measurementsList.Count(m => !m.IsSuccessful),
            TotalExecutionTime = TimeSpan.FromTicks(measurementsList.Sum(m => m.ExecutionTime.Ticks)),
            AverageExecutionTime = measurementsList.Count > 0
                ? TimeSpan.FromTicks((long)measurementsList.Average(m => m.ExecutionTime.Ticks))
                : TimeSpan.Zero,
            OperationStats = operationStats
        };

        _logger.LogInformation("📈 Performance Statistics: Operations={Total}, Success={Success}({SuccessRate:P1}), " +
            "AvgTime={AvgTime:F2}ms, TotalTime={TotalTime:F2}s",
            statistics.TotalOperations, statistics.SuccessfulOperations, statistics.SuccessRate,
            statistics.AverageExecutionTime.TotalMilliseconds, statistics.TotalExecutionTime.TotalSeconds);

        return statistics;
    }

    public void ClearStatistics()
    {
        ThrowIfDisposed();

        _measurements.Clear();
        _operationCounters.Clear();

        _logger.LogInformation("🧹 Performance statistics cleared");
    }

    private void UpdateOperationCounter(string operationName, TimeSpan executionTime, bool isSuccess)
    {
        _operationCounters.AddOrUpdate(
            operationName,
            new OperationCounter
            {
                Count = 1,
                TotalTime = executionTime,
                SuccessCount = isSuccess ? 1 : 0,
                FailureCount = isSuccess ? 0 : 1
            },
            (key, existing) => new OperationCounter
            {
                Count = existing.Count + 1,
                TotalTime = existing.TotalTime + executionTime,
                SuccessCount = existing.SuccessCount + (isSuccess ? 1 : 0),
                FailureCount = existing.FailureCount + (isSuccess ? 0 : 1)
            });
    }

    private static int CalculateActualParallelism(List<PerformanceMeasurement> measurements)
    {
        if (measurements.Count == 0) return 0;

        // 重複する時間帯を計算して実際の並列度を推定
        var timeSlots = measurements
            .SelectMany(m => new[]
            {
                new { Time = m.StartTime, Type = "Start" },
                new { Time = m.EndTime, Type = "End" }
            })
            .OrderBy(slot => slot.Time)
            .ToList();

        var maxConcurrent = 0;
        var currentConcurrent = 0;

        foreach (var slot in timeSlots)
        {
            if (slot.Type == "Start")
            {
                currentConcurrent++;
                maxConcurrent = Math.Max(maxConcurrent, currentConcurrent);
            }
            else
            {
                currentConcurrent--;
            }
        }

        return maxConcurrent;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        _logger.LogInformation("🏁 AsyncPerformanceAnalyzer disposed");
    }

    private sealed class OperationCounter
    {
        public int Count { get; init; }
        public TimeSpan TotalTime { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
    }
}
