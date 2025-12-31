using Baketa.Core.Abstractions.License;
using Baketa.Core.Events;
using Baketa.Core.License.Models;

namespace Baketa.Core.License.Events;

/// <summary>
/// プロモーションコード適用イベント
/// </summary>
/// <remarks>
/// Issue #243: LicenseManagerとPromotionCodeService間の連携を
/// EventAggregator経由で実現し、循環依存を回避
/// </remarks>
public sealed class PromotionAppliedEvent : EventBase
{
    /// <summary>
    /// プロモーション適用イベントを作成
    /// </summary>
    /// <param name="promotion">適用されたプロモーション情報</param>
    public PromotionAppliedEvent(PromotionInfo promotion)
    {
        Promotion = promotion ?? throw new ArgumentNullException(nameof(promotion));
    }

    /// <inheritdoc />
    public override string Name => "PromotionApplied";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 適用されたプロモーション情報
    /// </summary>
    public PromotionInfo Promotion { get; }

    /// <summary>
    /// 適用されたプラン
    /// </summary>
    public PlanType AppliedPlan => Promotion.Plan;

    /// <summary>
    /// プロモーション有効期限
    /// </summary>
    public DateTime ExpiresAt => Promotion.ExpiresAt;
}

/// <summary>
/// プロモーション解除イベント
/// </summary>
public sealed class PromotionRemovedEvent : EventBase
{
    /// <summary>
    /// プロモーション解除イベントを作成
    /// </summary>
    /// <param name="reason">解除理由</param>
    public PromotionRemovedEvent(string reason = "Manual removal")
    {
        Reason = reason;
    }

    /// <inheritdoc />
    public override string Name => "PromotionRemoved";

    /// <inheritdoc />
    public override string Category => "License";

    /// <summary>
    /// 解除理由
    /// </summary>
    public string Reason { get; }
}
