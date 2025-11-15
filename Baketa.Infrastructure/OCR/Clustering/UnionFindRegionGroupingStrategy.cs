using System;
using System.Collections.Generic;
using Baketa.Core.Abstractions.OCR;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Clustering;

/// <summary>
/// Union-Findアルゴリズムを使用したOCRテキスト領域グルーピング戦略
/// </summary>
/// <remarks>
/// Phase 3.4A: Gemini推奨のClean Architecture改善
/// - 既存の「processedRegions制約」問題を完全解決
/// - 数学的に正確な連結成分検出（グラフ理論）
/// - 任意段階の連鎖グルーピングに対応
///
/// 計算量:
/// - 時間: O(N² α(N))（全ペアの距離計算 + Union-Find操作）
/// - 空間: O(N)
///
/// 期待効果:
/// - 3チャンク → 1グループ統合（deltaY=113.5px < verticalThreshold=166.86px のケース）
/// - 座標ズレ解消（1回翻訳 → 1回オーバーレイ表示）
/// </remarks>
public sealed class UnionFindRegionGroupingStrategy : IRegionGroupingStrategy
{
    private readonly ILogger<UnionFindRegionGroupingStrategy> _logger;

    public UnionFindRegionGroupingStrategy(ILogger<UnionFindRegionGroupingStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public List<List<OcrTextRegion>> GroupRegions(
        IReadOnlyList<OcrTextRegion> regions,
        BatchOcrOptions options)
    {
        if (regions == null || regions.Count == 0)
            return [];

        if (regions.Count == 1)
            return [[regions[0]]];

        // Union-Findデータ構造初期化
        var uf = new UnionFind(regions.Count);

        // 閾値計算（既存ロジックを踏襲）
        var verticalThreshold = options.ChunkGroupingDistance * 3.0;   // 垂直方向を大幅拡張
        var horizontalThreshold = options.ChunkGroupingDistance * 2.0; // 水平方向も拡張
        var extendedVerticalThreshold = verticalThreshold * 1.5;       // 段落内の遠い行も検出

        _logger.LogDebug("🔍 [UNION_FIND] グルーピング開始 - 領域数: {RegionCount}, 垂直閾値: {VerticalThreshold:F2}px, 水平閾値: {HorizontalThreshold:F2}px",
            regions.Count, verticalThreshold, horizontalThreshold);

        // 全ペアの距離を計算し、近接する領域を結合
        int unionCount = 0;
        for (int i = 0; i < regions.Count; i++)
        {
            for (int j = i + 1; j < regions.Count; j++)
            {
                if (AreRegionsClose(regions[i], regions[j], verticalThreshold, horizontalThreshold, extendedVerticalThreshold))
                {
                    if (uf.Union(i, j))
                    {
                        unionCount++;
                        _logger.LogTrace("🔗 [UNION_FIND] 領域結合: Region[{I}] ↔ Region[{J}]", i, j);
                    }
                }
            }
        }

        // 連結成分を取得
        var components = uf.GetConnectedComponents();

        // グループ化結果を構築
        var result = new List<List<OcrTextRegion>>();
        foreach (var component in components.Values)
        {
            var group = new List<OcrTextRegion>();
            foreach (var index in component)
            {
                group.Add(regions[index]);
            }
            result.Add(group);
        }

        _logger.LogInformation("✅ [UNION_FIND] グルーピング完了 - 入力: {InputCount}領域, 出力: {OutputCount}グループ, 結合回数: {UnionCount}",
            regions.Count, result.Count, unionCount);

        return result;
    }

    /// <summary>
    /// 2つの領域が近接しているかを判定
    /// 既存のFindNearbyRegions()ロジックを簡素化・統合
    /// </summary>
    private static bool AreRegionsClose(
        OcrTextRegion region1,
        OcrTextRegion region2,
        double verticalThreshold,
        double horizontalThreshold,
        double extendedVerticalThreshold)
    {
        // 中心点間の距離計算
        var centerX1 = region1.Bounds.X + region1.Bounds.Width / 2.0;
        var centerY1 = region1.Bounds.Y + region1.Bounds.Height / 2.0;
        var centerX2 = region2.Bounds.X + region2.Bounds.Width / 2.0;
        var centerY2 = region2.Bounds.Y + region2.Bounds.Height / 2.0;

        var deltaX = Math.Abs(centerX2 - centerX1);
        var deltaY = Math.Abs(centerY2 - centerY1);

        // 条件1: 水平方向に近い（同じ行）
        if (deltaY <= region1.Bounds.Height * 1.0 && deltaX <= horizontalThreshold)
            return true;

        // 条件2: 垂直方向に近い（次の行/折り返し）
        if (IsTextWrappedOrNextLine(region1, region2, deltaY, verticalThreshold))
            return true;

        // 条件3: 段落内の遠い行
        if (IsParagraphText(region1, region2, deltaY, extendedVerticalThreshold))
            return true;

        return false;
    }

    /// <summary>
    /// テキストが折り返しまたは次の行かどうかを判定（簡素化版）
    /// </summary>
    private static bool IsTextWrappedOrNextLine(OcrTextRegion region1, OcrTextRegion region2, double deltaY, double verticalThreshold)
    {
        if (deltaY > verticalThreshold)
            return false;

        var left1 = region1.Bounds.Left;
        var right1 = region1.Bounds.Right;
        var left2 = region2.Bounds.Left;
        var right2 = region2.Bounds.Right;

        // 水平方向のオーバーラップまたは近接判定
        var horizontalOverlap = Math.Max(0, Math.Min(right1, right2) - Math.Max(left1, left2));
        var horizontalDistance = Math.Max(0, Math.Max(left2 - right1, left1 - right2));
        var maxWidth = Math.Max(region1.Bounds.Width, region2.Bounds.Width);

        // 垂直方向に近い かつ 水平方向で関連
        var isVerticallyClose = deltaY <= Math.Max(region1.Bounds.Height, region2.Bounds.Height) * 2.5;
        var isHorizontallyRelated = horizontalOverlap > 0 || horizontalDistance <= maxWidth * 0.8;

        return isVerticallyClose && isHorizontallyRelated;
    }

    /// <summary>
    /// 同一段落内のテキストかどうかを判定（簡素化版）
    /// </summary>
    private static bool IsParagraphText(OcrTextRegion region1, OcrTextRegion region2, double deltaY, double extendedVerticalThreshold)
    {
        if (deltaY > extendedVerticalThreshold)
            return false;

        var left1 = region1.Bounds.Left;
        var right1 = region1.Bounds.Right;
        var left2 = region2.Bounds.Left;
        var right2 = region2.Bounds.Right;

        // 水平方向での重複または近接
        var horizontalOverlap = Math.Max(0, Math.Min(right1, right2) - Math.Max(left1, left2));
        var paragraphWidth = Math.Max(region1.Bounds.Width, region2.Bounds.Width) * 2;
        var isInSameParagraphHorizontally = horizontalOverlap > 0 || Math.Abs(left1 - left2) <= paragraphWidth * 0.5;

        // 垂直方向での段落内距離
        var maxHeight = Math.Max(region1.Bounds.Height, region2.Bounds.Height);
        var isInSameParagraphVertically = deltaY <= maxHeight * 4.0;

        return isInSameParagraphHorizontally && isInSameParagraphVertically;
    }
}
