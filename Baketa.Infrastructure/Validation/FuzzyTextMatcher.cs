using Baketa.Core.Abstractions.Validation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Validation;

/// <summary>
/// ファジーテキストマッチング実装
/// レーベンシュタイン距離を用いた類似度計算
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: ローカルOCRとCloud AI結果の相互検証に使用
/// </remarks>
public sealed class FuzzyTextMatcher : IFuzzyTextMatcher
{
    private readonly ILogger<FuzzyTextMatcher> _logger;

    // テキスト長に応じた閾値
    private const float ShortTextThreshold = 0.90f;   // 1-5文字
    private const float MediumTextThreshold = 0.85f;  // 6-9文字
    private const float LongTextThreshold = 0.80f;    // 10文字以上

    private const int ShortTextMaxLength = 5;
    private const int MediumTextMaxLength = 9;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public FuzzyTextMatcher(ILogger<FuzzyTextMatcher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public float CalculateSimilarity(string text1, string text2)
    {
        // 両方null/空の場合は完全一致
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2))
        {
            return 1.0f;
        }

        // 片方が空の場合は完全不一致
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2))
        {
            return 0.0f;
        }

        // 完全一致チェック（最も高速）
        if (string.Equals(text1, text2, StringComparison.Ordinal))
        {
            return 1.0f;
        }

        // レーベンシュタイン距離を計算
        var editDistance = CalculateEditDistance(text1, text2);
        var maxLength = Math.Max(text1.Length, text2.Length);

        // 類似度 = 1 - (編集距離 / 最大長)
        var similarity = 1.0f - ((float)editDistance / maxLength);

        return Math.Clamp(similarity, 0f, 1f);
    }

    /// <inheritdoc />
    public FuzzyMatchResult IsMatch(string text1, string text2)
    {
        var threshold = GetThresholdForLength(text1, text2);
        return IsMatch(text1, text2, threshold);
    }

    /// <inheritdoc />
    public FuzzyMatchResult IsMatch(string text1, string text2, float threshold)
    {
        var similarity = CalculateSimilarity(text1, text2);
        var editDistance = CalculateEditDistance(text1 ?? string.Empty, text2 ?? string.Empty);
        var isMatch = similarity >= threshold;

        _logger.LogDebug(
            "ファジーマッチング: Text1={Text1Len}文字, Text2={Text2Len}文字, 類似度={Similarity:F3}, 閾値={Threshold:F2}, 結果={Result}",
            text1?.Length ?? 0,
            text2?.Length ?? 0,
            similarity,
            threshold,
            isMatch ? "一致" : "不一致");

        return isMatch
            ? FuzzyMatchResult.Match(similarity, threshold, text1, text2, editDistance)
            : FuzzyMatchResult.NoMatch(similarity, threshold, text1, text2, editDistance);
    }

    /// <summary>
    /// テキスト長に基づいて閾値を取得
    /// </summary>
    private static float GetThresholdForLength(string? text1, string? text2)
    {
        var len1 = text1?.Length ?? 0;
        var len2 = text2?.Length ?? 0;
        var avgLength = (len1 + len2) / 2;

        return avgLength switch
        {
            <= ShortTextMaxLength => ShortTextThreshold,
            <= MediumTextMaxLength => MediumTextThreshold,
            _ => LongTextThreshold
        };
    }

    /// <summary>
    /// レーベンシュタイン距離（編集距離）を計算
    /// </summary>
    /// <remarks>
    /// TextChangeDetectionService.CalculateEditDistance()と同じアルゴリズム
    /// </remarks>
    private static int CalculateEditDistance(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1)) return text2?.Length ?? 0;
        if (string.IsNullOrEmpty(text2)) return text1.Length;

        var len1 = text1.Length;
        var len2 = text2.Length;

        // 効率化: 片方が著しく長い場合は早期リターン
        if (Math.Abs(len1 - len2) > Math.Max(len1, len2) * 0.8)
        {
            return Math.Max(len1, len2);
        }

        // DP行列による編集距離計算
        // メモリ効率のため、2行のみ使用
        var previousRow = new int[len2 + 1];
        var currentRow = new int[len2 + 1];

        // 初期化
        for (int j = 0; j <= len2; j++)
        {
            previousRow[j] = j;
        }

        // DP計算
        for (int i = 1; i <= len1; i++)
        {
            currentRow[0] = i;

            for (int j = 1; j <= len2; j++)
            {
                var cost = text1[i - 1] == text2[j - 1] ? 0 : 1;
                currentRow[j] = Math.Min(
                    Math.Min(previousRow[j] + 1, currentRow[j - 1] + 1),
                    previousRow[j - 1] + cost);
            }

            // 行を入れ替え
            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[len2];
    }
}
