using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Abstractions;

    /// <summary>
    /// 翻訳エンジンの基本実装を提供する機能を定義するインターフェース
    /// </summary>
    public interface ITranslationEngineInternal
    {
        /// <summary>
        /// 内部翻訳処理を実装します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レスポンス</returns>
        Task<TranslationResponse> TranslateInternalAsync(
            TranslationRequest request, 
            CancellationToken cancellationToken);
        
        /// <summary>
        /// 内部初期化処理を実装します
        /// </summary>
        /// <returns>初期化に成功した場合はtrue</returns>
        Task<bool> InitializeInternalAsync();
        
        /// <summary>
        /// 内部でサポートされている言語ペアを取得する処理を実装します
        /// </summary>
        /// <returns>サポートされている言語ペアのコレクション</returns>
        Task<IReadOnlyCollection<LanguagePair>> GetSupportedLanguagePairsInternalAsync();
    }
