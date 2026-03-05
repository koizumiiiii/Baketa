using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Processing;
using Baketa.Infrastructure.Processing.Strategies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// SmartProcessingPipelineService および関連戦略のDI登録拡張メソッド
/// Gemini分析により特定されたOCR処理パイプライン停止問題の修正
/// </summary>
public static class ProcessingServiceExtensions
{
    /// <summary>
    /// Processing関連サービスをDIコンテナに登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>サービスコレクション</returns>
    public static IServiceCollection AddProcessingServices(this IServiceCollection services, IConfiguration? configuration = null)
    {
        // 🎯 UltraThink Phase 21 修正: OCR処理パイプライン復旧のためのDI登録

        // 1. PipelineExecutionManager を登録（Strategy A: 排他制御実装）
        services.AddSingleton<IPipelineExecutionManager, PipelineExecutionManager>();

        // [Issue #500] Detection-Onlyフィルタ用キャッシュ（Singleton: サイクル間で矩形を保持）
        services.AddSingleton<IDetectionBoundsCache, DetectionBoundsCache>();

        // [Issue #500] ImageChangeDetectionSettings をappsettings.jsonからバインド
        var imageChangeSection = configuration?.GetSection("ImageChangeDetection");
        if (imageChangeSection?.Exists() == true)
        {
            services.Configure<ImageChangeDetectionSettings>(imageChangeSection);
        }
        else
        {
            services.Configure<ImageChangeDetectionSettings>(options => { });
        }

        // 2. SmartProcessingPipelineService本体を登録
        services.AddSingleton<ISmartProcessingPipelineService, SmartProcessingPipelineService>();

        // 3. 処理戦略をすべて登録（IEnumerable<IProcessingStageStrategy>として注入される）
        services.AddTransient<IProcessingStageStrategy, OcrExecutionStageStrategy>();
        // 🔥 [OLD_FLOW_REMOVAL] TranslationExecutionStageStrategy削除 - Phase 12.2新アーキテクチャ移行完了
        // 理由: CoordinateBasedTranslationService + AggregatedChunksReadyEventHandlerに統一
        services.AddSingleton<IProcessingStageStrategy, ImageChangeDetectionStageStrategy>();
        services.AddTransient<IProcessingStageStrategy, TextChangeDetectionStageStrategy>();

        // 4. デバッグ用ログ出力
        services.AddSingleton<IServiceCollection>(provider =>
        {
            // サービス登録確認のログ出力
            Console.WriteLine("🔧 [STRATEGY_A_FIX] ProcessingServices登録完了 - PipelineExecutionManager + SmartProcessingPipelineService + 3戦略");
            Console.WriteLine("🔧 [STRATEGY_A_FIX]   - IPipelineExecutionManager → PipelineExecutionManager (排他制御)");
            Console.WriteLine("🔧 [STRATEGY_A_FIX]   - ISmartProcessingPipelineService → SmartProcessingPipelineService");
            Console.WriteLine("🔧 [STRATEGY_A_FIX]   - IProcessingStageStrategy → OcrExecutionStageStrategy");
            // 🔥 [P3_FIX] TranslationExecutionStageStrategy削除済み - Phase 12.2アーキテクチャ移行完了
            // Console.WriteLine("🔧 [STRATEGY_A_FIX]   - IProcessingStageStrategy → TranslationExecutionStageStrategy");
            Console.WriteLine("🔧 [STRATEGY_A_FIX]   - IProcessingStageStrategy → ImageChangeDetectionStageStrategy");
            Console.WriteLine("🔧 [STRATEGY_A_FIX]   - IProcessingStageStrategy → TextChangeDetectionStageStrategy");
            return services;
        });

        return services;
    }
}
