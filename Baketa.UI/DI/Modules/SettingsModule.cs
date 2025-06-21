using Microsoft.Extensions.DependencyInjection;
using Baketa.UI.Services;
using Baketa.UI.ViewModels;

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

        return services;
    }
}
