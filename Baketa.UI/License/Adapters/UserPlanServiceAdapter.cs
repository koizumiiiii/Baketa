using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.License.Adapters;

/// <summary>
/// 後方互換性のためのIUserPlanServiceアダプタ
/// 新しいILicenseManagerをラップして既存のIUserPlanServiceインターフェースを提供
/// </summary>
public sealed class UserPlanServiceAdapter : IUserPlanService, IDisposable
{
    private readonly ILicenseManager _licenseManager;
    private readonly ILogger<UserPlanServiceAdapter> _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public UserPlanType CurrentPlan => MapToUserPlanType(_licenseManager.CurrentState.CurrentPlan);

    /// <inheritdoc/>
    public bool CanUseCloudOnlyEngine =>
        _licenseManager.CurrentState.CurrentPlan.HasCloudAiAccess() &&
        !_licenseManager.CurrentState.IsQuotaExceeded;

    /// <inheritdoc/>
    public bool IsMonthlyLimitExceeded => _licenseManager.CurrentState.IsQuotaExceeded;

    /// <inheritdoc/>
    public int MonthlyUsageCount => (int)Math.Min(_licenseManager.CurrentState.CloudAiTokensUsed, int.MaxValue);

    /// <inheritdoc/>
    public int MonthlyLimit => (int)Math.Min(_licenseManager.CurrentState.MonthlyTokenLimit, int.MaxValue);

    /// <inheritdoc/>
    public event EventHandler<UserPlanChangedEventArgs>? PlanChanged;

    /// <summary>
    /// UserPlanServiceAdapterを初期化
    /// </summary>
    public UserPlanServiceAdapter(
        ILicenseManager licenseManager,
        ILogger<UserPlanServiceAdapter> logger)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // イベント購読のセットアップ
        SetupEventSubscriptions();

        _logger.LogDebug("UserPlanServiceAdapter初期化: CurrentPlan={Plan}", CurrentPlan);
    }

    /// <summary>
    /// イベント購読をセットアップ
    /// 将来的に複数のイベントを購読する場合の拡張ポイント
    /// </summary>
    private void SetupEventSubscriptions()
    {
        // ライセンス状態変更イベント → UserPlanChangedイベントに変換
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // 将来の拡張用:
        // _licenseManager.TokenUsageWarning += OnTokenUsageWarning;
        // _licenseManager.SessionInvalidated += OnSessionInvalidated;
    }

    /// <summary>
    /// イベント購読を解除
    /// </summary>
    private void TeardownEventSubscriptions()
    {
        _licenseManager.StateChanged -= OnLicenseStateChanged;
    }

    /// <inheritdoc/>
    public UserPlanDetails GetPlanDetails()
    {
        var state = _licenseManager.CurrentState;
        var planType = MapToUserPlanType(state.CurrentPlan);

        return new UserPlanDetails(
            PlanType: planType,
            PlanName: GetPlanDisplayName(state.CurrentPlan),
            Description: GetPlanDescription(state.CurrentPlan),
            MonthlyLimit: (int)Math.Min(state.MonthlyTokenLimit, int.MaxValue),
            CloudAccessEnabled: state.CurrentPlan.HasCloudAiAccess(),
            SubscriptionExpiryDate: state.ExpirationDate);
    }

    /// <summary>
    /// PlanTypeをUserPlanTypeにマッピング
    /// </summary>
    private static UserPlanType MapToUserPlanType(PlanType planType)
    {
        // Free以外はすべてPremiumとして扱う（後方互換性）
        return planType == PlanType.Free ? UserPlanType.Free : UserPlanType.Premium;
    }

    /// <summary>
    /// プラン表示名を取得
    /// </summary>
    private static string GetPlanDisplayName(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => "Free",
            PlanType.Standard => "Standard",
            PlanType.Pro => "Pro",
            PlanType.Premia => "Premia",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// プラン説明を取得
    /// </summary>
    private static string GetPlanDescription(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => "無料プラン - 広告表示あり、ローカル翻訳のみ",
            PlanType.Standard => "Standardプラン - 広告なし、ローカル翻訳のみ",
            PlanType.Pro => "Proプラン - クラウドAI翻訳 400万トークン/月",
            PlanType.Premia => "Premiaプラン - クラウドAI翻訳 800万トークン/月",
            _ => "不明なプラン"
        };
    }

    /// <summary>
    /// ライセンス状態変更イベントハンドラ
    /// </summary>
    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        var oldUserPlanType = MapToUserPlanType(e.OldState.CurrentPlan);
        var newUserPlanType = MapToUserPlanType(e.NewState.CurrentPlan);

        // UserPlanTypeレベルで変更があった場合のみイベントを発行
        if (oldUserPlanType != newUserPlanType)
        {
            _logger.LogInformation(
                "プラン変更検出（後方互換イベント発行）: {OldPlan} -> {NewPlan}",
                oldUserPlanType, newUserPlanType);

            PlanChanged?.Invoke(this, new UserPlanChangedEventArgs(oldUserPlanType, newUserPlanType));
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        TeardownEventSubscriptions();
        _disposed = true;

        _logger.LogDebug("UserPlanServiceAdapter disposed");
    }
}
