using System;

namespace Baketa.Core.Abstractions.ErrorHandling;

/// <summary>
/// エラー統計情報を表します。
/// </summary>
public class ErrorStatistics
{
    /// <summary>
    /// 総エラー数
    /// </summary>
    public int TotalErrors { get; set; }

    /// <summary>
    /// 重大エラー数
    /// </summary>
    public int CriticalErrors { get; set; }

    /// <summary>
    /// 回復可能エラー数
    /// </summary>
    public int RecoverableErrors { get; set; }

    /// <summary>
    /// 回復不可能エラー数
    /// </summary>
    public int UnrecoverableErrors { get; set; }

    /// <summary>
    /// 最後のエラー発生時刻
    /// </summary>
    public DateTime? LastErrorTime { get; set; }

    /// <summary>
    /// 統計開始時刻
    /// </summary>
    public DateTime StartTime { get; set; } = DateTime.Now;
}
