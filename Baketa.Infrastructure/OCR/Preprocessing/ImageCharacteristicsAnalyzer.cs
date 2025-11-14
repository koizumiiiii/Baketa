using OpenCvSharp;

namespace Baketa.Infrastructure.OCR.Preprocessing;

/// <summary>
/// 画像特性分析結果
/// </summary>
public class ImageCharacteristics
{
    /// <summary>平均輝度（0-255）</summary>
    public double AverageBrightness { get; set; }

    /// <summary>標準偏差（コントラスト指標）</summary>
    public double Contrast { get; set; }

    /// <summary>明るい背景かどうか</summary>
    public bool IsBrightBackground { get; set; }

    /// <summary>暗い背景かどうか</summary>
    public bool IsDarkBackground { get; set; }

    /// <summary>高コントラストかどうか</summary>
    public bool IsHighContrast { get; set; }

    /// <summary>低コントラストかどうか</summary>
    public bool IsLowContrast { get; set; }

    /// <summary>テキスト密度（エッジ密度）</summary>
    public double TextDensity { get; set; }

    /// <summary>推奨される最適化モード</summary>
    public OptimizationMode RecommendedMode { get; set; }

    /// <summary>画像タイプの詳細分析</summary>
    public string ImageType { get; set; } = string.Empty;
}

/// <summary>
/// 画像特性分析器
/// 明るい背景・暗い背景・コントラストを自動判定
/// </summary>
public static class ImageCharacteristicsAnalyzer
{
    // 閾値定数
    private const double BRIGHT_THRESHOLD = 140.0; // 明るい背景の閾値
    private const double DARK_THRESHOLD = 80.0;    // 暗い背景の閾値
    private const double HIGH_CONTRAST_THRESHOLD = 45.0; // 高コントラストの閾値
    private const double LOW_CONTRAST_THRESHOLD = 20.0;  // 低コントラストの閾値
    private const double HIGH_TEXT_DENSITY_THRESHOLD = 0.015; // 高テキスト密度の閾値

    /// <summary>
    /// 画像の特性を総合的に分析
    /// </summary>
    /// <param name="input">分析対象の画像</param>
    /// <returns>画像特性分析結果</returns>
    public static ImageCharacteristics AnalyzeImage(Mat input)
    {
        if (input == null || input.Empty())
        {
            return CreateDefaultCharacteristics();
        }

        try
        {
            // グレースケール変換
            using var gray = ConvertToGrayScale(input);

            // 基本統計計算
            Cv2.MeanStdDev(gray, out var mean, out var stddev);
            var avgBrightness = mean.Val0;
            var contrast = stddev.Val0;

            // エッジ密度計算（テキスト密度の指標）
            var textDensity = CalculateTextDensity(gray);

            // 色相分析（明るい背景の特徴判定）
            var (HasBrightBackground, HasYellowish, HasBlueish) = AnalyzeColorCharacteristics(input);

            // 背景判定
            var isBright = avgBrightness > BRIGHT_THRESHOLD || HasBrightBackground;
            var isDark = avgBrightness < DARK_THRESHOLD && !HasBrightBackground;

            // コントラスト判定
            var isHighContrast = contrast > HIGH_CONTRAST_THRESHOLD;
            var isLowContrast = contrast < LOW_CONTRAST_THRESHOLD;

            // 推奨最適化モード決定
            var recommendedMode = DetermineOptimalMode(contrast, textDensity, isBright, isDark);

            // 画像タイプ分類
            var imageType = ClassifyImageType(contrast, textDensity, isBright, isDark);

            return new ImageCharacteristics
            {
                AverageBrightness = avgBrightness,
                Contrast = contrast,
                IsBrightBackground = isBright,
                IsDarkBackground = isDark,
                IsHighContrast = isHighContrast,
                IsLowContrast = isLowContrast,
                TextDensity = textDensity,
                RecommendedMode = recommendedMode,
                ImageType = imageType
            };
        }
        catch (Exception ex)
        {
            // エラー時はデフォルト特性を返す
            var defaultCharacteristics = CreateDefaultCharacteristics();
            defaultCharacteristics.ImageType = $"分析エラー: {ex.Message}";
            return defaultCharacteristics;
        }
    }

