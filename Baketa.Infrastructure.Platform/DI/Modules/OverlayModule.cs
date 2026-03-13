using System;
using System.Runtime.Versioning;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.UI.Overlays;
using Baketa.Core.UI.Overlay;
using Baketa.Infrastructure.Platform.Windows.Overlay;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// オーバーレイ関連サービスのDIモジュール
///
/// 🎯 [OVERLAY_UNIFICATION] Option C: Complete Unification（Gemini推奨）
/// - 統一されたIOverlayManagerインターフェースによる抽象化
/// - Win32OverlayManagerがWindowsOverlayWindowManagerをラップ
/// - Application層がInfrastructure層の具象実装から完全に分離
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
        // 診断ログ用のロガーファクトリー取得（DIコンテナ初期化時のみ一時的に使用）
        using var tempProvider = services.BuildServiceProvider();
        var loggerFactory = tempProvider.GetService<ILoggerFactory>();
        var logger = loggerFactory?.CreateLogger("Baketa.Infrastructure.Platform.DI.Modules.OverlayModule")
            ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

        // 🔥 [WIN32_OVERLAY_MIGRATION] Win32 Layered Window Factory登録
        services.AddSingleton<ILayeredOverlayWindowFactory, LayeredOverlayWindowFactory>();

        // 🔥 [DWM_BLUR_IMPLEMENTATION] DWM Composition Window Factory登録
        services.AddSingleton<ICompositionOverlayWindowFactory, CompositionOverlayWindowFactory>();

        // 🔧 [OVERLAY_UNIFICATION] Win32オーバーレイウィンドウマネージャー登録
        // Infrastructure.Platform層の具象実装
        services.AddSingleton<WindowsOverlayWindowManager>();
        services.AddSingleton<IOverlayWindowManager>(provider =>
            provider.GetRequiredService<WindowsOverlayWindowManager>());

        // 🎯 [OVERLAY_UNIFICATION] 統一オーバーレイマネージャー登録
        // Application層が依存するIOverlayManager実装
        services.AddSingleton<IOverlayManager, Win32OverlayManager>();

        // [Issue #497] ファントムカーソルウィンドウファクトリ
        services.AddSingleton<Func<ILogger, IPhantomCursorWindowAdapter>>(
            _ => loggerArg => new PhantomCursorWindowAdapter(loggerArg));

        logger.LogInformation("✅ [OVERLAY_UNIFICATION] Win32オーバーレイシステム登録完了");
        logger.LogDebug("   - LayeredOverlayWindowFactory → ILayeredOverlayWindowFactory");
        logger.LogDebug("   - CompositionOverlayWindowFactory → ICompositionOverlayWindowFactory [DWM_BLUR_IMPLEMENTATION]");
        logger.LogDebug("   - WindowsOverlayWindowManager → IOverlayWindowManager");
        logger.LogDebug("   - Win32OverlayManager → IOverlayManager (統一インターフェース)");

        return services;
    }
}
