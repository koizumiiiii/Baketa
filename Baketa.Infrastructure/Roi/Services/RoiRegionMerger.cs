using System.Drawing;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Roi.Services;

/// <summary>
/// [Issue #293] 隣接ROI領域を結合するサービス
/// Union-Findアルゴリズムで効率的に連結成分を抽出
/// </summary>
public interface IRoiRegionMerger
{
    /// <summary>
    /// 隣接する領域を結合し、統合されたバウンディングボックスを返す
    /// </summary>
    /// <param name="regions">変化が検出された領域の配列</param>
    /// <returns>結合後の領域リスト</returns>
    List<Rectangle> MergeAdjacentRegions(Rectangle[] regions);
}

/// <summary>
/// [Issue #293] Union-Findによる隣接領域結合サービス実装
/// </summary>
/// <remarks>
/// 計算量: O(n² × α(n)) ≈ O(n²) where n = 領域数
/// Union-Findのα(n)は実質的に定数（アッカーマン関数の逆関数）
/// 領域数が少ない（通常 &lt; 20）ため、O(n²)でも十分高速
/// </remarks>
public sealed class RoiRegionMerger : IRoiRegionMerger
{
    private readonly ILogger<RoiRegionMerger> _logger;
    private readonly int _adjacencyMargin;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="settings">ROI Manager設定（オプショナル）</param>
    /// <remarks>
    /// [コードレビュー対応] AdjacencyMarginを設定ファイルから取得可能に
    /// </remarks>
    public RoiRegionMerger(ILogger<RoiRegionMerger> logger, IOptions<RoiManagerSettings>? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _adjacencyMargin = settings?.Value.AdjacencyMargin ?? 5; // デフォルト: 5ピクセル
    }

    /// <inheritdoc />
    public List<Rectangle> MergeAdjacentRegions(Rectangle[] regions)
    {
        if (regions == null || regions.Length == 0)
        {
            return [];
        }

        if (regions.Length == 1)
        {
            return [regions[0]];
        }

        _logger.LogDebug("[Issue #293] 隣接領域結合開始: 入力領域数={Count}", regions.Length);

        // Union-Find初期化
        var parent = new int[regions.Length];
        var rank = new int[regions.Length];
        for (int i = 0; i < regions.Length; i++)
        {
            parent[i] = i;
            rank[i] = 0;
        }

        // 隣接判定とUnion
        for (int i = 0; i < regions.Length; i++)
        {
            for (int j = i + 1; j < regions.Length; j++)
            {
                if (AreAdjacent(regions[i], regions[j]))
                {
                    Union(parent, rank, i, j);
                }
            }
        }

        // 連結成分ごとにグループ化
        var groups = new Dictionary<int, List<Rectangle>>();
        for (int i = 0; i < regions.Length; i++)
        {
            var root = Find(parent, i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = [];
                groups[root] = list;
            }
            list.Add(regions[i]);
        }

        // 各グループのバウンディングボックスを計算
        var mergedRegions = new List<Rectangle>();
        foreach (var group in groups.Values)
        {
            var bbox = CalculateBoundingBox(group);
            mergedRegions.Add(bbox);
        }

        _logger.LogDebug("[Issue #293] 隣接領域結合完了: {Input}領域 → {Output}領域",
            regions.Length, mergedRegions.Count);

        return mergedRegions;
    }

    /// <summary>
    /// 2つの領域が隣接しているかどうかを判定
    /// </summary>
    /// <remarks>
    /// マージンを考慮して一方の矩形を拡張し、交差判定を行います。
    /// </remarks>
    private bool AreAdjacent(Rectangle a, Rectangle b)
    {
        // マージンを考慮して拡張した矩形で交差判定
        var expandedA = new Rectangle(
            a.X - _adjacencyMargin,
            a.Y - _adjacencyMargin,
            a.Width + _adjacencyMargin * 2,
            a.Height + _adjacencyMargin * 2);

        return expandedA.IntersectsWith(b);
    }

    /// <summary>
    /// Union-Find: Find操作（パス圧縮付き）
    /// </summary>
    private static int Find(int[] parent, int i)
    {
        if (parent[i] != i)
        {
            parent[i] = Find(parent, parent[i]); // パス圧縮
        }
        return parent[i];
    }

    /// <summary>
    /// Union-Find: Union操作（ランクによる結合）
    /// </summary>
    private static void Union(int[] parent, int[] rank, int x, int y)
    {
        var rootX = Find(parent, x);
        var rootY = Find(parent, y);

        if (rootX == rootY) return;

        // ランクが低い木を高い木の下に結合
        if (rank[rootX] < rank[rootY])
        {
            parent[rootX] = rootY;
        }
        else if (rank[rootX] > rank[rootY])
        {
            parent[rootY] = rootX;
        }
        else
        {
            parent[rootY] = rootX;
            rank[rootX]++;
        }
    }

    /// <summary>
    /// 複数の領域を包含するバウンディングボックスを計算
    /// </summary>
    private static Rectangle CalculateBoundingBox(List<Rectangle> regions)
    {
        if (regions.Count == 0) return Rectangle.Empty;
        if (regions.Count == 1) return regions[0];

        var minX = regions.Min(r => r.X);
        var minY = regions.Min(r => r.Y);
        var maxX = regions.Max(r => r.Right);
        var maxY = regions.Max(r => r.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }
}
