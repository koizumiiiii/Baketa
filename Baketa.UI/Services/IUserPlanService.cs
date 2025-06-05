using System;
using Baketa.Core.Translation.Models;

namespace Baketa.UI.Services;

/// <summary>
/// ユーザープラン判定サービス
/// </summary>
public interface IUserPlanService
{
    /// <summary>
    /// 現在のユーザープランタイプ
    /// </summary>
    UserPlanType CurrentPlan { get; }

    /// <summary>
    /// CloudOnlyエンジンが利用可能かどうか
    /// </summary>
    bool CanUseCloudOnlyEngine { get; }

    /// <summary>
    /// 月間利用制限を超過しているかどうか
    /// </summary>
    bool IsMonthlyLimitExceeded { get; }

    /// <summary>
    /// 今月の利用回数
    /// </summary>
    int MonthlyUsageCount { get; }

    /// <summary>
    /// 月間利用制限
    /// </summary>
    int MonthlyLimit { get; }

    /// <summary>
    /// プラン詳細情報の取得
    /// </summary>
    /// <returns>プラン詳細情報</returns>
    UserPlanDetails GetPlanDetails();

    /// <summary>
    /// プラン変更イベント
    /// </summary>
    event EventHandler<UserPlanChangedEventArgs> PlanChanged;
}

/// <summary>
/// ユーザープランタイプ
/// </summary>
public enum UserPlanType
{
    /// <summary>
    /// 無料プラン
    /// </summary>
    Free,

    /// <summary>
    /// 有料プラン
    /// </summary>
    Premium
}

/// <summary>
/// ユーザープラン詳細情報
/// </summary>
public record UserPlanDetails(
    UserPlanType PlanType,
    string PlanName,
    string Description,
    int MonthlyLimit,
    bool CloudAccessEnabled,
    DateTime? SubscriptionExpiryDate);

/// <summary>
/// プラン変更イベント引数
/// </summary>
public class UserPlanChangedEventArgs : EventArgs
{
    public UserPlanType OldPlan { get; }
    public UserPlanType NewPlan { get; }
    public DateTime ChangeDate { get; }

    public UserPlanChangedEventArgs(UserPlanType oldPlan, UserPlanType newPlan)
    {
        OldPlan = oldPlan;
        NewPlan = newPlan;
        ChangeDate = DateTime.UtcNow;
    }
}
