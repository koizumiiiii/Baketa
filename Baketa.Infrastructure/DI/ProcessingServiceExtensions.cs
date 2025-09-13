using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.Processing;
using Baketa.Infrastructure.Processing;
using Baketa.Infrastructure.Processing.Strategies;

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
    public static IServiceCollection AddProcessingServices(this IServiceCollection services)
    {
        // 🎯 UltraThink Phase 21 修正: OCR処理パイプライン復旧のためのDI登録

        // 1. SmartProcessingPipelineService本体を登録
        services.AddSingleton<ISmartProcessingPipelineService, SmartProcessingPipelineService>();

        // 2. 処理戦略をすべて登録（IEnumerable<IProcessingStageStrategy>として注入される）
        services.AddTransient<IProcessingStageStrategy, OcrExecutionStageStrategy>();
        services.AddTransient<IProcessingStageStrategy, TranslationExecutionStageStrategy>();
        services.AddTransient<IProcessingStageStrategy, ImageChangeDetectionStageStrategy>();
        services.AddTransient<IProcessingStageStrategy, TextChangeDetectionStageStrategy>();

        // 3. デバッグ用ログ出力
        services.AddSingleton<IServiceCollection>(provider => 
        {
            // サービス登録確認のログ出力
            Console.WriteLine("🔧 [DI_FIX] ProcessingServices登録完了 - SmartProcessingPipelineService + 4戦略");
            Console.WriteLine("🔧 [DI_FIX]   - ISmartProcessingPipelineService → SmartProcessingPipelineService");
            Console.WriteLine("🔧 [DI_FIX]   - IProcessingStageStrategy → OcrExecutionStageStrategy");
            Console.WriteLine("🔧 [DI_FIX]   - IProcessingStageStrategy → TranslationExecutionStageStrategy");
            Console.WriteLine("🔧 [DI_FIX]   - IProcessingStageStrategy → ImageChangeDetectionStageStrategy");
            Console.WriteLine("🔧 [DI_FIX]   - IProcessingStageStrategy → TextChangeDetectionStageStrategy");
            return services;
        });

        return services;
    }
}