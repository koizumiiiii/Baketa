using System;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CoreTranslationAbstractions = Baketa.Core.Translation.Abstractions;

namespace Baketa.Infrastructure.Translation;

    /// <summary>
    /// 翻訳サービスの依存性注入拡張メソッド
    /// </summary>
    public static class TranslationServiceExtensions
    {
        /// <summary>
        /// 翻訳サービスをDIコンテナに登録します
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddTranslationServices(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // αテスト向けOPUS-MT翻訳エンジンをファクトリーパターンで登録
            services.AddSingleton<AlphaOpusMtEngineFactory>();
            
            // αテスト向けOPUS-MT設定
            services.AddSingleton<AlphaOpusMtConfiguration>(provider =>
            {
                return new AlphaOpusMtConfiguration
                {
                    IsEnabled = true,
                    ModelsDirectory = "Models/OpusMT",
                    MaxSequenceLength = 256,
                    MemoryLimitMb = 300,
                    ThreadCount = 2
                };
            });

            // αテスト向けOPUS-MTエンジンオプション
            services.AddSingleton<AlphaOpusMtOptions>(provider =>
            {
                return new AlphaOpusMtOptions
                {
                    MaxSequenceLength = 256,
                    MemoryLimitMb = 300,
                    ThreadCount = 2
                };
            });

            // デフォルトの翻訳エンジンとしてAlphaOpusMtTranslationEngineを登録（フォールバック付き）
            services.AddSingleton<ITranslationEngine>(provider =>
            {
                var engineLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngine>>();
                var adapterLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngineAdapter>>();
                var mockLogger = provider.GetRequiredService<ILogger<MockTranslationEngine>>();
                
                try
                {
                    var factory = provider.GetRequiredService<AlphaOpusMtEngineFactory>();
                    var options = provider.GetRequiredService<AlphaOpusMtOptions>();
                    
                    // 日英翻訳エンジンを作成（αテスト用のデフォルト）
                    var engine = factory.CreateJapaneseToEnglishEngine(options, engineLogger);
                    
                    // アダプターでラップして旧インターフェースに適応
                    return new AlphaOpusMtTranslationEngineAdapter(engine, adapterLogger);
                }
                catch (Exception ex)
                {
                    // AlphaOpusMtTranslationEngineの初期化に失敗した場合、MockTranslationEngineを使用
                    engineLogger.LogWarning(ex, "AlphaOpusMtTranslationEngineの初期化に失敗しました。フォールバックのMockTranslationEngineを使用します。");
                    return new MockTranslationEngine(mockLogger);
                }
            });

            // フォールバック用のMockエンジンも登録（開発・テスト用）
            services.AddSingleton<MockTranslationEngine>();

            // 翻訳サービスを登録
            services.AddSingleton<ITranslationService, DefaultTranslationService>();

            return services;
        }

        /// <summary>
        /// 翻訳サービスをDIコンテナに登録します（カスタム設定）
        /// </summary>
        /// <param name="services">サービスコレクション</param>
        /// <param name="options">モックエンジンのオプション</param>
        /// <returns>サービスコレクション</returns>
        public static IServiceCollection AddTranslationServices(
            this IServiceCollection services,
            Action<MockTranslationOptions> options)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(options);

            var mockOptions = new MockTranslationOptions();
            options(mockOptions);

            // モックエンジンを登録（カスタム設定）
            services.AddSingleton<ITranslationEngine>(sp => new MockTranslationEngine(
                sp.GetRequiredService<ILogger<MockTranslationEngine>>(),
                mockOptions.SimulatedDelayMs,
                mockOptions.SimulatedErrorRate));

            // 翻訳サービスを登録
            services.AddSingleton<ITranslationService, DefaultTranslationService>();

            return services;
        }
    }

    /// <summary>
    /// モック翻訳エンジンのオプション
    /// </summary>
    public class MockTranslationOptions
    {
        /// <summary>
        /// シミュレートする処理遅延（ミリ秒）
        /// </summary>
        public int SimulatedDelayMs { get; set; }

        /// <summary>
        /// シミュレートするエラー率（0.0～1.0）
        /// </summary>
        public float SimulatedErrorRate { get; set; }
    }
