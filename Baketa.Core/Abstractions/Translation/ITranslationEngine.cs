using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

    /// <summary>
    /// 翻訳エンジンの機能を定義するインターフェース
    /// </summary>
    public interface ITranslationEngine : IDisposable
    {
        /// <summary>
        /// エンジン名を取得します
        /// </summary>
        string Name { get; }

        /// <summary>
        /// エンジンの説明を取得します
        /// </summary>
        string Description { get; }

        /// <summary>
        /// エンジンがオンライン接続を必要とするかどうかを示します
        /// </summary>
        bool RequiresNetwork { get; }

        /// <summary>
        /// サポートしている言語ペアを取得します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsAsync();

        /// <summary>
        /// 指定された言語ペアをサポートしているかどうかを確認します
        /// </summary>
        /// <param name="languagePair">確認する言語ペア</param>
        /// <returns>サポートしていればtrue</returns>
        Task<bool> SupportsLanguagePairAsync(LanguagePair languagePair);

        /// <summary>
        /// テキストを翻訳します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> TranslateAsync(
            TranslationRequest request,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// 複数のテキストをバッチ翻訳します
        /// </summary>
        /// <param name="requests">翻訳リクエストのコレクション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンスのコレクション</returns>
        Task<IReadOnlyList<TranslationResponse>> TranslateBatchAsync(
            IReadOnlyList<TranslationRequest> requests,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// エンジンの準備状態を確認します
        /// </summary>
        /// <returns>準備ができていればtrue</returns>
        Task<bool> IsReadyAsync();

        /// <summary>
        /// エンジンを初期化します
        /// </summary>
        /// <returns>初期化が成功すればtrue</returns>
        Task<bool> InitializeAsync();
    }
