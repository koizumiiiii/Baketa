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
        // 🔥 [PHASE3_REFACTORING] SimpleInPlaceTranslationOverlayManagerに切り替え
        // 旧実装（InPlaceTranslationOverlayManager）はメソッド本体が実行されない異常により削除
        services.AddSingleton<SimpleInPlaceOverlayManager>();

        // IInPlaceTranslationOverlayManagerインターフェース実装登録
        services.AddSingleton<IInPlaceTranslationOverlayManager>(serviceProvider =>
            serviceProvider.GetRequiredService<SimpleInPlaceOverlayManager>());

        Console.WriteLine("✅ [OVERLAY_UI] SimpleInPlaceOverlayManager登録完了（Phase 3 Refactoring）");
    }
}