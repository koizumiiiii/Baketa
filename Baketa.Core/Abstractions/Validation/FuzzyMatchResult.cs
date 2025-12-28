namespace Baketa.Core.Abstractions.Validation;

/// <summary>
/// ファジーマッチング結果
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: テキスト類似度判定結果
/// </remarks>
public sealed class FuzzyMatchResult
{
    /// <summary>
    /// マッチングが成功したか（類似度が閾値以上）
    /// </summary>
    public required bool IsMatch { get; init; }

    /// <summary>
    /// 類似度スコア (0.0 - 1.0)
    /// </summary>
    public required float Similarity { get; init; }

    /// <summary>
    /// 適用された閾値
    /// </summary>
    public required float AppliedThreshold { get; init; }

    /// <summary>
    /// 比較対象テキスト1
    /// </summary>
    public string? Text1 { get; init; }

    /// <summary>
    /// 比較対象テキスト2
    /// </summary>
    public string? Text2 { get; init; }

    /// <summary>
    /// 編集距離（レーベンシュタイン距離）
    /// </summary>
    public int EditDistance { get; init; }

    /// <summary>
    /// マッチング成功の結果を作成
    /// </summary>
    public static FuzzyMatchResult Match(float similarity, float threshold, string? text1 = null, string? text2 = null, int editDistance = 0)
        => new()
        {
            IsMatch = true,
            Similarity = similarity,
            AppliedThreshold = threshold,
            Text1 = text1,
            Text2 = text2,
            EditDistance = editDistance
        };

    /// <summary>
    /// マッチング失敗の結果を作成
    /// </summary>
    public static FuzzyMatchResult NoMatch(float similarity, float threshold, string? text1 = null, string? text2 = null, int editDistance = 0)
        => new()
        {
            IsMatch = false,
            Similarity = similarity,
            AppliedThreshold = threshold,
            Text1 = text1,
            Text2 = text2,
            EditDistance = editDistance
        };
}
