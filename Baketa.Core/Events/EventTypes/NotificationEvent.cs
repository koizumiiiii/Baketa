using System;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 通知メッセージの種類
/// </summary>
public enum NotificationType
{
    /// <summary>
    /// 情報メッセージ
    /// </summary>
    Information,

    /// <summary>
    /// 警告メッセージ
    /// </summary>
    Warning,

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    Error,

    /// <summary>
    /// 成功メッセージ
    /// </summary>
    Success
}

/// <summary>
/// ユーザー通知イベント
/// </summary>
public class NotificationEvent(
    string message,
    NotificationType type = NotificationType.Information,
    string? title = null,
    int displayTime = 0,
    Exception? relatedError = null) : EventBase
{
    /// <summary>
    /// 通知メッセージ
    /// </summary>
    public string Message { get; } = message ?? throw new ArgumentNullException(nameof(message));

    /// <summary>
    /// 通知種別
    /// </summary>
    public NotificationType Type { get; } = type;

    /// <summary>
    /// タイトル（オプション）
    /// </summary>
    public string Title { get; } = title ?? GetDefaultTitle(type);

    /// <summary>
    /// 表示時間（ミリ秒, 0=自動）
    /// </summary>
    public int DisplayTime { get; } = displayTime;

    /// <summary>
    /// 関連例外（存在する場合）
    /// </summary>
    public Exception? RelatedError { get; } = relatedError;



    /// <inheritdoc />
    public override string Name => "Notification";

    /// <inheritdoc />
    public override string Category => "UI";

    /// <summary>
    /// 通知種別に基づいたデフォルトタイトルを取得
    /// </summary>
    /// <param name="type">通知種別</param>
    /// <returns>デフォルトタイトル</returns>
    private static string GetDefaultTitle(NotificationType type)
    {
        return type switch
        {
            NotificationType.Information => "情報",
            NotificationType.Warning => "警告",
            NotificationType.Error => "エラー",
            NotificationType.Success => "成功",
            _ => "通知"
        };
    }
}
