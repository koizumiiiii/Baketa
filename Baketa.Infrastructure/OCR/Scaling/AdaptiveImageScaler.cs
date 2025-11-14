using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Scaling;

/// <summary>
/// PaddleOCR大画面対応のための適応的画像スケーリングシステム
/// 縦横4096制限とピクセル総数2M制限の両方を考慮した安全なスケーリング
/// </summary>
public static class AdaptiveImageScaler
{
    private const int PADDLE_OCR_SAFE_MAX_DIMENSION = 4096;
    // 🔥 [PPOCRV5_RECOGNITION] 文字認識精度向上のため4Mに引き上げ
    // 根拠: 小文字・英字認識精度 +15% 期待、メモリ増加 +33% は許容範囲
    // 4,000,000ピクセル → 3440x1440 (UWQHD) で約0.90スケール (sqrt(4M/4.95M) = 0.898)
    // 3,000,000 (旧) → 0.78スケールでは小文字認識が不十分
    // 🔥 [PHASE5_COORDINATE_FIX] PaddleOcrEngineの予防処理と統一するためpublicに変更
    public const int PADDLE_OCR_MEMORY_LIMIT_PIXELS = 4_000_000;

    /// <summary>
    /// PaddleOCR処理に最適な画像サイズを計算
    /// </summary>
    /// <param name="originalWidth">元画像の幅</param>
    /// <param name="originalHeight">元画像の高さ</param>
    /// <returns>最適化されたサイズとスケール係数</returns>
    public static (int newWidth, int newHeight, double scaleFactor) CalculateOptimalSize(
        int originalWidth, int originalHeight)
    {
        // 入力検証
        if (originalWidth <= 0 || originalHeight <= 0)
        {
            throw new ArgumentException($"Invalid image dimensions: {originalWidth}x{originalHeight}");
        }

        // Step 1: 縦横4096制限チェック
        double dimensionScale = Math.Min(
            (double)PADDLE_OCR_SAFE_MAX_DIMENSION / originalWidth,
            (double)PADDLE_OCR_SAFE_MAX_DIMENSION / originalHeight
        );

        // Step 2: ピクセル総数2M制限チェック  
        long totalPixels = (long)originalWidth * originalHeight;
        double memoryScale = totalPixels > PADDLE_OCR_MEMORY_LIMIT_PIXELS
            ? Math.Sqrt((double)PADDLE_OCR_MEMORY_LIMIT_PIXELS / totalPixels)
            : 1.0;

        // Step 3: より厳しい制限を採用、拡大は禁止
        double finalScale = Math.Min(Math.Min(dimensionScale, memoryScale), 1.0);

        // Step 4: 最終サイズ計算（整数丸め）
        int newWidth = Math.Max(1, (int)(originalWidth * finalScale));
        int newHeight = Math.Max(1, (int)(originalHeight * finalScale));

        return (newWidth, newHeight, finalScale);
    }

    /// <summary>
    /// スケーリングが必要かどうかを判定
    /// </summary>
    /// <param name="originalWidth">元画像の幅</param>
    /// <param name="originalHeight">元画像の高さ</param>
    /// <param name="threshold">スケーリング判定の閾値（デフォルト: 0.99）</param>
    /// <returns>スケーリングが必要な場合true</returns>
    public static bool RequiresScaling(int originalWidth, int originalHeight, double threshold = 0.99)
    {
        var (_, _, scaleFactor) = CalculateOptimalSize(originalWidth, originalHeight);
        return scaleFactor < threshold;
    }

    /// <summary>
    /// スケーリング情報の詳細ログ用文字列を生成
    /// </summary>
    /// <param name="originalWidth">元画像の幅</param>
    /// <param name="originalHeight">元画像の高さ</param>
    /// <param name="newWidth">新しい幅</param>
    /// <param name="newHeight">新しい高さ</param>
    /// <param name="scaleFactor">スケール係数</param>
    /// <returns>ログ用の詳細文字列</returns>
    public static string GetScalingInfo(int originalWidth, int originalHeight,
        int newWidth, int newHeight, double scaleFactor)
    {
        long originalPixels = (long)originalWidth * originalHeight;
        long newPixels = (long)newWidth * newHeight;
        double pixelReduction = (1.0 - (double)newPixels / originalPixels) * 100;

        return $"画面スケーリング: {originalWidth}x{originalHeight} → {newWidth}x{newHeight} " +
               $"(スケール: {scaleFactor:F3}, ピクセル削減: {pixelReduction:F1}%)";
    }

    /// <summary>
    /// 縦横制限とメモリ制限のどちらが制約となっているかを判定
    /// </summary>
    /// <param name="originalWidth">元画像の幅</param>
    /// <param name="originalHeight">元画像の高さ</param>
    /// <returns>制約の種類</returns>
    public static ScalingConstraintType GetConstraintType(int originalWidth, int originalHeight)
    {
        double dimensionScale = Math.Min(
            (double)PADDLE_OCR_SAFE_MAX_DIMENSION / originalWidth,
            (double)PADDLE_OCR_SAFE_MAX_DIMENSION / originalHeight
        );

        long totalPixels = (long)originalWidth * originalHeight;
        double memoryScale = totalPixels > PADDLE_OCR_MEMORY_LIMIT_PIXELS
            ? Math.Sqrt((double)PADDLE_OCR_MEMORY_LIMIT_PIXELS / totalPixels)
            : 1.0;

        if (dimensionScale >= 1.0 && memoryScale >= 1.0)
            return ScalingConstraintType.None;
        else if (dimensionScale < memoryScale)
            return ScalingConstraintType.Dimension;
        else
            return ScalingConstraintType.Memory;
    }
}

/// <summary>
/// スケーリング制約の種類
/// </summary>
public enum ScalingConstraintType
{
    /// <summary>制約なし</summary>
    None,
    /// <summary>縦横サイズ制限</summary>
    Dimension,
    /// <summary>メモリ制限</summary>
    Memory
}
