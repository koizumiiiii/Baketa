using Baketa.Core.License.Models;

namespace Baketa.Core.Payment.Models;

/// <summary>
/// チェックアウトセッション（決済ページへのリダイレクト情報）
/// </summary>
public sealed record CheckoutSession
{
    /// <summary>
    /// セッションID
    /// </summary>
    public required string SessionId { get; init; }

    /// <summary>
    /// チェックアウトURL（ブラウザで開くURL）
    /// </summary>
    public required string CheckoutUrl { get; init; }

    /// <summary>
    /// セッション有効期限
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// セッションが有効かどうか
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;
}

/// <summary>
/// チェックアウトセッション作成リクエスト
/// </summary>
public sealed record CreateCheckoutRequest
{
    /// <summary>
    /// ユーザーID
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// 購入するプラン
    /// </summary>
    public required PlanType TargetPlan { get; init; }

    /// <summary>
    /// 課金サイクル
    /// </summary>
    public required BillingCycle BillingCycle { get; init; }

    /// <summary>
    /// FastSpring製品API名を生成
    /// </summary>
    public string GetProductApiName() =>
        $"baketa-{TargetPlan.ToString().ToLowerInvariant()}-{BillingCycle.GetProductSuffix()}";
}

/// <summary>
/// サブスクリプション情報
/// </summary>
public sealed record SubscriptionInfo
{
    /// <summary>
    /// FastSpringサブスクリプションID
    /// </summary>
    public required string SubscriptionId { get; init; }

    /// <summary>
    /// FastSpring顧客ID
    /// </summary>
    public required string CustomerId { get; init; }

    /// <summary>
    /// 現在のプラン
    /// </summary>
    public required PlanType CurrentPlan { get; init; }

    /// <summary>
    /// 課金サイクル
    /// </summary>
    public required BillingCycle BillingCycle { get; init; }

    /// <summary>
    /// サブスクリプション状態
    /// </summary>
    public required SubscriptionStatus Status { get; init; }

    /// <summary>
    /// 次回請求日
    /// </summary>
    public DateTime? NextBillingDate { get; init; }

    /// <summary>
    /// 最終決済日
    /// </summary>
    public DateTime? LastPaymentDate { get; init; }

    /// <summary>
    /// 決済方法
    /// </summary>
    public string? PaymentMethod { get; init; }

    /// <summary>
    /// 次回更新時のプラン（変更予約がある場合）
    /// </summary>
    public PlanType? NextPlan { get; init; }

    /// <summary>
    /// サブスクリプションがアクティブかどうか
    /// </summary>
    public bool IsActive => Status == SubscriptionStatus.Active;

    /// <summary>
    /// キャンセル予約されているかどうか
    /// </summary>
    public bool IsCancellationPending => NextPlan == PlanType.Free;
}

/// <summary>
/// サブスクリプション状態
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>アクティブ</summary>
    Active,

    /// <summary>一時停止中</summary>
    Paused,

    /// <summary>キャンセル済み</summary>
    Canceled,

    /// <summary>決済失敗</summary>
    PaymentFailed,

    /// <summary>期限切れ</summary>
    Expired
}

/// <summary>
/// 決済履歴エントリ
/// </summary>
public sealed record PaymentHistoryEntry
{
    /// <summary>
    /// 決済ID
    /// </summary>
    public required string PaymentId { get; init; }

    /// <summary>
    /// FastSpring注文ID
    /// </summary>
    public required string OrderId { get; init; }

    /// <summary>
    /// プラン
    /// </summary>
    public required PlanType Plan { get; init; }

    /// <summary>
    /// 課金サイクル
    /// </summary>
    public required BillingCycle BillingCycle { get; init; }

    /// <summary>
    /// 金額（円）
    /// </summary>
    public required int AmountYen { get; init; }

    /// <summary>
    /// 通貨
    /// </summary>
    public required string Currency { get; init; }

    /// <summary>
    /// 決済状態
    /// </summary>
    public required PaymentStatus Status { get; init; }

    /// <summary>
    /// イベントタイプ
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// 決済日時
    /// </summary>
    public required DateTime CreatedAt { get; init; }
}

/// <summary>
/// 決済状態
/// </summary>
public enum PaymentStatus
{
    /// <summary>完了</summary>
    Completed,

    /// <summary>返金済み</summary>
    Refunded,

    /// <summary>失敗</summary>
    Failed,

    /// <summary>処理中</summary>
    Pending
}

/// <summary>
/// カスタマーポータルURL応答
/// </summary>
public sealed record CustomerPortalUrl
{
    /// <summary>
    /// セキュアポータルURL
    /// </summary>
    public required string Url { get; init; }

    /// <summary>
    /// URL有効期限
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>
    /// URLが有効かどうか
    /// </summary>
    public bool IsValid => DateTime.UtcNow < ExpiresAt;
}

/// <summary>
/// 決済操作結果
/// </summary>
public sealed record PaymentResult
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static PaymentResult CreateSuccess() => new() { Success = true };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static PaymentResult CreateFailure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// 決済操作結果（データ付き）
/// </summary>
public sealed record PaymentResult<T>
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 結果データ（成功時）
    /// </summary>
    public T? Data { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static PaymentResult<T> CreateSuccess(T data) => new()
    {
        Success = true,
        Data = data
    };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static PaymentResult<T> CreateFailure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}
