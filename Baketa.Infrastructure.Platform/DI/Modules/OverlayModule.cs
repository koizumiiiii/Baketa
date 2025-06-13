using System.Runtime.Versioning;
using Baketa.Infrastructure.Platform.Windows.Overlay;
using Baketa.Core.UI.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// オーバーレイ関連サービスのDIモジュール
/// </summary>
[SupportedOSPlatform("windows")]
public static class OverlayModule
{
    /// <summary>
    /// オーバーレイ関連サービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection RegisterOverlayServices(this IServiceCollection services)
    {
        // Windows固有の実装を登録
        services.AddSingleton<WindowsOverlayWindowManager>();
        
        // プラットフォーム固有実装をインターフェースに登録
        services.AddSingleton<IOverlayWindowManager>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<WindowsOverlayWindowManager>>();
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new WindowsOverlayWindowManager(logger, loggerFactory);
        });
        
        return services;
    }
}