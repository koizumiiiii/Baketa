using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Text;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation
{
    /// <summary>
    /// テスト用のモック翻訳エンジン
    /// </summary>
    public class MockTranslationEngine : TranslationEngineBase
    {
        private readonly Dictionary<string, string> _presetTranslations = new Dictionary<string, string>();
        private readonly RandomNumberGenerator _rng = RandomNumberGenerator.Create();
        private readonly int _simulatedDelayMs;
        private readonly float _simulatedErrorRate;

        /// <summary>
        /// エンジン名
        /// </summary>
        public override string Name => "MockTranslationEngine";

        /// <summary>
        /// エンジンの説明
        /// </summary>
        public override string Description => "テスト用のモック翻訳エンジン";

        /// <summary>
        /// エンジンがオンライン接続を必要とするかどうか
        /// </summary>
        public override bool RequiresNetwork => false;

        /// <summary>
        /// サポートしている言語ペア
        /// </summary>
        private readonly HashSet<LanguagePair> _supportedLanguagePairs;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="simulatedDelayMs">シミュレートする処理遅延（ミリ秒）</param>
        /// <param name="simulatedErrorRate">シミュレートするエラー率（0.0～1.0）</param>
        public MockTranslationEngine(
            ILogger<MockTranslationEngine> logger,
            int simulatedDelayMs = 0,
            float simulatedErrorRate = 0.0f)
            : base(logger)
        {
            _simulatedDelayMs = simulatedDelayMs;
            _simulatedErrorRate = Math.Clamp(simulatedErrorRate, 0.0f, 1.0f);

            // サポートする言語ペアを定義
            _supportedLanguagePairs = new HashSet<LanguagePair>
            {
                // 英語 → 日本語
                new LanguagePair { SourceLanguage = new Language { Code = "en", DisplayName = "English" }, TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" } },
                // 日本語 → 英語
                new LanguagePair { SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" }, TargetLanguage = new Language { Code = "en", DisplayName = "English" } },
                // 英語 → 中国語（簡体字）
                new LanguagePair { SourceLanguage = new Language { Code = "en", DisplayName = "English" }, TargetLanguage = new Language { Code = "zh-CN", DisplayName = "Chinese (Simplified)" } },
                // 中国語（簡体字） → 英語
                new LanguagePair { SourceLanguage = new Language { Code = "zh-CN", DisplayName = "Chinese (Simplified)" }, TargetLanguage = new Language { Code = "en", DisplayName = "English" } },
                // 日本語 → 中国語（簡体字）
                new LanguagePair { SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" }, TargetLanguage = new Language { Code = "zh-CN", DisplayName = "Chinese (Simplified)" } },
                // 中国語（簡体字） → 日本語
                new LanguagePair { SourceLanguage = new Language { Code = "zh-CN", DisplayName = "Chinese (Simplified)" }, TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" } },
                // 英語 → 中国語（繁体字）
                new LanguagePair { SourceLanguage = new Language { Code = "en", DisplayName = "English" }, TargetLanguage = new Language { Code = "zh-TW", DisplayName = "Chinese (Traditional)" } },
                // 中国語（繁体字） → 英語
                new LanguagePair { SourceLanguage = new Language { Code = "zh-TW", DisplayName = "Chinese (Traditional)" }, TargetLanguage = new Language { Code = "en", DisplayName = "English" } },
                // 日本語 → 中国語（繁体字）
                new LanguagePair { SourceLanguage = new Language { Code = "ja", DisplayName = "Japanese" }, TargetLanguage = new Language { Code = "zh-TW", DisplayName = "Chinese (Traditional)" } },
                // 中国語（繁体字） → 日本語
                new LanguagePair { SourceLanguage = new Language { Code = "zh-TW", DisplayName = "Chinese (Traditional)" }, TargetLanguage = new Language { Code = "ja", DisplayName = "Japanese" } }
            };

            // テスト用の翻訳セットを初期化
            InitializePresetTranslations();
        }

        /// <summary>
        /// テスト用の事前定義翻訳を追加します
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="translatedText">翻訳結果テキスト</param>
        public void AddPresetTranslation(string sourceText, string translatedText)
        {
            _presetTranslations[sourceText] = translatedText;
        }

        /// <summary>
        /// テスト用の事前定義翻訳を初期化します
        /// </summary>
        private void InitializePresetTranslations()
        {
            // 英語 → 日本語
            _presetTranslations["Hello"] = "こんにちは";
            _presetTranslations["Good morning"] = "おはようございます";
            _presetTranslations["Thank you"] = "ありがとう";
            _presetTranslations["Goodbye"] = "さようなら";

            // 日本語 → 英語
            _presetTranslations["こんにちは"] = "Hello";
            _presetTranslations["おはようございます"] = "Good morning";
            _presetTranslations["ありがとう"] = "Thank you";
            _presetTranslations["さようなら"] = "Goodbye";

            // 英語 → 中国語（簡体字）
            _presetTranslations["Hello_zh-CN"] = "你好";
            _presetTranslations["Good morning_zh-CN"] = "早上好";
            _presetTranslations["Thank you_zh-CN"] = "谢谢";
            _presetTranslations["Goodbye_zh-CN"] = "再见";

            // 日本語 → 中国語（簡体字）
            _presetTranslations["こんにちは_zh-CN"] = "你好";
            _presetTranslations["おはようございます_zh-CN"] = "早上好";
            _presetTranslations["ありがとう_zh-CN"] = "谢谢";
            _presetTranslations["さようなら_zh-CN"] = "再见";

            // 英語 → 中国語（繁体字）
            _presetTranslations["Hello_zh-TW"] = "你好";
            _presetTranslations["Good morning_zh-TW"] = "早安";
            _presetTranslations["Thank you_zh-TW"] = "謝謝";
            _presetTranslations["Goodbye_zh-TW"] = "再見";

            // 日本語 → 中国語（繁体字）
            _presetTranslations["こんにちは_zh-TW"] = "你好";
            _presetTranslations["おはようございます_zh-TW"] = "早安";
            _presetTranslations["ありがとう_zh-TW"] = "謝謝";
            _presetTranslations["さようなら_zh-TW"] = "再見";
        }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        public override Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
        {
            return Task.FromResult<IReadOnlyCollection<LanguagePair>>(_supportedLanguagePairs.ToList());
        }
        
        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します
        /// 名前空間移行のため、言語コードベースで比較
        /// </summary>
        /// <param name="languagePair">確認する言語ペア</param>
        /// <returns>サポートしていればtrue</returns>
        public override async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(languagePair);
            ArgumentNullException.ThrowIfNull(languagePair.SourceLanguage);
            ArgumentNullException.ThrowIfNull(languagePair.TargetLanguage);
            
            // 言語コードを比較してマッチングする
            var supportedPairs = await GetSupportedLanguagePairsAsync().ConfigureAwait(false);
            
            // 言語コードベースで比較する
            foreach (var supportedPair in supportedPairs)
            {
                bool sourceMatches = string.Equals(supportedPair.SourceLanguage.Code, languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase);
                bool targetMatches = string.Equals(supportedPair.TargetLanguage.Code, languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase);
                
                // 地域コードがある場合はそれも確認
                // RegionCodeプロパティは新しいクラスでは削除されたため、コードから取得
                string? sourceRegionCode = null;
                string? targetRegionCode = null;
                
                if (supportedPair.SourceLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    var parts = supportedPair.SourceLanguage.Code.Split('-');
                    if (parts.Length > 1) sourceRegionCode = parts[1];
                }
                
                if (languagePair.SourceLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    var parts = languagePair.SourceLanguage.Code.Split('-');
                    if (parts.Length > 1) sourceRegionCode = parts[1];
                }
                
                if (supportedPair.TargetLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    var parts = supportedPair.TargetLanguage.Code.Split('-');
                    if (parts.Length > 1) targetRegionCode = parts[1];
                }
                
                if (languagePair.TargetLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    var parts = languagePair.TargetLanguage.Code.Split('-');
                    if (parts.Length > 1) targetRegionCode = parts[1];
                }
                
                if (sourceRegionCode != null)
                {
                    // 地域コードの比較を行う
                    if (languagePair.SourceLanguage.Code.Contains('-', StringComparison.Ordinal))
                    {
                        var parts = languagePair.SourceLanguage.Code.Split('-');
                        if (parts.Length > 1)
                        {
                            string languagePairRegionCode = parts[1];
                            sourceMatches = sourceMatches && string.Equals(
                        sourceRegionCode,
                            languagePairRegionCode,
                                StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                
                if (targetRegionCode != null)
                {
                    // 地域コードの比較を行う
                    if (languagePair.TargetLanguage.Code.Contains('-', StringComparison.Ordinal))
                    {
                        var parts = languagePair.TargetLanguage.Code.Split('-');
                        if (parts.Length > 1)
                        {
                            string languagePairRegionCode = parts[1];
                            targetMatches = targetMatches && string.Equals(
                                targetRegionCode,
                                languagePairRegionCode,
                                StringComparison.OrdinalIgnoreCase);
                        }
                    }
                }
                
                // フルコード(zh-CN)と分割コード(zh + CN)の両方をサポート
                if (!sourceMatches && supportedPair.SourceLanguage.Code.Contains('-', StringComparison.Ordinal) && !languagePair.SourceLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    string[] codeParts = supportedPair.SourceLanguage.Code.Split('-');
                    sourceMatches = string.Equals(codeParts[0], languagePair.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase);

                }
                
                if (!targetMatches && supportedPair.TargetLanguage.Code.Contains('-', StringComparison.Ordinal) && !languagePair.TargetLanguage.Code.Contains('-', StringComparison.Ordinal))
                {
                    string[] codeParts = supportedPair.TargetLanguage.Code.Split('-');
                    targetMatches = string.Equals(codeParts[0], languagePair.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase);

                }
                
                if (sourceMatches && targetMatches)
                {
                    return true;
                }
            }
            
            // どの言語ペアもマッチしなかった場合
            return false;
        }

        /// <summary>
        /// エンジン固有の初期化処理を実装します
        /// </summary>
        /// <returns>初期化が成功すればtrue</returns>
        protected override Task<bool> InitializeInternalAsync()
        {
            // モックエンジンは常に初期化成功とする
            return Task.FromResult(true);
        }

        /// <summary>
        /// エンジン固有の翻訳処理を実装します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        protected override async Task<TranslationResponse> TranslateInternalAsync(
            TranslationRequest request,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            
            // シミュレートする遅延
            if (_simulatedDelayMs > 0)
            {
                await Task.Delay(_simulatedDelayMs, cancellationToken).ConfigureAwait(false);
            }

            // シミュレートするエラー
            if (_simulatedErrorRate > 0 && GetRandomDouble(_rng) < _simulatedErrorRate)
            {
                return CreateErrorResponse(
                    request,
                    TranslationError.InternalError,
                    "シミュレートされたランダムエラーが発生しました。");
            }

            string translatedText;

            // 事前定義された翻訳を検索
            string key = request.SourceText;

            // 中国語の場合は地域コードを含めたキーを使用
            // コードから地域コードを取得
            if (request.TargetLanguage.Code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = request.TargetLanguage.Code.Split('-');
                if (parts.Length > 1)
                {
                    key = $"{request.SourceText}_{request.TargetLanguage.Code}";
                }
            }

            if (_presetTranslations.TryGetValue(key, out string? preset))
            {
                translatedText = preset;
            }
            else
            {
                // プリセットがない場合は簡易翻訳を行う
                translatedText = GenerateSimpleMockTranslation(request);
            }

            var response = TranslationResponse.CreateSuccessWithConfidence(
                request,
                translatedText,
                Name,
                0, // 処理時間は基底クラスで設定
                0.85f + (float)(GetRandomDouble(_rng) * 0.15f)); // 0.85～1.0のランダムな信頼度スコア

            return response;
        }

        /// <summary>
        /// 簡易的なモック翻訳を生成します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <returns>モック翻訳テキスト</returns>
        private static string GenerateSimpleMockTranslation(TranslationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            
            // 言語ペアに基づいて簡易的な翻訳を行う
            if (request.SourceLanguage.Code == "en")
            {
                if (request.TargetLanguage.Code == "ja")
                {
                    return $"[英→日] {request.SourceText}";
                }
                else if (request.TargetLanguage.Code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.TargetLanguage.Code.EndsWith("CN", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[英→中(簡)] {request.SourceText}";
                    }
                    else if (request.TargetLanguage.Code.EndsWith("TW", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[英→中(繁)] {request.SourceText}";
                    }
                }
            }
            else if (request.SourceLanguage.Code == "ja")
            {
                if (request.TargetLanguage.Code == "en")
                {
                    return $"[日→英] {request.SourceText}";
                }
                else if (request.TargetLanguage.Code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                {
                    if (request.TargetLanguage.Code.EndsWith("CN", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[日→中(簡)] {request.SourceText}";
                    }
                    else if (request.TargetLanguage.Code.EndsWith("TW", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[日→中(繁)] {request.SourceText}";
                    }
                }
            }
            else if (request.SourceLanguage.Code.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
            {
                if (request.TargetLanguage.Code == "en")
                {
                    if (request.SourceLanguage.Code.EndsWith("CN", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[中(簡)→英] {request.SourceText}";
                    }
                    else if (request.SourceLanguage.Code.EndsWith("TW", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[中(繁)→英] {request.SourceText}";
                    }
                }
                else if (request.TargetLanguage.Code == "ja")
                {
                    if (request.SourceLanguage.Code.EndsWith("CN", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[中(簡)→日] {request.SourceText}";
                    }
                    else if (request.SourceLanguage.Code.EndsWith("TW", StringComparison.OrdinalIgnoreCase))
                    {
                        return $"[中(繁)→日] {request.SourceText}";
                    }
                }
            }

            // 未サポートの言語ペアの場合はそのまま返す
            return $"[未サポート: {request.SourceLanguage.Code} → {request.TargetLanguage.Code}] {request.SourceText}";
        }
        
        /// <summary>
        /// 0から1の間の暗号的に安全な乱数を生成します
        /// </summary>
        /// <returns>0.0から1.0の間のランダムな値</returns>
        private static double GetRandomDouble(RandomNumberGenerator rng)
        {
            ArgumentNullException.ThrowIfNull(rng);
            
            byte[] randomBytes = new byte[4];
            rng.GetBytes(randomBytes);
            uint randomUInt = BitConverter.ToUInt32(randomBytes, 0);
            return randomUInt / (double)uint.MaxValue;
        }

        /// <summary>
        /// リソースの解放
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放する場合はtrue</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // マネージドリソースの解放
                _rng?.Dispose();
            }

            // 基底クラスのDisposeを呼び出す
            base.Dispose(disposing);
        }
    }
}
