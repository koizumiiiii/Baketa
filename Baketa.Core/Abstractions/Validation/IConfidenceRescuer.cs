using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.Validation;

/// <summary>
/// 低信頼度テキストの救済インターフェース
/// Cloud AI結果との一致により、通常閾値未満のテキストを救済
/// </summary>
/// <remarks>
/// Issue #78 Phase 3: 低信頼度テキスト救済ロジック
/// 救済条件:
/// - 信頼度 0.30-0.70
/// - Cloud AIと類似度 80%以上
/// - テキスト長 3文字以上
/// </remarks>
public interface IConfidenceRescuer
{
    /// <summary>
    /// 低信頼度テキストの救済を試行
    /// </summary>
    /// <param name="chunk">低信頼度のテキストチャンク</param>
    /// <param name="cloudDetectedText">Cloud AIが検出したテキスト</param>
    /// <returns>救済結果</returns>
    RescueResult TryRescue(TextChunk chunk, string cloudDetectedText);

    /// <summary>
    /// 複数のCloud AI検出テキストから救済を試行
    /// </summary>
    /// <param name="chunk">低信頼度のテキストチャンク</param>
    /// <param name="cloudDetectedTexts">Cloud AIが検出したテキストのリスト</param>
    /// <returns>救済結果</returns>
    RescueResult TryRescue(TextChunk chunk, IReadOnlyList<string> cloudDetectedTexts);
}

/// <summary>
/// 救済結果
/// </summary>
public sealed record RescueResult
{
    /// <summary>
    /// 救済されたか
    /// </summary>
    public required bool IsRescued { get; init; }

    /// <summary>
    /// マッチング類似度（救済成功時）
    /// </summary>
    public required float MatchSimilarity { get; init; }

    /// <summary>
    /// マッチしたCloud AIテキスト（救済成功時）
    /// </summary>
    public string? MatchedCloudText { get; init; }

    /// <summary>
    /// 救済理由または失敗理由
    /// </summary>
    public string? Reason { get; init; }

    /// <summary>
    /// 救済成功の結果を作成
    /// </summary>
    public static RescueResult Rescued(float similarity, string matchedText, string reason)
        => new()
        {
            IsRescued = true,
            MatchSimilarity = similarity,
            MatchedCloudText = matchedText,
            Reason = reason
        };

    /// <summary>
    /// 救済失敗の結果を作成
    /// </summary>
    public static RescueResult NotRescued(string reason)
        => new()
        {
            IsRescued = false,
            MatchSimilarity = 0f,
            MatchedCloudText = null,
            Reason = reason
        };
}
