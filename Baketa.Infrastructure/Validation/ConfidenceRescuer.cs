using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Validation;

/// <summary>
/// 低信頼度テキストの救済実装
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: 低信頼度テキスト救済ロジック
/// 救済条件:
/// - 信頼度 0.30-0.70
/// - Cloud AIと類似度 80%以上
/// - テキスト長 3文字以上
/// </remarks>
public sealed class ConfidenceRescuer : IConfidenceRescuer
{
    private readonly IFuzzyTextMatcher _fuzzyMatcher;
    private readonly ILogger<ConfidenceRescuer> _logger;

    // 救済条件の閾値
    private const float MinConfidenceForRescue = 0.30f;
    private const float MaxConfidenceForRescue = 0.70f;
    private const float MinSimilarityForRescue = 0.80f;
    private const int MinTextLengthForRescue = 3;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ConfidenceRescuer(
        IFuzzyTextMatcher fuzzyMatcher,
        ILogger<ConfidenceRescuer> logger)
    {
        _fuzzyMatcher = fuzzyMatcher ?? throw new ArgumentNullException(nameof(fuzzyMatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public RescueResult TryRescue(TextChunk chunk, string cloudDetectedText)
    {
        return TryRescue(chunk, [cloudDetectedText]); // C# 12 コレクション式
    }

    /// <inheritdoc />
    public RescueResult TryRescue(TextChunk chunk, IReadOnlyList<string> cloudDetectedTexts)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        var confidence = chunk.AverageConfidence;
        var localText = chunk.CombinedText;

        // 基本条件チェック
        if (!IsEligibleForRescue(chunk, out var reason))
        {
            _logger.LogDebug(
                "救済対象外: Text='{Text}', Reason={Reason}",
                localText.Length > 20 ? localText[..20] + "..." : localText,
                reason);

            return RescueResult.NotRescued(reason);
        }

        // 各Cloud検出テキストとマッチング
        var bestMatch = FindBestRescueMatch(localText, cloudDetectedTexts);

        if (bestMatch.Similarity >= MinSimilarityForRescue)
        {
            _logger.LogDebug(
                "救済成功: Text='{Text}', Confidence={Confidence:F3}, CloudMatch='{CloudMatch}', Similarity={Similarity:F3}",
                localText.Length > 20 ? localText[..20] + "..." : localText,
                confidence,
                bestMatch.MatchedText.Length > 20 ? bestMatch.MatchedText[..20] + "..." : bestMatch.MatchedText,
                bestMatch.Similarity);

            return RescueResult.Rescued(
                bestMatch.Similarity,
                bestMatch.MatchedText,
                $"Cloud AIテキストと{bestMatch.Similarity:P0}一致");
        }

        _logger.LogDebug(
            "救済失敗: Text='{Text}', BestSimilarity={Similarity:F3} (閾値={Threshold:F2})",
            localText.Length > 20 ? localText[..20] + "..." : localText,
            bestMatch.Similarity,
            MinSimilarityForRescue);

        return RescueResult.NotRescued(
            $"最高類似度 {bestMatch.Similarity:P0} が閾値 {MinSimilarityForRescue:P0} 未満");
    }

    /// <summary>
    /// 救済対象として適格かチェック
    /// </summary>
    private static bool IsEligibleForRescue(TextChunk chunk, out string reason)
    {
        var confidence = chunk.AverageConfidence;
        var text = chunk.CombinedText;

        // 信頼度が範囲外
        if (confidence < MinConfidenceForRescue)
        {
            reason = $"信頼度 {confidence:F3} が最低閾値 {MinConfidenceForRescue} 未満";
            return false;
        }

        if (confidence >= MaxConfidenceForRescue)
        {
            reason = $"信頼度 {confidence:F3} が上限 {MaxConfidenceForRescue} 以上（通常検証対象）";
            return false;
        }

        // テキストが短すぎる
        if (string.IsNullOrEmpty(text) || text.Length < MinTextLengthForRescue)
        {
            reason = $"テキスト長 {text?.Length ?? 0} が最低 {MinTextLengthForRescue} 未満";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    /// <summary>
    /// 救済のための最良マッチを検索
    /// </summary>
    private (float Similarity, string MatchedText) FindBestRescueMatch(
        string localText,
        IReadOnlyList<string> cloudTexts)
    {
        var bestSimilarity = 0f;
        var bestText = string.Empty;

        foreach (var cloudText in cloudTexts)
        {
            if (string.IsNullOrEmpty(cloudText))
            {
                continue;
            }

            var similarity = _fuzzyMatcher.CalculateSimilarity(localText, cloudText);

            if (similarity > bestSimilarity)
            {
                bestSimilarity = similarity;
                bestText = cloudText;
            }
        }

        return (bestSimilarity, bestText);
    }
}
