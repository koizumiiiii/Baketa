using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Baketa.Application.Services.Capture;
using System;
using System.IO;

namespace Baketa.Application.DI.Modules;

/// <summary>
/// キャプチャサービス関連のDIモジュール
/// </summary>
public sealed class CaptureModule : ServiceModuleBase
{
    /// <summary>
    /// サービスを登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    public override void RegisterServices(IServiceCollection services)
    {
        // キャプチャサービス統合開始ログ
        var logger = services.BuildServiceProvider().GetService<ILogger<CaptureModule>>();
        logger?.LogInformation("キャプチャサービス統合モジュール登録開始");
        
        // ◆ 依存モジュールを明示的に登録
        var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
        platformModule.RegisterServices(services);
        
        var adaptiveCaptureModule = new Baketa.Infrastructure.Platform.DI.Modules.AdaptiveCaptureModule();
        adaptiveCaptureModule.RegisterServices(services);
        
        // ◆ 依存サービスの確認登録（すでに他で登録されている場合は無視される）
        // IEventAggregatorがApplicationModuleで登録されているが、依存関係を明確にする
        if (!services.Any(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Events.IEventAggregator)))
        {
            services.AddSingleton<Baketa.Core.Abstractions.Events.IEventAggregator, 
                Baketa.Core.Events.Implementation.EventAggregator>();
        }
        
        // レガシーキャプチャサービス（フォールバック用）
        services.AddSingleton<AdvancedCaptureService>();
        
        // 適応的キャプチャサービス（メイン）
        services.AddSingleton<AdaptiveCaptureService>(provider => {
            try 
            {
                var gpuDetector = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.IGPUEnvironmentDetector>();
                var strategyFactory = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.ICaptureStrategyFactory>();
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureService>>();
                var eventAggregator = provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                
                logger.LogDebug("AdaptiveCaptureService インスタンス作成");
                var service = new AdaptiveCaptureService(gpuDetector, strategyFactory, logger, eventAggregator);
                logger.LogInformation("AdaptiveCaptureService 登録完了");
                return service;
            }
            catch (Exception ex)
            {
                var logger = provider.GetService<Microsoft.Extensions.Logging.ILogger<CaptureModule>>();
                logger?.LogError(ex, "AdaptiveCaptureService作成失敗");
                throw;
            }
        });
        
        // 適応的キャプチャサービスアダプター
        services.AddSingleton<AdaptiveCaptureServiceAdapter>(provider => {
            try 
            {
                var adaptiveService = provider.GetRequiredService<AdaptiveCaptureService>();
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureServiceAdapter>>();
                
                logger.LogDebug("AdaptiveCaptureServiceAdapter インスタンス作成");
                var adapter = new AdaptiveCaptureServiceAdapter(adaptiveService, logger);
                logger.LogInformation("AdaptiveCaptureServiceAdapter 登録完了");
                return adapter;
            }
            catch (Exception ex)
            {
                var logger = provider.GetService<Microsoft.Extensions.Logging.ILogger<CaptureModule>>();
                logger?.LogError(ex, "AdaptiveCaptureServiceAdapter作成失敗");
                throw;
            }
        });
        
        // 適応的キャプチャサービスをメインとして使用（Windows Graphics Capture API実装）
        services.AddSingleton<ICaptureService>(provider => {
            try 
            {
                var adapter = provider.GetRequiredService<AdaptiveCaptureServiceAdapter>();
                var logger = provider.GetService<Microsoft.Extensions.Logging.ILogger<CaptureModule>>();
                logger?.LogInformation("ICaptureService として AdaptiveCaptureServiceAdapter を登録");
                return adapter;
            }
            catch (Exception ex)
            {
                var logger = provider.GetService<Microsoft.Extensions.Logging.ILogger<CaptureModule>>();
                logger?.LogError(ex, "ICaptureService 登録失敗");
                throw;
            }
        });
        
        // 適応的キャプチャサービスインターフェースも登録（将来のため）
        services.AddSingleton<Baketa.Core.Abstractions.Capture.IAdaptiveCaptureService>(provider => 
            provider.GetRequiredService<AdaptiveCaptureService>());
        
        // レガシーインターフェースも保持（互換性のため）
        services.AddSingleton<IAdvancedCaptureService>(provider => provider.GetRequiredService<AdvancedCaptureService>());
        
        // TODO: 以下のサービスはインターフェース定義後に有効化
        // services.AddSingleton<IGameProfileManager, GameProfileManager>();
        // services.AddSingleton<IGameDetectionService, GameDetectionService>();
        
        logger?.LogInformation("キャプチャサービス統合モジュール登録完了 - AdaptiveCaptureServiceをメインとして使用");
    }
    
}
