using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;

namespace Baketa.Core.Abstractions.Validation;

/// <summary>
/// ローカルOCRとCloud AI結果の相互検証インターフェース
/// ハルシネーション・ノイズ除去を実現
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: 相互検証ロジック
/// - 両方で検出 → 採用
/// - 片方のみ → 除外
/// - 信頼度 &lt; 0.30 → 除外
/// </remarks>
public interface ICrossValidator
{
    /// <summary>
    /// 相互検証を実行
    /// </summary>
    /// <param name="localOcrChunks">ローカルOCR結果（座標+テキスト+信頼度）</param>
    /// <param name="cloudAiResponse">Cloud AI翻訳結果（検出テキスト+翻訳）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>検証済み結果</returns>
    Task<CrossValidationResult> ValidateAsync(
        IReadOnlyList<TextChunk> localOcrChunks,
        ImageTranslationResponse cloudAiResponse,
        CancellationToken cancellationToken = default);
}
