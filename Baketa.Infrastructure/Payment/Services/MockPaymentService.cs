using Baketa.Core.Abstractions.Payment;
using Baketa.Core.License.Models;
using Baketa.Core.Payment.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Payment.Services;

/// <summary>
/// 決済サービスのモック実装
/// 開発・テスト環境で使用
/// </summary>
public sealed class MockPaymentService : IPaymentService
{
    private readonly ILogger<MockPaymentService> _logger;
    private readonly Dictionary<string, SubscriptionInfo> _subscriptions = new();

    public MockPaymentService(ILogger<MockPaymentService> logger)
    {
        _logger = logger;
        _logger.LogInformation("MockPaymentService initialized (mock mode)");
    }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public Task<PaymentResult<CheckoutSession>> CreateCheckoutSessionAsync(
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

        var session = new CheckoutSession
        {
            SessionId = $"mock_session_{Guid.NewGuid():N}",
            CheckoutUrl = $"https://mock.fastspring.com/checkout/{subscriptionId}",
            ExpiresAt = DateTime.UtcNow.AddMinutes(30)
        };

        _logger.LogInformation(
            "[Mock] Checkout session created: {SessionId}, subscription auto-activated",
            session.SessionId);

        return Task.FromResult(PaymentResult<CheckoutSession>.CreateSuccess(session));
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
        var monthlyPrice = plan switch
        {
            PlanType.Standard => 100,
            PlanType.Pro => 300,
            PlanType.Premia => 500,
            _ => 0
        };

        if (cycle == BillingCycle.Yearly)
        {
            // 年額は20%OFF
            return (int)(monthlyPrice * 12 * 0.8);
        }

        return monthlyPrice;
    }
}
