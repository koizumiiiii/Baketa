using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.Imaging.Filters;
using Baketa.Infrastructure.Imaging.Services;
using Microsoft.Extensions.Logging;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;

namespace Baketa.Infrastructure.DI.Modules;

/// <summary>
/// OpenCvSharp画像処理機能のDI登録モジュール
/// Phase 3: OCR精度向上のためのOpenCV前処理機能を提供
/// </summary>
[AutoRegister]
[ModulePriority(ModulePriority.Core)]
public sealed class OpenCvProcessingModule : ServiceModuleBase
{
    /// <summary>
    /// サービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // OpenCV画像フィルターを登録
        RegisterOpenCvFilters(services);
        
        // ゲーム最適化前処理サービスを登録
        RegisterPreprocessingServices(services);
    }

    /// <summary>
    /// OpenCV画像フィルターを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterOpenCvFilters(IServiceCollection services)
    {
        // 適応的二値化フィルター
        services.AddTransient<OpenCvAdaptiveThresholdFilter>(provider =>
        {
            var logger = provider.GetService<ILogger<OpenCvAdaptiveThresholdFilter>>();
            return new OpenCvAdaptiveThresholdFilter(logger);
        });
        
        // 色ベースマスキングフィルター
        services.AddTransient<OpenCvColorBasedMaskingFilter>(provider =>
        {
            var logger = provider.GetService<ILogger<OpenCvColorBasedMaskingFilter>>();
            return new OpenCvColorBasedMaskingFilter(logger);
        });
    }

    /// <summary>
    /// 前処理サービスを登録
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterPreprocessingServices(IServiceCollection services)
    {
        // ゲーム最適化前処理サービスを登録
        services.AddScoped<GameOptimizedPreprocessingService>();
        
        // 既存の前処理サービスをゲーム最適化版で置き換え
        services.AddScoped<IOcrPreprocessingService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<GameOptimizedPreprocessingService>>();
            return new GameOptimizedPreprocessingService(logger);
        });
    }
}