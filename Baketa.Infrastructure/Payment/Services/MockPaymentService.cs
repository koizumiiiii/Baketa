using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Payment;
using Baketa.Core.License.Models;
using Baketa.Core.Payment.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Payment.Services;

/// <summary>
/// 決済サービスのモック実装
/// 開発・テスト環境で使用
/// テストモード時は実際の決済処理なしにプラン変更を即座に反映
/// </summary>
public sealed class MockPaymentService : IPaymentService
{
    private readonly ILogger<MockPaymentService> _logger;
    private readonly ILicenseManager? _licenseManager;
    private readonly Dictionary<string, SubscriptionInfo> _subscriptions = new();

    public MockPaymentService(
        ILogger<MockPaymentService> logger,
        ILicenseManager? licenseManager = null)
    {
        _logger = logger;
        _licenseManager = licenseManager;
        _logger.LogInformation(
            "MockPaymentService initialized (mock mode, LicenseManager={HasLicenseManager})",
            licenseManager is not null);
    }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public async Task<PaymentResult<CheckoutSession>> CreateCheckoutSessionAsync(
        string userId,
        PlanType targetPlan,
        BillingCycle billingCycle,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "[Mock] Creating checkout session for user {UserId}, plan {Plan}, cycle {Cycle}",
            userId, targetPlan, billingCycle);

        // モックでは即座にサブスクリプションを作成
        var subscriptionId = $"mock_sub_{Guid.NewGuid():N}";
        var subscription = new SubscriptionInfo
        {
            SubscriptionId = subscriptionId,
            CustomerId = $"mock_cust_{userId}",
            CurrentPlan = targetPlan,
            BillingCycle = billingCycle,
            Status = SubscriptionStatus.Active,
            NextBillingDate = DateTime.UtcNow.AddMonths(billingCycle == BillingCycle.Monthly ? 1 : 12),
            LastPaymentDate = DateTime.UtcNow,
            PaymentMethod = "mock_card"
        };

        _subscriptions[userId] = subscription;

        // テストモード: LicenseManagerに即座にプラン変更を反映
        if (_licenseManager is not null)
        {
            var success = await _licenseManager.SetTestPlanAsync(targetPlan, cancellationToken)
                .ConfigureAwait(false);

            if (success)
            {
                _logger.LogInformation(
                    "🧪 [Mock] テストモード: プラン {Plan} を即座に反映しました（決済処理スキップ）",
                    targetPlan);

                // テストモードでは決済URLなしで成功を返す
                var testSession = new CheckoutSession
                {
                    SessionId = $"test_session_{Guid.NewGuid():N}",
                    CheckoutUrl = string.Empty, // URLなし = 決済画面遷移なし
                    ExpiresAt = DateTime.UtcNow.AddMinutes(30)
                };

                return PaymentResult<CheckoutSession>.CreateSuccess(testSession);
            }
        }

        // 通常のモック動作（URLを返す）
        var session = new CheckoutSession
        {
            SessionId = $"mock_session_{Guid.NewGuid():N}",
            CheckoutUrl = $"https://mock.fastspring.com/checkout/{subscriptionId}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _logger.LogInformation(
            "[Mock] Checkout session created: {SessionId}, subscription auto-activated",
            session.SessionId);

        return PaymentResult<CheckoutSession>.CreateSuccess(session);
    }

    /// <inheritdoc/>
    public Task<PaymentResult<SubscriptionInfo?>> GetSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Getting subscription for user {UserId}", userId);

        if (_subscriptions.TryGetValue(userId, out var subscription))
        {
            return Task.FromResult(PaymentResult<SubscriptionInfo?>.CreateSuccess(subscription));
        }

        return Task.FromResult(PaymentResult<SubscriptionInfo?>.CreateSuccess(null));
    }

    /// <inheritdoc/>
    public Task<PaymentResult> CancelSubscriptionAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Mock] Canceling subscription for user {UserId}", userId);

        if (_subscriptions.TryGetValue(userId, out var subscription))
        {
            // キャンセル予約状態に更新
            _subscriptions[userId] = subscription with
            {
                NextPlan = PlanType.Free
            };

            _logger.LogInformation(
                "[Mock] Subscription {SubscriptionId} marked for cancellation",
                subscription.SubscriptionId);

            return Task.FromResult(PaymentResult.CreateSuccess());
        }

        return Task.FromResult(PaymentResult.CreateFailure(
            "SUBSCRIPTION_NOT_FOUND",
            "サブスクリプションが見つかりません"));
    }

    /// <inheritdoc/>
    public Task<PaymentResult<CustomerPortalUrl>> GetSecurePortalUrlAsync(
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Getting portal URL for user {UserId}", userId);

        var portalUrl = new CustomerPortalUrl
        {
            Url = $"https://mock.fastspring.com/account/{userId}",
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };

        return Task.FromResult(PaymentResult<CustomerPortalUrl>.CreateSuccess(portalUrl));
    }

    /// <inheritdoc/>
    public Task<PaymentResult<IReadOnlyList<PaymentHistoryEntry>>> GetPaymentHistoryAsync(
        string userId,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("[Mock] Getting payment history for user {UserId}, limit {Limit}", userId, limit);

        var history = new List<PaymentHistoryEntry>();

        if (_subscriptions.TryGetValue(userId, out var subscription))
        {
            // モックの決済履歴を生成
            history.Add(new PaymentHistoryEntry
            {
                PaymentId = $"mock_pay_{Guid.NewGuid():N}",
                OrderId = $"mock_order_{Guid.NewGuid():N}",
                Plan = subscription.CurrentPlan,
                BillingCycle = subscription.BillingCycle,
                AmountYen = GetPlanPrice(subscription.CurrentPlan, subscription.BillingCycle),
                Currency = "JPY",
                Status = PaymentStatus.Completed,
                EventType = "subscription.charge.completed",
                CreatedAt = subscription.LastPaymentDate ?? DateTime.UtcNow
            });
        }

        return Task.FromResult(PaymentResult<IReadOnlyList<PaymentHistoryEntry>>.CreateSuccess(history));
    }

    /// <summary>
    /// プランの価格を取得（円）
    /// </summary>
    private static int GetPlanPrice(PlanType plan, BillingCycle cycle)
    {
        // Issue #125: Standardプラン廃止
        // Issue #257: Pro/Premium/Ultimate 3段階構成に改定（USD建て）
        var monthlyPrice = plan switch
        {
            PlanType.Pro => 300,       // $3
            PlanType.Premium => 500,   // $5
            PlanType.Ultimate => 900,  // $9
            _ => 0
        };

        if (cycle == BillingCycle.Yearly)
        {
            // 年額は20%OFF（オーバーフロー防止のため先にdoubleに変換）
            return (int)((double)monthlyPrice * 12 * 0.8);
        }

        return monthlyPrice;
    }
}
