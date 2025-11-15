using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.DI;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Infrastructure.OCR.PostProcessing;
using Microsoft.Extensions.DependencyInjection;

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
        Console.WriteLine("🔧 [PHASE12.2_DIAG] TimedAggregatorModule.RegisterConfigurableServices() 開始");

        try
        {
            // 設定デバッグ情報出力
            Console.WriteLine("🔧 [PHASE12.2_DIAG] LogConfigurationDebug() 実行直前");
            LogConfigurationDebug();
            Console.WriteLine("✅ [PHASE12.2_DIAG] LogConfigurationDebug() 完了");

            // TimedAggregatorSettings の型安全な設定登録
            Console.WriteLine("🔧 [PHASE12.2_DIAG] RegisterSettings<TimedAggregatorSettings>() 実行直前");
            RegisterSettings<TimedAggregatorSettings>(services);
            Console.WriteLine("✅ [PHASE12.2_DIAG] RegisterSettings<TimedAggregatorSettings>() 完了");

            // 🔥 [PHASE2.1_CLEAN_ARCH] CoordinateTransformationService登録はPlatformModuleに移動
            // Clean Architecture準拠: Windows固有API依存サービスはPlatform層で登録

            // CoordinateBasedLineBreakProcessorの登録
            Console.WriteLine("🔧 [PHASE12.2_DIAG] CoordinateBasedLineBreakProcessor登録直前");
            services.AddSingleton<CoordinateBasedLineBreakProcessor>();
            Console.WriteLine("✅ [NEW_CONFIG] CoordinateBasedLineBreakProcessor登録完了");

            // TimedChunkAggregatorの登録（Singleton - バッファ状態維持のため）
            Console.WriteLine("🔧 [PHASE12.2_DIAG] TimedChunkAggregator登録直前");
            services.AddSingleton<TimedChunkAggregator>();
            Console.WriteLine("✅ [NEW_CONFIG] TimedChunkAggregator登録完了 - Singleton（バッファ状態維持）");

            // EnhancedBatchOcrIntegrationServiceの登録（Singleton - TimedChunkAggregator連携のため）
            Console.WriteLine("🔧 [PHASE12.2_DIAG] EnhancedBatchOcrIntegrationService登録直前");
            services.AddSingleton<EnhancedBatchOcrIntegrationService>();
            Console.WriteLine("✅ [NEW_CONFIG] EnhancedBatchOcrIntegrationService登録完了");

            // Phase 26-4: ITextChunkAggregatorServiceインターフェース登録 - Clean Architecture対応
            Console.WriteLine("🔧 [PHASE12.2_DIAG] ITextChunkAggregatorService登録直前");
            services.AddSingleton<ITextChunkAggregatorService>(provider =>
                provider.GetRequiredService<EnhancedBatchOcrIntegrationService>());
            Console.WriteLine("✅ [PHASE26] ITextChunkAggregatorService → EnhancedBatchOcrIntegrationService マッピング完了");

            Console.WriteLine("🎯 [NEW_CONFIG] TimedAggregatorModule - 新設定システム統合完了");
            Console.WriteLine("✅ [PHASE12.2_DIAG] TimedAggregatorModule.RegisterConfigurableServices() 完全完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE12.2_DIAG] TimedAggregatorModule.RegisterConfigurableServices() 失敗: {ex.GetType().Name}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] Message: {ex.Message}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] StackTrace: {ex.StackTrace}");
            throw;
        }
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
