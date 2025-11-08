using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Abstractions.UI;
using Baketa.UI.Services;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// オーバーレイUIモジュール
/// インプレース翻訳オーバーレイシステムのDI登録
/// </summary>
public sealed class OverlayUIModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // 🔥 [OVERLAY_UNIFICATION] Phase 3 - Option C完全統一
        // IInPlaceTranslationOverlayManager → IOverlayManager 移行完了
        // SimpleInPlaceOverlayManagerは廃止、Win32OverlayManagerに統一

        // ❌ [DEPRECATED] 旧実装を無効化 - すべてIOverlayManagerに移行
        // services.AddSingleton<SimpleInPlaceOverlayManager>();
        // services.AddSingleton<IInPlaceTranslationOverlayManager>(serviceProvider =>
        //     serviceProvider.GetRequiredService<SimpleInPlaceOverlayManager>());

        Console.WriteLine("✅ [OVERLAY_UNIFICATION] IOverlayManager統一完了 - SimpleInPlaceOverlayManager廃止");
    }
}