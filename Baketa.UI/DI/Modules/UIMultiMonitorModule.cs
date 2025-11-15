using Baketa.UI.Monitors;
using Baketa.UI.Overlay.MultiMonitor;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// UI層マルチモニターサポート用のDIモジュール
/// AvaloniaUI統合、アダプター機能、オーバーレイマネージャーを登録
/// </summary>
public static class UIMultiMonitorModule
{
    /// <summary>
    /// UI層マルチモニターサポートサービスを登録
    /// </summary>
    /// <param name=\"services\">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddUIMultiMonitorSupport(this IServiceCollection services)
    {
        // AvaloniaUI統合アダプターをシングルトンとして登録
        services.AddSingleton<AvaloniaMultiMonitorAdapter>();

        // マルチモニター対応オーバーレイマネージャーをシングルトンとして登録
        services.AddSingleton<MultiMonitorOverlayManager>();

        return services;
    }
}
