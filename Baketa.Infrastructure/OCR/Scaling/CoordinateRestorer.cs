using System.Drawing;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Models.OCR;

namespace Baketa.Infrastructure.OCR.Scaling;

/// <summary>
/// スケーリングされた画像の座標を元の画像座標に復元するシステム
/// </summary>
public static class CoordinateRestorer
{
    /// <summary>
    /// スケーリングされた座標を元のスケールに復元（Gemini改善版 - 境界クリッピング対応）
    /// </summary>
    /// <param name="scaledRect">スケーリング後の座標</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="originalImageSize">元画像サイズ（境界クリッピング用）</param>
    /// <returns>元スケールでの座標</returns>
    /// <remarks>
    /// 🎯 [P0-2_GEMINI_FIX] Math.Round四捨五入問題の完全解決:
    /// - Math.Floor（座標）+ Math.Ceiling（サイズ）で累積誤差を防止
    /// - 右下座標(x2, y2)先計算方式で精度向上
    /// - 境界クリッピングで画像範囲外アクセス防止
    /// </remarks>
    public static Rectangle RestoreOriginalCoordinates(Rectangle scaledRect, double scaleFactor, Size originalImageSize)
    {
        if (Math.Abs(scaleFactor - 1.0) < 0.001) // スケーリングされていない場合
        {
            return scaledRect;
        }

        if (scaleFactor <= 0)
        {
            throw new ArgumentException($"Invalid scale factor: {scaleFactor}");
        }

        // 🔥 [P0-2_GEMINI_IMPROVED] 浮動小数点演算を先に実行
        double originalX = scaledRect.X / scaleFactor;
        double originalY = scaledRect.Y / scaleFactor;
        double originalWidth = scaledRect.Width / scaleFactor;
        double originalHeight = scaledRect.Height / scaleFactor;

        // 🔥 [P0-2_GEMINI_IMPROVED] 左上はFloor、右下はCeilingで最大精度確保
        int x1 = (int)Math.Floor(originalX);
        int y1 = (int)Math.Floor(originalY);
        int x2 = (int)Math.Ceiling(originalX + originalWidth);
        int y2 = (int)Math.Ceiling(originalY + originalHeight);

        // 🔥 [P0-2_GEMINI_IMPROVED] 境界クリッピング - 画像範囲外アクセス防止
        x1 = Math.Max(0, x1);
        y1 = Math.Max(0, y1);
        x2 = Math.Min(originalImageSize.Width, x2);
        y2 = Math.Min(originalImageSize.Height, y2);

        // 🔥 [P0-2_GEMINI_IMPROVED] クリッピング後の座標から幅・高さ計算
        int finalWidth = Math.Max(0, x2 - x1);
        int finalHeight = Math.Max(0, y2 - y1);

        return new Rectangle(x1, y1, finalWidth, finalHeight);
    }

    /// <summary>
    /// OCRテキスト領域の座標を復元（境界クリッピング対応）
    /// </summary>
    /// <param name="scaledRegion">スケーリング後のテキスト領域</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="originalImageSize">元画像サイズ（境界クリッピング用）</param>
    /// <returns>元スケールでのテキスト領域</returns>
    /// <remarks>
    /// 🎯 [P0-2_GEMINI_FIX] RestoreOriginalCoordinatesに元画像サイズを渡す
    /// </remarks>
    public static OcrTextRegion RestoreTextRegion(OcrTextRegion scaledRegion, double scaleFactor, Size originalImageSize)
    {
        if (Math.Abs(scaleFactor - 1.0) < 0.001) // スケーリングされていない場合
        {
            return scaledRegion;
        }

        // 🔥 [P0-2_FIX] 元画像サイズを渡して境界クリッピングを有効化
        var restoredBounds = RestoreOriginalCoordinates(scaledRegion.Bounds, scaleFactor, originalImageSize);

        return new OcrTextRegion(
            text: scaledRegion.Text,
            bounds: restoredBounds,
            confidence: scaledRegion.Confidence
        );
    }

    /// <summary>
    /// OCR結果全体の座標を復元（境界クリッピング対応）
    /// </summary>
    /// <param name="scaledResults">スケーリング後のOCR結果</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="originalImage">元画像（復元後のOcrResultsで使用）</param>
    /// <returns>元スケールでのOCR結果</returns>
    /// <remarks>
    /// 🎯 [P0-2_FIX] 元画像サイズを自動取得して境界クリッピングを有効化
    /// </remarks>
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

        // 🔥 [P0-2_FIX] 元画像サイズを取得
        var originalImageSize = new Size(originalImage.Width, originalImage.Height);

        // 各テキスト領域の座標を復元
        var restoredRegions = scaledResults.TextRegions
            .Select(region => RestoreTextRegion(region, scaleFactor, originalImageSize))
            .ToList();

        // ROIも復元（存在する場合）
        Rectangle? restoredRoi = null;
        if (scaledResults.RegionOfInterest.HasValue)
        {
            restoredRoi = RestoreOriginalCoordinates(scaledResults.RegionOfInterest.Value, scaleFactor, originalImageSize);
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
    /// 複数の座標を一括で復元（境界クリッピング対応）
    /// </summary>
    /// <param name="scaledRectangles">スケーリング後の座標リスト</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <param name="originalImageSize">元画像サイズ（境界クリッピング用）</param>
    /// <returns>元スケールでの座標リスト</returns>
    /// <remarks>
    /// 🎯 [P0-2_FIX] 境界クリッピングを有効化
    /// </remarks>
    public static IList<Rectangle> RestoreMultipleCoordinates(IEnumerable<Rectangle> scaledRectangles, double scaleFactor, Size originalImageSize)
    {
        ArgumentNullException.ThrowIfNull(scaledRectangles);

        return [.. scaledRectangles.Select(rect => RestoreOriginalCoordinates(rect, scaleFactor, originalImageSize))];
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
