using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

// 明示的な名前空間への参照を指定
using CoreTrEngine = Baketa.Core.Abstractions.Translation.ITranslationEngine;
using NewTrEngine = Baketa.Core.Translation.Abstractions.ITranslationEngine;
using Baketa.Core.Translation.Models;
using Baketa.Core.Abstractions.Translation;
using TransModels = Baketa.Core.Translation.Models;


namespace Baketa.Core.Translation.Common;

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
        public async Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            // コアモデルに変換
            var coreRequest = new TranslationRequest
            {
                SourceText = request.SourceText,
                SourceLanguage = new Language
                { 
                    Code = request.SourceLanguage.Code,
                    Name = request.SourceLanguage.DisplayName,  // DisplayNameの値をNameに設定
                    DisplayName = request.SourceLanguage.DisplayName
                },
                TargetLanguage = new Language
                { 
                    Code = request.TargetLanguage.Code,
                    Name = request.TargetLanguage.DisplayName,  // DisplayNameの値をNameに設定
                    DisplayName = request.TargetLanguage.DisplayName
                }
            };

            // コアエンジンで翻訳
            var coreResponse = await _coreEngine.TranslateAsync(coreRequest, cancellationToken).ConfigureAwait(false);

            // レスポンスを変換
            return new TranslationResponse
            {
                RequestId = Guid.Parse(coreResponse.RequestId.ToString()),
                SourceText = coreResponse.SourceText,
                TranslatedText = coreResponse.TranslatedText,
                SourceLanguage = new Language
                { 
                    Code = coreResponse.SourceLanguage.Code,
                    DisplayName = coreResponse.SourceLanguage.Name, // Nameの値をDisplayNameに設定
                    Name = coreResponse.SourceLanguage.Name
                },
                TargetLanguage = new Language
                { 
                    Code = coreResponse.TargetLanguage.Code,
                    DisplayName = coreResponse.TargetLanguage.Name, // Nameの値をDisplayNameに設定
                    Name = coreResponse.TargetLanguage.Name
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
        public async Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(requests);

            if (requests.Count == 0)
            {
                return [];
            }

            // 各リクエストを個別に処理する方法に変更
            // LINQを使用してコレクション初期化を簡素化
            var tasks = requests.Select(request => TranslateAsync(request, cancellationToken)).ToList();

            // すべてのタスクが完了するのを待つ
            return await Task.WhenAll(tasks).ConfigureAwait(false);
        }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        public async Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync()
        {
            var corePairs = await _coreEngine.GetSupportedLanguagePairsAsync().ConfigureAwait(false);

            return [.. corePairs.Select(p => new LanguagePair
            {
                SourceLanguage = new Language
                { 
                    Code = p.SourceLanguage.Code,
                    DisplayName = p.SourceLanguage.Name, // Nameの値をDisplayNameに設定
                    Name = p.SourceLanguage.Name
                },
                TargetLanguage = new Language
                { 
                    Code = p.TargetLanguage.Code,
                    DisplayName = p.TargetLanguage.Name, // Nameの値をDisplayNameに設定
                    Name = p.TargetLanguage.Name
                }
            })];
        }

        /// <summary>
        /// 指定した言語ペアがサポートされているか確認します
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>サポートされている場合はtrue</returns>
        public async Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair)
        {
        ArgumentNullException.ThrowIfNull(languagePair);

        var corePair = new LanguagePair
        {
        SourceLanguage = new Language
        { 
        Code = languagePair.SourceLanguage.Code,
        Name = languagePair.SourceLanguage.DisplayName, // DisplayNameの値をNameに設定
        DisplayName = languagePair.SourceLanguage.DisplayName
        },
        TargetLanguage = new Language
        { 
        Code = languagePair.TargetLanguage.Code,
        Name = languagePair.TargetLanguage.DisplayName, // DisplayNameの値をNameに設定
        DisplayName = languagePair.TargetLanguage.DisplayName
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
        public async Task<Models.LanguageDetectionResult> DetectLanguageAsync(
        string text, 
        CancellationToken cancellationToken = default)
        {
        // コアエンジンでは言語検出機能が実装されていない場合、
        // 今回はコア側にない機能なので、検出機能をここで簡易実装
        
        // 簡易検出結果を作成
        var result = new Models.LanguageDetectionResult
        {
        DetectedLanguage = new Language
        { 
        Code = "auto", 
        DisplayName = "自動検出" 
        },
        Confidence = 0.5f,
        EngineName = _coreEngine.Name
        };
        
        return await Task.FromResult(result).ConfigureAwait(false);
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
                    (_coreEngine as IDisposable)?.Dispose();
                }

                _disposed = true;
            }
        }
    }
