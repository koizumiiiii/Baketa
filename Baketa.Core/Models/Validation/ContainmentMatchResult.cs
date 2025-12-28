namespace Baketa.Core.Models.Validation;

/// <summary>
/// 包含マッチング結果
/// </summary>
/// <remarks>
/// Issue #78 Phase 3.5: 双方向マッチング（統合/分割）の判定結果
/// </remarks>
public sealed class ContainmentMatchResult
{
    /// <summary>
    /// マッチングタイプ
    /// </summary>
    public required ContainmentMatchType MatchType { get; init; }

    /// <summary>
    /// マッチしたCloud AIテキストのインデックス（複数の場合あり）
    /// </summary>
    public required IReadOnlyList<int> MatchedCloudTextIndices { get; init; }

    /// <summary>
    /// マッチしたローカルチャンクのインデックス（統合時のみ、複数の場合あり）
    /// </summary>
    public IReadOnlyList<int>? MatchedLocalChunkIndices { get; init; }

    /// <summary>
    /// 包含されたテキスト（統合/分割対象）
    /// </summary>
    public IReadOnlyList<string>? ContainedTexts { get; init; }

    /// <summary>
    /// マッチなし
    /// </summary>
    public static ContainmentMatchResult NoMatch => new()
    {
        MatchType = ContainmentMatchType.NoMatch,
        MatchedCloudTextIndices = []
    };

    /// <summary>
    /// 統合が必要（複数ローカル ⊂ 1 Cloud AI）
    /// </summary>
    public static ContainmentMatchResult NeedsMerge(
        int cloudTextIndex,
        IReadOnlyList<int> localChunkIndices,
        IReadOnlyList<string> containedTexts)
        => new()
        {
            MatchType = ContainmentMatchType.NeedsMerge,
            MatchedCloudTextIndices = [cloudTextIndex],
            MatchedLocalChunkIndices = localChunkIndices,
            ContainedTexts = containedTexts
        };

    /// <summary>
    /// 分割が必要（1ローカル ⊃ 複数 Cloud AI）
    /// </summary>
    public static ContainmentMatchResult NeedsSplit(
        IReadOnlyList<int> cloudTextIndices,
        IReadOnlyList<string> containedTexts)
        => new()
        {
            MatchType = ContainmentMatchType.NeedsSplit,
            MatchedCloudTextIndices = cloudTextIndices,
            ContainedTexts = containedTexts
        };
}

/// <summary>
/// 包含マッチングタイプ
/// </summary>
public enum ContainmentMatchType
{
    /// <summary>マッチなし</summary>
    NoMatch,

    /// <summary>統合が必要（複数ローカル ⊂ 1 Cloud AI）</summary>
    NeedsMerge,

    /// <summary>分割が必要（1ローカル ⊃ 複数 Cloud AI）</summary>
    NeedsSplit
}
