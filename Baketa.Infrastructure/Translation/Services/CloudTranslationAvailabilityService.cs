using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Cloud翻訳機能の利用可否を統合管理するサービス実装
/// Issue #273: ライセンス状態とユーザー設定を統合して一貫した可用性判定を提供
/// </summary>
public sealed class CloudTranslationAvailabilityService : ICloudTranslationAvailabilityService, IDisposable
{
    private readonly ILicenseManager _licenseManager;
    private readonly IBonusTokenService? _bonusTokenService;
    private readonly IUnifiedSettingsService _unifiedSettingsService;
    private readonly ILogger<CloudTranslationAvailabilityService> _logger;
    private readonly object _stateLock = new();

    private bool _isEntitled;
    private bool _isPreferred;
    private bool _wasEnabled;
    private bool _disposed;

    public CloudTranslationAvailabilityService(
        ILicenseManager licenseManager,
        IUnifiedSettingsService unifiedSettingsService,
        ILogger<CloudTranslationAvailabilityService> logger,
        IBonusTokenService? bonusTokenService = null)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _bonusTokenService = bonusTokenService;

        // 初期状態を設定
        // LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
        _isEntitled = _licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation);
        var currentSettings = _unifiedSettingsService.GetTranslationSettings();
        _isPreferred = !currentSettings.UseLocalEngine;
        _wasEnabled = IsEffectivelyEnabled;

        // ライセンス状態変更の監視
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // 翻訳設定変更の監視（IUnifiedSettingsService経由）
        _unifiedSettingsService.SettingsChanged += OnSettingsChanged;

        _logger.LogInformation(
            "CloudTranslationAvailabilityService 初期化完了: IsEntitled={IsEntitled}, IsPreferred={IsPreferred}, IsEffectivelyEnabled={IsEffectivelyEnabled}",
            _isEntitled, _isPreferred, IsEffectivelyEnabled);
    }

    /// <inheritdoc />
    public bool IsEntitled { get { lock (_stateLock) { return _isEntitled; } } }

    /// <inheritdoc />
    public bool IsPreferred { get { lock (_stateLock) { return _isPreferred; } } }

    /// <inheritdoc />
    public bool IsEffectivelyEnabled { get { lock (_stateLock) { return _isEntitled && _isPreferred; } } }

    /// <inheritdoc />
    public event EventHandler<CloudTranslationAvailabilityChangedEventArgs>? AvailabilityChanged;

    /// <inheritdoc />
    public async Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Cloud翻訳可用性を再評価します");

        // ライセンス状態を強制更新
        await _licenseManager.RefreshStateAsync(cancellationToken).ConfigureAwait(false);

        // LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
        var newEntitled = _licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation);
        var currentSettings = _unifiedSettingsService.GetTranslationSettings();
        var newPreferred = !currentSettings.UseLocalEngine;

        UpdateState(newEntitled, newPreferred, CloudTranslationChangeReason.Initialization);
    }

    /// <inheritdoc />
    public async Task SetPreferredAsync(bool preferred, CancellationToken cancellationToken = default)
    {
        bool currentIsPreferred;
        bool currentIsEntitled;
        lock (_stateLock)
        {
            currentIsPreferred = _isPreferred;
            currentIsEntitled = _isEntitled;
        }

        if (currentIsPreferred == preferred)
        {
            _logger.LogDebug("Cloud翻訳希望設定は変更なし: {Preferred}", preferred);
            return;
        }

        _logger.LogInformation("Cloud翻訳希望設定を変更: {OldValue} → {NewValue}", currentIsPreferred, preferred);

        // 現在の設定をクローンして更新
        var currentSettings = _unifiedSettingsService.GetTranslationSettings();
        if (currentSettings is not TranslationSettings concreteSettings)
        {
            _logger.LogWarning("設定のクローンに失敗しました: 具象型TranslationSettingsへのキャストに失敗");
            return;
        }
        var settings = concreteSettings.Clone();

        // [Issue #280+#281] UseLocalEngine と EnableCloudAiTranslation を両方更新
        settings.UseLocalEngine = !preferred;
        settings.EnableCloudAiTranslation = preferred;
        settings.DefaultEngine = (preferred && currentIsEntitled) ? TranslationEngine.Gemini : TranslationEngine.NLLB200;

        await _unifiedSettingsService.UpdateTranslationSettingsAsync(settings, cancellationToken).ConfigureAwait(false);

        if (preferred && currentIsEntitled)
        {
            _logger.LogInformation("DefaultEngine を Gemini に自動設定");
        }
        else if (!preferred)
        {
            _logger.LogInformation("DefaultEngine を NLLB200 に自動設定");
        }

        UpdateState(currentIsEntitled, preferred, CloudTranslationChangeReason.UserPreferenceChanged);
    }

    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        bool wasEntitled;
        bool currentPreferred;
        lock (_stateLock)
        {
            wasEntitled = _isEntitled;
            currentPreferred = _isPreferred;
        }

        // LicenseManager.IsFeatureAvailableでプラン・ボーナストークン両方をチェック
        var newEntitled = _licenseManager.IsFeatureAvailable(FeatureType.CloudAiTranslation);
        var reason = MapLicenseChangeReason(e.Reason);

        _logger.LogInformation(
            "ライセンス状態変更検出: OldPlan={OldPlan}, NewPlan={NewPlan}, NewEntitled={NewEntitled}, Reason={Reason}",
            e.OldState.CurrentPlan, e.NewState.CurrentPlan, newEntitled, reason);

        UpdateState(newEntitled, currentPreferred, reason);

        // 利用資格を得た場合、ユーザー設定も自動的に有効化（新規アップグレードのUX向上）
        if (newEntitled && !wasEntitled)
        {
            _logger.LogInformation("利用資格を取得したため、Cloud翻訳を自動有効化します");
            FireAndForgetSetPreferredAsync(true);
        }
    }

    /// <summary>
    /// Fire-and-forget呼び出しを安全に行うヘルパーメソッド
    /// </summary>
    private async void FireAndForgetSetPreferredAsync(bool preferred)
    {
        try
        {
            await SetPreferredAsync(preferred, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud翻訳の自動有効化中に予期せぬエラーが発生しました。");
        }
    }

    private void OnSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 翻訳設定の変更のみを処理
        if (e.SettingsType != SettingsType.Translation)
        {
            return;
        }

        bool currentIsPreferred;
        bool currentIsEntitled;
        lock (_stateLock)
        {
            currentIsPreferred = _isPreferred;
            currentIsEntitled = _isEntitled;
        }

        var currentSettings = _unifiedSettingsService.GetTranslationSettings();
        // [Issue #280+#281] UseLocalEngineで判定
        var newPreferred = !currentSettings.UseLocalEngine;

        if (currentIsPreferred != newPreferred)
        {
            _logger.LogDebug("翻訳設定変更検出: IsPreferred {OldValue} → {NewValue}", currentIsPreferred, newPreferred);
            UpdateState(currentIsEntitled, newPreferred, CloudTranslationChangeReason.UserPreferenceChanged);
        }
    }

    private void UpdateState(bool newEntitled, bool newPreferred, CloudTranslationChangeReason reason)
    {
        CloudTranslationAvailabilityChangedEventArgs? eventArgs = null;

        lock (_stateLock)
        {
            var wasEnabled = _wasEnabled;
            _isEntitled = newEntitled;
            _isPreferred = newPreferred;
            var nowEnabled = _isEntitled && _isPreferred;

            if (wasEnabled != nowEnabled)
            {
                _wasEnabled = nowEnabled;

                _logger.LogInformation(
                    "Cloud翻訳可用性が変更されました: {WasEnabled} → {NowEnabled} (Reason: {Reason})",
                    wasEnabled, nowEnabled, reason);

                eventArgs = new CloudTranslationAvailabilityChangedEventArgs
                {
                    WasEnabled = wasEnabled,
                    IsEnabled = nowEnabled,
                    Reason = reason,
                    IsEntitled = _isEntitled,
                    IsPreferred = _isPreferred
                };
            }
        }

        // イベント発火はロック外で行う（デッドロック防止）
        if (eventArgs is not null)
        {
            AvailabilityChanged?.Invoke(this, eventArgs);
        }
    }

    /// <summary>
    /// LicenseChangeReason を CloudTranslationChangeReason にマッピング
    /// </summary>
    private static CloudTranslationChangeReason MapLicenseChangeReason(LicenseChangeReason licenseReason)
    {
        return licenseReason switch
        {
            LicenseChangeReason.InitialLoad or LicenseChangeReason.CacheLoad or LicenseChangeReason.ServerRefresh
                => CloudTranslationChangeReason.Initialization,
            LicenseChangeReason.PlanUpgrade => CloudTranslationChangeReason.PlanUpgrade,
            LicenseChangeReason.PlanDowngrade or LicenseChangeReason.SubscriptionExpired
                => CloudTranslationChangeReason.PlanDowngrade,
            LicenseChangeReason.PromotionApplied => CloudTranslationChangeReason.PromotionApplied,
            LicenseChangeReason.PromotionExpired => CloudTranslationChangeReason.PromotionExpired,
            LicenseChangeReason.Logout or LicenseChangeReason.SessionInvalidation
                => CloudTranslationChangeReason.Logout,
            _ => CloudTranslationChangeReason.Initialization
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _licenseManager.StateChanged -= OnLicenseStateChanged;
        _unifiedSettingsService.SettingsChanged -= OnSettingsChanged;
    }
}
