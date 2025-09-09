using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.BatchProcessing;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// TimedChunkAggregator専用モジュール
/// 新設定システムを使用した完全自律型実装
/// 既存のInfrastructureModuleから完全分離
/// </summary>
public class TimedAggregatorModule : ConfigurableServiceModuleBase
{
    protected override void RegisterConfigurableServices(IServiceCollection services)
    {
        Console.WriteLine("🚀 [NEW_CONFIG] TimedAggregatorModule - 新設定システムによる登録開始");
        
        // 設定デバッグ情報出力
        LogConfigurationDebug();
        
        // TimedAggregatorSettings の型安全な設定登録
        RegisterSettings<TimedAggregatorSettings>(services);
        
        // CoordinateBasedLineBreakProcessorの登録
        services.AddSingleton<CoordinateBasedLineBreakProcessor>();
        Console.WriteLine("✅ [NEW_CONFIG] CoordinateBasedLineBreakProcessor登録完了");
        
        // TimedChunkAggregatorの登録（Singleton - バッファ状態維持のため）
        services.AddSingleton<TimedChunkAggregator>();
        Console.WriteLine("✅ [NEW_CONFIG] TimedChunkAggregator登録完了 - Singleton（バッファ状態維持）");
        
        // EnhancedBatchOcrIntegrationServiceの登録（Singleton - TimedChunkAggregator連携のため）
        services.AddSingleton<EnhancedBatchOcrIntegrationService>();
        Console.WriteLine("✅ [NEW_CONFIG] EnhancedBatchOcrIntegrationService登録完了");
        
        Console.WriteLine("🎯 [NEW_CONFIG] TimedAggregatorModule - 新設定システム統合完了");
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // EnhancedBatchOcrIntegrationServiceの依存関係は自動的にDIコンテナが解決
        // ここでは直接的な依存関係のみを指定
        yield return typeof(Baketa.Infrastructure.DI.Modules.InfrastructureModule); // ITranslationService等の基本依存関係
    }
}