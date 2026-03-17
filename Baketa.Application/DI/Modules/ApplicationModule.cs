using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Application.Configuration;
using Baketa.Application.DI.Modules;
using Baketa.Application.Services.Capture;
using Baketa.Application.Services.Events;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Capture;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Handlers;
using Baketa.Core.Events.Implementation;
using Baketa.Core.Models.Processing;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Platform.DI.Modules;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using EventAggregatorImpl = Baketa.Core.Events.Implementation.EventAggregator;
using TranslationAbstractions = Baketa.Core.Abstractions.Translation;

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
        // 🔥 [PHASE12.2_DI_DEBUG] ApplicationModule.RegisterServices()実行開始
        Console.WriteLine("🔥🔥🔥 [PHASE12.2_DI_DEBUG] ApplicationModule.RegisterServices()実行開始");

        // 環境設定は、BuildServiceProviderが存在しないか必要なパッケージがないため
        // コメントアウトし、デフォルト値を使用
        //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>()
        //    ?? Core.DI.BaketaEnvironment.Production;
        var environment = Core.DI.BaketaEnvironment.Production;

        // 🎯 UltraThink Phase 1: オーバーレイ自動削除システム設定登録（Gemini Review対応）
        Console.WriteLine("🔥 [PHASE12.2_DI_DEBUG] RegisterAutoOverlayCleanupSettings()呼び出し直前");
        RegisterAutoOverlayCleanupSettings(services);
        Console.WriteLine("✅ [PHASE12.2_DI_DEBUG] RegisterAutoOverlayCleanupSettings()完了");

        // OCR処理モジュールは Infrastructure.DI.OcrProcessingModule で登録

        // OCRアプリケーションサービス
        Console.WriteLine("🔥 [PHASE12.2_DI_DEBUG] RegisterOcrApplicationServices()呼び出し直前");
        RegisterOcrApplicationServices(services);
        Console.WriteLine("✅ [PHASE12.2_DI_DEBUG] RegisterOcrApplicationServices()完了");

        // 翻訳アプリケーションサービス
        Console.WriteLine("🔥 [PHASE12.2_DI_DEBUG] RegisterTranslationApplicationServices()呼び出し直前");
        RegisterTranslationApplicationServices(services);
        Console.WriteLine("✅ [PHASE12.2_DI_DEBUG] RegisterTranslationApplicationServices()完了");

        // その他のアプリケーションサービス
        Console.WriteLine("🔥 [PHASE12.2_DI_DEBUG] RegisterOtherApplicationServices()呼び出し直前");
        RegisterOtherApplicationServices(services, environment);
        Console.WriteLine("✅ [PHASE12.2_DI_DEBUG] RegisterOtherApplicationServices()完了");

        // イベントハンドラー
        Console.WriteLine("🔥🔥🔥 [PHASE12.2_DI_DEBUG] RegisterEventHandlers()呼び出し直前");
        RegisterEventHandlers(services);
        Console.WriteLine("✅✅✅ [PHASE12.2_DI_DEBUG] RegisterEventHandlers()完了");

        Console.WriteLine("✅✅✅ [PHASE12.2_DI_DEBUG] ApplicationModule.RegisterServices()完了");
    }

    /// <summary>
    /// OCRアプリケーションサービスを登録します。
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterOcrApplicationServices(IServiceCollection services)
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
        // [Issue #542] TextTranslationClientを明示的に注入
        if (!services.Any(s => s.ServiceType == typeof(TranslationAbstractions.ITranslationService)))
        {
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.TextTranslationClient>();
            services.AddSingleton<TranslationAbstractions.ITranslationService>(sp =>
            {
                var logger = sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DefaultTranslationService>>();
                var engines = sp.GetServices<TranslationAbstractions.ITranslationEngine>();
                var configuration = sp.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>();
                var eventAggregator = sp.GetService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                var textTranslationClient = sp.GetService<Baketa.Infrastructure.Translation.Services.TextTranslationClient>();
                return new DefaultTranslationService(logger, engines, configuration, eventAggregator, textTranslationClient);
            });
        }

        // [Issue #293 Phase 8] TranslationGatekeeperService廃止 - TextChangeDetectionServiceに統合済み

        // 🚀 翻訳モデル事前ロード戦略 - Clean Architecture準拠実装
        // UltraPhase 10.5: TranslationModelLoaderが DI初期化時にハングを引き起こすため一時的に無効化
        // services.AddSingleton<Baketa.Application.Services.IApplicationInitializer,
        //     Baketa.Application.Services.TranslationModelLoader>();

        // 🔧 PHASE 3: TranslationPipelineService DI Registration (Critical Issue対応)
        // 🎯 [OVERLAY_UNIFICATION] IOverlayManager統合 - Geminiレビュー指摘事項対応
        services.AddSingleton<Baketa.Application.Services.Translation.TranslationPipelineService>(provider =>
        {
            var eventAggregator = provider.GetRequiredService<IEventAggregator>();
            var settingsService = provider.GetRequiredService<IUnifiedSettingsService>();
            var translationService = provider.GetRequiredService<TranslationAbstractions.ITranslationService>();
            var overlayManager = provider.GetRequiredService<Baketa.Core.Abstractions.UI.Overlays.IOverlayManager>();
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

        // 🎯 [OPTION_A] CoordinateBasedTranslationService正式登録 - SmartProcessingPipelineService統合
        services.AddSingleton<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>(provider =>
        {
            Console.WriteLine("🎯 [OPTION_A] CoordinateBasedTranslationService Factory開始");

            try
            {
                Console.WriteLine("🔍 [OPTION_A] ITranslationProcessingFacade取得中...");
                var processingFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Processing.ITranslationProcessingFacade>();
                Console.WriteLine($"✅ [OPTION_A] ITranslationProcessingFacade取得成功: {processingFacade.GetType().Name}");

                Console.WriteLine("🔍 [OPTION_A] IConfigurationFacade取得中...");
                var configurationFacade = provider.GetRequiredService<Baketa.Core.Abstractions.Configuration.IConfigurationFacade>();
                Console.WriteLine($"✅ [OPTION_A] IConfigurationFacade取得成功: {configurationFacade.GetType().Name}");

                Console.WriteLine("🔍 [OPTION_A] IStreamingTranslationService取得中...");
                var streamingService = provider.GetService<TranslationAbstractions.IStreamingTranslationService>();
                Console.WriteLine($"✅ [OPTION_A] IStreamingTranslationService取得成功: {streamingService?.GetType().Name ?? "null"}");

                Console.WriteLine("🔍 [OPTION_A] ITextChunkAggregatorService取得中...");
                var textChunkAggregatorService = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITextChunkAggregatorService>();
                Console.WriteLine($"✅ [OPTION_A] ITextChunkAggregatorService取得成功: {textChunkAggregatorService.GetType().Name}");

                Console.WriteLine("🔍 [OPTION_A] ISmartProcessingPipelineService取得中...");
                var pipelineService = provider.GetRequiredService<Baketa.Core.Abstractions.Processing.ISmartProcessingPipelineService>();
                Console.WriteLine($"✅ [OPTION_A] ISmartProcessingPipelineService取得成功: {pipelineService.GetType().Name}");

                // [Issue #230] テキストベース変化検知サービス取得
                var textChangeDetectionService = provider.GetService<Baketa.Core.Abstractions.Processing.ITextChangeDetectionService>();
                Console.WriteLine($"✅ [Issue #230] ITextChangeDetectionService取得: {(textChangeDetectionService != null ? "成功" : "null (オプショナル)")}");

                Console.WriteLine("🎯 [OPTION_A] CoordinateBasedTranslationService インスタンス作成開始（12パラメータ）");
                var logger = provider.GetService<ILogger<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>>();
                var translationModeService = provider.GetService<Baketa.Core.Abstractions.Services.ITranslationModeService>(); // 🔧 [SINGLESHOT_FIX]
                // [Issue #290] Fork-Join並列実行用の依存関係
                var fallbackOrchestrator = provider.GetService<Baketa.Core.Translation.Abstractions.IFallbackOrchestrator>();
                var licenseManager = provider.GetService<Baketa.Core.Abstractions.License.ILicenseManager>();
                var cloudTranslationAvailabilityService = provider.GetService<Baketa.Core.Abstractions.Translation.ICloudTranslationAvailabilityService>();
                // [Issue #293] ROI学習マネージャー（ヒートマップ値取得用）
                var roiManager = provider.GetService<Baketa.Core.Abstractions.Roi.IRoiManager>();
                Console.WriteLine($"✅ [Issue #293] IRoiManager取得: {(roiManager != null ? $"成功 (Enabled={roiManager.IsEnabled})" : "null (オプショナル)")}");
                // [Issue #293] ウィンドウ情報取得用
                var windowManager = provider.GetService<Baketa.Core.Abstractions.Platform.IWindowManager>();
                Console.WriteLine($"✅ [Issue #293] IWindowManager取得: {(windowManager != null ? "成功" : "null (オプショナル)")}");
                var instance = new Baketa.Application.Services.Translation.CoordinateBasedTranslationService(
                    processingFacade,
                    configurationFacade,
                    streamingService,
                    textChunkAggregatorService, // 🎯 [OPTION_A] 追加パラメータ
                    pipelineService, // 🎯 [OPTION_A] 追加パラメータ - SmartProcessingPipelineService統合
                    textChangeDetectionService, // [Issue #230/#293] テキスト変化検知（Gatekeeper統合）
                    translationModeService, // 🔧 [SINGLESHOT_FIX] Singleshotモード判定用
                    fallbackOrchestrator, // [Issue #290] Fork-Join Cloud AI翻訳
                    licenseManager, // [Issue #290] ライセンスチェック
                    cloudTranslationAvailabilityService, // [Issue #290] Cloud翻訳可用性チェック
                    roiManager, // [Issue #293] ROI学習マネージャー（ヒートマップ値取得用）
                    windowManager, // [Issue #293] ウィンドウ情報取得用
                    provider.GetService<Microsoft.Extensions.Options.IOptionsMonitor<Baketa.Core.Settings.ImageChangeDetectionSettings>>(), // [Issue #401] 画面安定化設定
                    provider.GetService<Baketa.Core.Abstractions.Translation.ICloudTranslationCache>(), // [Issue #415] Cloud翻訳キャッシュ
                    provider.GetService<Baketa.Core.Abstractions.Processing.IDetectionBoundsCache>(), // [Issue #508] Detection-Onlyキャッシュからのフォールバックヒント
                    logger);
                Console.WriteLine("✅ [OPTION_A] CoordinateBasedTranslationService インスタンス作成完了 - 画面変化検知＋テキスト変化検知＋Singleshotバイパス＋Fork-Join＋Gate統合済み");
                return instance;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [OPTION_A] CoordinateBasedTranslationService Factory失敗: {ex.GetType().Name}: {ex.Message}");
                throw;
            }
        });

        // 🔥 [ISSUE#163_PHASE5] 翻訳モードサービス登録（State Pattern実装）
        Console.WriteLine("🔥 [ISSUE#163_PHASE5] TranslationModeService DI登録開始");
        services.AddSingleton<Baketa.Application.Services.TranslationModes.LiveTranslationMode>();
        services.AddSingleton<Baketa.Application.Services.TranslationModes.SingleshotTranslationMode>();
        services.AddSingleton<Baketa.Application.Services.TranslationModes.TranslationModeService>();
        services.AddSingleton<Baketa.Core.Abstractions.Services.ITranslationModeService>(
            provider => provider.GetRequiredService<Baketa.Application.Services.TranslationModes.TranslationModeService>());
        Console.WriteLine("✅ [ISSUE#163_PHASE5] TranslationModeService DI登録完了");

        // 翻訳統合サービス（IEventAggregatorの依存を削除）
        services.AddSingleton<Baketa.Application.Services.Translation.TranslationOrchestrationService>(provider =>
        {
            Console.WriteLine("🔍 [DI_DEBUG] TranslationOrchestrationService Factory開始");

            try
            {
                var captureService = provider.GetRequiredService<ICaptureService>();
                var settingsService = provider.GetRequiredService<ISettingsService>();
                var ocrEngine = provider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                var eventAggregator = provider.GetRequiredService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                var translationService = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITranslationService>();
                var translationDictionaryService = (Baketa.Core.Abstractions.Services.ITranslationDictionaryService?)null; // REMOVED: 辞書翻訳削除済み
                var logger = provider.GetService<ILogger<Baketa.Application.Services.Translation.TranslationOrchestrationService>>();

                // 🎯 [OPTION_A] CoordinateBasedTranslationService取得 - AddSingletonで既に登録済み
                Console.WriteLine("🎯 [OPTION_A] CoordinateBasedTranslationService取得開始");
                var coordinateBasedTranslation = provider.GetRequiredService<Baketa.Application.Services.Translation.CoordinateBasedTranslationService>();
                Console.WriteLine($"✅ [OPTION_A] CoordinateBasedTranslationService取得成功 - SmartProcessingPipelineService統合済み");
                Console.WriteLine($"✅ [DI_DEBUG] EventAggregator取得成功: {eventAggregator.GetType().Name}");
                Console.WriteLine($"🚫 [DI_DEBUG] TranslationDictionaryService削除済み: {translationDictionaryService?.GetType().Name ?? "null - REMOVED"}");

                // 🚀 [Issue #290] Fork-Join並列実行用サービス取得（オプショナル）
                var fallbackOrchestrator = provider.GetService<Baketa.Core.Translation.Abstractions.IFallbackOrchestrator>();
                var licenseManager = provider.GetService<Baketa.Core.Abstractions.License.ILicenseManager>();
                Console.WriteLine($"🚀 [Issue #290] Fork-Join: FallbackOrchestrator={fallbackOrchestrator != null}, LicenseManager={licenseManager != null}");

                var ocrSettings = provider.GetRequiredService<IOptionsMonitor<Baketa.Core.Settings.OcrSettings>>();

                // Issue #293: 投機的OCRサービス（オプショナル）
                var speculativeOcrService = provider.GetService<Baketa.Core.Abstractions.OCR.ISpeculativeOcrService>();

                // [Issue #389] ウィンドウ存在チェック用
                var windowManagerAdapter = provider.GetService<Baketa.Core.Abstractions.Platform.Windows.Adapters.IWindowManagerAdapter>();

                // [Issue #410] テキスト変化検知キャッシュリセット用
                var textChangeDetectionService = provider.GetService<Baketa.Core.Abstractions.Processing.ITextChangeDetectionService>();

                return new Baketa.Application.Services.Translation.TranslationOrchestrationService(
                    captureService,
                    settingsService,
                    ocrEngine,
                    coordinateBasedTranslation,
                    eventAggregator,
                    ocrSettings,
                    translationService,
                    translationDictionaryService,
                    fallbackOrchestrator,
                    licenseManager,
                    speculativeOcrService,
                    windowManagerAdapter,
                    textChangeDetectionService,
                    provider.GetService<Baketa.Core.Abstractions.Translation.ICloudTranslationCache>(), // [Issue #415] Cloud翻訳キャッシュ
                    provider.GetService<Baketa.Core.Abstractions.Settings.IUnifiedSettingsService>(), // ONNXモデル オンデマンドロード/アンロード
                    provider.GetService<Baketa.Core.Abstractions.Processing.IDetectionBoundsCache>(), // [Issue #525] Detection-Onlyキャッシュ
                    provider.GetService<Baketa.Core.Abstractions.Services.IImageChangeDetectionService>(), // [Issue #525] 画像変化検知キャッシュ
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

        // [Issue #400] Phase 4並列翻訳削除: ParallelTranslationOrchestrator DI登録を無効化
        // Fork-Join Cloud AI翻訳が正規経路となったため不要
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

        // 🔧 [Issue #170] ローディング画面初期化サービス登録
        // 🚀 [Issue #193] GPU環境サービスを先に登録
        services.AddSingleton<Baketa.Core.Abstractions.Services.IGpuEnvironmentService, Baketa.Application.Services.GpuEnvironmentService>();

        // 🎯 [Issue #198] 初期化完了シグナル登録 - 翻訳サーバー起動の遅延制御
        // コンポーネントダウンロード・解凍完了まで翻訳サーバー起動を待機させる
        services.AddSingleton<Baketa.Core.Abstractions.Services.IInitializationCompletionSignal, Baketa.Application.Services.InitializationCompletionSignal>();

        services.AddSingleton<Baketa.Core.Abstractions.Services.ILoadingScreenInitializer, Baketa.Application.Services.ApplicationInitializer>();

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

        // 🔐 [Issue #168] Token Refresh Service - バックグラウンドトークン自動更新
        Console.WriteLine("🔐 [Issue #168] TokenRefreshService DI登録");
        services.AddSingleton<Services.Auth.TokenRefreshService>();
        services.AddSingleton<ITokenRefreshService>(provider => provider.GetRequiredService<Services.Auth.TokenRefreshService>());

        // 🎓 [Issue #293 Phase 10] 学習駆動型投機的OCRサービス
        Console.WriteLine("🎓 [Issue #293 Phase 10] LearningScheduler DI登録");
        services.AddSingleton<Services.Learning.LearningScheduler>();
        services.AddSingleton<Baketa.Core.Abstractions.Roi.ILearningScheduler>(
            provider => provider.GetRequiredService<Services.Learning.LearningScheduler>());

        // 🎓 [Issue #293 Phase 10] バックグラウンド学習サービス（IHostedService）
        // [Issue #293 Fix] IWindowManagerがオプショナル依存のため、ファクトリで明示的にnull許容
        Console.WriteLine("🎓 [Issue #293 Phase 10] BackgroundLearningService DI登録");
        services.AddSingleton<Services.Learning.BackgroundLearningService>(provider =>
        {
            return new Services.Learning.BackgroundLearningService(
                provider.GetRequiredService<Baketa.Core.Abstractions.Roi.ILearningScheduler>(),
                provider.GetService<Baketa.Core.Abstractions.OCR.ISpeculativeOcrService>(),
                provider.GetService<Baketa.Core.Abstractions.Roi.IRoiManager>(),
                provider.GetService<Baketa.Core.Abstractions.Services.ICaptureService>(),
                provider.GetService<Baketa.Core.Abstractions.Platform.IWindowManager>(),  // Optional - may be null
                provider.GetService<Services.UI.IWindowManagementService>(),
                provider.GetRequiredService<Baketa.Core.Abstractions.Monitoring.IResourceMonitor>(),
                provider.GetRequiredService<Baketa.Core.Abstractions.Services.ITranslationModeService>(),
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Baketa.Core.Settings.SpeculativeOcrSettings>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.Learning.BackgroundLearningService>>()
            );
        });
        services.AddHostedService(provider => provider.GetRequiredService<Services.Learning.BackgroundLearningService>());

        // 🎯 [Issue #324] ROI監視ホステッドサービス
        // 学習完了後にROI領域を監視し、テキスト送りを検知
        Console.WriteLine("🎯 [Issue #324] RoiMonitoringHostedService DI登録");
        services.AddSingleton<Services.Learning.RoiMonitoringHostedService>(provider =>
        {
            return new Services.Learning.RoiMonitoringHostedService(
                provider.GetService<Baketa.Core.Abstractions.Roi.IRoiChangeMonitorService>(),
                provider.GetService<Baketa.Core.Abstractions.Roi.IRoiManager>(),
                provider.GetService<Baketa.Core.Abstractions.Services.ICaptureService>(),
                provider.GetService<Services.UI.IWindowManagementService>(),
                provider.GetRequiredService<Baketa.Core.Abstractions.Services.ITranslationModeService>(),
                provider.GetService<Baketa.Core.Abstractions.Events.IEventAggregator>(),
                provider.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<Baketa.Core.Settings.RoiManagerSettings>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.Learning.RoiMonitoringHostedService>>()
            );
        });
        services.AddHostedService(provider => provider.GetRequiredService<Services.Learning.RoiMonitoringHostedService>());

        // [Issue #497] ファントムカーソルサービス
        services.AddSingleton<Services.Cursor.PhantomCursorHostedService>(provider =>
        {
            return new Services.Cursor.PhantomCursorHostedService(
                provider.GetService<Services.UI.IWindowManagementService>(),
                provider.GetService<Baketa.Core.Abstractions.Services.ICursorStateProvider>(),
                provider.GetService<Func<Microsoft.Extensions.Logging.ILogger, Baketa.Core.Abstractions.Services.IPhantomCursorWindowAdapter>>(),
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Services.Cursor.PhantomCursorHostedService>>()
            );
        });
        services.AddSingleton<Baketa.Core.Abstractions.Services.IPhantomCursorService>(
            provider => provider.GetRequiredService<Services.Cursor.PhantomCursorHostedService>());
        services.AddHostedService(provider => provider.GetRequiredService<Services.Cursor.PhantomCursorHostedService>());

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
        // 🔥 [PHASE12.2_DI_DEBUG] RegisterEventHandlers()実行開始
        Console.WriteLine("🔥🔥🔥 [PHASE12.2_DI_DEBUG] RegisterEventHandlers()メソッド内部開始");

        // 翻訳モード変更イベントプロセッサー
        Console.WriteLine("🔥 [PHASE12.2_DI_DEBUG] TranslationModeChangedEventProcessor登録");
        services.AddSingleton<Baketa.Application.Events.Processors.TranslationModeChangedEventProcessor>();
        Console.WriteLine("✅ [PHASE12.2_DI_DEBUG] TranslationModeChangedEventProcessor登録完了");


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

        // 🎉 [PHASE12.2] AggregatedChunksReadyEventHandler登録 - 2重翻訳アーキテクチャ排除
        Console.WriteLine("🎉 [PHASE12.2] AggregatedChunksReadyEventHandler DI登録 - TimedChunkAggregatorイベント駆動処理");
        services.AddSingleton<Baketa.Application.EventHandlers.Translation.AggregatedChunksReadyEventHandler>();
        services.AddSingleton<IEventProcessor<Baketa.Core.Events.Translation.AggregatedChunksReadyEvent>>(
            provider => provider.GetRequiredService<Baketa.Application.EventHandlers.Translation.AggregatedChunksReadyEventHandler>());

        // 🛑 [PHASE6.1] StopTranslationRequestEventHandler登録 - Stop処理問題修正
        Console.WriteLine("🛑 [PHASE6.1] StopTranslationRequestEventHandler DI登録 - Stop押下後も処理継続問題の修正");
        services.AddSingleton<Baketa.Application.EventHandlers.Translation.StopTranslationRequestEventHandler>();
        services.AddSingleton<IEventProcessor<Baketa.Core.Events.EventTypes.StopTranslationRequestEvent>>(
            provider => provider.GetRequiredService<Baketa.Application.EventHandlers.Translation.StopTranslationRequestEventHandler>());

        // 🔥 [ISSUE#163_PHASE5] SingleshotEventProcessor登録はUIModuleに移動（Clean Architecture準拠）

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
            var roiSettings = provider.GetService<IOptionsMonitor<RoiDiagnosticsSettings>>();

            // 🎯 Phase 3.17.9: IImageToReferencedSafeImageConverter注入修正
            var imageToReferencedConverter = provider.GetService<IImageToReferencedSafeImageConverter>();

            // 🔧 [SINGLESHOT_FIX] ITranslationModeService注入 - Singleshotモード検出のため
            var translationModeService = provider.GetService<Baketa.Core.Abstractions.Services.ITranslationModeService>();

            return new Baketa.Application.Events.Handlers.CaptureCompletedHandler(
                eventAggregator,
                chunkAggregatorService,
                provider.GetRequiredService<TranslationAbstractions.ILanguageConfigurationService>(),
                smartPipeline,
                logger,
                settings,
                roiSettings,
                imageToReferencedConverter,
                translationModeService);
        });
        services.AddSingleton<IEventProcessor<CaptureCompletedEvent>>(
            provider => provider.GetRequiredService<Baketa.Application.Events.Handlers.CaptureCompletedHandler>());
        Console.WriteLine("✅ [DI_DEBUG] CaptureCompletedHandler登録完了 - キャプチャ画像保存機能付き");

        // 🔥 [PHASE5] ROIImageCapturedEventHandler削除 - ROI廃止により不要

        // ⚡ [PHASE2_FIX] OcrRequestHandler登録 - 翻訳処理チェーン連鎖修復
        Console.WriteLine("🔍 [DI_DEBUG] OcrRequestHandler登録開始");
        services.AddSingleton<Baketa.Application.Events.Handlers.OcrRequestHandler>();
        services.AddSingleton<IEventProcessor<OcrRequestEvent>>(
            provider => provider.GetRequiredService<Baketa.Application.Events.Handlers.OcrRequestHandler>());
        Console.WriteLine("✅ [DI_DEBUG] OcrRequestHandler登録完了 - Phase 2翻訳チェーン修復");

        // 🔧 [Issue #195] ResourceMonitoringEventHandler登録 - 未処理イベント警告を解消
        services.AddSingleton<Baketa.Application.EventHandlers.ResourceMonitoringEventHandler>();
        services.AddSingleton<IEventProcessor<ResourceMonitoringEvent>>(
            provider => provider.GetRequiredService<Baketa.Application.EventHandlers.ResourceMonitoringEventHandler>());

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
        // NOTE: [PP-OCRv5削除] BatchOcrModule削除 - SuryaOcrModuleに移行
        yield return typeof(CaptureModule); // キャプチャサービス統合
        yield return typeof(OverlayOrchestrationModule); // オーバーレイ調整・管理システム（旧Phase15OverlayModule）
    }
}
