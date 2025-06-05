using System;
using System.Threading.Tasks;

namespace Baketa.UI.Services;

/// <summary>
/// 通知サービス
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// 成功メッセージを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間（ミリ秒）</param>
    Task ShowSuccessAsync(string title, string message, int duration = 3000);

    /// <summary>
    /// 情報メッセージを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間（ミリ秒）</param>
    Task ShowInformationAsync(string title, string message, int duration = 4000);

    /// <summary>
    /// 情報メッセージを表示（省略形）
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間（ミリ秒）</param>
    Task ShowInfoAsync(string title, string message, int duration = 4000);

    /// <summary>
    /// 警告メッセージを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間（ミリ秒）</param>
    Task ShowWarningAsync(string title, string message, int duration = 5000);

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="duration">表示時間（ミリ秒）、0は手動閉じるまで表示</param>
    Task ShowErrorAsync(string title, string message, int duration = 0);

    /// <summary>
    /// フォールバック通知を表示
    /// </summary>
    /// <param name="fromEngine">切り替え元エンジン</param>
    /// <param name="toEngine">切り替え先エンジン</param>
    /// <param name="reason">切り替え理由</param>
    Task ShowFallbackNotificationAsync(string fromEngine, string toEngine, string reason);

    /// <summary>
    /// エンジン状態変更通知を表示
    /// </summary>
    /// <param name="engineName">エンジン名</param>
    /// <param name="status">新しい状態</param>
    Task ShowEngineStatusChangeAsync(string engineName, string status);

    /// <summary>
    /// 確認ダイアログを表示
    /// </summary>
    /// <param name="title">タイトル</param>
    /// <param name="message">メッセージ</param>
    /// <param name="confirmText">確認ボタンテキスト</param>
    /// <param name="cancelText">キャンセルボタンテキスト</param>
    /// <returns>ユーザーが確認を選択した場合true</returns>
    Task<bool> ShowConfirmationAsync(string title, string message, 
        string confirmText = "OK", string cancelText = "キャンセル");

    /// <summary>
    /// 通知表示イベント
    /// </summary>
    event EventHandler<NotificationEventArgs> NotificationShown;
}

/// <summary>
/// 通知タイプ
/// </summary>
public enum NotificationType
{
    Success,
    Information,
    Warning,
    Error,
    FallbackNotification,
    EngineStatusChange
}

/// <summary>
/// 通知イベント引数
/// </summary>
public class NotificationEventArgs : EventArgs
{
    public NotificationType Type { get; }
    public string Title { get; }
    public string Message { get; }
    public int Duration { get; }
    public DateTime Timestamp { get; }

    public NotificationEventArgs(NotificationType type, string title, string message, int duration)
    {
        Type = type;
        Title = title;
        Message = message;
        Duration = duration;
        Timestamp = DateTime.UtcNow;
    }
}
