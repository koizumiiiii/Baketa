using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Patterns;

/// <summary>
/// サーキットブレーカーパターンのインターフェース
/// Phase2: C#側サーキットブレーカー実装
/// </summary>
/// <typeparam name="T">実行結果の型</typeparam>
public interface ICircuitBreaker<T>
{
    /// <summary>
    /// サーキットブレーカーの現在の状態
    /// </summary>
    CircuitBreakerState State { get; }
    
    /// <summary>
    /// サーキットが開いているかどうか
    /// </summary>
    bool IsCircuitOpen { get; }
    
    /// <summary>
    /// 失敗カウント
    /// </summary>
    int FailureCount { get; }
    
    /// <summary>
    /// 最後の失敗時刻
    /// </summary>
    DateTime? LastFailureTime { get; }
    
    /// <summary>
    /// 操作を実行し、サーキットブレーカーロジックを適用
    /// </summary>
    /// <param name="operation">実行する操作</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>操作の結果</returns>
    Task<T> ExecuteAsync(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// サーキットを手動でリセット
    /// </summary>
    void Reset();
    
    /// <summary>
    /// サーキットブレーカーの統計情報を取得
    /// </summary>
    CircuitBreakerStats GetStats();
}

/// <summary>
/// サーキットブレーカーの状態
/// </summary>
public enum CircuitBreakerState
{
    /// <summary>
    /// 閉じている（正常動作）
    /// </summary>
    Closed,
    
    /// <summary>
    /// 開いている（障害状態）
    /// </summary>
    Open,
    
    /// <summary>
    /// 半開き（復旧テスト中）
    /// </summary>
    HalfOpen
}

/// <summary>
/// サーキットブレーカー統計情報
/// </summary>
public record CircuitBreakerStats
{
    /// <summary>
    /// 総実行回数
    /// </summary>
    public long TotalExecutions { get; init; }
    
    /// <summary>
    /// 総失敗回数
    /// </summary>
    public long TotalFailures { get; init; }
    
    /// <summary>
    /// 連続失敗回数
    /// </summary>
    public int ConsecutiveFailures { get; init; }
    
    /// <summary>
    /// 失敗率
    /// </summary>
    public double FailureRate => TotalExecutions > 0 ? (double)TotalFailures / TotalExecutions : 0.0;
    
    /// <summary>
    /// 最後の成功時刻
    /// </summary>
    public DateTime? LastSuccessTime { get; init; }
    
    /// <summary>
    /// 最後の失敗時刻
    /// </summary>
    public DateTime? LastFailureTime { get; init; }
    
    /// <summary>
    /// サーキットが開いている時間
    /// </summary>
    public TimeSpan? CircuitOpenDuration { get; init; }
    
    /// <summary>
    /// サーキットブレーカーがオープンした回数
    /// </summary>
    public long CircuitOpenCount { get; init; }

    /// <summary>
    /// 🆕 フォールバック実行回数 - Gemini推奨統計機能
    /// </summary>
    public long FallbackExecutions { get; init; }
}