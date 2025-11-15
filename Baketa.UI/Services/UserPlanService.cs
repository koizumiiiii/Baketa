using System;
using Baketa.UI.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.UI.Services;

/// <summary>
/// ユーザープラン判定サービスの実装
/// </summary>
public class UserPlanService : IUserPlanService
{
    private readonly ILogger<UserPlanService> _logger;
    private readonly TranslationUIOptions _options;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="options">UI設定オプション</param>
    public UserPlanService(
        ILogger<UserPlanService> logger,
        IOptions<TranslationUIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);

        _logger = logger;
        _options = options.Value;

        // 現在は無料プランとして初期化（将来的にはライセンス確認ロジックを追加）
        CurrentPlan = UserPlanType.Free;

        _logger.LogInformation("UserPlanService initialized with plan: {Plan}", CurrentPlan);
    }

    /// <inheritdoc />
    public UserPlanType CurrentPlan { get; private set; }

    /// <inheritdoc />
    public bool CanUseCloudOnlyEngine => CurrentPlan == UserPlanType.Premium;

    /// <inheritdoc />
    public bool IsMonthlyLimitExceeded => MonthlyUsageCount >= MonthlyLimit;

    /// <inheritdoc />
    public int MonthlyUsageCount
    {
        get
        {
            // 現在は固定値を返す（将来的には実際の使用量を追跡）
            return CurrentPlan == UserPlanType.Free ? 150 : 0;
        }
    }

    /// <inheritdoc />
    public int MonthlyLimit
    {
        get
        {
            return CurrentPlan switch
            {
                UserPlanType.Free => 500,
                UserPlanType.Premium => int.MaxValue,
                _ => 0
            };
        }
    }

    /// <inheritdoc />
    public UserPlanDetails GetPlanDetails()
    {
        return CurrentPlan switch
        {
            UserPlanType.Free => new UserPlanDetails(
                UserPlanType.Free,
                "無料プラン",
                "LocalOnlyエンジンのみ利用可能。月500回まで翻訳可能。",
                500,
                false,
                null),

            UserPlanType.Premium => new UserPlanDetails(
                UserPlanType.Premium,
                "プレミアムプラン",
                "LocalOnly・CloudOnlyエンジン両方利用可能。無制限翻訳。",
                int.MaxValue,
                true,
                DateTime.UtcNow.AddMonths(1)),

            _ => throw new InvalidOperationException($"Unknown plan type: {CurrentPlan}")
        };
    }

    /// <inheritdoc />
    public event EventHandler<UserPlanChangedEventArgs>? PlanChanged;

    /// <summary>
    /// プランを変更する（テスト・管理用）
    /// </summary>
    /// <param name="newPlan">新しいプランタイプ</param>
    public void ChangePlan(UserPlanType newPlan)
    {
        if (CurrentPlan == newPlan)
            return;

        var oldPlan = CurrentPlan;
        CurrentPlan = newPlan;

        _logger.LogInformation("Plan changed from {OldPlan} to {NewPlan}", oldPlan, newPlan);

        PlanChanged?.Invoke(this, new UserPlanChangedEventArgs(oldPlan, newPlan));
    }

    /// <summary>
    /// プレミアムプランをシミュレート（開発・テスト用）
    /// </summary>
    public void SimulatePremiumPlan()
    {
        ChangePlan(UserPlanType.Premium);
    }

    /// <summary>
    /// 無料プランをシミュレート（開発・テスト用）
    /// </summary>
    public void SimulateFreePlan()
    {
        ChangePlan(UserPlanType.Free);
    }
}
