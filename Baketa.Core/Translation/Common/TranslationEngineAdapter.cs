using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 明示的な名前空間への参照を指定
using CoreTrEngine = Baketa.Core.Abstractions.Translation.ITranslationEngine;
using NewTrEngine = Baketa.Core.Translation.Abstractions.ITranslationEngine;

// 名前空間エイリアスの定義
using TransModels = Baketa.Core.Translation.Models;
using CoreModels = Baketa.Core.Models.Translation;

namespace Baketa.Core.Translation.Common
{
    /// <summary>
    /// 翻訳エンジンアダプター
    /// </summary>
    public class TranslationEngineAdapter : NewTrEngine
    {
        private readonly CoreTrEngine _coreEngine;
        private bool _disposed;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="coreEngine">コア翻訳エンジン</param>
        public TranslationEngineAdapter(CoreTrEngine coreEngine)
        {
            _coreEngine = coreEngine ?? throw new ArgumentNullException(nameof(coreEngine));
        }

        /// <summary>
        /// 翻訳エンジンの名称
        /// </summary>
        public string Name => _coreEngine.Name;

        /// <summary>
        /// 翻訳エンジンの説明
        /// </summary>
        public string Description => _coreEngine.Description;

        /// <summary>
        /// ネットワーク接続が必要かどうか
        /// </summary>
        public bool RequiresNetwork => _coreEngine.RequiresNetwork;

        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        public async Task<TransModels.TranslationResponse> TranslateAsync(
            TransModels.TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // コアモデルに変換
            var coreRequest = new CoreModels.TranslationRequest
            {
                SourceText = request.SourceText,
                SourceLanguage = new CoreModels.Language { 
                    Code = request.SourceLanguage.Code,
                    Name = request.SourceLanguage.Code  // DisplayNameの代わりにCodeをNameに設定
                },
                TargetLanguage = new CoreModels.Language { 
                    Code = request.TargetLanguage.Code,
                    Name = request.TargetLanguage.Code  // DisplayNameの代わりにCodeをNameに設定
                }
            };

            // コアエンジンで翻訳
            var coreResponse = await _coreEngine.TranslateAsync(coreRequest, cancellationToken).ConfigureAwait(false);

            // レスポンスを変換
            return new TransModels.TranslationResponse
            {
                RequestId = Guid.Parse(coreResponse.RequestId.ToString()),
                SourceText = coreResponse.SourceText,
                TranslatedText = coreResponse.TranslatedText,
                SourceLanguage = new TransModels.Language { 
                    Code = coreResponse.SourceLanguage.Code,
                    DisplayName = coreResponse.SourceLanguage.Name // Nameの値をDisplayNameに設定
                },
                TargetLanguage = new TransModels.Language { 
                    Code = coreResponse.TargetLanguage.Code,
                    DisplayName = coreResponse.TargetLanguage.Name // Nameの値をDisplayNameに設定
                },
                EngineName = coreResponse.EngineName ?? _coreEngine.Name,
                ProcessingTimeMs = coreResponse.ProcessingTimeMs,
                IsSuccess = true // プロパティの不一致に対応
            };
        }

        /// <summary>
        /// 複数のテキストを一括翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのリスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのリスト</returns>
        public async Task<IReadOnlyList<TransModels.TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TransModels.TranslationRequest> requests,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            if (requests.Count == 0)
            {
                return Array.Empty<TransModels.TranslationResponse>();
            }

            // コアモデルに変換
            var coreRequests = requests.Select(r => new CoreModels.TranslationRequest
            {
                SourceText = r.SourceText,
                SourceLanguage = new CoreModels.Language { 
                    Code = r.SourceLanguage.Code,
                    Name = r.SourceLanguage.Code // DisplayNameの代わりにCodeをNameに設定
                },
                TargetLanguage = new CoreModels.Language { 
                    Code = r.TargetLanguage.Code,
                    Name = r.TargetLanguage.Code // DisplayNameの代わりにCodeをNameに設定
                }
            }).ToList();

            // コアエンジンでバッチ翻訳
            var coreResponses = await _coreEngine.TranslateBatchAsync(coreRequests, cancellationToken).ConfigureAwait(false);

