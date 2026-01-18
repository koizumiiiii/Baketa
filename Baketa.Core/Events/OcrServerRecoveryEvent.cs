using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// OCRサーバー復旧イベントの種類
/// </summary>
public enum OcrRecoveryAction
{
    /// <summary>
    /// サーバー再起動開始
    /// </summary>
    RestartStarted,

    /// <summary>
    /// サーバー再起動完了
    /// </summary>
    RestartCompleted,

    /// <summary>
    /// サーバー再起動失敗
    /// </summary>
    RestartFailed
}

/// <summary>
/// OCRサーバー復旧イベント
/// Issue #300: OCRサーバータイムアウト時のユーザー通知・自動復旧機能
/// </summary>
public sealed class OcrServerRecoveryEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public DateTime Timestamp { get; }

    /// <inheritdoc />
    public string Name => "OcrServerRecovery";

    /// <inheritdoc />
    public string Category => "Server";

    /// <summary>
    /// 復旧アクションの種類
    /// </summary>
    public OcrRecoveryAction Action { get; }

    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 連続失敗回数（RestartStarted時）
    /// </summary>
    public int ConsecutiveFailures { get; }

    /// <summary>
    /// 復旧試行回数
    /// </summary>
    public int RecoveryAttempt { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public OcrServerRecoveryEvent(
        OcrRecoveryAction action,
        string message,
        int consecutiveFailures = 0,
        int recoveryAttempt = 1)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        Action = action;
        Message = message;
        ConsecutiveFailures = consecutiveFailures;
        RecoveryAttempt = recoveryAttempt;
    }

    /// <summary>
    /// サーバー再起動開始イベントを作成
    /// </summary>
    public static OcrServerRecoveryEvent CreateRestartStarted(int consecutiveFailures, int recoveryAttempt = 1)
    {
        return new OcrServerRecoveryEvent(
            OcrRecoveryAction.RestartStarted,
            "文字認識エンジンを復旧中です。しばらくお待ちください...",
            consecutiveFailures,
            recoveryAttempt);
    }

    /// <summary>
    /// サーバー再起動完了イベントを作成
    /// </summary>
    public static OcrServerRecoveryEvent CreateRestartCompleted(int recoveryAttempt = 1)
    {
        return new OcrServerRecoveryEvent(
            OcrRecoveryAction.RestartCompleted,
            "文字認識エンジンが復旧しました",
            0,
            recoveryAttempt);
    }

    /// <summary>
    /// サーバー再起動失敗イベントを作成
    /// </summary>
    public static OcrServerRecoveryEvent CreateRestartFailed(int recoveryAttempt)
    {
        return new OcrServerRecoveryEvent(
            OcrRecoveryAction.RestartFailed,
            "文字認識エンジンの復旧に失敗しました。Baketaを再起動してください。",
            0,
            recoveryAttempt);
    }
}
