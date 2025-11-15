using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OCRTextRegion = Baketa.Core.Abstractions.OCR.TextDetection.TextRegion;

namespace Baketa.Core.Abstractions.OCR.TextDetection;

/// <summary>
/// 複数の検出アルゴリズムの結果を統合するインターフェース
/// </summary>
public interface ITextRegionAggregator
{
    /// <summary>
    /// 複数の検出結果を統合します
    /// </summary>
    /// <param name="detectionResults">各検出器からの結果</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>統合された検出結果</returns>
    Task<IReadOnlyList<OCRTextRegion>> AggregateResultsAsync(
        IEnumerable<IReadOnlyList<OCRTextRegion>> detectionResults,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 時間的な追跡を適用します
    /// </summary>
    /// <param name="currentRegions">現在のフレームの検出結果</param>
    /// <param name="previousRegions">前のフレームの検出結果</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>追跡情報が更新された検出結果</returns>
    Task<IReadOnlyList<OCRTextRegion>> TrackRegionsAsync(
        IReadOnlyList<OCRTextRegion> currentRegions,
        IReadOnlyList<OCRTextRegion> previousRegions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 検出されたテキスト領域をスコアリングします
    /// </summary>
    /// <param name="regions">評価する領域</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>スコアリング済みの領域</returns>
    Task<IReadOnlyList<OCRTextRegion>> ScoreRegionsAsync(
        IReadOnlyList<OCRTextRegion> regions,
        CancellationToken cancellationToken = default);
}
