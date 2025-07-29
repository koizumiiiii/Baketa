using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Abstractions.UI;
using Baketa.UI.Services;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// オーバーレイUIモジュール
/// AR翻訳オーバーレイシステムのDI登録
/// </summary>
public sealed class OverlayUIModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // ARTranslationOverlayManager - AR風翻訳オーバーレイ管理サービス
        services.AddSingleton<IARTranslationOverlayManager, ARTranslationOverlayManager>();
        services.AddSingleton<ARTranslationOverlayManager>();
    }
}