using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using CoreTranslation = Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Logging;
using Baketa.Infrastructure.OCR.Measurement;
using Baketa.Infrastructure.Performance;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Baketa.Infrastructure.Translation.Services;
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
                
            // OCR関連サービス
            RegisterOcrServices(services);
            
            // スティッキーROIシステム（Issue #143 Week 3: 処理効率向上）
            RegisterStickyRoiServices(services);
            
            // HuggingFace Transformers OPUS-MT翻訳サービス（高品質版）を先に登録
            RegisterTransformersOpusMTServices(services);
            
            // 翻訳サービス（エンジン登録後）
            RegisterTranslationServices(services);
            
            // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
            RegisterWarmupServices(services);
            
            // GPU統合サービス（Issue #143 Week 2: DI統合とMulti-GPU対応）
            RegisterGpuServices(services);
            
            // パフォーマンス管理サービス
            RegisterPerformanceServices(services);
            
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
        /// 翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationServices(IServiceCollection services)
        {
            // 翻訳エンジンファクトリーを登録
            services.AddSingleton<Baketa.Core.Abstractions.Factories.ITranslationEngineFactory, Baketa.Core.Translation.Factories.DefaultTranslationEngineFactory>();
            
            // パフォーマンス監視サービスを登録 (Issue #144)
            services.AddSingleton<Baketa.Infrastructure.Translation.Services.ITranslationPerformanceMonitor, Baketa.Infrastructure.Translation.Services.TranslationPerformanceMonitor>();
            
            // 翻訳サービスを登録
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationService, DefaultTranslationService>();
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
            
            // 🔥 Issue #147: FixedSizeConnectionPoolを登録（Python翻訳エンジン接続プール）
            services.AddSingleton<Baketa.Infrastructure.Translation.Local.ConnectionPool.FixedSizeConnectionPool>();
            Console.WriteLine("🔥 FixedSizeConnectionPool登録完了 - Issue #147 接続プール統合");
            
            // 🚀 Issue #147: OptimizedPythonTranslationEngineを優先エンジンとして登録（接続プール統合版）
            services.AddSingleton<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>(provider =>
            {
                var logger = provider.GetRequiredService<ILogger<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>>();
                var connectionPool = provider.GetRequiredService<Baketa.Infrastructure.Translation.Local.ConnectionPool.FixedSizeConnectionPool>();
                var translationSettings = provider.GetRequiredService<IOptions<TranslationSettings>>();
                logger?.LogInformation("🚀 OptimizedPythonTranslationEngine初期化開始 - 接続プール統合版");
                return new Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine(logger, connectionPool, translationSettings);
            });
            
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                var logger = provider.GetService<ILogger<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>>();
                logger?.LogInformation("🔥 OptimizedPythonTranslationEngine（接続プール統合版）をITranslationEngineとして登録");
                var optimizedEngine = provider.GetRequiredService<Baketa.Infrastructure.Translation.Local.OptimizedPythonTranslationEngine>();
                // OptimizedPythonTranslationEngineは両方のITranslationEngineインターフェースを実装
                return (Baketa.Core.Abstractions.Translation.ITranslationEngine)optimizedEngine;
            });
            
            // フォールバック用にTransformersOpusMtEngineも登録（具象型）
            services.AddSingleton<TransformersOpusMtEngine>(provider =>
            {
                var logger = provider.GetService<ILogger<TransformersOpusMtEngine>>();
                logger?.LogInformation("🔧 TransformersOpusMtEngineフォールバック初期化");
                var settingsService = provider.GetRequiredService<IUnifiedSettingsService>();
                return new TransformersOpusMtEngine(logger!, settingsService);
            });
            
            Console.WriteLine($"🔥 Issue #147: OptimizedPythonTranslationEngine（接続プール統合版）を優先エンジンとして登録しました（削除した既存登録数: {existingTranslationEngines.Count}）");
            Console.WriteLine("⚡ Issue #147: 接続プール統合による接続ロック競合問題解決完了");
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
            
            // 翻訳精度検証システム（デバッグビルドのみ）
            // TODO: 翻訳精度検証システムは将来実装予定
            // #if DEBUG
            // services.AddSingleton<ITranslationAccuracyValidator, TranslationAccuracyValidator>();
            // #endif
            
            Console.WriteLine("✅ 統合パフォーマンス管理システム登録完了");
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
        /// このモジュールが依存する他のモジュールの型を取得します。
        /// </summary>
        /// <returns>依存モジュールの型のコレクション</returns>
        public override IEnumerable<Type> GetDependentModules()
        {
            yield return typeof(CoreModule);
            yield return typeof(ObjectPoolModule);
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
