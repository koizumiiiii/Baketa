using Baketa.Core.Abstractions.Imaging;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;

/// <summary>
/// 画像処理・前処理・変換を担当するサービス
/// </summary>
public interface IPaddleOcrImageProcessor
{
    /// <summary>
    /// IImageからMat形式に変換
    /// </summary>
    Task<Mat> ConvertToMatAsync(IImage image, Rectangle? roi, CancellationToken cancellationToken);

    /// <summary>
    /// スケーリング付きでIImageからMat形式に変換
    /// </summary>
    Task<(Mat mat, double scaleFactor)> ConvertToMatWithScalingAsync(IImage image, Rectangle? roi, CancellationToken cancellationToken);

    /// <summary>
    /// 言語別最適化前処理を適用
    /// </summary>
    Mat ApplyLanguageOptimizations(Mat inputMat, string language);

    /// <summary>
    /// 画像サイズ正規化
    /// </summary>
    Mat NormalizeImageDimensions(Mat inputMat);

    /// <summary>
    /// Mat検証
    /// </summary>
    bool ValidateMat(Mat mat);

    /// <summary>
    /// 予防的正規化を適用
    /// </summary>
    Mat ApplyPreventiveNormalization(Mat inputMat);
}
