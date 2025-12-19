using Baketa.Core.License.Extensions;

namespace Baketa.Core.License.Models;

/// <summary>
/// ライセンス状態を表す不変のスナップショット
/// </summary>
public sealed record LicenseState
{
    /// <summary>
    /// 現在のサブスクリプションプラン
    /// </summary>
    public required PlanType CurrentPlan { get; init; }

    /// <summary>
    /// 次回更新時のプラン（変更予約がある場合）
    /// </summary>
    public PlanType? NextPlan { get; init; }

    /// <summary>
    /// ユーザーID（未認証の場合はnull）
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// 契約開始日（トークンリセット計算用）
    /// </summary>
    public DateTime? ContractStartDate { get; init; }

    /// <summary>
    /// サブスクリプション有効期限
    /// </summary>
    public DateTime? ExpirationDate { get; init; }

    /// <summary>
    /// 現在の課金サイクル開始日
    /// </summary>
    public DateTime? BillingCycleStart { get; init; }

    /// <summary>
    /// 現在の課金サイクル終了日（トークンリセット日）
    /// </summary>
    public DateTime? BillingCycleEnd { get; init; }

    /// <summary>
    /// 現在の課金期間で使用したクラウドAIトークン数
    /// </summary>
    public long CloudAiTokensUsed { get; init; }

    /// <summary>
    /// セッションID（単一デバイス制限用）
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// キャッシュされたデータかどうか（オフラインモード）
    /// </summary>
    public bool IsCached { get; init; }

    /// <summary>
    /// キャッシュタイムスタンプ（サーバーからデータを取得した時刻）
    /// </summary>
    public DateTime? CacheTimestamp { get; init; }

    /// <summary>
    /// 最後にサーバーと同期した時刻
    /// </summary>
    public DateTime? LastServerSync { get; init; }

    /// <summary>
    /// HMAC署名（サーバーから取得、オフライン検証用）
    /// </summary>
    public string? Signature { get; init; }

    /// <summary>
    /// 署名検証用チャレンジトークン
    /// </summary>
    public string? ChallengeToken { get; init; }

    // --- 計算プロパティ ---

    /// <summary>
    /// プランに基づく月間トークン上限
    /// </summary>
    public long MonthlyTokenLimit => CurrentPlan.GetMonthlyTokenLimit();

    /// <summary>
    /// 現在の課金期間での残りトークン数
    /// </summary>
    public long RemainingTokens => Math.Max(0, MonthlyTokenLimit - CloudAiTokensUsed);

    /// <summary>
    /// 月間クォータを超過しているかどうか
    /// </summary>
    public bool IsQuotaExceeded => MonthlyTokenLimit > 0 && CloudAiTokensUsed >= MonthlyTokenLimit;

    /// <summary>
    /// サブスクリプションがアクティブかどうか
    /// </summary>
    public bool IsSubscriptionActive => ExpirationDate == null || ExpirationDate > DateTime.UtcNow;

    /// <summary>
    /// サブスクリプション期限切れかどうか
    /// </summary>
    public bool IsExpired => ExpirationDate.HasValue && ExpirationDate <= DateTime.UtcNow;

    /// <summary>
    /// サブスクリプション期限切れが近いかどうか（7日以内）
    /// </summary>
    public bool IsNearExpiry => ExpirationDate.HasValue &&
        ExpirationDate > DateTime.UtcNow &&
        (ExpirationDate.Value - DateTime.UtcNow).TotalDays <= 7;

    /// <summary>
    /// 有効期限までの残り日数（期限がない場合はnull）
    /// </summary>
    public int? DaysUntilExpiration => ExpirationDate.HasValue
        ? Math.Max(0, (int)(ExpirationDate.Value - DateTime.UtcNow).TotalDays)
        : null;

    /// <summary>
    /// トークン使用率（0-100%）
    /// </summary>
    public double TokenUsagePercentage => MonthlyTokenLimit > 0
        ? Math.Min(100, (double)CloudAiTokensUsed / MonthlyTokenLimit * 100)
        : 0;

    /// <summary>
    /// 無料プランかどうか
    /// </summary>
    public bool IsFree => CurrentPlan == PlanType.Free;

    /// <summary>
    /// 有料プランかどうか
    /// </summary>
    public bool IsPaid => CurrentPlan != PlanType.Free;

    /// <summary>
    /// クラウドAI翻訳が利用可能かどうか
    /// </summary>
    public bool HasCloudAiAccess => CurrentPlan.HasCloudAiAccess() && !IsQuotaExceeded && IsSubscriptionActive;

    /// <summary>
    /// 広告非表示かどうか
    /// </summary>
    public bool IsAdFree => !CurrentPlan.ShowsAds();

    /// <summary>
    /// 指定された機能が利用可能かどうか
    /// </summary>
    public bool IsFeatureAvailable(FeatureType feature)
    {
        // サブスクリプションが期限切れの場合、Freeプランの機能のみ利用可能
        if (IsExpired)
        {
            return PlanType.Free.IsFeatureAvailable(feature);
        }

        // クラウドAI翻訳はクォータチェックも必要
        if (feature == FeatureType.CloudAiTranslation && IsQuotaExceeded)
        {
            return false;
        }

        return CurrentPlan.IsFeatureAvailable(feature);
    }

    /// <summary>
    /// 次回トークンリセットまでの残り時間
    /// </summary>
    public TimeSpan? TimeUntilTokenReset => BillingCycleEnd.HasValue
        ? BillingCycleEnd.Value - DateTime.UtcNow
        : null;

    // --- ファクトリメソッド ---

    /// <summary>
    /// 匿名/無料ユーザー用のデフォルト状態
    /// </summary>
    public static LicenseState Default => new()
    {
        CurrentPlan = PlanType.Free,
        UserId = null,
        CloudAiTokensUsed = 0,
        IsCached = false
    };

    /// <summary>
    /// キャッシュから読み込んだ状態を作成
    /// </summary>
    public static LicenseState FromCache(LicenseState cached) => cached with
    {
        IsCached = true,
        CacheTimestamp = DateTime.UtcNow
    };

    /// <summary>
    /// トークン消費後の新しい状態を作成
    /// </summary>
    public LicenseState WithTokensConsumed(long tokensConsumed) => this with
    {
        CloudAiTokensUsed = CloudAiTokensUsed + tokensConsumed
    };

    /// <summary>
    /// サーバー同期後の新しい状態を作成
    /// </summary>
    public LicenseState WithServerSync() => this with
    {
        IsCached = false,
        LastServerSync = DateTime.UtcNow
    };
}
