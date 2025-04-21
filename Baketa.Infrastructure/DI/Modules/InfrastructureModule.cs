using Baketa.Core.Abstractions.DI;
using Baketa.Core.DI;
using Baketa.Core.DI.Attributes;
using Baketa.Core.DI.Modules;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;

namespace Baketa.Infrastructure.DI.Modules
{
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
        /// <param name="_">サービスコレクション</param>
        private static void RegisterTranslationServices(IServiceCollection _)
        {
            // 翻訳エンジンやサービスの登録
            // 例: services.AddSingleton<ITranslationEngine, OnnxTranslationEngine>();
            // 例: services.AddSingleton<ITranslationCache, MemoryTranslationCache>();
            
            // バックアップ翻訳サービス（APIベース）
            // 例: services.AddSingleton<ICloudTranslationProvider, GoogleTranslationProvider>();
            
            // 言語検出
            // 例: services.AddSingleton<ILanguageDetector, FastTextLanguageDetector>();
            
            // 翻訳ファクトリーは実装クラスが存在しないためコメントアウト
            // 例: services.AddSingleton<Func<string, ITranslationEngine>>(sp => engineType =>
            // {
            //     return engineType switch
            //     {
            //         "onnx" => sp.GetRequiredService<OnnxTranslationEngine>(),
            //         "cloud" => new CloudTranslationEngine(
            //             sp.GetRequiredService<ICloudTranslationProvider>(),
            //             sp.GetRequiredService<ITranslationCache>()
            //         ),
            //         _ => throw new ArgumentException($"不明な翻訳エンジンタイプ: {engineType}")
            //     };
            // });
        }
        
        /// <summary>
        /// データ永続化サービスを登録します。
        /// </summary>
        /// <param name="_">サービスコレクション</param>
        /// <param name="environment">アプリケーション実行環境</param>
        private static void RegisterPersistenceServices(IServiceCollection _, Core.DI.BaketaEnvironment environment)
        {
            // 設定保存サービス
            // 例: services.AddSingleton<ISettingsStorage, JsonSettingsStorage>();
            // 例: services.AddSingleton<IProfileStorage, JsonProfileStorage>();
            
            // 環境に応じたキャッシュ実装の選択
            if (environment == Core.DI.BaketaEnvironment.Development || environment == Core.DI.BaketaEnvironment.Test)
            {
                // 開発/テスト環境ではメモリキャッシュを使用
                // 例: services.AddSingleton<IOcrResultCache, MemoryOcrResultCache>();
                // 例: services.AddSingleton<IDictionaryCache, MemoryDictionaryCache>();
            }
            else
            {
                // 本番環境ではSQLiteキャッシュを使用
                // 例: services.AddSingleton<IOcrResultCache, SqliteOcrResultCache>();
                // 例: services.AddSingleton<IDictionaryCache, SqliteDictionaryCache>();
            }
            
            // バックグラウンド同期
            // 例: services.AddSingleton<IBackgroundSyncService, BackgroundSyncService>();
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
}