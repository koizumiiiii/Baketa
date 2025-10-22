using System.Runtime.Versioning;
using Baketa.Infrastructure.Platform.Windows.Overlay;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// オーバーレイ関連サービスのDIモジュール
///
/// 🎯 [WIN32_OVERLAY_MIGRATION] Phase 1: Win32 Layered Window統合
/// - ILayeredOverlayWindowFactory によるファクトリーパターン
/// - Avalonia依存を完全排除し、OS-nativeウィンドウシステムに移行
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
        // 🔥 [WIN32_OVERLAY_MIGRATION] Win32 Layered Window Factory登録
        services.AddSingleton<ILayeredOverlayWindowFactory, LayeredOverlayWindowFactory>();

        return services;
    }
}