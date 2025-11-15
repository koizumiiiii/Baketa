using System.Runtime.InteropServices;
using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// フォント特化型前処理システム
/// ゲーム特有のフォントに最適化された前処理を提供
/// </summary>
public static class FontSpecificPreprocessor
{
    /// <summary>
    /// フォントタイプの識別結果
    /// </summary>
    public enum FontType
    {
        /// <summary>標準的なゲームフォント</summary>
        Standard,
        /// <summary>小さな細いフォント</summary>
        SmallThin,
        /// <summary>太字・ボールドフォント</summary>
        Bold,
        /// <summary>ピクセルフォント・レトロゲーム</summary>
        Pixel,
        /// <summary>装飾フォント</summary>
        Decorative,
        /// <summary>手書き風フォント</summary>
        Handwritten,
        /// <summary>不明なフォント</summary>
        Unknown
    }

    /// <summary>
    /// フォント特性分析結果
    /// </summary>
    public class FontCharacteristics
    {
        public FontType DetectedType { get; set; }
        public double AverageStrokeWidth { get; set; }
        public double CharacterSpacing { get; set; }
        public double LineHeight { get; set; }
        public bool HasSerifs { get; set; }
        public bool IsMonospace { get; set; }
        public double TextSharpness { get; set; }
        public OptimizationMode RecommendedMode { get; set; }
        public string Description { get; set; } = string.Empty;
    }

    /// <summary>
    /// 画像からフォント特性を分析
    /// </summary>
    public static FontCharacteristics AnalyzeFontCharacteristics(Mat input)
    {
        try
        {
            using var gray = new Mat();
            if (input.Channels() == 3)
            {
                Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
            }
            else
            {
                input.CopyTo(gray);
            }

            var characteristics = new FontCharacteristics
            {
                // 1. ストローク幅分析
                AverageStrokeWidth = AnalyzeStrokeWidth(gray),

                // 2. 文字間隔分析
                CharacterSpacing = AnalyzeCharacterSpacing(gray),

                // 3. 行の高さ分析
                LineHeight = AnalyzeLineHeight(gray),

                // 4. セリフ検出
                HasSerifs = DetectSerifs(gray),

                // 5. 等幅フォント検出
                IsMonospace = DetectMonospace(gray),

                // 6. テキストの鮮明度
                TextSharpness = AnalyzeTextSharpness(gray)
            };

            // 7. フォントタイプ判定
            characteristics.DetectedType = ClassifyFontType(characteristics);

            // 8. 推奨最適化モード決定
            characteristics.RecommendedMode = DetermineOptimizationMode(characteristics);

            // 9. 説明文生成
            characteristics.Description = GenerateDescription(characteristics);

            return characteristics;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ フォント特性分析エラー: {ex.Message}");
            return new FontCharacteristics
            {
                DetectedType = FontType.Unknown,
                RecommendedMode = OptimizationMode.Standard,
                Description = "分析エラーのため標準処理を適用"
            };
        }
    }

    /// <summary>
    /// フォント特性に基づく最適化前処理
    /// </summary>
    public static Mat ProcessForFontType(Mat input, FontType fontType)
    {
        return fontType switch
        {
            FontType.SmallThin => ProcessSmallThinFont(input),
            FontType.Bold => ProcessBoldFont(input),
            FontType.Pixel => ProcessPixelFont(input),
            FontType.Decorative => ProcessDecorativeFont(input),
            FontType.Handwritten => ProcessHandwrittenFont(input),
            _ => ProcessStandardFont(input)
        };
    }

    /// <summary>
    /// 自動フォント判定＋最適化前処理
    /// </summary>
    public static Mat ProcessWithFontDetection(Mat input)
    {
        var characteristics = AnalyzeFontCharacteristics(input);

        Console.WriteLine($"🔍 フォント分析結果:");
        Console.WriteLine($"   📝 フォントタイプ: {characteristics.DetectedType}");
        Console.WriteLine($"   📏 ストローク幅: {characteristics.AverageStrokeWidth:F2}");
        Console.WriteLine($"   📐 文字間隔: {characteristics.CharacterSpacing:F2}");
        Console.WriteLine($"   📊 鮮明度: {characteristics.TextSharpness:F2}");
        Console.WriteLine($"   🎯 推奨モード: {characteristics.RecommendedMode}");
        Console.WriteLine($"   💬 説明: {characteristics.Description}");

        return ProcessForFontType(input, characteristics.DetectedType);
    }

