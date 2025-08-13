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
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Logging;
using Baketa.Infrastructure.OCR.Measurement;
using Baketa.Infrastructure.Performance;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Services.Settings;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
            
            // HuggingFace Transformers OPUS-MT翻訳サービス（高品質版）を先に登録
            RegisterTransformersOpusMTServices(services);
            
            // 翻訳サービス（エンジン登録後）
            RegisterTranslationServices(services);
            
            // ウォームアップサービス（Issue #143: コールドスタート遅延根絶）
            RegisterWarmupServices(services);
            
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
        /// HuggingFace Transformers OPUS-MT翻訳サービスを登録します。
        /// 語彙サイズ不整合問題を完全解決した高品質版です。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTransformersOpusMTServices(IServiceCollection services)
        {
            // 既存のITranslationEngine登録を全て削除して、TransformersOpusMtEngineのみを登録
            var existingTranslationEngines = services
                .Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Translation.ITranslationEngine))
                .ToList();
            
            foreach (var service in existingTranslationEngines)
            {
                services.Remove(service);
            }
            
            // ⚡ Phase 2 DI修正: UI応答性向上のためTransformersOpusMtEngineを遅延初期化
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                // バックグラウンドで初期化して、UIをブロックしない
                var logger = provider.GetService<ILogger<TransformersOpusMtEngine>>();
                logger?.LogInformation("🚀 TransformersOpusMtEngine遅延初期化開始 - UIブロック回避");
                var settingsService = provider.GetRequiredService<IUnifiedSettingsService>();
                return new TransformersOpusMtEngine(logger, settingsService);
            });
            
            // 🔧 ファサード実装バッチ処理ハング問題の修正: 具象型でも登録してServiceProviderからの直接取得を可能にする
            services.AddSingleton<TransformersOpusMtEngine>(provider => 
                provider.GetRequiredService<Baketa.Core.Abstractions.Translation.ITranslationEngine>() as TransformersOpusMtEngine 
                ?? throw new InvalidOperationException("TransformersOpusMtEngine の取得に失敗しました"));
            
            Console.WriteLine($"🔧 TransformersOpusMtEngine（組み込みLRUキャッシュ付き）を登録しました（削除した既存登録数: {existingTranslationEngines.Count}）");
            Console.WriteLine("⚡ Phase 1.1: LRU翻訳キャッシュ（1000エントリ）が組み込まれています");
        }
        
        /// <summary>
        /// パフォーマンス管理サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPerformanceServices(IServiceCollection services)
        {
            // GPUメモリ管理
            services.AddSingleton<IGpuMemoryManager, GpuMemoryManager>();
            
            // パフォーマンス分析サービス
            services.AddSingleton<IAsyncPerformanceAnalyzer, AsyncPerformanceAnalyzer>();
            
            // 翻訳精度検証システム（デバッグビルドのみ）
            // TODO: 翻訳精度検証システムは将来実装予定
            // #if DEBUG
            // services.AddSingleton<ITranslationAccuracyValidator, TranslationAccuracyValidator>();
            // #endif
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
