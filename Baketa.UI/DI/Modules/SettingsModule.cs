using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.UI.Services;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// 設定UI関連サービスのDIモジュール
/// </summary>
public static class SettingsModule
{
    /// <summary>
    /// 設定UI関連サービスをDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>設定されたサービスコレクション</returns>
    public static IServiceCollection AddSettingsServices(this IServiceCollection services)
    {
        // 設定変更追跡サービス
        services.AddSingleton<ISettingsChangeTracker, SettingsChangeTracker>();

        // 設定ウィンドウViewModel（明示的ファクトリ登録でIUnifiedSettingsService注入を保証）
        services.AddTransient<SettingsWindowViewModel>(provider =>
        {
            var serviceProvider = provider;
            var changeTracker = provider.GetRequiredService<ISettingsChangeTracker>();
            var eventAggregator = provider.GetRequiredService<Core.Abstractions.Events.IEventAggregator>();
            var settingsService = provider.GetRequiredService<ISettingsService>();
            var localizationService = provider.GetService<ILocalizationService>();
            var unifiedSettingsService = provider.GetService<IUnifiedSettingsService>();
            var logger = provider.GetService<ILogger<SettingsWindowViewModel>>();

            return new SettingsWindowViewModel(
                serviceProvider,
                changeTracker,
                eventAggregator,
                settingsService,
                localizationService,
                unifiedSettingsService,
                logger);
        });

        // 一般設定ViewModel（ランタイム設定を読み込むファクトリ登録）
        services.AddTransient<GeneralSettingsViewModel>(provider =>
        {
            var settingsService = provider.GetRequiredService<ISettingsService>();
            var eventAggregator = provider.GetRequiredService<Core.Abstractions.Events.IEventAggregator>();
            var localizationService = provider.GetService<ILocalizationService>();
            var changeTracker = provider.GetService<ISettingsChangeTracker>();
            var logger = provider.GetService<ILogger<GeneralSettingsViewModel>>();
            var licenseManager = provider.GetService<ILicenseManager>();
            var unifiedSettingsService = provider.GetService<IUnifiedSettingsService>();
            var tokenTracker = provider.GetService<Core.Translation.Abstractions.ITokenConsumptionTracker>();
            var bonusTokenService = provider.GetService<Core.Abstractions.License.IBonusTokenService>();

            // ランタイムで設定を読み込む
            var generalSettings = settingsService.GetCategorySettings<GeneralSettings>() ?? new GeneralSettings();

            // [Issue #243] IUnifiedSettingsServiceから最新の翻訳設定を取得
            // プロモーション適用後の設定変更が反映されるようにする
            TranslationSettings translationSettings;
            if (unifiedSettingsService != null)
            {
                var unifiedSettings = unifiedSettingsService.GetTranslationSettings();
                translationSettings = new TranslationSettings
                {
                    DefaultSourceLanguage = unifiedSettings.DefaultSourceLanguage,
                    DefaultTargetLanguage = unifiedSettings.DefaultTargetLanguage,
                    AutoDetectSourceLanguage = unifiedSettings.AutoDetectSourceLanguage,
                    TimeoutSeconds = unifiedSettings.TimeoutMs / 1000,
                    OverlayFontSize = unifiedSettings.OverlayFontSize,
                    EnableCloudAiTranslation = unifiedSettings.EnableCloudAiTranslation,
                    // [Issue #280+#281] UseLocalEngineも反映
                    UseLocalEngine = unifiedSettings.UseLocalEngine
                };
            }
            else
            {
                translationSettings = settingsService.GetCategorySettings<TranslationSettings>() ?? new TranslationSettings();
            }

            return new GeneralSettingsViewModel(
                generalSettings,
                eventAggregator,
                localizationService,
                changeTracker,
                logger,
                translationSettings,
                licenseManager,
                bonusTokenService,
                unifiedSettingsService,
                tokenTracker);
        });

        // アカウント設定ViewModel
        services.AddTransient<AccountSettingsViewModel>();

        // ライセンス情報ViewModel
        services.AddTransient<LicenseInfoViewModel>();

        return services;
    }
}