    /// <summary>
    /// ストローク幅を分析
    /// </summary>
    private static double AnalyzeStrokeWidth(Mat gray)
    {
        try
        {
            using var edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150);

            // モルフォロジー演算でストローク幅を推定
            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
            using var dilated = new Mat();
            Cv2.Dilate(edges, dilated, kernel);

            var totalPixels = Cv2.CountNonZero(dilated);
            var imageArea = gray.Width * gray.Height;

            return (double)totalPixels / imageArea * 100; // パーセンテージで返す
        }
        catch
        {
            return 5.0; // デフォルト値
        }
    }

    /// <summary>
    /// 文字間隔を分析
    /// </summary>
    private static double AnalyzeCharacterSpacing(Mat gray)
    {
        try
        {
            // 水平方向の投影でスペースを検出
            var horizontalProfile = new float[gray.Width];

            for (int x = 0; x < gray.Width; x++)
            {
                float sum = 0;
                for (int y = 0; y < gray.Height; y++)
                {
                    sum += gray.At<byte>(y, x);
                }
                horizontalProfile[x] = sum / gray.Height;
            }

            // スペース（低い値の領域）を検出
            var spaceCount = 0;
            var inSpace = false;
            var threshold = horizontalProfile.Average() * 0.7;

            for (int i = 0; i < horizontalProfile.Length; i++)
            {
                if (horizontalProfile[i] < threshold)
                {
                    if (!inSpace)
                    {
                        spaceCount++;
                        inSpace = true;
                    }
                }
                else
                {
                    inSpace = false;
                }
            }

            return spaceCount > 0 ? (double)gray.Width / spaceCount : gray.Width * 0.1;
        }
        catch
        {
            return 20.0; // デフォルト値
        }
    }

    /// <summary>
    /// 行の高さを分析
    /// </summary>
    private static double AnalyzeLineHeight(Mat gray)
    {
        try
        {
            // 垂直方向の投影でテキスト行を検出
            var verticalProfile = new float[gray.Height];

            for (int y = 0; y < gray.Height; y++)
            {
                float sum = 0;
                for (int x = 0; x < gray.Width; x++)
                {
                    sum += 255 - gray.At<byte>(y, x); // 文字部分（暗い部分）をカウント
                }
                verticalProfile[y] = sum / gray.Width;
            }

            // テキスト行の開始・終了を検出
            var threshold = verticalProfile.Max() * 0.3;
            var lineHeights = new List<int>();
            var inLine = false;
            var lineStart = 0;

            for (int i = 0; i < verticalProfile.Length; i++)
            {
                if (verticalProfile[i] > threshold)
                {
                    if (!inLine)
                    {
                        lineStart = i;
                        inLine = true;
                    }
                }
                else
                {
                    if (inLine)
                    {
                        lineHeights.Add(i - lineStart);
                        inLine = false;
                    }
                }
            }

            return lineHeights.Count > 0 ? lineHeights.Average() : 20.0;
        }
        catch
        {
            return 20.0; // デフォルト値
        }
    }

    /// <summary>
    /// セリフの存在を検出
    /// </summary>
    private static bool DetectSerifs(Mat gray)
    {
        try
        {
            // 細かいエッジディテールでセリフを検出
            using var sobelX = new Mat();
            using var sobelY = new Mat();
            Cv2.Sobel(gray, sobelX, MatType.CV_64F, 1, 0, 3);
            Cv2.Sobel(gray, sobelY, MatType.CV_64F, 0, 1, 3);

            using var magnitude = new Mat();
            Cv2.Magnitude(sobelX, sobelY, magnitude);

            var meanMagnitude = Cv2.Mean(magnitude).Val0;

            // セリフがあると細かいエッジが多くなる
            return meanMagnitude > 15.0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 等幅フォントかどうかを検出
    /// </summary>
    private static bool DetectMonospace(Mat gray)
    {
        try
        {
            var characterSpacing = AnalyzeCharacterSpacing(gray);
            var strokeWidth = AnalyzeStrokeWidth(gray);

            // 等幅フォントは文字間隔が一定
            return characterSpacing < 30 && strokeWidth > 3;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// テキストの鮮明度を分析
    /// </summary>
    private static double AnalyzeTextSharpness(Mat gray)
    {
        try
        {
            using var laplacian = new Mat();
            Cv2.Laplacian(gray, laplacian, MatType.CV_64F);

            using var abs_laplacian = new Mat();
            Cv2.ConvertScaleAbs(laplacian, abs_laplacian);

            return Cv2.Mean(abs_laplacian).Val0;
        }
        catch
        {
            return 10.0; // デフォルト値
        }
    }

    /// <summary>
    /// フォントタイプを分類
    /// </summary>
    private static FontType ClassifyFontType(FontCharacteristics characteristics)
    {
        // 小さくて細いフォント
        if (characteristics.AverageStrokeWidth < 3 && characteristics.LineHeight < 15)
            return FontType.SmallThin;

        // 太字フォント
        if (characteristics.AverageStrokeWidth > 8)
            return FontType.Bold;

        // ピクセルフォント（低鮮明度、等幅）
        if (characteristics.TextSharpness < 8 && characteristics.IsMonospace)
            return FontType.Pixel;

        // 装飾フォント（セリフあり）
        if (characteristics.HasSerifs)
            return FontType.Decorative;

        // 手書き風（不規則な間隔、低鮮明度）
        if (characteristics.CharacterSpacing > 50 && characteristics.TextSharpness < 12)
            return FontType.Handwritten;

        return FontType.Standard;
    }

    /// <summary>
    /// 最適化モードを決定
    /// </summary>
    private static OptimizationMode DetermineOptimizationMode(FontCharacteristics characteristics)
    {
        return characteristics.DetectedType switch
        {
            FontType.SmallThin => OptimizationMode.SmallTextEnhanced,
            FontType.Bold => OptimizationMode.ContrastEnhanced,
            FontType.Pixel => OptimizationMode.Combined,
            FontType.Decorative => OptimizationMode.UltraHighAccuracy,
            FontType.Handwritten => OptimizationMode.PerfectAccuracy,
            _ => OptimizationMode.Standard
        };
    }

    /// <summary>
    /// 説明文を生成
    /// </summary>
    private static string GenerateDescription(FontCharacteristics characteristics)
    {
        return characteristics.DetectedType switch
        {
            FontType.SmallThin => "小さく細いフォント - アップスケールと鮮明化が効果的",
            FontType.Bold => "太字フォント - コントラスト強化で識別向上",
            FontType.Pixel => "ピクセルフォント - 複合最適化で補完",
            FontType.Decorative => "装飾フォント - 超高精度処理が必要",
            FontType.Handwritten => "手書き風フォント - 完璧な前処理が必要",
            _ => "標準フォント - 基本最適化で十分"
        };
    }

    // フォントタイプ別の前処理メソッド群

    private static Mat ProcessSmallThinFont(Mat input)
    {
        var output = new Mat();

        // 2倍拡大
        using var upscaled = new Mat();
        Cv2.Resize(input, upscaled, new OpenCvSharp.Size(input.Width * 2, input.Height * 2), interpolation: InterpolationFlags.Cubic);

        // 鮮明化
        using var kernel = new Mat(3, 3, MatType.CV_32F);
        var kernelData = new float[] { 0, -1, 0, -1, 5, -1, 0, -1, 0 };
        kernel.SetArray(kernelData);
        using var sharpened = new Mat();
        Cv2.Filter2D(upscaled, sharpened, -1, kernel);

        // 元サイズに戻す
        Cv2.Resize(sharpened, output, new OpenCvSharp.Size(input.Width, input.Height), interpolation: InterpolationFlags.Area);

        return output;
    }

    private static Mat ProcessBoldFont(Mat input)
    {
        var output = new Mat();

        // コントラスト強化
        input.ConvertTo(output, -1, 1.3, -20);

        // 軽いブラー除去
        using var temp = new Mat();
        Cv2.GaussianBlur(output, temp, new OpenCvSharp.Size(3, 3), 0.5);
        Cv2.AddWeighted(output, 1.5, temp, -0.5, 0, output);

        return output;
    }

    private static Mat ProcessPixelFont(Mat input)
    {
        var output = new Mat();

        // ニアレストネイバー拡大でピクセル感を保持
        using var upscaled = new Mat();
        Cv2.Resize(input, upscaled, new OpenCvSharp.Size(input.Width * 2, input.Height * 2), interpolation: InterpolationFlags.Nearest);

        // 二値化で明確化
        using var gray = new Mat();
        if (upscaled.Channels() == 3)
        {
            Cv2.CvtColor(upscaled, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            upscaled.CopyTo(gray);
        }

        using var binary = new Mat();
        Cv2.Threshold(gray, binary, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);

        // 元サイズに戻す
        Cv2.Resize(binary, output, new OpenCvSharp.Size(input.Width, input.Height), interpolation: InterpolationFlags.Area);

        return output;
    }

    private static Mat ProcessDecorativeFont(Mat input)
    {
        // 装飾フォントは超高精度前処理を適用
        return UltraHighAccuracyPreprocessor.ProcessForUltraAccuracy(input);
    }

    private static Mat ProcessHandwrittenFont(Mat input)
    {
        var output = new Mat();

        // 手書き風は強力なノイズ除去と鮮明化
        using var denoised = new Mat();
        Cv2.FastNlMeansDenoising(input, denoised, 10, 7, 21);

        // アンシャープマスク
        using var blurred = new Mat();
        Cv2.GaussianBlur(denoised, blurred, new OpenCvSharp.Size(5, 5), 1.0);
        Cv2.AddWeighted(denoised, 2.0, blurred, -1.0, 0, output);

        return output;
    }

    private static Mat ProcessStandardFont(Mat input)
    {
        // 標準フォントは基本的な前処理
        return PPOCRv5Preprocessor.ProcessForPPOCRv5(input, OptimizationMode.Standard);
    }
}