    /// <summary>
    /// グレースケール変換
    /// </summary>
    private static Mat ConvertToGrayScale(Mat input)
    {
        var gray = new Mat();
        if (input.Channels() == 3)
        {
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);
        }
        else
        {
            input.CopyTo(gray);
        }
        return gray;
    }

    /// <summary>
    /// テキスト密度（エッジ密度）計算
    /// </summary>
    private static double CalculateTextDensity(Mat gray)
    {
        try
        {
            using var edges = new Mat();
            Cv2.Canny(gray, edges, 50, 150);
            var edgePixels = Cv2.CountNonZero(edges);
            var totalPixels = edges.Rows * edges.Cols;
            return edgePixels / (double)totalPixels;
        }
        catch
        {
            return 0.01; // デフォルト値
        }
    }

    /// <summary>
    /// 色相特性分析
    /// </summary>
    private static (bool HasBrightBackground, bool HasYellowish, bool HasBlueish) AnalyzeColorCharacteristics(Mat input)
    {
        if (input.Channels() != 3)
        {
            return (false, false, false);
        }

        try
        {
            // BGR -> HSV変換
            using var hsv = new Mat();
            Cv2.CvtColor(input, hsv, ColorConversionCodes.BGR2HSV);

            // HSVの各チャンネルを分離
            var hsvChannels = Cv2.Split(hsv);
            using var hue = hsvChannels[0];
            using var saturation = hsvChannels[1];
            using var value = hsvChannels[2];

            // V（明度）チャンネルの統計
            Cv2.MeanStdDev(value, out var valueMean, out var _);
            var avgValue = valueMean.Val0;

            // H（色相）チャンネルの分析
            Cv2.MeanStdDev(hue, out var hueMean, out var _);
            var avgHue = hueMean.Val0;

            // S（彩度）チャンネルの分析  
            Cv2.MeanStdDev(saturation, out var satMean, out var _);
            var avgSaturation = satMean.Val0;

            // 明るい背景判定（明度ベース）
            var hasBrightBackground = avgValue > 180; // HSVのV値で判定

            // 黄色系判定（色相10-30度、彩度中程度以上）
            var hasYellowish = avgHue >= 10 && avgHue <= 30 && avgSaturation > 80;

            // 青系判定（色相100-130度）
            var hasBlueish = avgHue >= 100 && avgHue <= 130;

            // リソース解放
            foreach (var channel in hsvChannels)
            {
                channel.Dispose();
            }

            return (hasBrightBackground, hasYellowish, hasBlueish);
        }
        catch
        {
            return (false, false, false);
        }
    }

    /// <summary>
    /// 最適な最適化モードを決定
    /// </summary>
    private static OptimizationMode DetermineOptimalMode(double contrast, double textDensity, bool isBright, bool isDark)
    {
        // 明るい背景の場合
        if (isBright)
        {
            if (textDensity > HIGH_TEXT_DENSITY_THRESHOLD)
                return OptimizationMode.SmallTextEnhanced; // 小さなテキスト多数
            if (contrast > HIGH_CONTRAST_THRESHOLD)
                return OptimizationMode.ContrastEnhanced;  // 既に高コントラスト
            return OptimizationMode.Combined; // バランス重視
        }

        // 暗い背景の場合
        if (isDark)
        {
            if (contrast < LOW_CONTRAST_THRESHOLD)
                return OptimizationMode.UltraHighAccuracy; // 低コントラスト -> 超高精度
            if (textDensity < 0.005)
                return OptimizationMode.KanjiEnhanced;     // テキスト密度低 -> 漢字強化
            return OptimizationMode.Combined; // 統合処理
        }

        // 中間の明るさの場合
        if (contrast < LOW_CONTRAST_THRESHOLD)
            return OptimizationMode.PerfectAccuracy; // 低コントラスト -> 極限精度
        if (textDensity > HIGH_TEXT_DENSITY_THRESHOLD)
            return OptimizationMode.SmallTextEnhanced; // 高密度テキスト

        return OptimizationMode.Combined; // デフォルト
    }

    /// <summary>
    /// 画像タイプ分類
    /// </summary>
    private static string ClassifyImageType(double contrast, double textDensity, bool isBright, bool isDark)
    {
        if (isBright && contrast > HIGH_CONTRAST_THRESHOLD)
            return "明るい高コントラスト（理想的）";
        if (isBright && contrast < LOW_CONTRAST_THRESHOLD)
            return "明るい低コントラスト（要注意）";
        if (isBright)
            return "明るい標準コントラスト";

        if (isDark && contrast < LOW_CONTRAST_THRESHOLD)
            return "暗い低コントラスト（困難）";
        if (isDark && textDensity < 0.005)
            return "暗い少量テキスト";
        if (isDark)
            return "暗い標準";

        if (contrast < LOW_CONTRAST_THRESHOLD)
            return "中間明度低コントラスト";
        if (textDensity > HIGH_TEXT_DENSITY_THRESHOLD)
            return "中間明度高密度テキスト";

        return "標準";
    }

    /// <summary>
    /// デフォルト特性を作成
    /// </summary>
    private static ImageCharacteristics CreateDefaultCharacteristics()
    {
        return new ImageCharacteristics
        {
            AverageBrightness = 128.0,
            Contrast = 30.0,
            IsBrightBackground = false,
            IsDarkBackground = false,
            IsHighContrast = false,
            IsLowContrast = false,
            TextDensity = 0.01,
            RecommendedMode = OptimizationMode.Standard,
            ImageType = "デフォルト"
        };
    }
}
