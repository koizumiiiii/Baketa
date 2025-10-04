using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// PaddleOCR結果の変換、座標復元、テキスト結合を担当するサービス
/// </summary>
public interface IPaddleOcrResultConverter
{
    /// <summary>
    /// PaddleOCR結果をOcrTextRegionに変換
    /// </summary>
    IReadOnlyList<OcrTextRegion> ConvertToTextRegions(PaddleOcrResult[] paddleResults, double scaleFactor, Rectangle? roi);

    /// <summary>
    /// 検出専用結果の変換
    /// </summary>
    IReadOnlyList<OcrTextRegion> ConvertDetectionOnlyResult(PaddleOcrResult[] paddleResults);

    /// <summary>
    /// 空結果の作成
    /// </summary>
    OcrResults CreateEmptyResult(IImage image, Rectangle? roi, TimeSpan processingTime);
}
