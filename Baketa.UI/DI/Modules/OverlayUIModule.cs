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
        // InPlaceTranslationOverlayManagerクラスの登録
        services.AddSingleton<InPlaceTranslationOverlayManager>();
        
        // 🔧 [PHASE18] IInPlaceTranslationOverlayManagerインターフェース実装登録追加
        services.AddSingleton<IInPlaceTranslationOverlayManager>(serviceProvider =>
            serviceProvider.GetRequiredService<InPlaceTranslationOverlayManager>());
        
        Console.WriteLine("✅ [OVERLAY_UI] IInPlaceTranslationOverlayManager実装登録完了");
    }
}