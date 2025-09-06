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
        // ✅ Phase 16統一: IInPlaceTranslationOverlayManagerはAvaloniaOverlayRendererが実装
        // Legacy直接登録を除去し、依存関係として必要なクラスのみ登録
        
        // InPlaceTranslationOverlayManager - AvaloniaOverlayRendererの依存関係として必要
        services.AddSingleton<InPlaceTranslationOverlayManager>();
        
        // 📝 注記: IInPlaceTranslationOverlayManagerインターフェースの実装は
        // Phase16UIOverlayModuleのAvaloniaOverlayRendererが統一提供
        Console.WriteLine("🔄 [OVERLAY_UI] IInPlaceTranslationOverlayManager実装をPhase16統一システムに委任");
    }
}