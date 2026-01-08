namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// Cloud翻訳機能の利用可否を統合管理するサービス
/// Issue #273: FeatureFlags.EnableCloudTranslation と Translation.EnableCloudAiTranslation の
/// 2つの設定を統合し、一貫した可用性判定を提供する
/// </summary>
public interface ICloudTranslationAvailabilityService
{
    /// <summary>
    /// Cloud翻訳の利用資格があるかどうか（プラン/ライセンスベース）
    /// Pro/Premium/Ultimate プラン、またはプロモーションコード適用時に true
    /// </summary>
    bool IsEntitled { get; }

    /// <summary>
    /// ユーザーがCloud翻訳を希望しているかどうか（ユーザー設定ベース）
    /// TranslationSettings.EnableCloudAiTranslation の値
    /// </summary>
    bool IsPreferred { get; }

    /// <summary>
    /// Cloud翻訳が実際に有効かどうか（計算プロパティ）
    /// IsEntitled AND IsPreferred の両方が true の場合のみ true
    /// </summary>
    bool IsEffectivelyEnabled { get; }

    /// <summary>
    /// Cloud翻訳の可用性が変更された時に発火するイベント
    /// プラン変更、プロモーション適用、ユーザー設定変更時に発火
    /// </summary>
    event EventHandler<CloudTranslationAvailabilityChangedEventArgs>? AvailabilityChanged;

    /// <summary>
    /// 現在の状態を強制的に再評価して更新する
    /// ログイン後やプラン変更後に呼び出す
    /// </summary>
    Task RefreshStateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ユーザーの希望設定を更新する
    /// UI層からCloud翻訳の有効/無効を切り替える際に使用
    /// </summary>
    /// <param name="preferred">Cloud翻訳を希望するかどうか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task SetPreferredAsync(bool preferred, CancellationToken cancellationToken = default);
}

/// <summary>
/// Cloud翻訳可用性変更イベントの引数
/// </summary>
public sealed class CloudTranslationAvailabilityChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更前の有効状態
    /// </summary>
    public required bool WasEnabled { get; init; }

    /// <summary>
    /// 変更後の有効状態
    /// </summary>
    public required bool IsEnabled { get; init; }

    /// <summary>
    /// 変更の理由
    /// </summary>
    public required CloudTranslationChangeReason Reason { get; init; }

    /// <summary>
    /// 利用資格があるかどうか
    /// </summary>
    public required bool IsEntitled { get; init; }

    /// <summary>
    /// ユーザーが希望しているかどうか
    /// </summary>
    public required bool IsPreferred { get; init; }
}

/// <summary>
/// Cloud翻訳可用性変更の理由
/// </summary>
public enum CloudTranslationChangeReason
{
    /// <summary>
    /// 初期化時
    /// </summary>
    Initialization,

    /// <summary>
    /// プランアップグレード（Free→Pro, Pro→Premium, Pro→Ultimate など）
    /// </summary>
    PlanUpgrade,

    /// <summary>
    /// プランダウングレード（Premium→Pro, Pro→Free など）
    /// </summary>
    PlanDowngrade,

    /// <summary>
    /// プロモーションコード適用
    /// </summary>
    PromotionApplied,

    /// <summary>
    /// プロモーション期限切れ
    /// </summary>
    PromotionExpired,

    /// <summary>
    /// ユーザーが手動で設定変更
    /// </summary>
    UserPreferenceChanged,

    /// <summary>
    /// ログイン状態復元
    /// </summary>
    LoginRestored,

    /// <summary>
    /// ログアウト
    /// </summary>
    Logout,

    /// <summary>
    /// オフライン検出
    /// </summary>
    OfflineDetected
}
