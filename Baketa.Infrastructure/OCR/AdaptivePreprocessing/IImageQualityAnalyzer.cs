using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 画像品質分析のインターフェース
/// </summary>
public interface IImageQualityAnalyzer
{
    /// <summary>
    /// 画像の品質指標を分析します
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>画像品質指標</returns>
    Task<ImageQualityMetrics> AnalyzeAsync(IAdvancedImage image);
    
    /// <summary>
    /// 画像のコントラスト値を計算します
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>コントラスト値 (0.0-1.0)</returns>
    Task<double> CalculateContrastAsync(IAdvancedImage image);
    
    /// <summary>
    /// 画像の明度値を計算します
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>明度値 (0.0-1.0)</returns>
    Task<double> CalculateBrightnessAsync(IAdvancedImage image);
    
    /// <summary>
    /// 画像のノイズレベルを計算します
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>ノイズレベル (0.0-1.0)</returns>
    Task<double> CalculateNoiseAsync(IAdvancedImage image);
    
    /// <summary>
    /// テキスト密度を分析します
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>テキスト密度情報</returns>
    Task<TextDensityMetrics> AnalyzeTextDensityAsync(IAdvancedImage image);
}

/// <summary>
/// 画像品質指標
/// </summary>
public record ImageQualityMetrics
{
    /// <summary>
    /// コントラスト値 (0.0-1.0)
    /// </summary>
    public double Contrast { get; init; }
    
    /// <summary>
    /// 明度値 (0.0-1.0)
    /// </summary>
    public double Brightness { get; init; }
    
    /// <summary>
    /// ノイズレベル (0.0-1.0)
    /// </summary>
    public double NoiseLevel { get; init; }
    
    /// <summary>
    /// シャープネス値 (0.0-1.0)
    /// </summary>
    public double Sharpness { get; init; }
    
    /// <summary>
    /// 画像解像度（幅）
    /// </summary>
    public int Width { get; init; }
    
    /// <summary>
    /// 画像解像度（高さ）
    /// </summary>
    public int Height { get; init; }
    
    /// <summary>
    /// 総画素数
    /// </summary>
    public int TotalPixels => Width * Height;
    
    /// <summary>
    /// 画像品質の総合スコア (0.0-1.0)
    /// </summary>
    public double OverallQuality { get; init; }
}

/// <summary>
/// テキスト密度分析結果
/// </summary>
public record TextDensityMetrics
{
    /// <summary>
    /// エッジ密度 (0.0-1.0)
    /// </summary>
    public double EdgeDensity { get; init; }
    
    /// <summary>
    /// 推定テキストサイズ（ピクセル）
    /// </summary>
    public double EstimatedTextSize { get; init; }
    
    /// <summary>
    /// テキスト領域の割合 (0.0-1.0)
    /// </summary>
    public double TextAreaRatio { get; init; }
    
    /// <summary>
    /// 文字間隔の推定値（ピクセル）
    /// </summary>
    public double EstimatedCharacterSpacing { get; init; }
    
    /// <summary>
    /// 行間隔の推定値（ピクセル）
    /// </summary>
    public double EstimatedLineSpacing { get; init; }
    
    /// <summary>
    /// テキストの方向性（0: 水平, 90: 垂直など）
    /// </summary>
    public double TextOrientation { get; init; }
}