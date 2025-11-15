using Baketa.Core.UI.Fullscreen;
using Baketa.Core.UI.Monitors;
using Baketa.Infrastructure.Platform.Windows.Fullscreen;
using Baketa.Infrastructure.Platform.Windows.Monitors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// マルチモニターサポート用のDIモジュール
/// Windows固有のモニター管理機能とフルスクリーン検出機能を登録
/// </summary>
public static class MultiMonitorModule
{
    /// <summary>
    /// マルチモニターサポートサービスを登録
    /// </summary>
    /// <param name=\"services\">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddMultiMonitorSupport(this IServiceCollection services)
    {
        // モニターマネージャーをシングルトンとして登録
        services.AddSingleton<IMonitorManager>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<WindowsMonitorManager>>();
            return new WindowsMonitorManager(logger);
        });

        // フルスクリーンモードサービスをシングルトンとして登録
        services.AddSingleton<IFullscreenModeService>(serviceProvider =>
        {
            var monitorManager = serviceProvider.GetRequiredService<IMonitorManager>();
            var logger = serviceProvider.GetRequiredService<ILogger<WindowsFullscreenModeService>>();
            return new WindowsFullscreenModeService(monitorManager, logger);
        });

        return services;
    }
}
