using Baketa.Core.Events;
using Baketa.Core.License.Models;

namespace Baketa.Core.License.Events;

/// <summary>
/// ライセンス状態変更の理由
/// </summary>
public enum LicenseChangeReason
{
    /// <summary>初回読み込み</summary>
    InitialLoad,

    /// <summary>サーバーからの更新</summary>
    ServerRefresh,

    /// <summary>キャッシュからの読み込み</summary>
    CacheLoad,

    /// <summary>プランアップグレード</summary>
    PlanUpgrade,

    /// <summary>プランダウングレード</summary>
    PlanDowngrade,

    /// <summary>トークン消費</summary>
    TokenConsumption,

    /// <summary>セッション無効化</summary>
    SessionInvalidation,

    /// <summary>ログアウト</summary>
    Logout,

    /// <summary>サブスクリプション期限切れ</summary>
    SubscriptionExpired,

    /// <summary>プロモーションコード適用</summary>
    PromotionApplied,

    /// <summary>プロモーション期限切れ/解除</summary>
    PromotionExpired,

    /// <summary>トークン使用量の同期更新（Issue #275）</summary>
    TokenUsageUpdated
}

/// <summary>
/// ライセンス状態変更イベント
/// </summary>
public sealed class LicenseStateChangedEvent : EventBase
{
    /// <summary>
    /// ライセンス状態変更イベントを作成
    /// </summary>
    /// <param name="oldState">変更前の状態</param>
    /// <param name="newState">変更後の状態</param>
    /// <param name="reason">変更理由</param>
    public LicenseStateChangedEvent(
        LicenseState oldState,
        LicenseState newState,
        LicenseChangeReason reason)
    {
        OldState = oldState;
        NewState = newState;
        Reason = reason;
    }

    /// <inheritdoc />
    public override string Name => "LicenseStateChanged";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 変更前の状態
    /// </summary>
    public LicenseState OldState { get; }

    /// <summary>
    /// 変更後の状態
    /// </summary>
    public LicenseState NewState { get; }

    /// <summary>
    /// 変更理由
    /// </summary>
    public LicenseChangeReason Reason { get; }

    /// <summary>
    /// プランが変更されたかどうか
    /// </summary>
    public bool IsPlanChanged => OldState.CurrentPlan != NewState.CurrentPlan;

    /// <summary>
    /// トークン使用量が変更されたかどうか
    /// </summary>
    public bool IsTokenUsageChanged => OldState.CloudAiTokensUsed != NewState.CloudAiTokensUsed;
}

/// <summary>
/// ライセンス状態変更イベント引数（EventHandler用）
/// </summary>
public sealed record LicenseStateChangedEventArgs(
    LicenseState OldState,
    LicenseState NewState,
    LicenseChangeReason Reason);
