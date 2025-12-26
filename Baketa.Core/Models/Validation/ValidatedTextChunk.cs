using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Models.Validation;

/// <summary>
/// 検証済みテキストチャンク
/// 座標: ローカルOCR、翻訳: Cloud AI を統合
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: 相互検証で採用されたテキストチャンク
/// </remarks>
public sealed class ValidatedTextChunk
{
    /// <summary>
    /// 元のテキストチャンク（座標情報）
    /// </summary>
    public required TextChunk OriginalChunk { get; init; }

    /// <summary>
    /// 検証状態
    /// </summary>
    public required ValidationStatus Status { get; init; }

    /// <summary>
    /// 翻訳テキスト（Cloud AIから取得、または Local翻訳）
    /// </summary>
    public required string TranslatedText { get; init; }

    /// <summary>
    /// Cloud AIが検出した元テキスト（ある場合）
    /// </summary>
    public string? CloudDetectedText { get; init; }

    /// <summary>
    /// マッチング類似度（相互検証時）
    /// </summary>
    public float? MatchSimilarity { get; init; }

    /// <summary>
    /// 救済されたか（低信頼度からの救済）
    /// </summary>
    public bool WasRescued { get; init; }

    /// <summary>
    /// 相互検証で採用されたチャンクを作成
    /// </summary>
    public static ValidatedTextChunk CrossValidated(
        TextChunk originalChunk,
        string translatedText,
        string cloudDetectedText,
        float matchSimilarity)
        => new()
        {
            OriginalChunk = originalChunk,
            Status = ValidationStatus.CrossValidated,
            TranslatedText = translatedText,
            CloudDetectedText = cloudDetectedText,
            MatchSimilarity = matchSimilarity,
            WasRescued = false
        };

    /// <summary>
    /// 救済されたチャンクを作成
    /// </summary>
    public static ValidatedTextChunk Rescued(
        TextChunk originalChunk,
        string translatedText,
        string cloudDetectedText,
        float matchSimilarity)
        => new()
        {
            OriginalChunk = originalChunk,
            Status = ValidationStatus.Rescued,
            TranslatedText = translatedText,
            CloudDetectedText = cloudDetectedText,
            MatchSimilarity = matchSimilarity,
            WasRescued = true
        };

    /// <summary>
    /// ローカルのみのチャンクを作成（ローカル翻訳使用）
    /// </summary>
    public static ValidatedTextChunk LocalOnly(
        TextChunk originalChunk,
        string localTranslatedText)
        => new()
        {
            OriginalChunk = originalChunk,
            Status = ValidationStatus.LocalOnly,
            TranslatedText = localTranslatedText,
            CloudDetectedText = null,
            MatchSimilarity = null,
            WasRescued = false
        };

    /// <summary>
    /// フィルタリングされたチャンクを作成
    /// </summary>
    public static ValidatedTextChunk Filtered(
        TextChunk originalChunk,
        string reason)
        => new()
        {
            OriginalChunk = originalChunk,
            Status = ValidationStatus.Filtered,
            TranslatedText = string.Empty,
            CloudDetectedText = null,
            MatchSimilarity = null,
            WasRescued = false
        };

    /// <summary>
    /// 強制統合されたチャンクを作成（Phase 3.5: 複数ローカル→1 Cloud AI）
    /// </summary>
    /// <param name="mergedChunk">統合後のチャンク（UnionRect座標）</param>
    /// <param name="translatedText">Cloud AIからの翻訳テキスト</param>
    /// <param name="cloudDetectedText">Cloud AIが検出した元テキスト</param>
    /// <param name="mergedChunkCount">統合されたチャンク数</param>
    public static ValidatedTextChunk ForceMerged(
        TextChunk mergedChunk,
        string translatedText,
        string cloudDetectedText,
        int mergedChunkCount)
        => new()
        {
            OriginalChunk = mergedChunk,
            Status = ValidationStatus.ForceMerged,
            TranslatedText = translatedText,
            CloudDetectedText = cloudDetectedText,
            MatchSimilarity = null,
            WasRescued = false,
            MergedChunkCount = mergedChunkCount
        };

    /// <summary>
    /// 分割されたチャンクを作成（Phase 3.5: 1ローカル→複数 Cloud AI）
    /// </summary>
    /// <param name="splitChunk">分割後のチャンク（按分座標 or Cloud AI座標）</param>
    /// <param name="translatedText">Cloud AIからの翻訳テキスト</param>
    /// <param name="cloudDetectedText">Cloud AIが検出した元テキスト</param>
    /// <param name="originalChunkId">分割元のチャンクID</param>
    public static ValidatedTextChunk Split(
        TextChunk splitChunk,
        string translatedText,
        string cloudDetectedText,
        int originalChunkId)
        => new()
        {
            OriginalChunk = splitChunk,
            Status = ValidationStatus.Split,
            TranslatedText = translatedText,
            CloudDetectedText = cloudDetectedText,
            MatchSimilarity = null,
            WasRescued = false,
            OriginalChunkIdBeforeSplit = originalChunkId
        };

    /// <summary>
    /// 統合されたチャンク数（ForceMerged時のみ）
    /// </summary>
    public int? MergedChunkCount { get; init; }

    /// <summary>
    /// 分割元のチャンクID（Split時のみ）
    /// </summary>
    public int? OriginalChunkIdBeforeSplit { get; init; }
}

/// <summary>
/// 検証状態
/// </summary>
public enum ValidationStatus
{
    /// <summary>ローカルOCRとCloud AI両方で検出・一致</summary>
    CrossValidated,

    /// <summary>ローカルOCRのみで検出（Cloud AIで未検出）</summary>
    LocalOnly,

    /// <summary>Cloud AIのみで検出（ローカルOCRで未検出）</summary>
    CloudOnly,

    /// <summary>低信頼度だがCloud AIと一致して救済</summary>
    Rescued,

    /// <summary>フィルタリングで除外</summary>
    Filtered,

    /// <summary>複数ローカルチャンクを1つのCloud AIテキストに統合（Phase 3.5）</summary>
    ForceMerged,

    /// <summary>1つのローカルチャンクを複数Cloud AIテキストに分割（Phase 3.5）</summary>
    Split
}
