using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.DI;
using Baketa.Infrastructure.OCR.MultiScale;
using Baketa.Infrastructure.OCR.Benchmarking;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// マルチスケールOCR処理のDIモジュール
/// </summary>
public class MultiScaleOcrModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // マルチスケールOCR処理
        services.AddSingleton<IMultiScaleOcrProcessor, SimpleMultiScaleOcrProcessor>();
        
        // ベンチマーキング・テスト
        services.AddSingleton<TestCaseGenerator>();
    }
}