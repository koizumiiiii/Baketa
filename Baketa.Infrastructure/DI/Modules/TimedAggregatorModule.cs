using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.PostProcessing;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Services.Coordinates;

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

        // 🎯 [P0_COORDINATE_TRANSFORM] 座標変換サービスの登録
        services.AddSingleton<ICoordinateTransformationService, CoordinateTransformationService>();
        Console.WriteLine("✅ [P0_COORDINATE_TRANSFORM] CoordinateTransformationService登録完了 - ROI→スクリーン座標変換");

        // CoordinateBasedLineBreakProcessorの登録
        services.AddSingleton<CoordinateBasedLineBreakProcessor>();
        Console.WriteLine("✅ [NEW_CONFIG] CoordinateBasedLineBreakProcessor登録完了");
        
        // TimedChunkAggregatorの登録（Singleton - バッファ状態維持のため）
        services.AddSingleton<TimedChunkAggregator>();
        Console.WriteLine("✅ [NEW_CONFIG] TimedChunkAggregator登録完了 - Singleton（バッファ状態維持）");
        
        // EnhancedBatchOcrIntegrationServiceの登録（Singleton - TimedChunkAggregator連携のため）
        services.AddSingleton<EnhancedBatchOcrIntegrationService>();
        Console.WriteLine("✅ [NEW_CONFIG] EnhancedBatchOcrIntegrationService登録完了");

        // Phase 26-4: ITextChunkAggregatorServiceインターフェース登録 - Clean Architecture対応
        services.AddSingleton<ITextChunkAggregatorService>(provider =>
            provider.GetRequiredService<EnhancedBatchOcrIntegrationService>());
        Console.WriteLine("✅ [PHASE26] ITextChunkAggregatorService → EnhancedBatchOcrIntegrationService マッピング完了");

        Console.WriteLine("🎯 [NEW_CONFIG] TimedAggregatorModule - 新設定システム統合完了");
    }
    
    /// <summary>
    /// このモジュールが依存する他のモジュールの型を取得します。
    /// </summary>
    /// <returns>依存モジュールの型のコレクション</returns>
    public override IEnumerable<Type> GetDependentModules()
    {
        // 🔧 UltraThink Phase 29: InfrastructureModuleへの依存を削除（循環依存解消）
        // InfrastructureModuleから先に読み込まれるため、ITranslationService等は既に登録済み
        yield return typeof(Baketa.Core.DI.Modules.CoreModule); // 基本的な設定システムのみ依存
    }
}