            // レスポンスリストを変換
            return coreResponses.Select(r => new TransModels.TranslationResponse
            {
                RequestId = Guid.Parse(r.RequestId.ToString()),
                SourceText = r.SourceText,
                TranslatedText = r.TranslatedText,
                SourceLanguage = new TransModels.Language { 
                    Code = r.SourceLanguage.Code,
                    DisplayName = r.SourceLanguage.Name // Nameの値をDisplayNameに設定
                },
                TargetLanguage = new TransModels.Language { 
                    Code = r.TargetLanguage.Code,
                    DisplayName = r.TargetLanguage.Name // Nameの値をDisplayNameに設定
                },
                EngineName = r.EngineName ?? _coreEngine.Name,
                ProcessingTimeMs = r.ProcessingTimeMs,
                IsSuccess = true // プロパティの不一致に対応
            }).ToList();
        }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        public async Task<IReadOnlyCollection<TransModels.LanguagePair>> GetSupportedLanguagePairsAsync()
        {
            var corePairs = await _coreEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);

            return corePairs.Select(p => new TransModels.LanguagePair
            {
                SourceLanguage = new TransModels.Language { 
                    Code = p.SourceLanguage.Code,
                    DisplayName = p.SourceLanguage.Name // Nameの値をDisplayNameに設定
                },
                TargetLanguage = new TransModels.Language { 
                    Code = p.TargetLanguage.Code,
                    DisplayName = p.TargetLanguage.Name // Nameの値をDisplayNameに設定
                }
            }).ToList();
        }

        /// <summary>
        /// 指定した言語ペアがサポートされているか確認します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>サポートされている場合はtrue</returns>
        public async Task<bool> SupportsLanguagePairAsync(TransModels.LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(languagePair);

            var corePair = new CoreModels.LanguagePair
            {
                SourceLanguage = new CoreModels.Language { 
                    Code = languagePair.SourceLanguage.Code,
                    Name = languagePair.SourceLanguage.Code // DisplayNameの代わりにCodeをNameに設定
                },
                TargetLanguage = new CoreModels.Language { 
                    Code = languagePair.TargetLanguage.Code,
                    Name = languagePair.TargetLanguage.Code // DisplayNameの代わりにCodeをNameに設定
                }
            };

            return await _coreEngine.SupportsLanguagePairAsync(corePair).ConfigureAwait(false);
        }

        /// <summary>
        /// 翻訳エンジンが準備完了しているか確認します
        /// </summary>
        /// <returns>準備完了している場合はtrue</returns>
        public async Task<bool> IsReadyAsync()
        {
            return await _coreEngine.IsReadyAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// 翻訳エンジンを初期化します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        public async Task<bool> InitializeAsync()
        {
            return await _coreEngine.InitializeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// テキストの言語を自動検出します
        /// </summary>
        /// <param name="text">検出対象テキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検出された言語と信頼度</returns>
        public Task<TransModels.LanguageDetectionResult> DetectLanguageAsync(string text, CancellationToken cancellationToken = default)
        {
            // 現在のコアエンジンでは言語検出が実装されていないため、簡易的な結果を返す
            var coreResult = new CoreModels.LanguageDetectionResult()
            {
                DetectedLanguage = new CoreModels.Language() { Code = "auto", Name = "自動検出" },
                Confidence = 0.5f,
                EngineName = _coreEngine.Name
            };
            
            // CoreModelsからTransModelsに変換
            var result = new TransModels.LanguageDetectionResult
            {
                DetectedLanguage = new TransModels.Language { Code = coreResult.DetectedLanguage.Code, DisplayName = coreResult.DetectedLanguage.Name },
                Confidence = coreResult.Confidence,
                EngineName = coreResult.EngineName
            };
            
            return Task.FromResult(result);
        }

        /// <summary>
        /// リソースを破棄します
        /// </summary>
        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを破棄します。
        /// </summary>
        /// <param name="disposing">マネージドリソースを破棄する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // マネージドリソースの破棄
                    _coreEngine?.Dispose();
                }

                _disposed = true;
            }
        }
    }
}