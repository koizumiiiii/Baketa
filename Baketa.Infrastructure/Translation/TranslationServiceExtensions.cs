using System;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

            // モックエンジンを登録（開発・テスト用）
            services.AddSingleton<ITranslationEngine, MockTranslationEngine>();

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
