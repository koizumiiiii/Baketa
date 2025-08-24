using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.DI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Baketa.Infrastructure.OCR.Strategies;
using Baketa.Infrastructure.DI.Modules;
using System;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// バッチOCR処理モジュール
/// Phase 2-B: バッチOCR処理システムのDI登録
/// </summary>
public sealed class BatchOcrModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // 🔧 修正: 明示的ファクトリパターンでImageDiagnosticsSaver確実注入
        services.AddSingleton<IBatchOcrProcessor>(serviceProvider =>
        {
            var ocrEngine = serviceProvider.GetRequiredService<IOcrEngine>();
            var performanceOrchestrator = serviceProvider.GetService<IPerformanceOrchestrator>();
            var performanceAnalyzer = serviceProvider.GetService<IAsyncPerformanceAnalyzer>();
            var logger = serviceProvider.GetService<ILogger<BatchOcrProcessor>>();
            var regionGenerator = serviceProvider.GetService<OcrRegionGenerator>();
            var advancedOptions = serviceProvider.GetService<IOptions<AdvancedSettings>>();
            var roiDiagnosticsOptions = serviceProvider.GetService<IOptions<RoiDiagnosticsSettings>>();
            
            // 🎯 CRITICAL: ImageDiagnosticsSaverを明示的に取得・注入
            var diagnosticsSaver = serviceProvider.GetService<ImageDiagnosticsSaver>();
            
            Console.WriteLine($"🔍 [BATCH-DI] ImageDiagnosticsSaver注入確認: {diagnosticsSaver != null}");
            Console.WriteLine($"🔍 [BATCH-DI] 他の依存関係確認: OCR={ocrEngine != null}, Options={advancedOptions != null}");
            
            var processor = new BatchOcrProcessor(
                ocrEngine,
                performanceOrchestrator,
                performanceAnalyzer,
                logger,
                regionGenerator,
                advancedOptions,
                roiDiagnosticsOptions,
                diagnosticsSaver  // 🎯 明示的注入
            );
            
            Console.WriteLine($"✅ [BATCH-DI] BatchOcrProcessor作成完了（diagnosticsSaver注入済み）");
            return processor;
        });
        
        // バッチOCR統合サービス
        services.AddSingleton<BatchOcrIntegrationService>();
    }

    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(DiagnosticModule);
        
        // 🏭 重要: 新しいファクトリシステムに依存
        yield return typeof(PaddleOcrModule);
        
        // インフラストラクチャモジュールにも依存
        yield return typeof(InfrastructureModule);
    }
}