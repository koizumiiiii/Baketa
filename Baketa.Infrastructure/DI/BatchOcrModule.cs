using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.DI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.BatchProcessing;
using Baketa.Infrastructure.DI.Modules;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// バッチOCR処理モジュール
/// Phase 2-B: バッチOCR処理システムのDI登録
/// </summary>
public sealed class BatchOcrModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // バッチOCRプロセッサー
        services.AddSingleton<IBatchOcrProcessor, BatchOcrProcessor>();
        
        // バッチOCR統合サービス
        services.AddSingleton<BatchOcrIntegrationService>();
    }

    public override IEnumerable<Type> GetDependentModules()
    {
        yield return typeof(DiagnosticModule);
    }
}