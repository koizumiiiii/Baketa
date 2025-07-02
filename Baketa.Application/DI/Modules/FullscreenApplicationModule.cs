using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.Application.Services.Capture;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// フルスクリーン管理サービス用DIモジュール
/// Application層のフルスクリーン統合管理サービスを登録
/// </summary>
public static class FullscreenApplicationModule
{
    /// <summary>
    /// フルスクリーン管理サービスをDIコンテナに登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <returns>更新されたサービスコレクション</returns>
    public static IServiceCollection AddFullscreenManagement(this IServiceCollection services)
    {
        // フルスクリーン管理サービス
        services.AddSingleton<FullscreenManagerService>(provider =>
        {
            var detectionService = provider.GetRequiredService<IFullscreenDetectionService>();
            var optimizationService = provider.GetRequiredService<IFullscreenOptimizationService>();
            var eventAggregator = provider.GetRequiredService<IEventAggregator>();
            var logger = provider.GetService<ILogger<FullscreenManagerService>>();
            
            return new FullscreenManagerService(detectionService, optimizationService, eventAggregator, logger);
        });
        
        return services;
    }
}
