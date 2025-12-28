namespace Baketa.Core.License.Models;

/// <summary>
/// プロモーションコード適用結果
/// Issue #237 Phase 2: プロモーションコード機能
/// </summary>
public sealed record PromotionCodeResult
{
    /// <summary>
    /// 適用に成功したか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 適用されたプランタイプ（成功時）
    /// </summary>
    public PlanType? AppliedPlan { get; init; }

    /// <summary>
    /// 有効期限（成功時）
    /// </summary>
    public DateTime? ExpiresAt { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public PromotionErrorCode? ErrorCode { get; init; }

    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static PromotionCodeResult CreateSuccess(PlanType plan, DateTime expiresAt, string message) => new()
    {
        Success = true,
        AppliedPlan = plan,
        ExpiresAt = expiresAt,
        Message = message
    };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static PromotionCodeResult CreateFailure(PromotionErrorCode errorCode, string message) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        Message = message
    };
}

/// <summary>
/// プロモーションコードエラーコード
/// </summary>
public enum PromotionErrorCode
{
    /// <summary>無効なコード形式</summary>
    InvalidFormat,

    /// <summary>コードが存在しない</summary>
    CodeNotFound,

    /// <summary>既に使用済み</summary>
    AlreadyRedeemed,

    /// <summary>コードの有効期限切れ</summary>
    CodeExpired,

    /// <summary>既にProプラン以上を利用中</summary>
    AlreadyProOrHigher,

    /// <summary>ネットワークエラー</summary>
    NetworkError,

    /// <summary>サーバーエラー</summary>
    ServerError,

    /// <summary>レートリミット</summary>
    RateLimited
}
