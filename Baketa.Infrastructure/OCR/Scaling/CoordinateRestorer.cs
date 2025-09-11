using System.Drawing;
using Baketa.Core.Models.OCR;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results;

namespace Baketa.Infrastructure.OCR.Scaling;

/// <summary>
/// スケーリングされた画像の座標を元の画像座標に復元するシステム
/// </summary>
public static class CoordinateRestorer
{
    /// <summary>
    /// スケーリングされた座標を元のスケールに復元
    /// </summary>
    /// <param name="scaledRect">スケーリング後の座標</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <returns>元スケールでの座標</returns>
    public static Rectangle RestoreOriginalCoordinates(Rectangle scaledRect, double scaleFactor)
    {
        if (Math.Abs(scaleFactor - 1.0) < 0.001) // スケーリングされていない場合
        {
            return scaledRect;
        }
        
        if (scaleFactor <= 0)
        {
            throw new ArgumentException($"Invalid scale factor: {scaleFactor}");
        }
        
        return new Rectangle(
            x: (int)Math.Round(scaledRect.X / scaleFactor),
            y: (int)Math.Round(scaledRect.Y / scaleFactor), 
            width: (int)Math.Round(scaledRect.Width / scaleFactor),
            height: (int)Math.Round(scaledRect.Height / scaleFactor)
        );
    }
    
    /// <summary>
    /// OCRテキスト領域の座標を復元
    /// </summary>
    /// <param name="scaledRegion">スケーリング後のテキスト領域</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <returns>元スケールでのテキスト領域</returns>
    public static OcrTextRegion RestoreTextRegion(OcrTextRegion scaledRegion, double scaleFactor)
    {
        if (Math.Abs(scaleFactor - 1.0) < 0.001) // スケーリングされていない場合
        {
            return scaledRegion;
        }
        
        var restoredBounds = RestoreOriginalCoordinates(scaledRegion.Bounds, scaleFactor);
        
        return new OcrTextRegion(
            text: scaledRegion.Text,
            bounds: restoredBounds,
            confidence: scaledRegion.Confidence
        );
    }
    
    /// <summary>
    /// OCR結果全体の座標を復元
    /// </summary>
    /// <param name="scaledResults">スケーリング後のOCR結果</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="originalImage">元画像（復元後のOcrResultsで使用）</param>
    /// <returns>元スケールでのOCR結果</returns>
    public static OcrResults RestoreOcrResults(OcrResults scaledResults, double scaleFactor, 
        Baketa.Core.Abstractions.Imaging.IImage originalImage)
    {
        ArgumentNullException.ThrowIfNull(scaledResults);
        ArgumentNullException.ThrowIfNull(originalImage);
        
        if (Math.Abs(scaleFactor - 1.0) < 0.001) // スケーリングされていない場合
        {
            return new OcrResults(
                scaledResults.TextRegions,
                originalImage, // 元画像を使用
                scaledResults.ProcessingTime,
                scaledResults.LanguageCode,
                scaledResults.RegionOfInterest,
                scaledResults.Text
            );
        }
        
        // 各テキスト領域の座標を復元
        var restoredRegions = scaledResults.TextRegions
            .Select(region => RestoreTextRegion(region, scaleFactor))
            .ToList();
        
        // ROIも復元（存在する場合）
        Rectangle? restoredRoi = null;
        if (scaledResults.RegionOfInterest.HasValue)
        {
            restoredRoi = RestoreOriginalCoordinates(scaledResults.RegionOfInterest.Value, scaleFactor);
        }
        
        return new OcrResults(
            restoredRegions,
            originalImage, // 元画像を使用
            scaledResults.ProcessingTime,
            scaledResults.LanguageCode,
            restoredRoi,
            scaledResults.Text // テキスト内容は変更なし
        );
    }
    
    /// <summary>
    /// 複数の座標を一括で復元
    /// </summary>
    /// <param name="scaledRectangles">スケーリング後の座標リスト</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <returns>元スケールでの座標リスト</returns>
    public static IList<Rectangle> RestoreMultipleCoordinates(IEnumerable<Rectangle> scaledRectangles, double scaleFactor)
    {
        ArgumentNullException.ThrowIfNull(scaledRectangles);
        
        return scaledRectangles
            .Select(rect => RestoreOriginalCoordinates(rect, scaleFactor))
            .ToList();
    }
    
    /// <summary>
    /// スケーリング情報のログ用文字列を生成
    /// </summary>
    /// <param name="originalRect">元の座標</param>
    /// <param name="scaledRect">スケーリング後の座標</param>
    /// <param name="restoredRect">復元後の座標</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <returns>座標復元情報の詳細文字列</returns>
    public static string GetRestorationInfo(Rectangle originalRect, Rectangle scaledRect, 
        Rectangle restoredRect, double scaleFactor)
    {
        var accuracy = CalculateRestorationAccuracy(originalRect, restoredRect);
        
        return $"座標復元: 元({originalRect.X},{originalRect.Y},{originalRect.Width}x{originalRect.Height}) " +
               $"→ 処理({scaledRect.X},{scaledRect.Y},{scaledRect.Width}x{scaledRect.Height}) " +
               $"→ 復元({restoredRect.X},{restoredRect.Y},{restoredRect.Width}x{restoredRect.Height}) " +
               $"[スケール: {scaleFactor:F3}, 精度: {accuracy:F1}%]";
    }
    
    /// <summary>
    /// 座標復元の精度を計算
    /// </summary>
    /// <param name="original">元の座標</param>
    /// <param name="restored">復元後の座標</param>
    /// <returns>復元精度（パーセント）</returns>
    public static double CalculateRestorationAccuracy(Rectangle original, Rectangle restored)
    {
        if (original.IsEmpty || restored.IsEmpty)
            return 0.0;
        
        // 位置とサイズの差分を計算
        double positionError = Math.Sqrt(
            Math.Pow(original.X - restored.X, 2) + 
            Math.Pow(original.Y - restored.Y, 2)
        );
        
        double sizeError = Math.Abs(original.Width - restored.Width) + 
                          Math.Abs(original.Height - restored.Height);
        
        // 元画像サイズに対する相対誤差
        double imageSize = Math.Sqrt(original.Width * original.Width + original.Height * original.Height);
        double totalError = (positionError + sizeError) / imageSize;
        
        // 精度として表現（100% - 誤差率）
        return Math.Max(0, Math.Min(100, (1.0 - totalError) * 100));
    }
}