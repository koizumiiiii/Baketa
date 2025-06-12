using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Application.Services.Capture;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// キャプチャサービス関連のDIモジュール
/// </summary>
public class CaptureModule : EnhancedServiceModuleBase
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // キャプチャサービスの実装を登録
        services.AddSingleton<AdvancedCaptureService>();
        
        // 両方のインターフェースが同じインスタンスを参照するように設定
        services.AddSingleton<ICaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
        services.AddSingleton<IAdvancedCaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
        
        // TODO: 以下のサービスはインターフェース定義後に有効化
        // services.AddSingleton<IGameProfileManager, GameProfileManager>();
        // services.AddSingleton<IGameDetectionService, GameDetectionService>();
        
        Logger?.LogDebug("キャプチャサービスを登録しました");
    }
    
    /// <summary>
    /// モジュールの依存関係を取得します
    /// </summary>
    /// <returns>依存するモジュールのタイプ配列</returns>
    public override Type[] GetDependencies()
    {
        return
        [
            // プラットフォームモジュール（IGdiScreenCapturer用）
            typeof(Baketa.Infrastructure.Platform.DI.Modules.PlatformModule)
            // TODO: インフラストラクチャモジュールやイベントモジュールは定義後に追加
        ];
    }
    
    /// <summary>
    /// モジュールの優先度を取得します
    /// </summary>
    public override int Priority => 100; // 標準的な優先度
    
    /// <summary>
    /// モジュール名を取得します
    /// </summary>
    public override string ModuleName => "Capture Module";
    
    /// <summary>
    /// モジュールの説明を取得します
    /// </summary>
    public override string Description => "画面キャプチャサービスとその最適化機能を提供します";
}
