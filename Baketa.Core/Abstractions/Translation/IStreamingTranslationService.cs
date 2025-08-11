using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// ストリーミング翻訳サービスインターフェース
/// 🔥 [STREAMING] 段階的結果表示: バッチ翻訳中に完了チャンクから逐次配信
/// </summary>
public interface IStreamingTranslationService
{
    /// <summary>
    /// バッチ翻訳を段階的に処理し、完了したチャンクから順次結果を配信
    /// </summary>
    /// <param name="texts">翻訳するテキストのリスト</param>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">ターゲット言語</param>
    /// <param name="onChunkCompleted">チャンク完了時のコールバック</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>全翻訳結果</returns>
    Task<List<string>> TranslateBatchWithStreamingAsync(
        IList<string> texts,
        Language sourceLanguage,
        Language targetLanguage,
        Action<int, string> onChunkCompleted,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 翻訳の進行状況を取得
    /// </summary>
    Core.Translation.Models.TranslationProgress GetProgress();
}

