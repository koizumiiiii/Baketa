using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.OCR;
using System.Diagnostics;

namespace Baketa.Infrastructure.OCR.Ensemble.Strategies;

/// <summary>
/// 重み付き投票による結果融合戦略
/// </summary>
public class WeightedVotingFusionStrategy(ILogger<WeightedVotingFusionStrategy> logger) : ResultFusionStrategyBase
{
    public override string StrategyName => "WeightedVoting";
    public override string Description => "重み付き投票により複数エンジンの結果を融合";

    public override async Task<EnsembleOcrResults> FuseResultsAsync(
        IReadOnlyList<IndividualEngineResult> individualResults,
        FusionParameters parameters,
        CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("重み付き投票融合開始: {EngineCount}エンジン", individualResults.Count);

        try
        {
            // 成功した結果のみを対象とする
            var successfulResults = individualResults.Where(r => r.IsSuccessful).ToList();
            if (successfulResults.Count == 0)
            {
                return CreateEmptyResult(individualResults, sw.Elapsed);
            }

            // 全ての候補領域を収集
            var allRegions = CollectAllRegions(successfulResults);
            logger.LogDebug("候補領域数: {RegionCount}", allRegions.Count);

            // 類似領域をグループ化
            var regionGroups = GroupSimilarRegions(allRegions, parameters);
            logger.LogDebug("領域グループ数: {GroupCount}", regionGroups.Count);

            // 各グループで重み付き投票を実行
            var fusedRegions = await ProcessRegionGroupsAsync(regionGroups, parameters).ConfigureAwait(false);
            logger.LogDebug("融合後領域数: {FinalCount}", fusedRegions.Count);

            // 融合詳細情報を作成
            var fusionDetails = CreateFusionDetails(regionGroups, fusedRegions);

            // 最終結果を構築
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
                "重み付き投票融合完了: {FinalRegions}領域, 融合時間={ElapsedMs}ms",
                fusedRegions.Count, sw.ElapsedMilliseconds);

            return ensembleResults;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "重み付き投票融合中にエラーが発生しました");
            return CreateEmptyResult(individualResults, sw.Elapsed);
        }
    }

    /// <summary>
    /// 全ての候補領域を収集
    /// </summary>
    private List<CandidateRegion> CollectAllRegions(IReadOnlyList<IndividualEngineResult> results)
    {
        List<CandidateRegion> candidates = [];

        foreach (var result in results)
        {
            foreach (var region in result.Results.TextRegions)
            {
                candidates.Add(new CandidateRegion(
                    region,
                    result.EngineName,
                    result.Weight,
                    result.Role));
            }
        }

        return candidates;
    }

    /// <summary>
    /// 類似領域をグループ化
    /// </summary>
    private List<RegionGroup> GroupSimilarRegions(
        List<CandidateRegion> allRegions,
        FusionParameters parameters)
    {
        List<RegionGroup> groups = [];
        HashSet<CandidateRegion> processed = [];

        foreach (var region in allRegions)
        {
            if (processed.Contains(region)) continue;

            var group = new RegionGroup { Regions = [region] };
            processed.Add(region);

            // 類似領域を探してグループに追加
            foreach (var otherRegion in allRegions)
            {
                if (processed.Contains(otherRegion)) continue;

                var similarity = CalculateRegionSimilarity(region.Region, otherRegion.Region);
                if (similarity.OverallSimilarity >= parameters.SimilarityThreshold)
                {
                    group.Regions.Add(otherRegion);
                    processed.Add(otherRegion);
                }
            }

            groups.Add(group);
        }

        return groups;
    }

    /// <summary>
    /// 領域グループを処理して融合領域を生成
    /// </summary>
    private async Task<List<OcrTextRegion>> ProcessRegionGroupsAsync(
        List<RegionGroup> regionGroups,
        FusionParameters parameters)
    {
        List<OcrTextRegion> fusedRegions = [];

        foreach (var group in regionGroups)
        {
            // 最小合意数のチェック
            if (group.Regions.Count < parameters.MinimumAgreementCount)
            {
                // 単一エンジンの結果も信頼度によっては採用
                var bestRegion = group.Regions
                    .OrderByDescending(r => r.Region.Confidence * r.Weight)
                    .First();

                if (bestRegion.Region.Confidence >= parameters.MinimumConfidenceThreshold)
                {
                    fusedRegions.Add(bestRegion.Region);
                }
                continue;
            }

            // 重み付き投票で融合
            var fusedRegion = await FuseRegionGroupAsync(group, parameters).ConfigureAwait(false);
            if (fusedRegion != null)
            {
                fusedRegions.Add(fusedRegion);
            }
        }

        return fusedRegions;
    }

    /// <summary>
    /// 領域グループを融合
    /// </summary>
    private Task<OcrTextRegion?> FuseRegionGroupAsync(
        RegionGroup group,
        FusionParameters parameters)
    {
        // 重み付きスコアを計算
        var totalWeight = group.Regions.Sum(r => r.Weight);
        var weightedConfidence = group.Regions.Sum(r => r.Region.Confidence * r.Weight) / totalWeight;

        // 信頼度チェック
        if (weightedConfidence < parameters.MinimumConfidenceThreshold)
        {
            return Task.FromResult<OcrTextRegion?>(null);
        }

        // 重み付き境界を計算
        var regions = group.Regions.Select(r => r.Region).ToList();
        var weights = group.Regions.Select(r => r.Weight).ToList();
        var consensusBounds = CalculateWeightedBounds(regions, weights);

        // 合意テキストを生成
        var texts = group.Regions.Select(r => r.Region.Text).ToList();
        var consensusText = GenerateConsensusText(texts, weights);

        return Task.FromResult<OcrTextRegion?>(new OcrTextRegion(
            consensusText,
            consensusBounds,
            weightedConfidence));
    }

    /// <summary>
    /// 融合詳細情報を作成
    /// </summary>
    private ResultFusionDetails CreateFusionDetails(
        List<RegionGroup> regionGroups,
        List<OcrTextRegion> fusedRegions)
    {
        var totalCandidates = regionGroups.Sum(g => g.Regions.Count);
        var agreedRegions = regionGroups.Count(g => g.Regions.Count > 1);
        var conflictedRegions = regionGroups.Count(g => g.Regions.Count > 2);

        List<RegionFusionDetail> regionDetails = [];
        for (int i = 0; i < fusedRegions.Count; i++)
        {
            var correspondingGroup = regionGroups[i];
            var sourceEngines = correspondingGroup.Regions.Select(r => r.EngineName).ToList();
            
            var detail = new RegionFusionDetail(
                i,
                sourceEngines,
                fusedRegions[i].Confidence,
                fusedRegions[i].Text,
                DetermineFusionDecisionType(correspondingGroup),
                GenerateDecisionReason(correspondingGroup));

            regionDetails.Add(detail);
        }

        return new ResultFusionDetails(
            totalCandidates,
            fusedRegions.Count,
            agreedRegions,
            conflictedRegions,
            agreedRegions > 0 ? (double)agreedRegions / regionGroups.Count : 0,
            regionDetails);
    }

    /// <summary>
    /// 融合決定タイプを判定
    /// </summary>
    private FusionDecisionType DetermineFusionDecisionType(RegionGroup group)
    {
        if (group.Regions.Count == 1)
            return FusionDecisionType.SingleEngine;

        var uniqueTexts = group.Regions.Select(r => r.Region.Text).Distinct().Count();
        if (uniqueTexts == 1)
            return FusionDecisionType.Unanimous;

        if (group.Regions.Count >= 3)
            return FusionDecisionType.Majority;

        return FusionDecisionType.WeightedSelection;
    }

    /// <summary>
    /// 決定理由を生成
    /// </summary>
    private string GenerateDecisionReason(RegionGroup group)
    {
        var engineCount = group.Regions.Count;
        var avgConfidence = group.Regions.Average(r => r.Region.Confidence);
        var totalWeight = group.Regions.Sum(r => r.Weight);

        return $"{engineCount}エンジン合意, 平均信頼度={avgConfidence:F3}, 総重み={totalWeight:F1}";
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
/// 候補領域情報
/// </summary>
internal sealed record CandidateRegion(
    OcrTextRegion Region,
    string EngineName,
    double Weight,
    EnsembleEngineRole Role);

/// <summary>
/// 領域グループ
/// </summary>
internal sealed class RegionGroup
{
    public List<CandidateRegion> Regions { get; set; } = [];
}
