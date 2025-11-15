using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// バッチ翻訳をサポートする翻訳サービスのインターフェース
/// </summary>
public interface ITranslationServiceWithBatch : ITranslationService
{
    /// <summary>
    /// 複数のテキストをバッチで翻訳します
    /// </summary>
    /// <param name="texts">翻訳するテキストのリスト</param>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">ターゲット言語</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>翻訳結果のリスト</returns>
    Task<List<string>> TranslateBatchAsync(
        List<string> texts,
        Language sourceLanguage,
        Language targetLanguage,
        CancellationToken cancellationToken = default);
}
