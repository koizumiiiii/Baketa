using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Baketa.Core.Services;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Models;
using System;
using System.Collections.Generic;

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
            
            // αテスト向けOPUS-MT翻訳サービス（TranslationServiceExtensionsで登録されるためコメントアウト）
            // RegisterAlphaOpusMTServices(services);
            
            // データ永続化
            RegisterPersistenceServices(services, environment);
        }

        /// <summary>
        /// OCR関連サービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        private static void RegisterOcrServices(IServiceCollection _)
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
        }
        
        /// <summary>
        /// 翻訳サービスを登録します。
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        private static void RegisterTranslationServices(IServiceCollection services)
        {
            // αテスト向けのTranslationServiceExtensionsを使用
            services.AddTranslationServices();
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
                return new AlphaOpusMtConfiguration
                {
                    IsEnabled = true,
                    ModelsDirectory = "Models/SentencePiece",
                    MaxSequenceLength = 256,
                    MemoryLimitMb = 300,
                    ThreadCount = 2
                };
            });
            
            // αテスト向けOPUS-MT翻訳エンジンファクトリー
            services.AddSingleton<AlphaOpusMtEngineFactory>();
            
            // αテスト向けOPUS-MT翻訳サービス
            services.AddSingleton<AlphaOpusMtTranslationService>();
            
            // TODO: AlphaOpusMT翻訳エンジンをITranslationEngineとして登録
            // 現在は別の箇所でMockTranslationEngineが登録されている
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
        }
    }
