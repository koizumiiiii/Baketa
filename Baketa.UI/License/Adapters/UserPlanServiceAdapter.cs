using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
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
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly IUnifiedSettingsService? _unifiedSettingsService;
    private readonly ILogger<UserPlanServiceAdapter> _logger;
    private bool _disposed;

    /// <inheritdoc/>
    public UserPlanType CurrentPlan => MapToUserPlanType(_licenseManager.CurrentState.CurrentPlan);

    /// <inheritdoc/>
    /// <remarks>
    /// [Issue #280+#281] プランまたはボーナストークンでCloud AI利用可能
    /// </remarks>
    public bool CanUseCloudOnlyEngine =>
        (_licenseManager.CurrentState.CurrentPlan.HasCloudAiAccess() &&
         !_licenseManager.CurrentState.IsQuotaExceeded) ||
        (_bonusTokenService?.GetTotalRemainingTokens() ?? 0) > 0;

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
        ILogger<UserPlanServiceAdapter> logger,
        IBonusTokenService? bonusTokenService = null,
        IUnifiedSettingsService? unifiedSettingsService = null)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bonusTokenService = bonusTokenService;
        _unifiedSettingsService = unifiedSettingsService;

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
    // Issue #125: Standardプラン廃止
    private static string GetPlanDisplayName(PlanType planType)
    {
        // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
        return planType switch
        {
            PlanType.Free => "Free",
            PlanType.Pro => "Pro",
            PlanType.Premium => "Premium",
            PlanType.Ultimate => "Ultimate",
            _ => "Unknown"
        };
    }

    /// <summary>
    /// プラン説明を取得
    /// </summary>
    // Issue #125: Standardプラン廃止、広告機能廃止
    // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
    private static string GetPlanDescription(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => "無料プラン - ローカル翻訳のみ",
            PlanType.Pro => "Proプラン - クラウドAI翻訳 1,000万トークン/月",
            PlanType.Premium => "Premiumプラン - クラウドAI翻訳 2,000万トークン/月",
            PlanType.Ultimate => "Ultimateプラン - クラウドAI翻訳 5,000万トークン/月",
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

        // [Issue #243] プロモーション適用時にCloud AI翻訳を自動有効化
        if (newUserPlanType == UserPlanType.Premium && e.Reason == LicenseChangeReason.PromotionApplied)
        {
            _ = EnableCloudAiTranslationAsync();
        }
    }

    /// <summary>
    /// [Issue #243] Cloud AI翻訳を有効化
    /// プロモーションコード適用時に自動でCloud AI翻訳を有効にする
    /// </summary>
    private async Task EnableCloudAiTranslationAsync()
    {
        if (_unifiedSettingsService == null)
        {
            _logger.LogWarning("[Issue #243] UnifiedSettingsServiceがnullのためCloud AI翻訳を有効化できません");
            return;
        }

        try
        {
            var currentSettings = _unifiedSettingsService.GetTranslationSettings();

            // 既に有効な場合はスキップ
            if (currentSettings.EnableCloudAiTranslation)
            {
                _logger.LogDebug("Cloud AI翻訳は既に有効です");
                return;
            }

            // 新しい設定を作成（EnableCloudAiTranslation = true）
            var newSettings = new CloudAiEnabledTranslationSettings(currentSettings);
            await _unifiedSettingsService.UpdateTranslationSettingsAsync(newSettings).ConfigureAwait(false);

            _logger.LogInformation("[Issue #243] Cloud AI翻訳を自動で有効化しました（プランアップグレード）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud AI翻訳の自動有効化に失敗しました");
        }
    }

    /// <summary>
    /// [Issue #243] Cloud AI翻訳を有効化した設定ラッパー
    /// </summary>
    private sealed class CloudAiEnabledTranslationSettings : ITranslationSettings
    {
        private readonly ITranslationSettings _baseSettings;

        public CloudAiEnabledTranslationSettings(ITranslationSettings baseSettings)
        {
            _baseSettings = baseSettings;
        }

        public bool AutoDetectSourceLanguage => _baseSettings.AutoDetectSourceLanguage;
        public string DefaultSourceLanguage => _baseSettings.DefaultSourceLanguage;
        public string DefaultTargetLanguage => _baseSettings.DefaultTargetLanguage;
        public string DefaultEngine => _baseSettings.DefaultEngine;
        public bool UseLocalEngine => false; // Cloud AI使用時はfalse
        public double ConfidenceThreshold => _baseSettings.ConfidenceThreshold;
        public int TimeoutMs => _baseSettings.TimeoutMs;
        public int OverlayFontSize => _baseSettings.OverlayFontSize;
        public bool EnableCloudAiTranslation => true; // 有効化
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
