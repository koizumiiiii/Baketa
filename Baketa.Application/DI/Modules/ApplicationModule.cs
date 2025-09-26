using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.DI;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Baketa.Application.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using TranslationAbstractions = Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Services;
using Baketa.Application.Services.Capture;
using Baketa.Core.Events.Implementation;
using EventAggregatorImpl = Baketa.Core.Events.Implementation.EventAggregator;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Services;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.DI;
using Baketa.Application.Services.Events;
using Microsoft.Extensions.Logging;
using Baketa.Core.Settings;
using Baketa.Core.Models.Processing;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Events.Handlers;
using Microsoft.Extensions.Configuration;
using Baketa.Application.Configuration;

namespace Baketa.Application.DI.Modules;

    /// <summary>
    /// アプリケーションレイヤーのサービスを登録するモジュール。
    /// ビジネスロジックやユースケースの実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Application)]
    public sealed class ApplicationModule : ServiceModuleBase
    {
        /// <summary>
        /// アプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            // 環境設定は、BuildServiceProviderが存在しないか必要なパッケージがないため
            // コメントアウトし、デフォルト値を使用
            //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>() 
            //    ?? Core.DI.BaketaEnvironment.Production;
            var environment = Core.DI.BaketaEnvironment.Production;
            
            // 🎯 UltraThink Phase 1: オーバーレイ自動削除システム設定登録（Gemini Review対応）
            RegisterAutoOverlayCleanupSettings(services);
            
            // OCR処理モジュールは Infrastructure.DI.OcrProcessingModule で登録
            
            // OCRアプリケーションサービス
            RegisterOcrApplicationServices(services);
            
            // 翻訳アプリケーションサービス
            RegisterTranslationApplicationServices(services);
            
            // その他のアプリケーションサービス
            RegisterOtherApplicationServices(services, environment);
            
            // イベントハンドラー
            RegisterEventHandlers(services);
        }

        /// <summary>
        /// OCRアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterOcrApplicationServices(IServiceCollection _)
        {
            // OCR関連のアプリケーションサービス
            // 例: services.AddSingleton<IOcrService, OcrService>();
            // 例: services.AddSingleton<IOcrProfileService, OcrProfileService>();
            // 例: services.AddSingleton<IOcrConfigurationService, OcrConfigurationService>();
            
            // OCR結果処理サービス
            // 例: services.AddSingleton<IOcrResultProcessor, OcrResultProcessor>();
            // 例: services.AddSingleton<IOcrTextFormatter, OcrTextFormatter>();
        }
        
        /// <summary>
        /// 翻訳アプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationApplicationServices(IServiceCollection services)
        {
            // TranslationServiceExtensionsが呼ばれていない場合の保険でDefaultTranslationServiceを登録
            if (!services.Any(s => s.ServiceType == typeof(TranslationAbstractions.ITranslationService)))
            {
                services.AddSingleton<TranslationAbstractions.ITranslationService, DefaultTranslationService>();
            }
            
            // 🚀 翻訳モデル事前ロード戦略 - Clean Architecture準拠実装
            services.AddSingleton<Baketa.Application.Services.IApplicationInitializer,
                Baketa.Application.Services.TranslationModelLoader>();

            // 🔧 PHASE 3: TranslationPipelineService DI Registration (Critical Issue対応)
            services.AddSingleton<Baketa.Application.Services.Translation.TranslationPipelineService>(provider =>
            {
                var eventAggregator = provider.GetRequiredService<IEventAggregator>();
                var settingsService = provider.GetRequiredService<IUnifiedSettingsService>();
                var translationService = provider.GetRequiredService<TranslationAbstractions.ITranslationService>();
                var overlayManager = provider.GetRequiredService<Baketa.Core.Abstractions.UI.IInPlaceTranslationOverlayManager>();
                var logger = provider.GetRequiredService<ILogger<Baketa.Application.Services.Translation.TranslationPipelineService>>();
                var languageConfig = provider.GetRequiredService<ILanguageConfigurationService>();

                return new Baketa.Application.Services.Translation.TranslationPipelineService(
                    eventAggregator,
                    settingsService,
                    translationService,
                    overlayManager,
                    logger,
                    languageConfig);
            });
            // 🚨 [PHASE_A_FIX] DI登録競合解決 - PriorityAwareOcrCompletedHandlerに一本化
            // services.AddSingleton<IEventProcessor<OcrCompletedEvent>>(
            //     provider => provider.GetRequiredService<Baketa.Application.Services.Translation.TranslationPipelineService>());
            
            // 🚨 [REGRESSION_FIX] エラーハンドリング統一による回帰問題を修正するため一時的に無効化
            // services.AddSingleton<Baketa.Application.Services.Translation.ITranslationErrorHandlerService, 
            //     Baketa.Application.Services.Translation.TranslationErrorHandlerService>();
            
            // ファサードパターン: 依存関係注入の複雑さを軽減
            services.AddSingleton<Baketa.Core.Abstractions.Processing.ITranslationProcessingFacade, 
                Baketa.Application.Services.Processing.TranslationProcessingFacade>();
            services.AddSingleton<Baketa.Core.Abstractions.Configuration.IConfigurationFacade,
                Baketa.Application.Services.Configuration.ConfigurationFacade>();
            
            // 🔥 [STREAMING] ストリーミング翻訳サービス: 段階的結果表示による12.7秒→数秒体感速度向上
            Console.WriteLine("🔍 [DI_DEBUG] StreamingTranslationService登録開始");
            services.AddSingleton<TranslationAbstractions.IStreamingTranslationService, Baketa.Application.Services.Translation.StreamingTranslationService>();
            Console.WriteLine("✅ [DI_DEBUG] StreamingTranslationService登録完了");
            
            // 🚀 [NLLB_TEST] CoordinateBasedTranslationService一時無効化 - NLLB-200 TPL Dataflowテスト用
            /*
            services.AddSingleton<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>(provider =>
            {
                Console.WriteLine("🔍 [DI_DEBUG] CoordinateBasedTranslationService Factory開始 (Phase 2.1更新版)");
                
                try
                {
                    Console.WriteLine("🔍 [DI_DEBUG] ITranslationProcessingFacade取得中...");
                    var processingFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Processing.ITranslationProcessingFacade>();
                    Console.WriteLine($"✅ [DI_DEBUG] ITranslationProcessingFacade取得成功: {processingFacade.GetType().Name}");
                    
                    Console.WriteLine("🔍 [DI_DEBUG] IConfigurationFacade取得中...");
                    var configurationFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Configuration.IConfigurationFacade>();
                    Console.WriteLine($"✅ [DI_DEBUG] IConfigurationFacade取得成功: {configurationFacade.GetType().Name}");
                    
                    Console.WriteLine("🔍 [DI_DEBUG] IStreamingTranslationService取得中...");
                    var streamingService = provider.GetService<TranslationAbstractions.IStreamingTranslationService>();
                    Console.WriteLine($"✅ [DI_DEBUG] IStreamingTranslationService取得成功: {streamingService?.GetType().Name ?? "null"}");
                    
                    Console.WriteLine("🔧 [DI_DEBUG] CoordinateBasedTranslationService インスタンス作成開始 (Service Locator除去済み)");
                    var logger = provider.GetService<ILogger<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>>();
                    var instance = new Baketa.Application.Services.Translation.CoordinateBasedTranslationService(
                        processingFacade,
                        configurationFacade,
                        streamingService,
                        logger);
                    Console.WriteLine("✅ [DI_DEBUG] CoordinateBasedTranslationService インスタンス作成完了 (Phase 2.1)");
                    return instance;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [DI_DEBUG] CoordinateBasedTranslationService Factory失敗: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            });
            */
            
            // 翻訳統合サービス（IEventAggregatorの依存を削除）
            services.AddSingleton<Baketa.Application.Services.Translation.TranslationOrchestrationService>(provider =>
            {
                Console.WriteLine("🔍 [DI_DEBUG] TranslationOrchestrationService Factory開始");
                
                try
                {
                    var captureService = provider.GetRequiredService<ICaptureService>();
                    var settingsService = provider.GetRequiredService<ISettingsService>();
                    var ocrEngine = provider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                    // [REMOVED] var translationEngineFactory = provider.GetRequiredService<ITranslationEngineFactory>();
                    var eventAggregator = provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                    var translationService = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITranslationService>();
                    var translationDictionaryService = (Baketa.Core.Abstractions.Services.ITranslationDictionaryService?)null; // REMOVED: 辞書翻訳削除済み
                    var logger = provider.GetService<ILogger<Baketa.Application.Services.Translation.TranslationOrchestrationService>>();
                    
                    // 🎯 [PHASE17] CoordinateBasedTranslationService有効化 - TimedChunkAggregator統合
                    Console.WriteLine("🚀 [PHASE17] CoordinateBasedTranslationService取得開始 - TimedChunkAggregator統合用");
                    var coordinateBasedTranslation = provider.GetService<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>();
                    if (coordinateBasedTranslation == null)
                    {
                        Console.WriteLine("⚠️ [PHASE17] CoordinateBasedTranslationService未登録 - 新規作成");
                        var processingFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Processing.ITranslationProcessingFacade>();
                        var configurationFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Configuration.IConfigurationFacade>();
                        var streamingTranslationService = provider.GetService<Baketa.Core.Abstractions.Translation.IStreamingTranslationService>();
                        var textChunkAggregatorService = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITextChunkAggregatorService>();
                        var loggerForCoordinate = provider.GetService<ILogger<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>>();
                        coordinateBasedTranslation = new Baketa.Application.Services.Translation.CoordinateBasedTranslationService(
                            processingFacade,
                            configurationFacade,
                            streamingTranslationService,
                            textChunkAggregatorService,
                            loggerForCoordinate);
                    }
                    Console.WriteLine($"✅ [PHASE17] CoordinateBasedTranslationService準備完了 - TimedChunkAggregator統合有効");
                    Console.WriteLine($"✅ [DI_DEBUG] EventAggregator取得成功: {eventAggregator.GetType().Name}");
                    Console.WriteLine($"🚫 [DI_DEBUG] TranslationDictionaryService削除済み: {translationDictionaryService?.GetType().Name ?? "null - REMOVED"}");
                    
                    var ocrSettings = provider.GetRequiredService<IOptionsMonitor<Baketa.Core.Settings.OcrSettings>>();
                    return new Baketa.Application.Services.Translation.TranslationOrchestrationService(
                        captureService,
                        settingsService,
                        ocrEngine,
                        // [REMOVED] translationEngineFactory,
                        coordinateBasedTranslation,
                        eventAggregator,
                        ocrSettings,
                        translationService,
                        translationDictionaryService,
                        logger);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 [DI_DEBUG] TranslationOrchestrationService Factory失敗: {ex.GetType().Name}: {ex.Message}");
                    throw;
                }
            });
            services.AddSingleton<Baketa.Application.Services.Translation.ITranslationOrchestrationService>(
                provider => provider.GetRequiredService<Baketa.Application.Services.Translation.TranslationOrchestrationService>());
            
            // OPUS-MT削除済み: NLLB-200統一によりOpusMtPrewarmService不要
            
            // 🚫 [REMOVED] 翻訳辞書サービス削除済み - NLLB-200専用システムに統一
            // Console.WriteLine("🔍 [DI_DEBUG] TranslationDictionaryService登録開始");
            // services.AddSingleton<Baketa.Core.Abstractions.Services.ITranslationDictionaryService, 
            //     Baketa.Application.Services.Translation.TranslationDictionaryService>();
            // Console.WriteLine("✅ [DI_DEBUG] TranslationDictionaryService登録完了");
            
            // 翻訳関連のアプリケーションサービス（将来拡張）
            // 例: services.AddSingleton<ITranslationService, TranslationService>();
            // 例: services.AddSingleton<ITranslationProfileService, TranslationProfileService>();
            // 例: services.AddSingleton<ILanguageService, LanguageService>();
            
            // 翻訳カスタマイズ（将来拡張）
            // 例: services.AddSingleton<IDictionaryService, DictionaryService>();
            // 例: services.AddSingleton<ITextReplacementService, TextReplacementService>();
        }
        
        /// <summary>
        /// その他のアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        /// <summary>
        /// その他のアプリケーションサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        private static void RegisterOtherApplicationServices(IServiceCollection services, Core.DI.BaketaEnvironment environment)
        {
            // イベント集約機構の登録
            RegisterEventAggregator(services);
            
            // イベントハンドラー初期化サービス
            services.AddSingleton<EventHandlerInitializationService>();
            
            // キャプチャサービスの登録
            RegisterCaptureServices(services);
            
            // フルスクリーン管理サービス
            services.AddFullscreenManagement();
            
            // 🎯 Phase 3.1: SafeImageFactory登録 (ISafeImageFactory DI解決エラー修正)
            services.AddSingleton<Baketa.Core.Abstractions.Memory.ISafeImageFactory, Baketa.Application.Services.Memory.SafeImageFactory>();
            
            // 🎯 Phase 3.1: IImageLifecycleManager登録 (WindowsImageFactory依存関係解決)
            services.AddSingleton<Baketa.Core.Abstractions.Memory.IImageLifecycleManager, Baketa.Application.Services.Memory.ImageLifecycleManager>();
            
            // 🎯 Phase 3.11: IReferencedSafeImageFactory登録 (SafeImage早期破棄問題解決)
            services.AddSingleton<Baketa.Core.Abstractions.Memory.IReferencedSafeImageFactory, Baketa.Application.Services.Memory.ReferencedSafeImageFactory>();
            
            // 🔧 診断レポートサービス（UI制御フロー責務分離 - Phase 6.2.1）
            // IHostedServiceとして登録しアプリケーションライフサイクルと連動
            services.AddSingleton<Services.Diagnostics.DiagnosticReportService>();
            services.AddSingleton<Services.Diagnostics.IDiagnosticReportService>(
                provider => provider.GetRequiredService<Services.Diagnostics.DiagnosticReportService>());
            services.AddHostedService<Services.Diagnostics.DiagnosticReportService>(
                provider => provider.GetRequiredService<Services.Diagnostics.DiagnosticReportService>());
            
            // 🔧 ウィンドウ管理サービス（UI制御フロー責務分離 - Phase 6.2.2）
            services.AddSingleton<Services.UI.IWindowManagementService, Services.UI.WindowManagementService>();
            
            // 🎯 オーバーレイ自動削除サービス（UltraThink Phase 1: オーバーレイ自動消去システム）
            // Gemini Review: IHostedService統合により自動初期化を実現
            services.AddSingleton<Services.UI.AutoOverlayCleanupService>();
            services.AddSingleton<Baketa.Core.Abstractions.UI.IAutoOverlayCleanupService>(
                provider => provider.GetRequiredService<Services.UI.AutoOverlayCleanupService>());
            services.AddHostedService(provider => provider.GetRequiredService<Services.UI.AutoOverlayCleanupService>());
            
            // 🎯 オーバーレイ位置調整サービス（UltraThink Phase 10.3: クリーンアーキテクチャ準拠）
            // TextChunkから位置調整ロジックを分離し、責務の明確化を実現
            services.AddSingleton<IOverlayPositioningService, Services.UI.OverlayPositioningService>();
            
            // 🔧 翻訳制御サービス（UI制御フロー責務分離 - Phase 6.2.3）
            services.AddSingleton<Services.Translation.ITranslationControlService, Services.Translation.TranslationControlService>();
            
            // 統合サービス
            // 例: services.AddSingleton<ITranslationIntegrationService, TranslationIntegrationService>();
            
            // テキスト処理
            // 例: services.AddSingleton<ITextAnalysisService, TextAnalysisService>();
            
            // デバッグサービス（開発環境のみ）
            if (environment == Core.DI.BaketaEnvironment.Development)
            {
                // 例: services.AddSingleton<IDevelopmentService, DevelopmentService>();
                // 例: services.AddSingleton<IDebugConsoleService, DebugConsoleService>();
            }
        }
        
        /// <summary>
        /// イベント集約機構を登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterEventAggregator(IServiceCollection services)
        {
            // 🚨 [UltraThink修正] 重複登録を削除 - CoreModule.AddEventAggregator()で既に登録済み
            // EventAggregatorはCoreModuleで登録されているため、ここでは追加登録しない
            // services.AddSingleton<Baketa.Core.Abstractions.Events.IEventAggregator, Baketa.Core.Events.Implementation.EventAggregator>();
                
            // 既存の自動登録サービスは削除して手動初期化に変更
        }
        
        /// <summary>
        /// キャプチャサービスを登録します。
        /// 実際のキャプチャサービス実装はCaptureModuleで行われます。
        /// </summary>
        /// <param name="_">サービスコレクション（使用しない）</param>
        private static void RegisterCaptureServices(IServiceCollection _)
        {
            // キャプチャサービスはCaptureModuleで登録されるため、ここでは何もしない
            // CaptureModuleにより以下が登録される:
            // - AdaptiveCaptureService (コア適応的キャプチャ)
            // - AdaptiveCaptureServiceAdapter (ICaptureService実装)
            // - AdvancedCaptureService (拡張機能)
            
            // TODO: 将来的な拡張用コメント
            // ゲームプロファイル管理サービス（未実装）
            // services.AddSingleton<IGameProfileManager, GameProfileManager>();
            
            // ゲーム自動検出サービス（未実装）
            // services.AddSingleton<IGameDetectionService, GameDetectionService>();
        }
        
        /// <summary>
        /// イベントハンドラーを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterEventHandlers(IServiceCollection services)
        {
            // 翻訳モード変更イベントプロセッサー
            services.AddSingleton<Baketa.Application.Events.Processors.TranslationModeChangedEventProcessor>();
            
            
            // 🚀 [ROI_PIPELINE] OCR完了イベント処理は TranslationPipelineService で統合処理
            // OcrCompletedHandler_Improved は削除済み (TranslationPipelineService に統合)
            
            // 🎯 [PHASE5] 優先度付きOCR完了ハンドラー - 画面中央優先度翻訳システム
            // PriorityAwareOcrCompletedHandler は CoreModule で登録される
            
            // 翻訳要求イベントハンドラー
            services.AddSingleton<Baketa.Core.Events.Handlers.TranslationRequestHandler>();
            services.AddSingleton<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationRequestEvent>>(
                provider => provider.GetRequiredService<Baketa.Core.Events.Handlers.TranslationRequestHandler>());
            
            // バッチ翻訳要求イベントハンドラー
            services.AddSingleton<Baketa.Core.Events.Handlers.BatchTranslationRequestHandler>();
            services.AddSingleton<IEventProcessor<Baketa.Core.Events.EventTypes.BatchTranslationRequestEvent>>(
                provider => provider.GetRequiredService<Baketa.Core.Events.Handlers.BatchTranslationRequestHandler>());
            
            // 🔄 [FIX] TranslationCompletedHandler登録 - TranslationCompletedEvent中継処理
            Console.WriteLine("🔄 [FIX] TranslationCompletedHandler DI登録 - 翻訳完了イベント中継修復");
            services.AddSingleton<Baketa.Application.EventHandlers.TranslationCompletedHandler>();
            services.AddSingleton<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationCompletedEvent>>(
                provider => provider.GetRequiredService<Baketa.Application.EventHandlers.TranslationCompletedHandler>());

            // 🔄 [FIX] TranslationWithBoundsCompletedHandler復活 - 翻訳結果をTextChunkに反映するため必須
            Console.WriteLine("🔄 [FIX] TranslationWithBoundsCompletedHandler DI登録復活 - 翻訳結果反映修復");
            services.AddSingleton<Baketa.Application.EventHandlers.TranslationWithBoundsCompletedHandler>();
            services.AddSingleton<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationWithBoundsCompletedEvent>>(
                provider => provider.GetRequiredService<Baketa.Application.EventHandlers.TranslationWithBoundsCompletedHandler>());
            
            // 手動イベントプロセッサー登録サービスは削除（EventHandlerInitializationServiceに置き換え）
            
            // 他のイベントハンドラーの登録
            
            // ⚡ [ARCHITECTURAL_FIX] CaptureCompletedHandler登録 - Application層に適切配置
            Console.WriteLine("🔍 [DI_DEBUG] CaptureCompletedHandler登録開始 - Application層配置");
            services.AddSingleton<Baketa.Application.Events.Handlers.CaptureCompletedHandler>(provider =>
            {
                var eventAggregator = provider.GetRequiredService<IEventAggregator>();

                // 🎯 Phase 26: ITextChunkAggregatorService抽象化による Clean Architecture準拠
                var chunkAggregatorService = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITextChunkAggregatorService>();

                var smartPipeline = provider.GetService<ISmartProcessingPipelineService>();
                var logger = provider.GetService<ILogger<Baketa.Application.Events.Handlers.CaptureCompletedHandler>>();
                var settings = provider.GetService<IOptionsMonitor<ProcessingPipelineSettings>>();
                var diagnosticsSaver = provider.GetService<Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics.ImageDiagnosticsSaver>();
                var roiSettings = provider.GetService<IOptionsMonitor<RoiDiagnosticsSettings>>();

                // 🎯 Phase 3.17.9: IImageToReferencedSafeImageConverter注入修正
                var imageToReferencedConverter = provider.GetService<IImageToReferencedSafeImageConverter>();

                return new Baketa.Application.Events.Handlers.CaptureCompletedHandler(
                    eventAggregator,
                    chunkAggregatorService,
                    provider.GetRequiredService<TranslationAbstractions.ILanguageConfigurationService>(),
                    smartPipeline,
                    logger,
                    settings,
                    diagnosticsSaver,
                    roiSettings,
                    imageToReferencedConverter);
            });
            services.AddSingleton<IEventProcessor<CaptureCompletedEvent>>(
                provider => provider.GetRequiredService<Baketa.Application.Events.Handlers.CaptureCompletedHandler>());
            Console.WriteLine("✅ [DI_DEBUG] CaptureCompletedHandler登録完了 - キャプチャ画像保存機能付き");
            
            // ⚡ [PHASE2_FIX] OcrRequestHandler登録 - 翻訳処理チェーン連鎖修復
            Console.WriteLine("🔍 [DI_DEBUG] OcrRequestHandler登録開始");
            services.AddSingleton<Baketa.Application.Events.Handlers.OcrRequestHandler>();
            services.AddSingleton<IEventProcessor<OcrRequestEvent>>(
                provider => provider.GetRequiredService<Baketa.Application.Events.Handlers.OcrRequestHandler>());
            Console.WriteLine("✅ [DI_DEBUG] OcrRequestHandler登録完了 - Phase 2翻訳チェーン修復");
            
            // 自動登録が必要な場合は必要に応じて実装
            // RegisterEventHandlersAutomatically(services);
        }
        
        /// <summary>
        /// イベントハンドラーを反射を使用して自動的に登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterEventHandlersAutomatically(IServiceCollection _)
        {
            // 現在は実装が必要なインターフェースが存在しないため、
            // 必要に応じて実装を追加してください。
            // 
            // 例: アセンブリからイベントハンドラーを探して登録するコード
            // var handlerTypes = typeof(ApplicationModule).Assembly
            //     .GetTypes()
            //     .Where(t => t.Namespace?.StartsWith("Baketa.Application.Handlers") == true
            //             && !t.IsInterface
            //             && !t.IsAbstract
            //             && t.GetInterfaces().Any(i => i.IsGenericType 
            //                 && i.GetGenericTypeDefinition() == typeof(IEventHandler<>)));
        }
        
        /// <summary>
        /// オーバーレイ自動削除システムの設定を登録します。
        /// Gemini Review: IOptionsパターンによる設定外部化
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterAutoOverlayCleanupSettings(IServiceCollection services)
        {
            services.ConfigureOptions<AutoOverlayCleanupOptionsSetup>();
        }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            yield return typeof(PlatformModule); // PlatformModule → InfrastructureModule間接依存で十分
            // 🔧 UltraThink Phase 4-6 修正: 直接InfrastructureModule依存を除去し重複登録解決
            // yield return typeof(InfrastructureModule); // PlatformModule経由で間接取得
            yield return typeof(BatchOcrModule); // バッチOCR処理モジュール
            yield return typeof(CaptureModule); // キャプチャサービス統合
            // 🗑️ [PHASE18] Phase15OverlayModule削除完了 - 統一オーバーレイシステムに移行
        }
    }
