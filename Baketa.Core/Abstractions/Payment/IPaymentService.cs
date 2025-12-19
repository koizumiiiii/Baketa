using Baketa.Core.License.Models;
using Baketa.Core.Payment.Models;

namespace Baketa.Core.Abstractions.Payment;

/// <summary>
/// 決済サービスインターフェース
/// FastSpring統合を抽象化し、決済操作を提供
/// </summary>
public interface IPaymentService
{
    /// <summary>
    /// チェックアウトセッションを作成
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="targetPlan">購入するプラン</param>
    /// <param name="billingCycle">課金サイクル</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>チェックアウトセッション</returns>
    Task<PaymentResult<CheckoutSession>> CreateCheckoutSessionAsync(
        string userId,
        PlanType targetPlan,
        BillingCycle billingCycle,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サブスクリプション情報を取得
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>サブスクリプション情報（存在しない場合はnull）</returns>
    Task<PaymentResult<SubscriptionInfo?>> GetSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// サブスクリプションをキャンセル
    /// 現在の請求期間終了時にFreeプランにダウングレード
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>キャンセル結果</returns>
    Task<PaymentResult> CancelSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// セキュアなカスタマーポータルURLを取得
    /// ユーザーが決済情報を管理するためのURL
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>カスタマーポータルURL</returns>
    Task<PaymentResult<CustomerPortalUrl>> GetSecurePortalUrlAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 決済履歴を取得
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="limit">取得件数上限</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>決済履歴リスト</returns>
    Task<PaymentResult<IReadOnlyList<PaymentHistoryEntry>>> GetPaymentHistoryAsync(
        string userId,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 決済サービスが利用可能かどうか
    /// </summary>
    bool IsAvailable { get; }
}
