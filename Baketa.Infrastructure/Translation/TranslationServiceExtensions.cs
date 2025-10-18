using System;
using System.IO;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Factories;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Cloud;
using Baketa.Infrastructure.Translation.Local;
// OPUS-MT ONNX実装とSentencePiece削除済み
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

            // ITranslationEngineFactoryを登録（TranslationOrchestrationServiceで必要）
            services.AddSingleton<ITranslationEngineFactory, DefaultTranslationEngineFactory>();

            // ✨ 実際のAI翻訳エンジンを登録（優先順位順）

            // 1. NLLB-200エンジン（ローカル、最優先）
            // NOTE: GrpcTranslationEngineAdapterはInfrastructureModuleで具象クラス登録済み

            // 2. Geminiエンジン（クラウド、第2優先）
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine, GeminiTranslationEngine>();

#if DEBUG
            // 開発環境でのみMockエンジンを登録（デバッグ・テスト用）
            services.AddSingleton<MockTranslationEngine>();
#endif

            // 翻訳サービスを登録
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationService, DefaultTranslationService>();

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
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(sp => new MockTranslationEngine(
                sp.GetRequiredService<ILogger<MockTranslationEngine>>(),
                mockOptions.SimulatedDelayMs,
                mockOptions.SimulatedErrorRate));

            // 翻訳サービスを登録
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationService, DefaultTranslationService>();

            return services;
        }
        
        /// <summary>
        /// 言語ペアに応じたモデルファイル名を取得
        /// </summary>
        /// <param name="sourceLanguage">ソース言語</param>
        /// <param name="targetLanguage">ターゲット言語</param>
        /// <returns>モデルファイル名</returns>
        private static string GetModelFileNameForLanguagePair(string sourceLanguage, string targetLanguage)
        {
            // 正規化（zh-cn -> zh など）
            var normalizedSource = NormalizeLanguageCode(sourceLanguage);
            var normalizedTarget = NormalizeLanguageCode(targetLanguage);
            
            return $"opus-mt-{normalizedSource}-{normalizedTarget}.model";
        }
        
        /// <summary>
        /// 言語コードを正規化
        /// </summary>
        /// <param name="languageCode">言語コード</param>
        /// <returns>正規化された言語コード</returns>
        private static string NormalizeLanguageCode(string languageCode)
        {
            return languageCode.ToLowerInvariant() switch
            {
                "zh-cn" or "zh-hans" => "zh",
                "zh-tw" or "zh-hant" => "zh",
                "en" => "en",
                "ja" => "ja",
                _ => languageCode.ToLowerInvariant()
            };
        }
        
        /// <summary>
        /// 言語コードから表示名を取得
        /// </summary>
        /// <param name="languageCode">言語コード</param>
        /// <returns>言語の表示名</returns>
        private static string GetLanguageDisplayName(string languageCode)
        {
            return languageCode.ToLowerInvariant() switch
            {
                "ja" => "Japanese",
                "en" => "English",
                "zh" or "zh-cn" or "zh-hans" => "Chinese (Simplified)",
                "zh-tw" or "zh-hant" => "Chinese (Traditional)",
                "ko" => "Korean",
                "fr" => "French",
                "de" => "German",
                "es" => "Spanish",
                "pt" => "Portuguese",
                "ru" => "Russian",
                _ => languageCode.ToUpperInvariant()
            };
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
