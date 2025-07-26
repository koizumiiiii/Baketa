using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Abstractions.UI;
using Baketa.UI.Services;

namespace Baketa.UI.DI.Modules;

/// <summary>
/// オーバーレイUIモジュール
/// Phase 2-C: 複数ウィンドウオーバーレイシステムのDI登録
/// </summary>
public sealed class OverlayUIModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // MultiWindowOverlayManager - 複数ウィンドウオーバーレイ管理サービス
        services.AddSingleton<IMultiWindowOverlayManager, MultiWindowOverlayManager>();
    }
}