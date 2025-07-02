using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Platform.Windows.Capture;

namespace Baketa.Infrastructure.Platform.DI.Modules;

/// <summary>
/// フルスクリーンサービス用DIモジュール
/// Windows固有のフルスクリーン検出・最適化サービスを登録
/// </summary>
public static class FullscreenModule
{
    /// <summary>
    /// フルスクリーンサービスをDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>更新されたサービスコレクション</returns>
    public static IServiceCollection AddFullscreenServices(this IServiceCollection services)
    {
        // フルスクリーン検出サービス
        services.AddSingleton<IFullscreenDetectionService>(provider =>
        {
            var logger = provider.GetService<ILogger<WindowsFullscreenDetectionService>>();
            return new WindowsFullscreenDetectionService(logger);
        });
        
        // フルスクリーン最適化サービス
        services.AddSingleton<IFullscreenOptimizationService>(provider =>
        {
            var detectionService = provider.GetRequiredService<IFullscreenDetectionService>();
            var captureService = provider.GetRequiredService<IAdvancedCaptureService>();
            var logger = provider.GetService<ILogger<WindowsFullscreenOptimizationService>>();
            
            return new WindowsFullscreenOptimizationService(detectionService, captureService, logger);
        });
        
        return services;
    }
}
