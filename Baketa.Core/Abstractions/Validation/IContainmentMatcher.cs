using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;

namespace Baketa.Core.Abstractions.Validation;

/// <summary>
/// 包含マッチングインターフェース
/// </summary>
/// <remarks>
/// Issue #78 Phase 3.5: 双方向マッチング
/// - 統合方向: 複数ローカルチャンク ⊂ 1つのCloud AIテキスト → Force Merge
/// - 分割方向: 1つのローカルチャンク ⊃ 複数のCloud AIテキスト → Split
/// </remarks>
public interface IContainmentMatcher
{
    /// <summary>
    /// 境界を考慮した包含チェック
    /// </summary>
    /// <param name="text">検索対象テキスト</param>
    /// <param name="container">包含元テキスト</param>
    /// <returns>包含されているか（境界考慮）</returns>
    /// <remarks>
    /// 境界判定:
    /// - 空白、句読点は境界
    /// - 日本語: 文字種の変わり目（ひらがな↔カタカナ↔漢字↔英数字）も境界
    /// </remarks>
    bool IsContainedWithBoundary(string text, string container);

    /// <summary>
    /// 統合が必要なチャンクを検出（複数ローカル ⊂ 1 Cloud AI）
    /// </summary>
    /// <param name="unmatchedChunks">ファジーマッチングで未マッチのローカルチャンク</param>
    /// <param name="cloudTexts">Cloud AI検出テキスト</param>
    /// <returns>統合が必要なチャンクグループのリスト（Cloud AIテキストインデックスごと）</returns>
    IReadOnlyList<MergeGroup> FindMergeGroups(
        IReadOnlyList<TextChunk> unmatchedChunks,
        IReadOnlyList<string> cloudTexts);

    /// <summary>
    /// 分割が必要なチャンクを検出（1 ローカル ⊃ 複数 Cloud AI）
    /// </summary>
    /// <param name="unmatchedChunk">ファジーマッチングで未マッチのローカルチャンク</param>
    /// <param name="cloudTextItems">Cloud AI検出テキスト（BoundingBox座標含む）</param>
    /// <returns>分割情報（null = 分割不要）</returns>
    /// <remarks>
    /// Issue #275: TranslatedTextItemを受け取ることでBoundingBox座標を保持
    /// </remarks>
    SplitInfo? FindSplitInfo(
        TextChunk unmatchedChunk,
        IReadOnlyList<TranslatedTextItem> cloudTextItems);
}

/// <summary>
/// 統合グループ（複数ローカルチャンク → 1 Cloud AIテキスト）
/// </summary>
public sealed class MergeGroup
{
    /// <summary>
    /// 統合対象のローカルチャンク
    /// </summary>
    public required IReadOnlyList<TextChunk> LocalChunks { get; init; }

    /// <summary>
    /// 対応するCloud AIテキストのインデックス
    /// </summary>
    public required int CloudTextIndex { get; init; }

    /// <summary>
    /// Cloud AIテキスト
    /// </summary>
    public required string CloudText { get; init; }
}

/// <summary>
/// 分割情報（1ローカルチャンク → 複数 Cloud AIテキスト）
/// </summary>
public sealed class SplitInfo
{
    /// <summary>
    /// 分割対象のローカルチャンク
    /// </summary>
    public required TextChunk LocalChunk { get; init; }

    /// <summary>
    /// 含まれているCloud AIテキストのインデックスと位置情報
    /// </summary>
    public required IReadOnlyList<SplitSegment> Segments { get; init; }
}

/// <summary>
/// 分割セグメント（Cloud AIテキストの位置情報）
/// </summary>
public sealed class SplitSegment
{
    /// <summary>
    /// Cloud AIテキストのインデックス
    /// </summary>
    public required int CloudTextIndex { get; init; }

    /// <summary>
    /// Cloud AIテキスト
    /// </summary>
    public required string CloudText { get; init; }

    /// <summary>
    /// ローカルテキスト内での開始位置
    /// </summary>
    public required int StartIndex { get; init; }

    /// <summary>
    /// ローカルテキスト内での終了位置
    /// </summary>
    public required int EndIndex { get; init; }

    /// <summary>
    /// Cloud AIのBoundingBox座標（ピクセル座標）
    /// Issue #275: AI翻訳時の正確な座標配置のため
    /// </summary>
    public Int32Rect? CloudBoundingBox { get; init; }
}
