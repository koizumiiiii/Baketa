using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.Ensemble;

/// <summary>
/// 複数OCRエンジンの結果を融合する戦略のインターフェース
/// </summary>
public interface IResultFusionStrategy
{
    /// <summary>
    /// 戦略名
    /// </summary>
    string StrategyName { get; }

    /// <summary>
    /// 戦略の説明
    /// </summary>
    string Description { get; }

    /// <summary>
    /// 複数の認識結果を融合して最終結果を生成
    /// </summary>
    Task<EnsembleOcrResults> FuseResultsAsync(
        IReadOnlyList<IndividualEngineResult> individualResults,
        FusionParameters parameters,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 戦略が指定されたエンジン構成に適用可能かチェック
    /// </summary>
    bool IsApplicable(IReadOnlyList<EnsembleEngineInfo> engines);

    /// <summary>
    /// 戦略の推奨パラメータを取得
    /// </summary>
    FusionParameters GetRecommendedParameters(IReadOnlyList<EnsembleEngineInfo> engines);
}

/// <summary>
/// 結果融合パラメータ
/// </summary>
public record FusionParameters(
    double MinimumConfidenceThreshold = 0.3,
    double SimilarityThreshold = 0.7,
    double OverlapThreshold = 0.5,
    int MinimumAgreementCount = 2,
    bool UseWeightedVoting = true,
    bool ApplyConflictResolution = true,
    bool FilterLowConfidenceResults = true,
    double ConflictResolutionThreshold = 0.8,
    Dictionary<string, object>? StrategySpecificParameters = null);

/// <summary>
/// テキスト領域の類似度情報
/// </summary>
public record RegionSimilarity(
    OcrTextRegion Region1,
    OcrTextRegion Region2,
    double LocationSimilarity,
    double TextSimilarity,
    double SizeSimilarity,
    double OverallSimilarity);

/// <summary>
/// 融合候補領域
/// </summary>
public record FusionCandidate(
    IReadOnlyList<OcrTextRegion> SourceRegions,
    IReadOnlyList<string> SourceEngines,
    IReadOnlyList<double> SourceWeights,
    double AggregatedConfidence,
    string ConsensusText,
    System.Drawing.Rectangle ConsensusBounds);

/// <summary>
/// 基本的な結果融合戦略の抽象基底クラス
/// </summary>
public abstract class ResultFusionStrategyBase : IResultFusionStrategy
{
    public abstract string StrategyName { get; }
    public abstract string Description { get; }

    public abstract Task<EnsembleOcrResults> FuseResultsAsync(
        IReadOnlyList<IndividualEngineResult> individualResults,
        FusionParameters parameters,
        CancellationToken cancellationToken = default);

    public virtual bool IsApplicable(IReadOnlyList<EnsembleEngineInfo> engines)
    {
        return engines.Count >= 2 && engines.Any(e => e.IsEnabled);
    }

    public virtual FusionParameters GetRecommendedParameters(IReadOnlyList<EnsembleEngineInfo> engines)
    {
        return new FusionParameters();
    }

    /// <summary>
    /// テキスト領域間の類似度を計算
    /// </summary>
    protected virtual RegionSimilarity CalculateRegionSimilarity(OcrTextRegion region1, OcrTextRegion region2)
    {
        var locationSim = CalculateLocationSimilarity(region1.Bounds, region2.Bounds);
        var textSim = CalculateTextSimilarity(region1.Text, region2.Text);
        var sizeSim = CalculateSizeSimilarity(region1.Bounds, region2.Bounds);
        var overallSim = (locationSim * 0.4 + textSim * 0.4 + sizeSim * 0.2);

        return new RegionSimilarity(region1, region2, locationSim, textSim, sizeSim, overallSim);
    }

    /// <summary>
    /// 位置の類似度を計算
    /// </summary>
    protected virtual double CalculateLocationSimilarity(System.Drawing.Rectangle bounds1, System.Drawing.Rectangle bounds2)
    {
        var intersection = System.Drawing.Rectangle.Intersect(bounds1, bounds2);
        var union = System.Drawing.Rectangle.Union(bounds1, bounds2);

        if (union.Width == 0 || union.Height == 0) return 0.0;

        return (double)(intersection.Width * intersection.Height) / (union.Width * union.Height);
    }

    /// <summary>
    /// テキストの類似度を計算（Levenshtein距離ベース）
    /// </summary>
    protected virtual double CalculateTextSimilarity(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) && string.IsNullOrEmpty(text2)) return 1.0;
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return 0.0;

        var distance = CalculateLevenshteinDistance(text1, text2);
        var maxLength = Math.Max(text1.Length, text2.Length);

        return 1.0 - (double)distance / maxLength;
    }

    /// <summary>
    /// サイズの類似度を計算
    /// </summary>
    protected virtual double CalculateSizeSimilarity(System.Drawing.Rectangle bounds1, System.Drawing.Rectangle bounds2)
    {
        var area1 = bounds1.Width * bounds1.Height;
        var area2 = bounds2.Width * bounds2.Height;

        if (area1 == 0 && area2 == 0) return 1.0;
        if (area1 == 0 || area2 == 0) return 0.0;

        var ratio = (double)Math.Min(area1, area2) / Math.Max(area1, area2);
        return ratio;
    }

    /// <summary>
    /// Levenshtein距離を計算
    /// </summary>
    protected virtual int CalculateLevenshteinDistance(string s1, string s2)
    {
        var matrix = new int[s1.Length + 1, s2.Length + 1];

        for (int i = 0; i <= s1.Length; i++)
            matrix[i, 0] = i;

        for (int j = 0; j <= s2.Length; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= s1.Length; i++)
        {
            for (int j = 1; j <= s2.Length; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[s1.Length, s2.Length];
    }

    /// <summary>
    /// 複数のテキスト領域から重み付き平均境界を計算
    /// </summary>
    protected virtual System.Drawing.Rectangle CalculateWeightedBounds(
        IReadOnlyList<OcrTextRegion> regions,
        IReadOnlyList<double> weights)
    {
        if (regions.Count == 0) return new System.Drawing.Rectangle();

        double totalWeight = weights.Sum();
        if (totalWeight == 0) return regions[0].Bounds;

        double weightedX = 0, weightedY = 0, weightedRight = 0, weightedBottom = 0;

        for (int i = 0; i < regions.Count; i++)
        {
            var weight = weights[i] / totalWeight;
            var bounds = regions[i].Bounds;

            weightedX += bounds.X * weight;
            weightedY += bounds.Y * weight;
            weightedRight += (bounds.X + bounds.Width) * weight;
            weightedBottom += (bounds.Y + bounds.Height) * weight;
        }

        return new System.Drawing.Rectangle(
            (int)Math.Round(weightedX),
            (int)Math.Round(weightedY),
            (int)Math.Round(weightedRight - weightedX),
            (int)Math.Round(weightedBottom - weightedY));
    }

    /// <summary>
    /// 複数のテキストから重み付き合意テキストを生成
    /// </summary>
    protected virtual string GenerateConsensusText(
        IReadOnlyList<string> texts,
        IReadOnlyList<double> weights)
    {
        if (texts.Count == 0) return "";
        if (texts.Count == 1) return texts[0];

        // 最も重みの大きいテキストを基準とする
        var maxWeightIndex = weights.ToList().IndexOf(weights.Max());
        return texts[maxWeightIndex];
    }
}
