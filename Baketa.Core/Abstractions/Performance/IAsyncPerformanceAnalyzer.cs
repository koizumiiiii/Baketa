using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Performance;

/// <summary>
/// 非同期処理のパフォーマンス分析インターフェース
/// </summary>
public interface IAsyncPerformanceAnalyzer
{
    /// <summary>
    /// 非同期操作の実行時間を測定
    /// </summary>
    Task<PerformanceMeasurement> MeasureAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 並列操作のパフォーマンスを測定
    /// </summary>
    Task<ParallelPerformanceMeasurement> MeasureParallelAsync<T>(
        IEnumerable<Func<CancellationToken, Task<T>>> operations,
        string operationGroupName,
        int maxDegreeOfParallelism = -1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のパフォーマンス統計を取得
    /// </summary>
    AsyncPerformanceStatistics GetStatistics();

    /// <summary>
    /// 統計をクリア
    /// </summary>
    void ClearStatistics();
}

/// <summary>
/// パフォーマンス測定結果
/// </summary>
public record PerformanceMeasurement
{
    public string OperationName { get; init; } = string.Empty;
    public TimeSpan ExecutionTime { get; init; }
    public DateTime StartTime { get; init; }
    public DateTime EndTime { get; init; }
    public bool IsSuccessful { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object> Metadata { get; init; } = [];
}

/// <summary>
/// 並列パフォーマンス測定結果
/// </summary>
public class ParallelPerformanceMeasurement
{
    public string OperationGroupName { get; init; } = string.Empty;
    public TimeSpan TotalExecutionTime { get; init; }
    public TimeSpan AverageExecutionTime { get; init; }
    public TimeSpan MaxExecutionTime { get; init; }
    public TimeSpan MinExecutionTime { get; init; }
    public int TotalOperations { get; init; }
    public int SuccessfulOperations { get; init; }
    public int FailedOperations { get; init; }
    public int ActualDegreeOfParallelism { get; init; }
    public double Throughput { get; init; } // operations per second
    public List<PerformanceMeasurement> IndividualMeasurements { get; init; } = [];
}

/// <summary>
/// 非同期パフォーマンス統計
/// </summary>
public class AsyncPerformanceStatistics
{
    public int TotalOperations { get; init; }
    public int SuccessfulOperations { get; init; }
    public int FailedOperations { get; init; }
    public TimeSpan TotalExecutionTime { get; init; }
    public TimeSpan AverageExecutionTime { get; init; }
    public double SuccessRate => TotalOperations > 0 ? (double)SuccessfulOperations / TotalOperations : 0.0;
    public Dictionary<string, OperationStatistics> OperationStats { get; init; } = [];
}

/// <summary>
/// 操作別統計
/// </summary>
public class OperationStatistics
{
    public string OperationName { get; init; } = string.Empty;
    public int Count { get; init; }
    public TimeSpan TotalTime { get; init; }
    public TimeSpan AverageTime { get; init; }
    public TimeSpan MinTime { get; init; }
    public TimeSpan MaxTime { get; init; }
    public int SuccessCount { get; init; }
    public int FailureCount { get; init; }
    public double SuccessRate => Count > 0 ? (double)SuccessCount / Count : 0.0;
}