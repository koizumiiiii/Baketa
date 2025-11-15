using Baketa.Core.DI;
using Baketa.Infrastructure.OCR.Benchmarking;
using Baketa.Infrastructure.OCR.MultiScale;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.DI;

/// <summary>
/// マルチスケールOCR処理のDIモジュール
/// </summary>
public sealed class MultiScaleOcrModule : ServiceModuleBase
{
    public override void RegisterServices(IServiceCollection services)
    {
        // マルチスケールOCR処理
        services.AddSingleton<IMultiScaleOcrProcessor, SimpleMultiScaleOcrProcessor>();

        // ベンチマーキング・テスト
        services.AddSingleton<TestCaseGenerator>();
    }
}
