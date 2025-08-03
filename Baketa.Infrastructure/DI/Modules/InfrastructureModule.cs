using System;
using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Complete;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.Measurement;
using Baketa.Core.Abstractions.Performance;
using Baketa.Infrastructure.Performance;
using System;
using System.Collections.Generic;
using System.IO;

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
            
            // 翻訳サービス
            RegisterTranslationServices(services);
            
            // αテスト向けOPUS-MT翻訳サービス
            RegisterAlphaOpusMTServices(services);
            
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
        /// αテスト向けOPUS-MT翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterAlphaOpusMTServices(IServiceCollection services)
        {
            // αテスト向けOPUS-MT設定
            services.AddSingleton<AlphaOpusMtConfiguration>(provider =>
            {
                // 実際のアプリケーションでは設定サービスから取得
                // より安全な絶対パス解決：アプリケーションルートディレクトリを特定
                var appRoot = AppDomain.CurrentDomain.BaseDirectory;
                
                // 開発環境と本番環境で適切にModelsディレクトリを見つける
                var modelsDirectory = FindModelsDirectory(appRoot);
                var sentencePieceModelsPath = Path.Combine(modelsDirectory, "SentencePiece");
                
                return new AlphaOpusMtConfiguration
                {
                    IsEnabled = true,
                    ModelsDirectory = sentencePieceModelsPath,
                    MaxSequenceLength = 256,
                    MemoryLimitMb = 300,
                    ThreadCount = 2
                };
            });
            
            // αテスト向けOPUS-MT翻訳エンジンファクトリー
            services.AddSingleton<AlphaOpusMtEngineFactory>();
            
            // αテスト向けOPUS-MT翻訳サービス
            services.AddSingleton<AlphaOpusMtTranslationService>();
            
            // AlphaOpusMT翻訳エンジンをITranslationEngineとして登録（DefaultTranslationServiceで使用される）
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                var factory = provider.GetRequiredService<AlphaOpusMtEngineFactory>();
                var options = new AlphaOpusMtOptions
                {
                    MaxSequenceLength = 256,
                    MemoryLimitMb = 300,
                    ThreadCount = 2
                };
                var engineLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngine>>();
                var adapterLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngineAdapter>>();
                
                // 日英翻訳エンジンを作成（デフォルト）
                var alphaEngine = factory.CreateJapaneseToEnglishEngine(options, engineLogger);
                
                // アダプターでラップして旧インターフェースに適応
                var adapter = new AlphaOpusMtTranslationEngineAdapter(alphaEngine, adapterLogger);
                
                return adapter;
            });
        }
        
        /// <summary>
        /// パフォーマンス管理サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterPerformanceServices(IServiceCollection services)
        {
            // GPUメモリ管理
            services.AddSingleton<IGpuMemoryManager, GpuMemoryManager>();
            
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
