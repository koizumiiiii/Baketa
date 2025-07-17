using System;
using System.IO;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Factories;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.SentencePiece;
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

            // αテスト向けOPUS-MTエンジンオプション（AlphaOpusMtEngineFactoryとAlphaOpusMtConfigurationはInfrastructureModuleで登録済み）
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
            services.AddSingleton<Baketa.Core.Abstractions.Translation.ITranslationEngine>(provider =>
            {
                var engineLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngine>>();
                var adapterLogger = provider.GetRequiredService<ILogger<AlphaOpusMtTranslationEngineAdapter>>();
                var simpleSentencePieceLogger = provider.GetRequiredService<ILogger<SimpleSentencePieceEngine>>();
                var mockLogger = provider.GetRequiredService<ILogger<MockTranslationEngine>>();
                
                try
                {
                    engineLogger.LogInformation("AlphaOpusMtTranslationEngineの初期化を開始します");
                    
                    var factory = provider.GetRequiredService<AlphaOpusMtEngineFactory>();
                    var options = provider.GetRequiredService<AlphaOpusMtOptions>();
                    
                    engineLogger.LogInformation("AlphaOpusMtEngineFactoryを取得しました");
                    
                    // 日英翻訳エンジンを作成（αテスト用のデフォルト）
                    var engine = factory.CreateJapaneseToEnglishEngine(options, engineLogger);
                    
                    engineLogger.LogInformation("AlphaOpusMtTranslationEngineの作成が完了しました");
                    
                    // アダプターでラップして旧インターフェースに適応
                    var adapter = new AlphaOpusMtTranslationEngineAdapter(engine, adapterLogger);
                    
                    engineLogger.LogInformation("AlphaOpusMtTranslationEngineAdapterの作成が完了しました");
                    
                    return adapter;
                }
                catch (Exception ex)
                {
                    // AlphaOpusMtTranslationEngineの初期化に失敗した場合、SimpleSentencePieceEngineを試行
                    engineLogger.LogWarning(ex, "AlphaOpusMtTranslationEngineの初期化に失敗しました。SimpleSentencePieceEngineを試行します。");
                    
                    try
                    {
                        var configuration = provider.GetRequiredService<AlphaOpusMtConfiguration>();
                        var settingsService = provider.GetRequiredService<Baketa.Core.Services.ISettingsService>();
                        
                        // 設定から言語ペアを取得
                        var sourceLanguage = settingsService.GetValue("Translation:Languages:DefaultSourceLanguage", "ja");
                        var targetLanguage = settingsService.GetValue("Translation:Languages:DefaultTargetLanguage", "en");
                        
                        // 言語ペアに応じたモデルファイルを選択
                        var modelFileName = GetModelFileNameForLanguagePair(sourceLanguage, targetLanguage);
                        var tokenizerPath = Path.Combine(configuration.ModelsDirectory, modelFileName);
                        
                        simpleSentencePieceLogger.LogInformation("設定された言語ペア: {SourceLanguage} → {TargetLanguage}", sourceLanguage, targetLanguage);
                        simpleSentencePieceLogger.LogInformation("使用するモデルファイル: {ModelFile}", modelFileName);
                        
                        if (File.Exists(tokenizerPath))
                        {
                            simpleSentencePieceLogger.LogInformation("SimpleSentencePieceEngineの初期化を開始します");
                            
                            var languagePair = new LanguagePair
                            {
                                SourceLanguage = new Language { Code = sourceLanguage, DisplayName = GetLanguageDisplayName(sourceLanguage) },
                                TargetLanguage = new Language { Code = targetLanguage, DisplayName = GetLanguageDisplayName(targetLanguage) }
                            };
                            
                            var simpleSentencePieceEngine = new SimpleSentencePieceEngine(tokenizerPath, languagePair, simpleSentencePieceLogger);
                            
                            simpleSentencePieceLogger.LogInformation("SimpleSentencePieceEngineの作成が完了しました");
                            
                            return simpleSentencePieceEngine;
                        }
                        else
                        {
                            simpleSentencePieceLogger.LogWarning("SentencePieceモデルファイルが見つかりません: {TokenizerPath}", tokenizerPath);
                        }
                    }
                    catch (Exception simpleSentencePieceEx)
                    {
                        simpleSentencePieceLogger.LogWarning(simpleSentencePieceEx, "SimpleSentencePieceEngineの初期化に失敗しました。MockTranslationEngineを使用します。");
                    }
                    
                    // 最後のフォールバックとしてMockTranslationEngineを使用
                    mockLogger.LogWarning("MockTranslationEngineを使用します（最終フォールバック）");
                    return new MockTranslationEngine(mockLogger);
                }
            });

            // フォールバック用のMockエンジンも登録（開発・テスト用）
            services.AddSingleton<MockTranslationEngine>();

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
