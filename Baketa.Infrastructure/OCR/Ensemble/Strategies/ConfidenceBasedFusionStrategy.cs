using System.Diagnostics;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Ensemble.Strategies;

/// <summary>
/// 信頼度ベースの結果融合戦略
/// </summary>
public class ConfidenceBasedFusionStrategy(ILogger<ConfidenceBasedFusionStrategy> logger) : ResultFusionStrategyBase
{
    public override string StrategyName => "ConfidenceBased";
    public override string Description => "各エンジンの信頼度に基づいて最適な結果を選択";

    public override async Task<EnsembleOcrResults> FuseResultsAsync(
        IReadOnlyList<IndividualEngineResult> individualResults,
        FusionParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("信頼度ベース融合開始: {EngineCount}エンジン", individualResults.Count);

        try
        {
            // 成功した結果のみを対象とする
            var successfulResults = individualResults.Where(r => r.IsSuccessful).ToList();
            if (successfulResults.Count == 0)
            {
                return CreateEmptyResult(individualResults, sw.Elapsed);
            }

            // エンジンごとの信頼度スコアを計算
            var engineScores = CalculateEngineConfidenceScores(successfulResults);
            logger.LogDebug("エンジン信頼度スコア: {Scores}",
                string.Join(", ", engineScores.Select(kvp => $"{kvp.Key}={kvp.Value:F3}")));

            // 信頼度ベースの融合を実行
            var fusedRegions = await PerformConfidenceBasedFusionAsync(successfulResults, engineScores, parameters).ConfigureAwait(false);

            // 融合詳細情報を作成
            var fusionDetails = CreateFusionDetails(successfulResults, fusedRegions, engineScores);

            var ensembleResults = new EnsembleOcrResults(
                fusedRegions,
                individualResults.Count > 0 ? individualResults[0].Results.SourceImage : throw new InvalidOperationException("No source image available"),
                sw.Elapsed,
                individualResults.Count > 0 ? individualResults[0].Results.LanguageCode ?? "unknown" : "unknown")
            {
                IndividualResults = individualResults,
                FusionDetails = fusionDetails,
                FusionStrategy = StrategyName,
                EnsembleConfidence = CalculateEnsembleConfidence(fusedRegions),
                EnsembleProcessingTime = sw.Elapsed
            };

            logger.LogInformation(
                "信頼度ベース融合完了: {FinalRegions}領域, 融合時間={ElapsedMs}ms",
                fusedRegions.Count, sw.ElapsedMilliseconds);

            return ensembleResults;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "信頼度ベース融合中にエラーが発生しました");
            return CreateEmptyResult(individualResults, sw.Elapsed);
        }
    }

    /// <summary>
    /// エンジンごとの信頼度スコアを計算
    /// </summary>
    private Dictionary<string, double> CalculateEngineConfidenceScores(
        IReadOnlyList<IndividualEngineResult> results)
    {
        Dictionary<string, double> engineScores = [];

        foreach (var result in results)
        {
            if (result.Results.TextRegions.Count == 0)
            {
                engineScores[result.EngineName] = 0.0;
                continue;
            }

            // 平均信頼度とエンジン重みを組み合わせ
            var averageConfidence = result.Results.TextRegions.Average(r => r.Confidence);
            var weightedScore = averageConfidence * result.Weight;

            // 領域数による補正（多すぎても少なすぎても減点）
            var regionCountFactor = CalculateRegionCountFactor(result.Results.TextRegions.Count);

            engineScores[result.EngineName] = weightedScore * regionCountFactor;
        }

        return engineScores;
    }

    /// <summary>
    /// 領域数による補正係数を計算
    /// </summary>
    private double CalculateRegionCountFactor(int regionCount)
    {
        // 最適な領域数を5-15と仮定
        if (regionCount >= 5 && regionCount <= 15)
            return 1.0;

        if (regionCount < 5)
            return 0.8 + (regionCount / 5.0) * 0.2; // 少ない場合は減点

        // 多すぎる場合も減点（ノイズの可能性）
        return Math.Max(0.5, 1.0 - (regionCount - 15) * 0.02);
    }

    /// <summary>
    /// 信頼度ベースの融合を実行
    /// </summary>
    private async Task<List<OcrTextRegion>> PerformConfidenceBasedFusionAsync(
        IReadOnlyList<IndividualEngineResult> results,
        Dictionary<string, double> engineScores,
        FusionParameters parameters)
    {
        List<OcrTextRegion> fusedRegions = [];

        // 最も信頼度の高いエンジンを特定
        var sortedEngines = engineScores.OrderByDescending(kvp => kvp.Value).ToList();
        var bestEngine = sortedEngines[0].Key;
        var bestResult = results.First(r => r.EngineName == bestEngine);

        logger.LogDebug("最高信頼度エンジン: {EngineName} (スコア: {Score:F3})",
            bestEngine, engineScores[bestEngine]);

        // 基準となる領域を取得
        var baseRegions = bestResult.Results.TextRegions
            .Where(r => r.Confidence >= parameters.MinimumConfidenceThreshold)
            .ToList();

        foreach (var baseRegion in baseRegions)
        {
            // 他のエンジンから対応する領域を探索
            var correspondingRegions = await FindCorrespondingRegionsAsync(
                baseRegion, results, bestEngine, parameters).ConfigureAwait(false);

            // 信頼度ベースで最適な領域を選択
            var selectedRegion = SelectBestRegionByConfidence(
                baseRegion, correspondingRegions, engineScores, parameters);

            if (selectedRegion != null)
            {
                fusedRegions.Add(selectedRegion);
            }
        }

        // 他のエンジンからの高信頼度領域も追加検討
        await AddSupplementaryRegionsAsync(fusedRegions, results, engineScores, parameters).ConfigureAwait(false);

        return fusedRegions;
    }

    /// <summary>
    /// 対応する領域を他のエンジンから探索
    /// </summary>
    private Task<List<CorrespondingRegion>> FindCorrespondingRegionsAsync(
        OcrTextRegion baseRegion,
        IReadOnlyList<IndividualEngineResult> results,
        string baseEngine,
        FusionParameters parameters)
    {
        List<CorrespondingRegion> correspondingRegions = [];

        foreach (var result in results.Where(r => r.EngineName != baseEngine))
        {
            foreach (var region in result.Results.TextRegions)
            {
                var similarity = CalculateRegionSimilarity(baseRegion, region);

                if (similarity.OverallSimilarity >= parameters.SimilarityThreshold)
                {
                    correspondingRegions.Add(new CorrespondingRegion(
                        region, result.EngineName, similarity.OverallSimilarity));
                }
            }
        }

        return Task.FromResult(correspondingRegions);
    }

    /// <summary>
    /// 信頼度ベースで最適な領域を選択
    /// </summary>
    private OcrTextRegion? SelectBestRegionByConfidence(
        OcrTextRegion baseRegion,
        List<CorrespondingRegion> correspondingRegions,
        Dictionary<string, double> engineScores,
        FusionParameters parameters)
    {
        List<CandidateSelection> candidates =
        [
            new(baseRegion, "base", 1.0, baseRegion.Confidence)
        ];

        // 対応領域を候補に追加
        foreach (var corrRegion in correspondingRegions)
        {
            var engineScore = engineScores.GetValueOrDefault(corrRegion.EngineName, 0.0);
            var adjustedConfidence = corrRegion.Region.Confidence * corrRegion.Similarity * engineScore;

            candidates.Add(new CandidateSelection(
                corrRegion.Region, corrRegion.EngineName, corrRegion.Similarity, adjustedConfidence));
        }

        // 最も調整信頼度の高い候補を選択
        var sortedCandidates = candidates.OrderByDescending(c => c.AdjustedConfidence).ToList();
        var bestCandidate = sortedCandidates[0];

        return bestCandidate.AdjustedConfidence >= parameters.MinimumConfidenceThreshold
            ? bestCandidate.Region
            : null;
    }

    /// <summary>
    /// 補完的な高信頼度領域を追加
    /// </summary>
    private Task AddSupplementaryRegionsAsync(
        List<OcrTextRegion> fusedRegions,
        IReadOnlyList<IndividualEngineResult> results,
        Dictionary<string, double> engineScores,
        FusionParameters parameters)
    {
        foreach (var result in results)
        {
            var engineScore = engineScores.GetValueOrDefault(result.EngineName, 0.0);

            foreach (var region in result.Results.TextRegions)
            {
                var adjustedConfidence = region.Confidence * engineScore;

                // 高信頼度で既存領域と重複しない場合のみ追加
                if (adjustedConfidence >= parameters.ConflictResolutionThreshold &&
                    !HasSignificantOverlap(region, fusedRegions, parameters.OverlapThreshold))
                {
                    fusedRegions.Add(region);
                }
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 既存領域との重複をチェック
    /// </summary>
    private bool HasSignificantOverlap(OcrTextRegion newRegion, List<OcrTextRegion> existingRegions, double threshold)
    {
        return existingRegions.Any(existing =>
            CalculateLocationSimilarity(newRegion.Bounds, existing.Bounds) > threshold);
    }

    /// <summary>
    /// 融合詳細情報を作成
    /// </summary>
    private ResultFusionDetails CreateFusionDetails(
        IReadOnlyList<IndividualEngineResult> results,
        List<OcrTextRegion> fusedRegions,
        Dictionary<string, double> engineScores)
    {
        var totalCandidates = results.Sum(r => r.Results.TextRegions.Count);
        List<RegionFusionDetail> regionDetails = [];

        for (int i = 0; i < fusedRegions.Count; i++)
        {
            var region = fusedRegions[i];
            var sourceEngine = FindSourceEngine(region, results);

            var detail = new RegionFusionDetail(
                i,
                [sourceEngine],
                region.Confidence,
                region.Text,
                FusionDecisionType.ConfidenceBased,
                $"信頼度ベース選択 (エンジンスコア: {engineScores.GetValueOrDefault(sourceEngine, 0):F3})");

            regionDetails.Add(detail);
        }

        return new ResultFusionDetails(
            totalCandidates,
            fusedRegions.Count,
            0, // 信頼度ベースでは合意という概念は適用しない
            0,
            1.0, // 常に最適解を選択
            regionDetails);
    }

    /// <summary>
    /// 領域のソースエンジンを特定
    /// </summary>
    private string FindSourceEngine(OcrTextRegion region, IReadOnlyList<IndividualEngineResult> results)
    {
        foreach (var result in results)
        {
            if (result.Results.TextRegions.Contains(region))
                return result.EngineName;
        }
        return "Unknown";
    }

    /// <summary>
    /// アンサンブル信頼度を計算
    /// </summary>
    private double CalculateEnsembleConfidence(List<OcrTextRegion> regions)
    {
        if (regions.Count == 0) return 0.0;
        return regions.Average(r => r.Confidence);
    }

    /// <summary>
    /// 空の結果を作成
    /// </summary>
    private EnsembleOcrResults CreateEmptyResult(
        IReadOnlyList<IndividualEngineResult> individualResults,
        TimeSpan processingTime)
    {
        return new EnsembleOcrResults(
            [],
            individualResults.Count > 0 ? individualResults[0].Results.SourceImage : throw new InvalidOperationException("No source image available"),
            processingTime,
            individualResults.Count > 0 ? individualResults[0].Results.LanguageCode ?? "unknown" : "unknown")
        {
            IndividualResults = individualResults,
            FusionDetails = new ResultFusionDetails(0, 0, 0, 0, 0, []),
            FusionStrategy = StrategyName,
            EnsembleConfidence = 0.0,
            EnsembleProcessingTime = processingTime
        };
    }
}

/// <summary>
/// 対応領域情報
/// </summary>
internal sealed record CorrespondingRegion(
    OcrTextRegion Region,
    string EngineName,
    double Similarity);

/// <summary>
/// 候補選択情報
/// </summary>
internal sealed record CandidateSelection(
    OcrTextRegion Region,
    string EngineName,
    double Similarity,
    double AdjustedConfidence);
