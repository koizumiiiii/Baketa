using System.Collections.Generic;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.TextProcessing;

/// <summary>
/// OCR結果のテキスト領域を結合するインターフェース
/// </summary>
public interface ITextMerger
{
    /// <summary>
    /// テキスト領域を適切に結合して文章を再構成
    /// </summary>
    /// <param name="textRegions">OCRで検出されたテキスト領域のリスト</param>
    /// <returns>結合されたテキスト</returns>
    string MergeTextRegions(IReadOnlyList<OcrTextRegion> textRegions);
    
    /// <summary>
    /// テキスト領域を行単位でグループ化
    /// </summary>
    /// <param name="textRegions">OCRで検出されたテキスト領域のリスト</param>
    /// <returns>行ごとにグループ化されたテキスト領域</returns>
    List<List<OcrTextRegion>> GroupTextRegionsByLine(IReadOnlyList<OcrTextRegion> textRegions);
}