using System.Collections.Generic;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// OCRテキスト領域のグルーピング戦略を定義するインターフェース
/// Strategy Patternにより、異なるグルーピングアルゴリズム（Union-Find, 階層的クラスタリング等）を切り替え可能
/// </summary>
/// <remarks>
/// Phase 3.4A: Gemini推奨のClean Architecture改善
/// - 既存のFindNearbyRegions()の「processedRegions制約」問題を解決
/// - グルーピングロジックをBatchOcrProcessorから分離
/// - 数学的に正確なアルゴリズム（Union-Find）の採用を可能に
/// </remarks>
public interface IRegionGroupingStrategy
{
    /// <summary>
    /// テキスト領域をグループ化し、近接するリージョンを単一グループにまとめる
    /// </summary>
    /// <param name="regions">グループ化対象のOCRテキスト領域リスト</param>
    /// <param name="options">バッチOCR処理オプション（ChunkGroupingDistance含む）</param>
    /// <returns>グループ化されたテキスト領域のリスト（各要素は1グループ）</returns>
    /// <remarks>
    /// 実装例:
    /// - UnionFindRegionGroupingStrategy: グラフベースの連結成分検出（O(N² α(N))）
    /// - HierarchicalClusteringStrategy: 階層的クラスタリング（O(N² log N)）
    /// - LegacyIterativeStrategy: 既存のFindNearbyRegions()互換（互換性テスト用）
    /// </remarks>
    List<List<OcrTextRegion>> GroupRegions(
        IReadOnlyList<OcrTextRegion> regions,
        BatchOcrOptions options);
}
