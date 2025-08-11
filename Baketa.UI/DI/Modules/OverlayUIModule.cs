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
        // InPlaceTranslationOverlayManager - インプレース翻訳オーバーレイ管理サービス
        services.AddSingleton<IInPlaceTranslationOverlayManager, InPlaceTranslationOverlayManager>();
        services.AddSingleton<InPlaceTranslationOverlayManager>();
    }
}