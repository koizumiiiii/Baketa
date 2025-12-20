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

        // 設定ウィンドウViewModel
        services.AddTransient<SettingsWindowViewModel>();

        // 一般設定ViewModel（ランタイム設定を読み込むファクトリ登録）
        services.AddTransient<GeneralSettingsViewModel>(provider =>
        {
            var settingsService = provider.GetRequiredService<ISettingsService>();
            var eventAggregator = provider.GetRequiredService<Core.Abstractions.Events.IEventAggregator>();
            var localizationService = provider.GetService<ILocalizationService>();
            var changeTracker = provider.GetService<ISettingsChangeTracker>();
            var logger = provider.GetService<ILogger<GeneralSettingsViewModel>>();

            // ランタイムで設定を読み込む
            var generalSettings = settingsService.GetCategorySettings<GeneralSettings>() ?? new GeneralSettings();
            var translationSettings = settingsService.GetCategorySettings<TranslationSettings>() ?? new TranslationSettings();

            return new GeneralSettingsViewModel(
                generalSettings,
                eventAggregator,
                localizationService,
                changeTracker,
                logger,
                translationSettings);
        });

        // アカウント設定ViewModel
        services.AddTransient<AccountSettingsViewModel>();

        // ライセンス情報ViewModel
        services.AddTransient<LicenseInfoViewModel>();

        return services;
    }
}
