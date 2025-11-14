using System;
using System.IO;
using Baketa.Application.Services.Capture;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

        // 🚨 [UltraThink修正] EventAggregator重複登録を削除
        // EventAggregatorはCoreModuleで管理されているため、ここでは登録しない
        // CoreModuleが最初に実行されることを前提とする

        // レガシーキャプチャサービス（フォールバック用）
        services.AddSingleton<AdvancedCaptureService>();

        // 適応的キャプチャサービス（メイン）
        services.AddSingleton<AdaptiveCaptureService>(provider =>
        {
            try
            {
                var gpuDetector = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.ICaptureEnvironmentDetector>();
                var strategyFactory = provider.GetRequiredService<Baketa.Core.Abstractions.Capture.ICaptureStrategyFactory>();
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureService>>();
                var eventAggregator = provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                var loggingOptions = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Baketa.Core.Settings.LoggingSettings>>();

                // 🔄 画面変化検知サービスとイメージアダプターを取得（Phase 1対応）
                var changeDetectionService = provider.GetService<Baketa.Core.Abstractions.Services.IImageChangeDetectionService>();
                var imageAdapter = provider.GetService<Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowsImageAdapter>();

                logger.LogDebug("AdaptiveCaptureService インスタンス作成");
                logger.LogInformation($"🎯 画面変化検知サービス: {(changeDetectionService != null ? "有効" : "無効")}");
                logger.LogInformation($"🎯 イメージアダプター: {(imageAdapter != null ? "有効" : "無効")}");

                var service = new AdaptiveCaptureService(
                    gpuDetector,
                    strategyFactory,
                    logger,
                    eventAggregator,
                    loggingOptions,
                    changeDetectionService,  // 画面変化検知サービスを渡す
                    imageAdapter);           // イメージアダプターを渡す

                logger.LogInformation("AdaptiveCaptureService 登録完了 - 画面変化検知機能付き");
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
        services.AddSingleton<AdaptiveCaptureServiceAdapter>(provider =>
        {
            try
            {
                var adaptiveService = provider.GetRequiredService<AdaptiveCaptureService>();
                var logger = provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<AdaptiveCaptureServiceAdapter>>();
                var coordinateTransformationService = provider.GetRequiredService<Baketa.Core.Abstractions.Services.ICoordinateTransformationService>();
                var changeDetectionService = provider.GetService<Baketa.Core.Abstractions.Services.IImageChangeDetectionService>();

                logger.LogDebug("AdaptiveCaptureServiceAdapter インスタンス作成");
                logger.LogInformation($"🎯 [PHASE_C] EnhancedImageChangeDetectionService統合: {(changeDetectionService != null ? "有効" : "無効")}");
                logger.LogInformation("🎯 [WIN32_OVERLAY_FIX] CoordinateTransformationService統合: 動的ROIScaleFactor計算対応");

                var adapter = new AdaptiveCaptureServiceAdapter(adaptiveService, logger, coordinateTransformationService, changeDetectionService);
                logger.LogInformation("AdaptiveCaptureServiceAdapter 登録完了 - Phase C画面変化検知 + WIN32座標変換統合済み");
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
        services.AddSingleton<ICaptureService>(provider =>
        {
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
