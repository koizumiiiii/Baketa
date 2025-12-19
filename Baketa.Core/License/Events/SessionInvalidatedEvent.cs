using Baketa.Core.Events;

namespace Baketa.Core.License.Events;

/// <summary>
/// セッション無効化イベント（別デバイスでのログイン検出時）
/// </summary>
public sealed class SessionInvalidatedEvent : EventBase
{
    /// <summary>
    /// セッション無効化イベントを作成
    /// </summary>
    /// <param name="reason">無効化理由</param>
    /// <param name="newDeviceInfo">新しいデバイスの情報（取得できる場合）</param>
    public SessionInvalidatedEvent(string reason, string? newDeviceInfo = null)
    {
        Reason = reason;
        NewDeviceInfo = newDeviceInfo;
    }

    /// <inheritdoc />
    public override string Name => "SessionInvalidated";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 無効化理由
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// 新しいデバイスの情報
    /// </summary>
    public string? NewDeviceInfo { get; }

    /// <summary>
    /// ユーザーに表示するメッセージを取得
    /// </summary>
    public string GetUserMessage()
    {
        if (!string.IsNullOrEmpty(NewDeviceInfo))
        {
            return $"別のデバイス（{NewDeviceInfo}）でログインされたため、このセッションは無効になりました。";
        }

        return "別のデバイスでログインされたため、このセッションは無効になりました。再度ログインしてください。";
    }
}

/// <summary>
/// プラン期限切れ警告イベント
/// </summary>
public sealed class PlanExpirationWarningEvent : EventBase
{
    /// <summary>
    /// プラン期限切れ警告イベントを作成
    /// </summary>
    /// <param name="expirationDate">期限日</param>
    /// <param name="daysRemaining">残り日数</param>
    public PlanExpirationWarningEvent(DateTime expirationDate, int daysRemaining)
    {
        ExpirationDate = expirationDate;
        DaysRemaining = daysRemaining;
    }

    /// <inheritdoc />
    public override string Name => "PlanExpirationWarning";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 期限日
    /// </summary>
    public DateTime ExpirationDate { get; }

    /// <summary>
    /// 残り日数
    /// </summary>
    public int DaysRemaining { get; }

    /// <summary>
    /// 警告メッセージを取得
    /// </summary>
    public string GetWarningMessage() => DaysRemaining switch
    {
        0 => "プランの有効期限は本日までです。",
        1 => "プランの有効期限は明日までです。",
        _ => $"プランの有効期限まであと{DaysRemaining}日です。"
    };
}

/// <summary>
/// セッション無効化イベント引数（EventHandler用）
/// </summary>
public sealed record SessionInvalidatedEventArgs(
    string Reason,
    string? NewDeviceInfo);

/// <summary>
/// プラン期限切れ警告イベント引数（EventHandler用）
/// </summary>
public sealed record PlanExpirationWarningEventArgs(
    DateTime ExpirationDate,
    int DaysRemaining);
