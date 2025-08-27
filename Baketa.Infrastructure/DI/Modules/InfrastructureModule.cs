using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Settings;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using CoreTranslation = Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Diagnostics;
using Baketa.Infrastructure.OCR.Measurement;
using Baketa.Infrastructure.Performance;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local;
// OPUS-MT ONNX実装削除済み
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Baketa.Infrastructure.Translation.Services;
using Baketa.Infrastructure.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
            
            // 環境確認は、BuildServiceProviderが存在しないか必要なパッケージがないため
            // コメントアウトし、デフォルト値を使用
            //var environment = services.BuildServiceProvider().GetService<Core.DI.BaketaEnvironment>() 
            //    ?? Core.DI.BaketaEnvironment.Production;
            var environment = Core.DI.BaketaEnvironment.Production;
                
            // 設定ファイル統一: OCR設定をappsettings.jsonから読み込み
            // 注意: これはServiceModuleの外で設定される想定（Startup.cs等）
            
            // OCR関連サービス
            RegisterOcrServices(services);
            
            // スティッキーROIシステム（Issue #143 Week 3: 処理効率向上）
            RegisterStickyRoiServices(services);
            
            // HuggingFace Transformers OPUS-MT翻訳サービス（高品質版）を先に登録
            RegisterTransformersOpusMTServices(services);
            
            // 翻訳サービス（エンジン登録後）
            RegisterTranslationServices(services);
            
            // Phase 5: ポート競合防止機構サービス
            RegisterPortManagementServices(services);
            
            // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
            RegisterWarmupServices(services);
            
            // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
            RegisterGpuServices(services);
            
            // パフォーマンス管理サービス
            RegisterPerformanceServices(services);
            
            // Phase3: リソース監視システム
            RegisterResourceMonitoringServices(services);
            
            // データ永続化
            RegisterPersistenceServices(services, environment);
        }

        /// <summary>
        /// OCR関連サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterOcrServices(IServiceCollection services)
        {
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
        /// 翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationServices(IServiceCollection services)
        {
            // Phase2: サーキットブレーカー設定とサービス登録
            Console.WriteLine("🔧 [PHASE2] サーキットブレーカー登録開始");
            
            // サーキットブレーカー設定
            services.Configure<CircuitBreakerSettings>(options =>
            {
                options.FailureThreshold = 5;      // 5回失敗でサーキットオープン
                options.TimeoutMs = 30000;         // 30秒タイムアウト
                options.RecoveryTimeoutMs = 60000; // 60秒後に復旧テスト
            });
            
            // 翻訳専用サーキットブレーカー登録
            services.AddSingleton<ICircuitBreaker<Baketa.Core.Translation.Models.TranslationResponse>, TranslationCircuitBreaker>();
            Console.WriteLine("✅ [PHASE2] TranslationCircuitBreaker登録完了 - FailureThreshold: 5, RecoveryTimeout: 60s");
            
            // 翻訳エンジンファクトリーを登録
            services.AddSingleton<Baketa.Core.Abstractions.Factories.ITranslationEngineFactory, Baketa.Core.Translation.Factories.DefaultTranslationEngineFactory>();
            
            // パフォーマンス監視サービスを登録 (Issue #144)
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.ITranslationPerformanceMonitor, Baketa.Infrastructure.Translation.Services.TranslationPerformanceMonitor>();
            
            // 🚨 翻訳サーバー安定化: Python サーバーヘルスモニター（バックグラウンドサービス）
            Console.WriteLine("🔍 [DI_DEBUG] PythonServerHealthMonitor登録開始");
            
            // シングルトンとしても登録（直接取得のため）
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>();
            
            // HostedServiceとしても登録
            services.AddHostedService<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>(provider => 
                provider.GetRequiredService<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>());
            
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
                var settings = provider.GetRequiredService<Baketa.Infrastructure.Translation.Strategies.HybridStrategySettings>();
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy>>();
                
                var hybridStrategy = new Baketa.Infrastructure.Translation.Strategies.HybridTranslationStrategy(
                    strategies, metricsCollector, settings, logger);
                    
                Console.WriteLine("🎯 HybridTranslationStrategy登録完了 - 戦略統合オーケストレーター");
                return hybridStrategy;
            });
            
            // 翻訳サービスを登録
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationService, DefaultTranslationService>();
            
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
            
            // GPU OCRエンジン（Week 3 Phase 2: 統合最適化対応）
            services.AddSingleton<Baketa.Core.Abstractions.GPU.IGpuOcrEngine, Baketa.Infrastructure.OCR.GPU.MockGpuOcrEngine>();
            Console.WriteLine("✅ IGpuOcrEngine登録完了 - Mock実装");
            
            Console.WriteLine("✅ GPU統合サービス登録完了");
        }
        
        /// <summary>
        /// スティッキーROIシステムを登録します（Issue #143 Week 3: 処理効率向上）。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterStickyRoiServices(IServiceCollection services)
        {
            Console.WriteLine("🎯 スティッキーROIシステム登録開始 - Issue #143 Week 3");
            
            // スティッキーROI管理サービス
            services.AddSingleton<Baketa.Core.Abstractions.OCR.IStickyRoiManager, Baketa.Infrastructure.OCR.StickyRoi.InMemoryStickyRoiManager>();
            Console.WriteLine("✅ IStickyRoiManager登録完了 - InMemory実装");
            
            // ROI拡張OCRエンジン（デコレーターパターンで既存エンジンを拡張）
            // 注意: 実際のプロダクションでは適切なOCRエンジンファクトリーと統合が必要
            services.AddSingleton<Baketa.Infrastructure.OCR.StickyRoi.StickyRoiEnhancedOcrEngine>();
            Console.WriteLine("✅ StickyRoiEnhancedOcrEngine登録完了 - デコレーター実装");
            
            Console.WriteLine("✅ スティッキーROIシステム登録完了");
        }
        
        /// <summary>
        /// HuggingFace Transformers OPUS-MT翻訳サービスを登録します。
        /// 語彙サイズ不整合問題を完全解決した高品質版です。
        /// Issue #147: 接続プール統合による接続ロック競合問題解決
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTransformersOpusMTServices(IServiceCollection services)
        {
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
            
            // ✅ FixedSizeConnectionPool登録（動的ポート対応版）
            services.AddSingleton<IConnectionPool, Baketa.Infrastructure.Translation.Local.ConnectionPool.FixedSizeConnectionPool>();
            Console.WriteLine("✅ FixedSizeConnectionPool登録完了 - 動的ポート対応（NLLB-200/OPUS-MT自動切り替え）");
            
            // ✅ 接続プール統合版OptimizedPythonTranslationEngine（動的ポート対応 + Phase 2動的リソース管理）
            services.AddSingleton<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>>();
                var connectionPool = provider.GetRequiredService<IConnectionPool>();
                var configuration = provider.GetRequiredService<IConfiguration>();
                logger?.LogInformation("🔄 OptimizedPythonTranslationEngine初期化開始 - 接続プール統合版（動的ポート対応）");
                return new Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine(logger, connectionPool, configuration, null, null);
            });
            
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                var logger = provider.GetService<ILogger<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>>();
                logger?.LogInformation("🔥 OptimizedPythonTranslationEngine（接続プール統合版）をITranslationEngineとして登録");
                var optimizedEngine = provider.GetRequiredService<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>();
                // OptimizedPythonTranslationEngineは両方のITranslationEngineインターフェースを実装
                return (Baketa.Core.Abstractions.Translation.ITranslationEngine)optimizedEngine;
            });
            
            Console.WriteLine("🚀 OptimizedPythonTranslationEngine登録完了 - Pythonサーバー接続問題解決済み");
            
            Console.WriteLine($"🚀 Issue #147 Phase 3.2: OptimizedPythonTranslationEngineを使用してハイブリッド戦略を実運用（削除した既存登録数: {existingTranslationEngines.Count}）");
        }
        
        /// <summary>
        /// パフォーマンス管理サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPerformanceServices(IServiceCollection services)
        {
            Console.WriteLine("🚀 統合パフォーマンス管理システム登録開始 - Issue #143 Week 3 Phase 2");
            
            // GPUメモリ管理
            services.AddSingleton<IGpuMemoryManager, GpuMemoryManager>();
            
            // パフォーマンス分析サービス
            services.AddSingleton<IAsyncPerformanceAnalyzer, AsyncPerformanceAnalyzer>();
            
            // 統合パフォーマンスオーケストレーター（Week 3 Phase 2: 60-80%改善目標）
            services.AddSingleton<Baketa.Core.Abstractions.Performance.IPerformanceOrchestrator, Baketa.Infrastructure.Performance.IntegratedPerformanceOrchestrator>();
            Console.WriteLine("✅ IPerformanceOrchestrator登録完了 - 統合最適化システム");
            
            // 🚀 プール化×GPU最適化統合オーケストレーター（最終フェーズ）
            services.AddSingleton<Baketa.Infrastructure.Performance.PooledGpuOptimizationOrchestrator>();
            services.AddHostedService<Baketa.Infrastructure.Performance.PooledGpuOptimizationOrchestrator>(provider =>
                provider.GetRequiredService<Baketa.Infrastructure.Performance.PooledGpuOptimizationOrchestrator>());
            Console.WriteLine("🚀 PooledGpuOptimizationOrchestrator登録完了 - プール化×GPU最適化統合システム");
            
            // 翻訳精度検証システム（デバッグビルドのみ）
            // TODO: 翻訳精度検証システムは将来実装予定
            // #if DEBUG
            // services.AddSingleton<ITranslationAccuracyValidator, TranslationAccuracyValidator>();
            // #endif
            
            Console.WriteLine("✅ 統合パフォーマンス管理システム登録完了（プール化×GPU最適化含む）");
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
            
            Console.WriteLine("🎉 [PHASE3] 動的リソース監視システム登録完了");
        }
        
        /// <summary>
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            yield return typeof(ObjectPoolModule);
            yield return typeof(DiagnosticModule);
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
    }
