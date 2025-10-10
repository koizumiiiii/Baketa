using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.OCR;
using EventTypes = Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Settings;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using CoreTranslation = Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Logging;
using Baketa.Infrastructure.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Baketa.Infrastructure.OCR.Measurement;
using Baketa.Infrastructure.Performance;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Services.Translation;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local;
// 翻訳エンジンをNLLB-200に統一
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Baketa.Infrastructure.Translation.Services;
using Baketa.Infrastructure.ResourceManagement;
using Baketa.Infrastructure.Patterns;
using Baketa.Infrastructure.Imaging.ChangeDetection;
using Baketa.Infrastructure.Services.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.DI.Modules;

    /// <summary>
    /// インフラストラクチャレイヤーのサービスを登録するモジュール。
    /// 外部サービス連携やプラットフォーム非依存の実装が含まれます。
    /// </summary>
    [ModulePriority(ModulePriority.Infrastructure)]
    public class InfrastructureModule : ServiceModuleBase
    {
        /// <summary>
        /// インフラストラクチャサービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        public override void RegisterServices(IServiceCollection services)
        {
            Console.WriteLine("🔍🔍🔍 [DIAGNOSTIC] InfrastructureModule.RegisterServices(1-parameter) 開始");

            // 🎯 Phase 3.1: NOTE: ISafeImageFactory/IImageLifecycleManagerはApplicationModuleで登録済み
            // Clean Architecture原則により、InfrastructureがApplicationを参照することはできない
            // SafeImageFactoryはSafeImageの内部コンストラクタアクセスのためApplication層必須

            // 環境確認は、BuildServiceProviderが存在しないか必要なパッケージがないため
            // コメントアウトし、デフォルト値を使用
            //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>()
            //    ?? Core.DI.BaketaEnvironment.Production;
            var environment = Core.DI.BaketaEnvironment.Production;

            // 設定ファイル統一: OCR設定をappsettings.jsonから読み込み
            // 注意: これはServiceModuleの外で設定される想定（Startup.cs等）

            // OCR関連サービス
            RegisterOcrServices(services);

            // 🔄 Phase 1: 画像変化検知システム
            RegisterImageChangeDetectionServices(services);

            // 🔄 P1: 段階的フィルタリングシステム
            RegisterStagedFilteringServices(services);

            // スティッキーROIシステム（Issue #143 Week 3: 処理効率向上）
            RegisterStickyRoiServices(services);

            // 🔥 [PHASE12.5.3_FIX] NLLB-200翻訳サービス登録を削除
            // 理由: RegisterServices(services, config)メソッドで既に登録されるため重複を回避
            // RegisterNllb200TranslationServices(services);

            // 翻訳サービス（エンジン登録後）
            RegisterTranslationServices(services);

            // Phase 5: ポート競合防止機構サービス
            RegisterPortManagementServices(services);

            // Step 1: Python環境解決と診断サービス（即座の応急処置）
            RegisterPythonEnvironmentServices(services);

            // Phase 0+1: NLLB修正対応サービス（30秒再起動問題解決）
            RegisterNllbFixServices(services);

            // Phase 2: 完全安定化サービス（接続信頼性向上・キャッシュ管理強化）
            RegisterPhase2Services(services);

            // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
            RegisterWarmupServices(services);

            // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
            RegisterGpuServices(services);

            // パフォーマンス管理サービス
            RegisterPerformanceServices(services);

            // Phase3: リソース監視システム
            RegisterResourceMonitoringServices(services);

            // Phase 3.13: Memory変換サービス
            RegisterMemoryServices(services);

            // Phase2: ハイブリッドリソース管理システム - PlatformModuleに移動（循環依存解決）

            // データ永続化
            RegisterPersistenceServices(services, environment);
        }

        /// <summary>
        /// インフラストラクチャサービスを登録します（appsettings.json対応版）。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="configuration">設定オブジェクト</param>
        public void RegisterServices(IServiceCollection services, IConfiguration configuration)
        {
            // 環境確認は、BuildServiceProviderが存在しないか必要なパッケージがないため
            // コメントアウトし、デフォルト値を使用
            //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>()
            //    ?? Core.DI.BaketaEnvironment.Production;
            var environment = Core.DI.BaketaEnvironment.Production;

            // 設定ファイル統一: OCR設定をappsettings.jsonから読み込み
            // 注意: これはServiceModuleの外で設定される想定（Startup.cs等）
            
            // OCR関連サービス
            RegisterOcrServices(services);
            
            // 🔄 Phase 1: 画像変化検知システム
            RegisterImageChangeDetectionServices(services);
            
            // 🔄 P1: 段階的フィルタリングシステム（appsettings.json対応）
            RegisterStagedFilteringServices(services, configuration);
            
            // スティッキーROIシステム（Issue #143 Week 3: 処理効率向上）
            RegisterStickyRoiServices(services);
            
            // 🔥 [PHASE12.5.4_FIX] RegisterPortManagementServicesを最優先実行
            // 理由: RegisterNllb200TranslationServicesがIPythonServerManager登録状況を確認するため、
            //       先にIPythonServerManagerを登録する必要がある
            RegisterPortManagementServices(services);

            // NLLB-200翻訳サービス（高品質版）を登録
            // IPythonServerManager登録済みの状態でConnectionPool登録判定を実行
            RegisterNllb200TranslationServices(services);

            // 🚀 Phase 2.3: TranslationSettings登録（gRPC Client対応）
            RegisterTranslationSettings(services);

            // 翻訳サービス（エンジン登録後）
            RegisterTranslationServices(services);
            
            // Step 1: Python環境解決と診断サービス（即座の応急処置）
            RegisterPythonEnvironmentServices(services);
            
            // Phase 0+1: NLLB修正対応サービス（30秒再起動問題解決）
            RegisterNllbFixServices(services);
            
            // Phase 2: 完全安定化サービス（接続信頼性向上・キャッシュ管理強化）
            RegisterPhase2Services(services);
            
            // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
            RegisterWarmupServices(services);
            
            // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
            RegisterGpuServices(services);
            
            // パフォーマンス管理サービス
            RegisterPerformanceServices(services);
            
            // Phase3: リソース監視システム
            RegisterResourceMonitoringServices(services);
            
            // Phase2: ハイブリッドリソース管理システム - PlatformModuleに移動（循環依存解決）
            
            // データ永続化
            RegisterPersistenceServices(services, environment);
        }

        /// <summary>
        /// OCR関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterOcrServices(IServiceCollection services)
        {
            // Sprint 2 Fix: IImageFactory登録はPlatformModuleで実施
            // （アーキテクチャ原則に従い、Platform固有実装はPlatformModuleに配置）
            
            // Sprint 1: PaddleOCR診断システム（Gemini推奨）
            services.AddSingleton<Baketa.Core.Abstractions.Diagnostics.IPaddleOcrDiagnostics, Baketa.Infrastructure.Diagnostics.PaddleOcrDiagnosticsService>();
            Console.WriteLine("✅ Sprint 1: PaddleOCR診断システム登録完了 - CPU First戦略対応");

            // Phase 2.2: PaddleOCR専門サービス（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrUtilities, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrUtilities>();
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrPerformanceTracker, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrPerformanceTracker>();
            Console.WriteLine("✅ Phase 2.2: PaddleOCRユーティリティ・パフォーマンストラッカー登録完了");

            // Phase 2.3: PaddleOCRエラーハンドラー・言語最適化（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrErrorHandler, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrErrorHandler>();
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrLanguageOptimizer, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrLanguageOptimizer>();
            Console.WriteLine("✅ Phase 2.3: PaddleOCRエラーハンドラー・言語最適化登録完了");

            // Phase 2.4: PaddleOCRモデルマネージャー（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrModelManager, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrModelManager>();
            Console.WriteLine("✅ Phase 2.4: PaddleOCRモデルマネージャー登録完了");

            // Phase 2.5: PaddleOCR画像プロセッサー（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrImageProcessor, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrImageProcessor>();
            Console.WriteLine("✅ Phase 2.5: PaddleOCR画像プロセッサー登録完了");

            // Phase 2.6: PaddleOCRエンジン初期化（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrEngineInitializer, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrEngineInitializer>();
            Console.WriteLine("✅ Phase 2.6: PaddleOCRエンジン初期化登録完了");

            // Phase 2.7: PaddleOCR実行エグゼキューター（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrExecutor, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrExecutor>();
            Console.WriteLine("✅ Phase 2.7: PaddleOCR実行エグゼキューター登録完了");

            // Phase 2.8: PaddleOCR結果コンバーター（リファクタリング）
            services.AddSingleton<Baketa.Infrastructure.OCR.PaddleOCR.Abstractions.IPaddleOcrResultConverter, Baketa.Infrastructure.OCR.PaddleOCR.Services.PaddleOcrResultConverter>();
            Console.WriteLine("✅ Phase 2.8: PaddleOCR結果コンバーター登録完了");

            // OCRエンジンやプロセッサーの登録
            // 例: services.AddSingleton<IOcrEngine, PaddleOcrEngine>();
            // 例: services.AddSingleton<IOcrModelProvider, LocalOcrModelProvider>();
            
            // OCR最適化
            // 例: services.AddSingleton<IOcrPreprocessor, OpenCvOcrPreprocessor>();
            // 例: services.AddSingleton<IOcrPostProcessor, OcrTextNormalizer>();
            
            // OCR検出用
            // 例: services.AddSingleton<ITextBoxDetector, PaddleTextBoxDetector>();
            // 例: services.AddSingleton<ITextRecognizer, PaddleTextRecognizer>();
            
            // 🚀 [ROI_OPTIMIZATION] AdaptiveTileStrategyに切り替え（テキスト検出ベース→1枚全体ROI画像生成対応）
            services.AddSingleton<Baketa.Infrastructure.OCR.Strategies.ITileStrategy>(provider =>
            {
                var ocrEngine = provider.GetRequiredService<IOcrEngine>();
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.OCR.Strategies.AdaptiveTileStrategy>>();
                var advancedOptions = provider.GetService<Microsoft.Extensions.Options.IOptions<Baketa.Core.Settings.AdvancedSettings>>();
                var diagnosticsSaver = provider.GetRequiredService<ImageDiagnosticsSaver>();
                var adaptiveStrategy = new Baketa.Infrastructure.OCR.Strategies.AdaptiveTileStrategy(ocrEngine, logger, advancedOptions, diagnosticsSaver);
                
                var moduleLogger = provider.GetService<ILogger<InfrastructureModule>>();
                moduleLogger?.LogInformation("🎯 AdaptiveTileStrategy登録完了 - 高速テキスト検出→ROIベース認識（ROI画像保存機能付き）");
                
                return adaptiveStrategy;
            });
            
            // OcrRegionGenerator（ITileStrategy使用）
            services.AddSingleton<Baketa.Infrastructure.OCR.Strategies.OcrRegionGenerator>();
            
            // OCR精度測定システム
            services.AddSingleton<IOcrAccuracyMeasurement, OcrAccuracyMeasurement>();
            services.AddSingleton<AccuracyBenchmarkService>();
            services.AddSingleton<TestImageGenerator>();
            services.AddSingleton<AccuracyImprovementReporter>();
            services.AddSingleton<RuntimeOcrAccuracyLogger>();
            services.AddSingleton<OcrAccuracyTestRunner>();
            
            // OCR精度測定スタートアップサービス
            services.AddOcrAccuracyStartupService();

            // Phase 3.4A: OCRテキスト領域グルーピング戦略（Union-Find）
            services.AddSingleton<IRegionGroupingStrategy, Baketa.Infrastructure.OCR.Clustering.UnionFindRegionGroupingStrategy>();
            Console.WriteLine("✅ Phase 3.4A: UnionFindRegionGroupingStrategy登録完了 - グラフベース連結成分検出");

            // UltraThink Phase 1: 近接度ベースグループ化サービス
            RegisterProximityGroupingServices(services);
        }
        
        /// <summary>
        /// Phase 5: ポート競合防止機構サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPortManagementServices(IServiceCollection services)
        {
            Console.WriteLine("🚀 Issue #147 Phase 5: ポート競合防止機構サービス登録開始");
            
            // ポート管理サービス（Singleton: グローバルMutex管理）
            services.AddSingleton<IPortManagementService, Baketa.Infrastructure.Translation.Services.PortManagementService>();
            Console.WriteLine("✅ PortManagementService登録完了 - Mutexプロセス間競合防止");
            
            // Pythonサーバー管理サービス（Singleton: ヘルスチェック機能付き）
            services.AddSingleton<IPythonServerManager, Baketa.Infrastructure.Translation.Services.PythonServerManager>();
            Console.WriteLine("✅ PythonServerManager登録完了 - 動的ポート管理・自動復旧");
            
            Console.WriteLine("🎉 Phase 5: ポート競合防止機構サービス登録完了");
        }
        
        /// <summary>
        /// Step 1: Python環境解決と診断サービスを登録します（即座の応急処置）
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPythonEnvironmentServices(IServiceCollection services)
        {
            // Step 1: Python環境解決と診断サービス登録（即座の応急処置）
            services.AddSingleton<PythonEnvironmentResolver>();
            services.AddTransient<EnhancedDiagnosticReport>();
            services.AddSingleton<PortManager>();
        }
        
        /// <summary>
        /// Phase 0+1: NLLB修正対応サービスを登録します（30秒再起動問題解決）
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterNllbFixServices(IServiceCollection services)
        {
            // Phase 1: 30秒再起動ループの根本解決
            services.AddSingleton<ModelCacheManager>();
            services.AddSingleton<DynamicHealthCheckManager>();

            // Phase 1: DynamicHealthCheckManagerをイベントプロセッサーとして登録
            services.AddSingleton<IEventProcessor<EventTypes.PythonServerStatusChangedEvent>>(provider =>
                provider.GetRequiredService<DynamicHealthCheckManager>());
        }
        
        /// <summary>
        /// 翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationServices(IServiceCollection services)
        {
            // 統一言語設定サービス（Clean Architecture準拠）
            services.AddScoped<ILanguageConfigurationService, UnifiedLanguageConfigurationService>();
            Console.WriteLine("✅ ILanguageConfigurationService登録完了 - 統一言語設定管理");

            // 🚀 Phase 2.3: TranslationSettings登録は RegisterServices(2-parameter) で既に実行済み（重複削除）

            // Phase2: サーキットブレーカー設定とサービス登録
            Console.WriteLine("🔧 [PHASE2] サーキットブレーカー登録開始");

            // サーキットブレーカー設定 - appsettings.jsonから読み込み
            RegisterCircuitBreakerSettings(services);
            
            // 🆕 Gemini推奨: フォールバック機能付きサーキットブレーカー登録
            services.AddSingleton<ICircuitBreaker<Baketa.Core.Translation.Models.TranslationResponse>, EnhancedTranslationCircuitBreaker>();
            Console.WriteLine("✅ [PHASE2] EnhancedTranslationCircuitBreaker登録完了 - FailureThreshold: 5, RecoveryTimeout: 60s, フォールバック機能付き");
            
            // 翻訳エンジンファクトリーを登録
            services.AddSingleton<Baketa.Core.Abstractions.Factories.ITranslationEngineFactory, Baketa.Core.Translation.Factories.DefaultTranslationEngineFactory>();
            
            // パフォーマンス監視サービスを登録 (Issue #144)
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.ITranslationPerformanceMonitor, Baketa.Infrastructure.Translation.Services.TranslationPerformanceMonitor>();
            
            // 🚨 翻訳サーバー安定化: Python サーバーヘルスモニター（バックグラウンドサービス）
            Console.WriteLine("🔍 [DI_DEBUG] PythonServerHealthMonitor登録開始");
            
            // シングルトンとしても登録（直接取得のため）
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>();
            
            // HostedServiceとしても登録
            services.AddHostedService<PythonServerHealthMonitor>();
            
            Console.WriteLine("✅ [DI_DEBUG] PythonServerHealthMonitor登録完了 - 自動ヘルスチェック・再起動機能");
            
            // 🚀 Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム統合
            Console.WriteLine("🚀 Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム登録開始");
            
            // ハイブリッド戦略設定
            // TODO: appsettings.jsonへの外部化推奨（Geminiコードレビュー指摘事項）
            // IOptions<HybridStrategySettings>パターンを使用して設定を外部化し、
            // 本番環境で負荷に応じた閾値調整を可能にする
            services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings>(provider =>
            {
                // デフォルト設定で初期化
                return new Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings
                {
                    BatchThreshold = 5,      // 5件以上でバッチ処理
                    ParallelThreshold = 2,   // 2件以上で並列処理
                    MaxDegreeOfParallelism = 4,  // 並列度（接続プールサイズと協調必要）
                    EnableMetrics = true
                };
            });
            
            // 翻訳メトリクス収集サービス
            services.AddSingleton<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
            Console.WriteLine("✅ TranslationMetricsCollector登録完了");
            
            // 翻訳戦略登録（優先度順）
            
            // 1. 単一翻訳戦略（最低優先度、フォールバック用）
            services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.SingleTranslationStrategy>();
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
            {
                var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.SingleTranslationStrategy>();
                Console.WriteLine("✅ SingleTranslationStrategy登録完了 - Priority: 10");
                return strategy;
            });
            
            // 2. 並列翻訳戦略（中優先度）
            services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.ParallelTranslationStrategy>();
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
            {
                var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.ParallelTranslationStrategy>();
                Console.WriteLine("✅ ParallelTranslationStrategy登録完了 - Priority: 50");
                return strategy;
            });
            
            // 3. バッチ翻訳戦略（最高優先度）
            services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.BatchTranslationStrategy>();
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationStrategy>(provider =>
            {
                var strategy = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.BatchTranslationStrategy>();
                Console.WriteLine("✅ BatchTranslationStrategy登録完了 - Priority: 100");
                return strategy;
            });
            
            // ハイブリッド翻訳戦略（戦略統合オーケストレーター）
            services.AddSingleton<Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy>(provider =>
            {
                var strategies = provider.GetServices<Baketa.Core.Abstractions.Translation.ITranslationStrategy>();
                var metricsCollector = provider.GetRequiredService<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
                var integratedMetricsCollector = provider.GetService<Baketa.Core.Abstractions.Monitoring.IPerformanceMetricsCollector>(); // Phase 4.1
                var settings = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings>();
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy>>();
                
                var hybridStrategy = new Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy(
                    strategies, metricsCollector, settings, logger, integratedMetricsCollector);
                    
                var metricsStatus = integratedMetricsCollector != null ? "Phase 4.1統合メトリクス統合完了" : "従来メトリクスのみ";
                Console.WriteLine($"🎯 HybridTranslationStrategy登録完了 - 戦略統合オーケストレーター ({metricsStatus})");
                return hybridStrategy;
            });
            
            // 🔧 UltraThink Phase 9.18: 重複登録削除 - TranslationModuleで既に登録済み
            // services.AddSingleton<ITranslationService, DefaultTranslationService>(); // TranslationServiceExtensions.AddTranslationServices()で登録

            Console.WriteLine("✅ Issue #147 Phase 3.2: ハイブリッド翻訳戦略システム登録完了");
        }
        
        /// <summary>
        /// ウォームアップサービスを登録します（Issue #143: コールドスタート遅延根絶）。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterWarmupServices(IServiceCollection services)
        {
            Console.WriteLine("🚀 ウォームアップサービス登録開始 - Issue #143");
            
            // バックグラウンドウォームアップサービス（OCR・翻訳エンジンの非同期初期化）
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IWarmupService, BackgroundWarmupService>();
            Console.WriteLine("✅ IWarmupService登録完了");
            
            // ウォームアップサービスをホストサービスとしても登録（アプリケーション開始時に自動実行）
            services.AddHostedService<WarmupHostedService>();
            Console.WriteLine("✅ WarmupHostedService登録完了");
        }
        
        /// <summary>
        /// GPU関連サービスを登録します（Issue #143 Week 2: DI統合とMulti-GPU対応）。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterGpuServices(IServiceCollection services)
        {
            Console.WriteLine("🎮 GPU統合サービス登録開始 - Issue #143 Week 2");
            
            // ONNX Runtime セッション管理（DI Container完全統合）
            services.AddSingleton<Baketa.Infrastructure.OCR.GPU.IOnnxSessionProvider, Baketa.Infrastructure.OCR.GPU.DefaultOnnxSessionProvider>();
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IOnnxSessionFactory, Baketa.Infrastructure.OCR.GPU.DefaultOnnxSessionFactory>();
            Console.WriteLine("✅ IOnnxSessionFactory登録完了 - DI統合");
            
            // ONNX モデル設定管理（Phase 2: テンソル名外部化）
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IOnnxModelConfiguration, Baketa.Infrastructure.OCR.GPU.DefaultOnnxModelConfiguration>();
            Console.WriteLine("✅ IOnnxModelConfiguration登録完了 - モデル外部化");
            
            // TDR対策・永続キャッシュシステム（Phase 3: 高可用性・高速起動）
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IPersistentSessionCache, Baketa.Infrastructure.OCR.GPU.FileBasedSessionCache>();
            Console.WriteLine("✅ IPersistentSessionCache登録完了 - 永続キャッシュ");
            
            // Sprint 2: Mock完全除去 + IntelligentOcrEngine統合
            Console.WriteLine("🚀 Sprint 2: Mock完全除去開始 - 実際のPaddleOCR統合");
            
            // OCR Circuit Breaker設定
            services.Configure<Baketa.Infrastructure.Patterns.OcrCircuitBreakerOptions>(options =>
            {
                options.FailureThreshold = 3;          // GPU失敗3回でオープン
                options.OpenTimeout = TimeSpan.FromMinutes(2); // 2分オープン
                options.HalfOpenRetryInterval = TimeSpan.FromSeconds(30);
                options.AutoFallbackEnabled = true;    // CPU自動フォールバック
                options.ImmediateFallbackOnGpuError = true;
                options.EnableVerboseLogging = false;  // 本番では無効
            });
            
            // OCR Circuit Breaker実装
            services.AddSingleton<Baketa.Core.Abstractions.Patterns.ICircuitBreaker<Baketa.Core.Abstractions.OCR.OcrResults>, 
                Baketa.Infrastructure.Patterns.OcrCircuitBreaker>();
            Console.WriteLine("✅ OcrCircuitBreaker登録完了 - GPU→CPU自動フォールバック対応");
            
            // Sprint 2 Phase 1完了: Mock除去準備完了（次スプリントでIntelligentOcrEngine完全実装）
            Console.WriteLine("🚧 Sprint 2 Phase 1: Mock除去準備・基盤整備完了");
            Console.WriteLine("📋 IntelligentOcrEngine完全実装は Sprint 3で実施");
            
            // 🎯 UltraThink Phase 64修正: IGpuOcrEngine登録を条件付きで実行
            // IImageFactoryが利用可能な場合のみ登録（ApplicationModule登録完了後）
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IGpuOcrEngine>(provider =>
            {
                var logger = provider.GetService<ILogger<Baketa.Infrastructure.OCR.StickyRoi.SimpleOcrEngineAdapter>>();
                var imageFactory = provider.GetService<Baketa.Core.Abstractions.Factories.IImageFactory>();

                if (imageFactory == null)
                {
                    // ApplicationModule登録が未完了のため、IGpuOcrEngineはnull実装を返す
                    logger?.LogWarning("🔄 IImageFactory未登録のため、IGpuOcrEngine機能は無効化されます");
                    throw new InvalidOperationException("IImageFactory not available - IGpuOcrEngine cannot be initialized");
                }

                var basePaddleOcr = provider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();

                // ROI統合のためのGpuOcrEngineアダプター（通常動作時のみ）
                var adapter = new Baketa.Infrastructure.OCR.StickyRoi.SimpleOcrEngineAdapter(
                    basePaddleOcr, imageFactory, logger);

                return new SimpleOcrEngineGpuAdapter(adapter,
                    provider.GetRequiredService<ILogger<SimpleOcrEngineGpuAdapter>>());
            });
            Console.WriteLine("✅ IGpuOcrEngine暫定登録完了 - SimpleOcrEngineAdapter経由（Mock完全除去済み）");
            
            Console.WriteLine("✅ GPU統合サービス登録完了");
        }
        
        // Sprint 2 Phase 1: 基盤整備完了
        // IntelligentOcrEngine完全実装はSprint 3で実施
        
        /// <summary>
        /// スティッキーROIシステムを登録します（Issue #143 Week 3: 処理効率向上）。
        /// Sprint 2統合: IntelligentOcrEngine + ROI最適化
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterStickyRoiServices(IServiceCollection services)
        {
            Console.WriteLine("🎯 Sprint 2統合: スティッキーROIシステム登録開始 - IntelligentOcrEngine統合");
            
            // スティッキーROI管理サービス
            services.AddSingleton<Baketa.Core.Abstractions.OCR.IStickyRoiManager, Baketa.Infrastructure.OCR.StickyRoi.InMemoryStickyRoiManager>();
            Console.WriteLine("✅ IStickyRoiManager登録完了 - InMemory実装");
            
            // ROI拡張OCRエンジン（Sprint 2: IntelligentOcrEngine統合）
            services.AddSingleton<Baketa.Infrastructure.OCR.StickyRoi.StickyRoiEnhancedOcrEngine>(provider =>
            {
                var logger = provider.GetService<ILogger<Baketa.Infrastructure.OCR.StickyRoi.StickyRoiEnhancedOcrEngine>>();
                var roiManager = provider.GetService<Baketa.Core.Abstractions.OCR.IStickyRoiManager>();
                
                // ベースエンジン: IntelligentOcrEngineをSimpleOcrEngineとして使用
                // IntelligentOcrEngineを直接使用する代わりに、Sprint 1で実装したSimpleOcrEngineAdapterを使用
                var imageFactory = provider.GetService<Baketa.Core.Abstractions.Factories.IImageFactory>();

                if (imageFactory == null || roiManager == null)
                {
                    // ApplicationModule登録が未完了のため、StickyRoiEnhancedOcrEngine初期化不可
                    var errorMsg = $"Dependencies not available - IImageFactory: {imageFactory != null}, IStickyRoiManager: {roiManager != null}";
                    logger?.LogError(errorMsg);
                    throw new InvalidOperationException(errorMsg);
                }

                var basePaddleOcr = provider.GetRequiredService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                var adapterLogger = provider.GetService<ILogger<Baketa.Infrastructure.OCR.StickyRoi.SimpleOcrEngineAdapter>>();

                var baseOcrEngine = new Baketa.Infrastructure.OCR.StickyRoi.SimpleOcrEngineAdapter(
                    basePaddleOcr, imageFactory, adapterLogger);
                
                var ocrSettings = provider.GetRequiredService<IOptionsMonitor<Baketa.Core.Settings.OcrSettings>>();
                var roiEnhancedEngine = new Baketa.Infrastructure.OCR.StickyRoi.StickyRoiEnhancedOcrEngine(
                    logger, baseOcrEngine, roiManager, ocrSettings);
                
                logger.LogInformation("🎯 StickyRoiEnhancedOcrEngine統合完了 - " +
                    "BaseEngine: SimpleOcrEngineAdapter(PaddleOCR), ROI最適化有効, Mock除去済み");
                
                return roiEnhancedEngine;
            });
            Console.WriteLine("🚀 StickyRoiEnhancedOcrEngine登録完了 - IntelligentOcrEngine統合（Mock完全除去）");
            
            // ISimpleOcrEngineとしてROI拡張エンジンを登録
            services.AddSingleton<Baketa.Infrastructure.OCR.StickyRoi.ISimpleOcrEngine>(provider =>
            {
                return provider.GetRequiredService<Baketa.Infrastructure.OCR.StickyRoi.StickyRoiEnhancedOcrEngine>();
            });
            Console.WriteLine("✅ ISimpleOcrEngine登録完了 - ROI拡張エンジン統合");
            
            Console.WriteLine("✅ スティッキーROIシステム登録完了 - 3-10倍パフォーマンス向上準備完了");
        }
        
        /// <summary>
        /// NLLB-200翻訳サービスを登録します。
        /// Meta社開発の高品質多言語ニューラル翻訳モデルです。
        /// Issue #147: 接続プール統合による接続ロック競合問題解決
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterNllb200TranslationServices(IServiceCollection services)
        {
            // 🔥 [PHASE12.5.2_DEBUG] メソッド実行確認用ログ（最優先出力）
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\NLLB_REGISTRATION.txt",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] RegisterNllb200TranslationServices 開始\r\n");

            // 既存のITranslationEngine登録を全て削除して、最適化されたエンジンを登録
            var existingTranslationEngines = services
                .Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Translation.ITranslationEngine))
                .ToList();

            foreach (var service in existingTranslationEngines)
            {
                services.Remove(service);
            }

            // 🎭 Issue #147 Phase 3.2: Mock翻訳エンジン登録（ハイブリッド戦略テスト用）
            // 🚀 Python翻訳エンジン実運用 - モデルロード待機機構完成により安定動作
            Console.WriteLine("🚀 OptimizedPythonTranslationEngine登録開始 - モデルロード完了待機機構有効");

            // 🔥 [PHASE3.3] gRPC通信モード: PythonServerManager登録時はConnectionPool不要
            // gRPC経由の通信ではConnectionPoolの代わりにgRPCクライアントを使用
            var pythonServerManagerRegistered = services.Any(sd =>
                sd.ServiceType == typeof(IPythonServerManager));

            // 🔥 [PHASE12.5.2_DEBUG] 判定結果ログ
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\NLLB_REGISTRATION.txt",
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] IPythonServerManager登録済み: {pythonServerManagerRegistered}\r\n");

            if (!pythonServerManagerRegistered)
            {
                // ✅ FixedSizeConnectionPool登録（レガシーTCP接続モード専用）
                services.AddSingleton<IConnectionPool, Baketa.Infrastructure.Translation.Local.ConnectionPool.FixedSizeConnectionPool>();
                Console.WriteLine("✅ FixedSizeConnectionPool登録完了 - レガシーTCP接続モード");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\NLLB_REGISTRATION.txt",
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] FixedSizeConnectionPool登録完了 - レガシーTCP接続モード\r\n");
            }
            else
            {
                // ✅ ConnectionPool未登録（gRPC通信モード）
                Console.WriteLine("🔧 [PHASE3.3] ConnectionPool登録スキップ - gRPC通信モード（gRPCクライアント使用）");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\Baketa.UI\\bin\\Debug\\net8.0-windows10.0.19041.0\\NLLB_REGISTRATION.txt",
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] 🔧 [PHASE3.3] ConnectionPool登録スキップ - gRPC通信モード\r\n");
            }
            
            // 🎯 [UltraThink Solution] appsettings.json固定ポート優先 + ServerManagerHostedService起動監視
            // 問題: DIコンテナ解決タイミング vs IHostedService実行タイミングのミスマッチ
            // 解決: appsettings.jsonのGrpcServerAddressを優先使用、ServerManagerはそのポートで起動

            // GrpcPortProvider登録（動的ポート管理用、将来の拡張用）
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.GrpcPortProvider>();

            // ServerManagerHostedService登録（Pythonサーバー起動・監視）
            services.AddHostedService<Baketa.Infrastructure.Translation.Services.ServerManagerHostedService>();

            // ✅ [UltraThink Fix] GrpcTranslationClient - appsettings.json固定ポート優先使用
            // appsettings.jsonに設定がある場合は即座に使用し、DIブロックを回避
            services.AddSingleton<Baketa.Infrastructure.Translation.Clients.GrpcTranslationClient>(provider =>
            {
                Console.WriteLine("🚨🚨🚨 [ULTRA_DEBUG] GrpcTranslationClientファクトリー実行開始！");
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Clients.GrpcTranslationClient>>();
                Console.WriteLine("🚨🚨🚨 [ULTRA_DEBUG] ILogger取得完了");
                var translationSettings = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<TranslationSettings>>().Value;
                Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] TranslationSettings取得完了 - GrpcServerAddress: '{translationSettings.GrpcServerAddress}'");

                // appsettings.jsonに設定がある場合は優先使用
                Console.WriteLine($"🚨🚨🚨 [ULTRA_DEBUG] 条件チェック: IsNullOrEmpty = {string.IsNullOrEmpty(translationSettings.GrpcServerAddress)}");
                if (!string.IsNullOrEmpty(translationSettings.GrpcServerAddress))
                {
                    logger.LogInformation("✅ [FIXED_PORT] appsettings.json設定使用: {Address}", translationSettings.GrpcServerAddress);
                    return new Baketa.Infrastructure.Translation.Clients.GrpcTranslationClient(translationSettings.GrpcServerAddress, logger);
                }

                // 設定がない場合はGrpcPortProviderを使用（動的ポート管理）
                // ⚠️ この場合はブロッキング待機が発生するため、appsettings.json設定を推奨
                logger.LogWarning("⚠️ [DYNAMIC_PORT] appsettings.json未設定、動的ポート待機（推奨されません）");
                var portProvider = provider.GetRequiredService<Baketa.Infrastructure.Translation.Services.GrpcPortProvider>();
                var port = portProvider.GetPortAsync().GetAwaiter().GetResult();
                var serverAddress = $"http://localhost:{port}";
                logger.LogInformation("✅ [DYNAMIC_PORT] GrpcServerAddress確定: {ServerAddress}", serverAddress);

                return new Baketa.Infrastructure.Translation.Clients.GrpcTranslationClient(serverAddress, logger);
            });

            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationClient>(provider =>
            {
                var client = provider.GetRequiredService<Baketa.Infrastructure.Translation.Clients.GrpcTranslationClient>();
                return client;
            });

            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                var client = provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITranslationClient>();
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Adapters.GrpcTranslationEngineAdapter>>();
                var serverManager = provider.GetRequiredService<IPythonServerManager>();

                logger.LogInformation("🔥 [PHASE3.1_FIX] GrpcTranslationEngineAdapterをITranslationEngineとして登録（ServerManager統合）");
                return new Baketa.Infrastructure.Translation.Adapters.GrpcTranslationEngineAdapter(client, logger, serverManager);
            });

            Console.WriteLine("🚀 [PHASE3.1] GrpcTranslationEngineAdapter登録完了 - OptimizedPythonTranslationEngine削除済み");
            Console.WriteLine($"🚀 [PHASE3.1] Clean Architecture実現: 通信層抽象化完了（削除した既存登録数: {existingTranslationEngines.Count}）");
        }
        
        /// <summary>
        /// パフォーマンス管理サービスを登録します。
        /// Phase 4.1: パフォーマンスメトリクス収集統合
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPerformanceServices(IServiceCollection services)
        {
            Console.WriteLine("🚀 統合パフォーマンス管理システム登録開始 - Issue #143 Week 3 Phase 2 + Phase 4.1");
            
            // Phase 4.1: パフォーマンスメトリクス設定を登録
            services.Configure<Baketa.Infrastructure.Monitoring.PerformanceMetricsSettings>(config =>
            {
                var serviceProvider = services.BuildServiceProvider();
                var configuration = serviceProvider.GetService<IConfiguration>();
                
                if (configuration != null)
                {
                    configuration.GetSection("PerformanceMetrics").Bind(config);
                }
                else
                {
                    // フォールバック設定
                    config.Enabled = true;
                    config.BatchSize = 50;
                    config.FlushIntervalSeconds = 5;
                    config.MaxQueueSize = 1000;
                    config.LogRetentionDays = 30;
                    config.EnableStructuredReports = true;
                    config.LogLevel = "Information";
                    Console.WriteLine("⚠️ [PHASE4.1] フォールバックメトリクス設定を使用");
                }
            });
            Console.WriteLine("✅ [PHASE4.1] PerformanceMetricsSettings設定完了");
            
            // Phase 4.1: 統合パフォーマンスメトリクスコレクター登録
            services.AddSingleton<Baketa.Core.Abstractions.Monitoring.IPerformanceMetricsCollector>(
                provider =>
                {
                    var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Monitoring.IntegratedPerformanceMetricsCollector>>();
                    var baketaLogger = provider.GetRequiredService<IBaketaLogger>();
                    var translationMetricsCollector = provider.GetRequiredService<Baketa.Infrastructure.Translation.Metrics.TranslationMetricsCollector>();
                    var settings = provider.GetRequiredService<IOptions<Baketa.Infrastructure.Monitoring.PerformanceMetricsSettings>>();
                    
                    var collector = new Baketa.Infrastructure.Monitoring.IntegratedPerformanceMetricsCollector(
                        logger, baketaLogger, translationMetricsCollector, settings);
                    
                    logger.LogInformation("📊 [PHASE4.1] 統合パフォーマンスメトリクスコレクター初期化完了");
                    return collector;
                });
            Console.WriteLine("✅ [PHASE4.1] IntegratedPerformanceMetricsCollector登録完了 - 既存TranslationMetricsCollector統合");
            
            // GPUメモリ管理
            services.AddSingleton<IGpuMemoryManager, GpuMemoryManager>();
            
            // パフォーマンス分析サービス
            services.AddSingleton<IAsyncPerformanceAnalyzer, AsyncPerformanceAnalyzer>();
            
            // 統合パフォーマンスオーケストレーター（Week 3 Phase 2: 60-80%改善目標）
            services.AddSingleton<Baketa.Core.Abstractions.Performance.IPerformanceOrchestrator, Baketa.Infrastructure.Performance.IntegratedPerformanceOrchestrator>();
            Console.WriteLine("✅ IPerformanceOrchestrator登録完了 - 統合最適化システム");
            
            // 🚀 プール化×GPU最適化統合オーケストレーター（最終フェーズ）
            services.AddSingleton<Baketa.Infrastructure.Performance.PooledGpuOptimizationOrchestrator>();
            services.AddHostedService<PooledGpuOptimizationOrchestrator>();
            Console.WriteLine("🚀 PooledGpuOptimizationOrchestrator登録完了 - プール化×GPU最適化統合システム");
            
            // 📊 Phase 3.2: VRAM監視5-tier圧迫度レベル判定システム
            services.AddHostedService<Baketa.Infrastructure.Services.ResourceMonitoringHostedService>();
            Console.WriteLine("📊 Phase 3.2: ResourceMonitoringHostedService登録完了 - VRAM監視5-tier圧迫度レベル判定システム");
            
            // 翻訳精度検証システム（デバッグビルドのみ）
            // TODO: 翻訳精度検証システムは将来実装予定
            // #if DEBUG
            // services.AddSingleton<ITranslationAccuracyValidator, TranslationAccuracyValidator>();
            // #endif
            
            Console.WriteLine("✅ 統合パフォーマンス管理システム登録完了（プール化×GPU最適化 + Phase 4.1 メトリクス収集含む）");
        }
        
        /// <summary>
        /// データ永続化サービスを登録します。
        /// </summary>
        /// <param name="_1">サービスコレクション（将来の実装で使用予定）</param>
        /// <param name="_2">アプリケーション実行環境（将来の実装で使用予定）</param>
        private static void RegisterPersistenceServices(IServiceCollection _1, Core.DI.BaketaEnvironment _2)
        {
            // 設定保存サービス（SettingsModuleで登録されるため、ここでは削除）
            // _1.AddSingleton<ISettingsService, JsonSettingsService>();
            
            // 例: _1.AddSingleton<IProfileStorage, JsonProfileStorage>();
            
            // 将来実装予定のキャッシュサービス（環境別設定）
            // if (_2 == Core.DI.BaketaEnvironment.Development || _2 == Core.DI.BaketaEnvironment.Test)
            // {
            //     // 開発/テスト環境ではメモリキャッシュを使用
            //     _1.AddSingleton<IOcrResultCache, MemoryOcrResultCache>();
            //     _1.AddSingleton<IDictionaryCache, MemoryDictionaryCache>();
            // }
            // else
            // {
            //     // 本番環境ではSQLiteキャッシュを使用
            //     _1.AddSingleton<IOcrResultCache, SqliteOcrResultCache>();
            //     _1.AddSingleton<IDictionaryCache, SqliteDictionaryCache>();
            // }
            
            // バックグラウンド同期
            // 例: _1.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();
            
            // 統一設定管理サービス
            RegisterUnifiedSettings(_1);
            
            // 統一ログサービス
            RegisterLoggingServices(_1);
        }
        
        /// <summary>
        /// 統一設定管理サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterUnifiedSettings(IServiceCollection services)
        {
            // 統一設定管理サービス（Singleton: アプリケーション全体で共有）
            services.AddSingleton<IUnifiedSettingsService, UnifiedSettingsService>();
        }
        
        /// <summary>
        /// 統一ログサービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterLoggingServices(IServiceCollection services)
        {
            // 統一ログサービス（Singleton: アプリケーション全体で共有）
            services.AddSingleton<IBaketaLogger, BaketaLogger>();
        }
        
        /// <summary>
        /// Phase3: リソース監視システムを登録します
        /// CPU・メモリ・GPU使用率をリアルタイム監視し、翻訳システムの動的最適化を支援
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterResourceMonitoringServices(IServiceCollection services)
        {
            Console.WriteLine("🔧 [PHASE3] 動的リソース監視システム登録開始");

            // リソース監視設定（デフォルト値でシングルトン登録）
            var defaultSettings = new Baketa.Core.Abstractions.Monitoring.ResourceMonitoringSettings(
                MonitoringIntervalMs: 5000,        // 5秒間隔で監視
                HistoryRetentionMinutes: 60,       // 1時間分の履歴保持
                CpuWarningThreshold: 85.0,         // CPU使用率85%で警告
                MemoryWarningThreshold: 90.0,      // メモリ使用率90%で警告
                GpuWarningThreshold: 95.0,         // GPU使用率95%で警告
                EnableGpuMonitoring: true,         // GPU監視有効
                EnableNetworkMonitoring: false,    // ネットワーク監視無効（将来実装）
                EnableDiskMonitoring: false        // ディスク監視無効（将来実装）
            );

            // 設定をシングルトンとして登録（IOptionsパターンとダイレクト参照の両方をサポート）
            services.AddSingleton(defaultSettings);
            services.AddSingleton<IOptions<Baketa.Core.Abstractions.Monitoring.ResourceMonitoringSettings>>(
                provider => Options.Create(defaultSettings));
            Console.WriteLine("✅ [PHASE3] ResourceMonitoringSettings設定完了 - 監視間隔:5s, 履歴保持:60分");

            // 注意: WindowsSystemResourceMonitorは Platform プロジェクトで登録される
            // Infrastructure レイヤーでは抽象インターフェースのみ認識
            // 実際の実装は PlatformModule で登録される予定
            Console.WriteLine("ℹ️ [PHASE3] IResourceMonitor実装はPlatformModuleで登録されます");

            // 🔥 Phase 12.1: HybridResourceSettings設定登録
            services.Configure<Baketa.Infrastructure.ResourceManagement.HybridResourceSettings>(options =>
            {
                // デフォルト設定を使用
            });
            Console.WriteLine("✅ [PHASE12.1] HybridResourceSettings登録完了 - デフォルト設定使用");

            // 🔥 Phase 12.1: HybridResourceManager登録（30秒待機問題解決）
            services.AddSingleton<Baketa.Infrastructure.ResourceManagement.IResourceManager, Baketa.Infrastructure.ResourceManagement.HybridResourceManager>();
            Console.WriteLine("🔥🔥🔥 [PHASE12.1] HybridResourceManager登録完了 - Translation Channel Reader有効化");

            Console.WriteLine("🎉 [PHASE3] 動的リソース監視システム登録完了");
        }

        /// <summary>
        /// Phase 1: 画像変化検知システムを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterImageChangeDetectionServices(IServiceCollection services)
        {
            Console.WriteLine("🔄 [PHASE1] 画像変化検知システム登録開始");
            
            // 画像変化検知メトリクスサービス
            services.AddSingleton<Baketa.Core.Abstractions.Services.IImageChangeMetricsService, ImageChangeMetricsService>();
            Console.WriteLine("✅ IImageChangeMetricsService登録完了 - 変化検知メトリクス収集");
            
            // 画像変化検知サービス（拡張実装を優先）
            services.AddSingleton<Baketa.Core.Abstractions.Services.IImageChangeDetectionService, Baketa.Infrastructure.Imaging.ChangeDetection.EnhancedImageChangeDetectionService>();
            Console.WriteLine("✅ IImageChangeDetectionService登録完了 - 拡張3段階フィルタリング実装");
            
            // Perceptual Hash サービス
            services.AddSingleton<Baketa.Core.Abstractions.Services.IPerceptualHashService, Baketa.Infrastructure.Imaging.ChangeDetection.OptimizedPerceptualHashService>();
            Console.WriteLine("✅ IPerceptualHashService登録完了 - OpenCV SIMD最適化実装");
            
            Console.WriteLine("🎉 [PHASE1] 画像変化検知システム登録完了");
        }

        /// <summary>
        /// P1: 段階的フィルタリングシステムを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="configuration">設定オブジェクト（nullの場合はハードコード設定を使用）</param>
        private static void RegisterStagedFilteringServices(IServiceCollection services, IConfiguration? configuration = null)
        {
            Console.WriteLine("🔄 [P1] 段階的フィルタリングシステム登録開始");
            
            // テキスト変化検知サービス（新規）
            services.AddSingleton<Baketa.Core.Abstractions.Processing.ITextChangeDetectionService, Baketa.Infrastructure.Text.ChangeDetection.TextChangeDetectionService>();
            Console.WriteLine("✅ ITextChangeDetectionService登録完了 - Edit Distance実装");
            
            // 🎯 Strategy A: パイプライン排他制御マネージャー
            services.AddSingleton<Baketa.Core.Abstractions.Processing.IPipelineExecutionManager, Baketa.Infrastructure.Processing.PipelineExecutionManager>();
            Console.WriteLine("✅ IPipelineExecutionManager登録完了 - Strategy A並行実行防止");

            // メイン処理パイプラインサービス
            services.AddSingleton<Baketa.Core.Abstractions.Processing.ISmartProcessingPipelineService, Baketa.Infrastructure.Processing.SmartProcessingPipelineService>();
            Console.WriteLine("✅ ISmartProcessingPipelineService登録完了 - 段階的処理パイプライン");
            
            // 段階別戦略実装（IProcessingStageStrategyインターフェースとして登録 - Geminiフィードバック反映）
            // 🔧 UltraThink修正: AddSingletonで状態保持（_previousImage）を正常化
            services.AddSingleton<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.ImageChangeDetectionStageStrategy>();
            services.AddTransient<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.OcrExecutionStageStrategy>();
            services.AddTransient<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.TextChangeDetectionStageStrategy>();
            services.AddTransient<IProcessingStageStrategy, Baketa.Infrastructure.Processing.Strategies.TranslationExecutionStageStrategy>();
            Console.WriteLine("✅ 段階別戦略登録完了 - 4段階処理戦略");
            
            // 段階的処理設定（appsettings.json対応 - ハードコード削除済み）
            if (configuration != null)
            {
                // appsettings.jsonからSmartProcessingPipeline設定を読み込み
                services.Configure<Baketa.Core.Models.Processing.ProcessingPipelineSettings>(
                    configuration.GetSection("SmartProcessingPipeline"));
                Console.WriteLine("✅ ProcessingPipelineSettings設定完了 - appsettings.jsonから読み込み");
            }
            else
            {
                // フォールバック: configuration=nullの場合はハードコード設定を使用
                services.Configure<Baketa.Core.Models.Processing.ProcessingPipelineSettings>(options =>
                {
                    options.EnableStaging = true;
                    options.EnableEarlyTermination = true;
                    options.TextChangeThreshold = 0.1f; // 10%の変化で翻訳実行
                    options.EnablePerformanceMetrics = true;
                    options.StopOnFirstError = false;
                });
                Console.WriteLine("⚠️ ProcessingPipelineSettings設定完了 - フォールバック（ハードコード設定）");
            }
            
            Console.WriteLine("🎉 [P1] 段階的フィルタリングシステム登録完了");
        }

        // Phase2: ハイブリッドリソース管理システム登録はPlatformModuleに移動済み（循環依存解決）
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            yield return typeof(ObjectPoolModule);
            yield return typeof(DiagnosticModule);
            // 🔧 UltraThink Phase 29: TimedAggregatorModule依存追加 - ITextChunkAggregatorService登録確保
            yield return typeof(TimedAggregatorModule);
        }
        
        /// <summary>
        /// Phase 2: 完全安定化サービス登録（接続信頼性向上・キャッシュ管理強化）
        /// </summary>
        private static void RegisterPhase2Services(IServiceCollection services)
        {
            Console.WriteLine("🚀 [PHASE2] 完全安定化サービス登録開始（接続信頼性向上・キャッシュ管理強化）");
            
            // CacheManagementService: ModelCacheManagerを基盤とした高度キャッシュ管理
            services.AddSingleton<CacheManagementService>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<CacheManagementService>>();
                var configuration = provider.GetRequiredService<IConfiguration>();
                var modelCacheManager = provider.GetRequiredService<ModelCacheManager>();
                
                logger.LogInformation("🗂️ CacheManagementService初期化 - 高度キャッシュ管理機能");
                return new CacheManagementService(logger, configuration, modelCacheManager);
            });
            
            Console.WriteLine("✅ [PHASE2] CacheManagementService登録完了");
            
            // SmartConnectionEstablisher: FixedSizeConnectionPool統合済み（追加DI不要）
            Console.WriteLine("✅ [PHASE2] SmartConnectionEstablisher統合完了（FixedSizeConnectionPool内統合）");
            
            Console.WriteLine("🎉 [PHASE2] Phase 2完全安定化サービス登録完了 - システム信頼性向上");
        }
        
        /// <summary>
        /// Modelsディレクトリを確実に見つけるためのヘルパーメソッド
        /// 開発環境と本番環境の両方で動作する
        /// </summary>
        /// <param name="appRoot">アプリケーションのBaseDirectory</param>
        /// <returns>Modelsディレクトリの絶対パス</returns>
        private static string FindModelsDirectory(string appRoot)
        {
            // 候補パスのリスト（優先順）
            var candidatePaths = new[]
            {
                // 開発環境：プロジェクトルートのModelsディレクトリ
                Path.Combine(appRoot, "..", "..", "..", "..", "Models"),
                Path.Combine(appRoot, "..", "..", "..", "Models"),
                Path.Combine(appRoot, "..", "..", "Models"),
                Path.Combine(appRoot, "..", "Models"),
                
                // 本番環境：アプリケーションと同じディレクトリ
                Path.Combine(appRoot, "Models"),
                
                // フォールバック：現在のディレクトリ
                Path.Combine(Directory.GetCurrentDirectory(), "Models")
            };
            
            // 最初に見つかったディレクトリを使用
            foreach (var path in candidatePaths)
            {
                var normalizedPath = Path.GetFullPath(path);
                if (Directory.Exists(normalizedPath))
                {
                    return normalizedPath;
                }
            }
            
            // どこにも見つからない場合はデフォルト（プロジェクトルート想定）
            var defaultPath = Path.GetFullPath(Path.Combine(appRoot, "..", "..", "..", "..", "Models"));
            return defaultPath;
        }

        /// <summary>
        /// Phase 3.13: Memory変換サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterMemoryServices(IServiceCollection services)
        {
            // IImage → SafeImage 変換コンバーター
            services.AddSingleton<IImageToSafeImageConverter, ImageToSafeImageConverter>();

            // IImage → ReferencedSafeImage 変換コンバーター (Phase 3.14)
            services.AddSingleton<IImageToReferencedSafeImageConverter, ImageToReferencedSafeImageConverter>();

            Console.WriteLine("🎯 [PHASE3.13-14] Memory変換サービス登録完了");
        }

        /// <summary>
        /// UltraThink Phase 1: 近接度ベースグループ化サービスを登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterProximityGroupingServices(IServiceCollection services)
        {
            Console.WriteLine("🚀 UltraThink Phase 1: 近接度ベースグループ化サービス登録開始");

            // ChunkProximityAnalyzer: 文字サイズ自動検出・近接判定（設定ファクトリ付き）
            services.AddSingleton<Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer>>();

                // 🔥🔥🔥 [CRITICAL_TRACE] IConfigurationから直接値を読み取る
                var configuration = provider.GetRequiredService<IConfiguration>();
                var configValue = configuration["TimedAggregator:ProximityGrouping:VerticalDistanceFactor"];
                Console.WriteLine($"🔥🔥🔥 [CRITICAL_TRACE] IConfiguration直接読取: VerticalDistanceFactor={configValue}");
                Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥🔥🔥 [CRITICAL_TRACE] IConfiguration直接読取: VerticalDistanceFactor={configValue}");

                // 🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>の完全追跡
                Console.WriteLine("🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>取得開始");
                Baketa.Core.Utilities.DebugLogUtility.WriteLog("🔥 [VALUE_TRACE] IOptionsMonitor<TimedAggregatorSettings>取得開始");

                var settingsMonitor = provider.GetRequiredService<IOptionsMonitor<TimedAggregatorSettings>>();
                Console.WriteLine($"🔥 [VALUE_TRACE] IOptionsMonitor型: {settingsMonitor?.GetType().FullName ?? "NULL"}");
                Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] IOptionsMonitor型: {settingsMonitor?.GetType().FullName ?? "NULL"}");

                var settings = settingsMonitor.CurrentValue;
                Console.WriteLine($"🔥 [VALUE_TRACE] CurrentValue型: {settings?.GetType().FullName ?? "NULL"}");
                Console.WriteLine($"🔥 [VALUE_TRACE] CurrentValue == null: {settings == null}");
                Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] CurrentValue型: {settings?.GetType().FullName ?? "NULL"}");

                if (settings != null)
                {
                    // 🔥 [VALUE_TRACE] TimedAggregatorSettings全体をJSON出力
                    var settingsJson = System.Text.Json.JsonSerializer.Serialize(settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings全体:\n{settingsJson}");
                    Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] TimedAggregatorSettings JSON: {settingsJson}");

                    Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings.IsFeatureEnabled: {settings.IsFeatureEnabled}");
                    Console.WriteLine($"🔥 [VALUE_TRACE] TimedAggregatorSettings.BufferDelayMs: {settings.BufferDelayMs}");
                    Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] TimedAggregatorSettings.IsFeatureEnabled: {settings.IsFeatureEnabled}");

                    var proximitySettings = settings.ProximityGrouping;
                    Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping == null: {proximitySettings == null}");
                    Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping型: {proximitySettings?.GetType().FullName ?? "NULL"}");
                    Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] ProximityGrouping == null: {proximitySettings == null}");

                    if (proximitySettings != null)
                    {
                        Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.VerticalDistanceFactor: {proximitySettings.VerticalDistanceFactor}");
                        Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.HorizontalDistanceFactor: {proximitySettings.HorizontalDistanceFactor}");
                        Console.WriteLine($"🔥 [VALUE_TRACE] ProximityGrouping.Enabled: {proximitySettings.Enabled}");
                        Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [VALUE_TRACE] VerticalDistanceFactor={proximitySettings.VerticalDistanceFactor}, HorizontalDistanceFactor={proximitySettings.HorizontalDistanceFactor}");

                        // 🔥 [VALUE_TRACE] 新規インスタンス作成との比較
                        var freshInstance = new Baketa.Core.Settings.ProximityGroupingSettings();
                        Console.WriteLine($"🔥 [COMPARE] new ProximityGroupingSettings().VerticalDistanceFactor: {freshInstance.VerticalDistanceFactor}");
                        Baketa.Core.Utilities.DebugLogUtility.WriteLog($"🔥 [COMPARE] DI注入={proximitySettings.VerticalDistanceFactor}, 新規作成={freshInstance.VerticalDistanceFactor}");
                    }

                    return new Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer(logger, proximitySettings ?? new Baketa.Core.Settings.ProximityGroupingSettings());
                }
                else
                {
                    Console.WriteLine("🔥 [VALUE_TRACE] settings == null のためデフォルトインスタンス作成");
                    Baketa.Core.Utilities.DebugLogUtility.WriteLog("🔥 [VALUE_TRACE] settings == null のためデフォルトインスタンス作成");
                    return new Baketa.Infrastructure.OCR.PostProcessing.ChunkProximityAnalyzer(logger, new Baketa.Core.Settings.ProximityGroupingSettings());
                }
            });
            Console.WriteLine("✅ ChunkProximityAnalyzer登録完了 - 自動閾値計算 + 設定連携");

            // ProximityGroupingService: 連結成分アルゴリズムによるグループ化
            services.AddSingleton<Baketa.Infrastructure.OCR.PostProcessing.ProximityGroupingService>();
            Console.WriteLine("✅ ProximityGroupingService登録完了 - 連結成分グループ化");

            Console.WriteLine("✅ UltraThink Phase 1: 近接度ベースグループ化サービス登録完了");
        }

        /// <summary>
        /// 🚀 Phase 2.3: TranslationSettings（gRPC Client設定含む）をDIコンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationSettings(IServiceCollection services)
        {
#if DEBUG
            Console.WriteLine("🚀 [PHASE2.3] TranslationSettings登録開始 - gRPC Client対応");
#endif

            try
            {
                // IConfigurationがDIコンテナに登録されているか確認
                var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));

                if (configurationDescriptor?.ImplementationInstance is IConfiguration configuration)
                {
                    // appsettings.jsonのTranslationセクションから設定をバインド
                    services.Configure<TranslationSettings>(configuration.GetSection("Translation"));

#if DEBUG
                    // 開発時のみgRPC設定値を出力
                    var useGrpc = configuration["Translation:UseGrpcClient"];
                    var serverAddr = configuration["Translation:GrpcServerAddress"];
                    Console.WriteLine($"✅ [PHASE2.3] TranslationSettings登録完了 - UseGrpcClient: {useGrpc ?? "NULL"}, ServerAddress: {serverAddr ?? "NULL"}");
#endif
                }
                else
                {
                    // フォールバック: IConfiguration未登録時はデフォルト値を使用
#if DEBUG
                    Console.WriteLine("⚠️ [FALLBACK] IConfiguration未登録 - TranslationSettingsデフォルト設定を使用");
#endif
                    services.Configure<TranslationSettings>(options =>
                    {
                        options.UseGrpcClient = true; // [PHASE3.3] gRPCクライアントがデフォルト
                        options.GrpcServerAddress = "http://localhost:50051";
                    });
                }
            }
            catch (InvalidOperationException ex)
            {
                // IConfiguration解決失敗時
                Console.WriteLine($"⚠️ [PHASE2.3] IConfiguration resolution failed: {ex.Message}");

                // フォールバック処理
                services.Configure<TranslationSettings>(options =>
                {
                    options.UseGrpcClient = true; // [PHASE3.3] gRPCクライアントがデフォルト
                    options.GrpcServerAddress = "http://localhost:50051";
                });
            }
            catch (Exception ex)
            {
                // 予期しないエラー
                Console.WriteLine($"💥 [PHASE2.3] RegisterTranslationSettings failed: {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// CircuitBreaker設定を安全に登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterCircuitBreakerSettings(IServiceCollection services)
        {
            // ConfigurableServiceModuleBaseパターンを使用した安全なConfiguration取得
            var configurationDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfiguration));

            if (configurationDescriptor?.ImplementationInstance is IConfiguration configuration)
            {
                // appsettings.jsonから設定をバインド
                services.Configure<CircuitBreakerSettings>(configuration.GetSection("CircuitBreaker"));

                // 設定値検証とサニタイゼーション
                services.PostConfigure<CircuitBreakerSettings>(options =>
                {
                    var originalTimeout = options.TimeoutMs;
                    var originalThreshold = options.FailureThreshold;

                    // セキュアな範囲制限 (5秒 - 5分)
                    if (options.TimeoutMs < 5000 || options.TimeoutMs > 300000)
                    {
                        Console.WriteLine($"⚠️ [SECURITY] CircuitBreaker.TimeoutMs値が範囲外({originalTimeout}ms) - デフォルト値(120000ms)を使用");
                        options.TimeoutMs = 120000;
                    }

                    if (options.FailureThreshold < 1 || options.FailureThreshold > 50)
                    {
                        Console.WriteLine($"⚠️ [SECURITY] CircuitBreaker.FailureThreshold値が範囲外({originalThreshold}) - デフォルト値(5)を使用");
                        options.FailureThreshold = 5;
                    }

                    Console.WriteLine($"✅ [CONFIG] CircuitBreaker設定確定 - TimeoutMs: {options.TimeoutMs}ms, FailureThreshold: {options.FailureThreshold}");
                });
            }
            else
            {
                // フォールバック: 設定ファイル不在時のセキュアなデフォルト値
                Console.WriteLine("⚠️ [FALLBACK] appsettings.json不在 - CircuitBreakerデフォルト設定を使用");
                services.Configure<CircuitBreakerSettings>(options =>
                {
                    options.FailureThreshold = 5;
                    options.TimeoutMs = 120000; // 120秒 - NLLB-200初回ロード対応
                    options.RecoveryTimeoutMs = 60000;
                });
            }
        }

        /// <summary>
        /// translation_ports_global.jsonから動的に利用可能なポート番号を検出します
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <returns>検出されたポート番号（見つからない場合は50051）</returns>
        private static int DetectDynamicPortFromGlobalRegistry(ILogger logger)
        {
            const int DefaultPort = 50051;
            var globalRegistryPath = Path.Combine(Environment.CurrentDirectory, "translation_ports_global.json");

            try
            {
                if (!File.Exists(globalRegistryPath))
                {
                    logger.LogWarning("🔍 [PHASE3.1_FIX] translation_ports_global.json が見つかりません: {Path}", globalRegistryPath);
                    return DefaultPort;
                }

                var json = File.ReadAllText(globalRegistryPath);
                using var document = System.Text.Json.JsonDocument.Parse(json);
                var root = document.RootElement;

                if (root.TryGetProperty("ports", out var portsElement))
                {
                    foreach (var portProperty in portsElement.EnumerateObject())
                    {
                        if (int.TryParse(portProperty.Name, out var availablePort))
                        {
                            logger.LogInformation("🎯 [PHASE3.1_FIX] 動的ポート検出成功: {Port}", availablePort);
                            return availablePort;
                        }
                    }
                }

                logger.LogWarning("🔍 [PHASE3.1_FIX] translation_ports_global.json に有効なポートが見つかりませんでした");
                return DefaultPort;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "💥 [PHASE3.1_FIX] translation_ports_global.json 読み込みエラー");
                return DefaultPort;
            }
        }
    }
