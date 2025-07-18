using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.AdaptivePreprocessing;

/// <summary>
/// 適応的前処理パラメータ最適化のインターフェース
/// </summary>
public interface IAdaptivePreprocessingParameterOptimizer
{
    /// <summary>
    /// 画像特性に基づいて最適な前処理パラメータを決定します
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>最適化された前処理パラメータ</returns>
    Task<AdaptivePreprocessingParameters> OptimizeParametersAsync(IAdvancedImage image);
    
    /// <summary>
    /// 画像品質指標に基づいて前処理パラメータを調整します
    /// </summary>
    /// <param name="qualityMetrics">画像品質指標</param>
    /// <param name="textDensityMetrics">テキスト密度指標</param>
    /// <returns>調整された前処理パラメータ</returns>
    Task<AdaptivePreprocessingParameters> AdjustParametersAsync(
        ImageQualityMetrics qualityMetrics, 
        TextDensityMetrics textDensityMetrics);
    
    /// <summary>
    /// パラメータ最適化の詳細結果を取得します
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <returns>詳細な最適化結果</returns>
    Task<AdaptivePreprocessingResult> OptimizeWithDetailsAsync(IAdvancedImage image);
}

/// <summary>
/// 適応的前処理パラメータ
/// </summary>
public record AdaptivePreprocessingParameters
{
    /// <summary>
    /// ガンマ補正値
    /// </summary>
    public double Gamma { get; init; } = 1.0;
    
    /// <summary>
    /// コントラスト調整値
    /// </summary>
    public double Contrast { get; init; } = 1.0;
    
    /// <summary>
    /// 明度調整値
    /// </summary>
    public double Brightness { get; init; }
    
    /// <summary>
    /// ノイズ除去強度 (0.0-1.0)
    /// </summary>
    public double NoiseReduction { get; init; } = 0.1;
    
    /// <summary>
    /// シャープニング強度 (0.0-1.0)
    /// </summary>
    public double Sharpening { get; init; }
    
    /// <summary>
    /// 二値化閾値 (0-255、-1で自動)
    /// </summary>
    public int BinarizationThreshold { get; init; } = -1;
    
    /// <summary>
    /// 膨張・収縮処理のカーネルサイズ
    /// </summary>
    public int MorphologyKernelSize { get; init; } = 1;
    
    /// <summary>
    /// ガウシアンブラーのカーネルサイズ
    /// </summary>
    public int GaussianBlurKernelSize { get; init; }
    
    /// <summary>
    /// エッジ保持平滑化の強度 (0.0-1.0)
    /// </summary>
    public double EdgePreservingSmoothing { get; init; }
    
    /// <summary>
    /// OCR検出閾値
    /// </summary>
    public double DetectionThreshold { get; init; } = 0.3;
    
    /// <summary>
    /// OCR認識閾値
    /// </summary>
    public double RecognitionThreshold { get; init; } = 0.3;
    
    /// <summary>
    /// 最適化の信頼度 (0.0-1.0)
    /// </summary>
    public double OptimizationConfidence { get; init; } = 0.5;
    
    /// <summary>
    /// パラメータ適用の優先度
    /// </summary>
    public PreprocessingPriority Priority { get; init; } = PreprocessingPriority.Balanced;
}

/// <summary>
/// 前処理の優先度
/// </summary>
public enum PreprocessingPriority
{
    /// <summary>
    /// 速度優先（軽い処理のみ）
    /// </summary>
    Speed,
    
    /// <summary>
    /// バランス重視
    /// </summary>
    Balanced,
    
    /// <summary>
    /// 品質優先（重い処理も許可）
    /// </summary>
    Quality,
    
    /// <summary>
    /// 小さなテキスト特化
    /// </summary>
    SmallText,
    
    /// <summary>
    /// 低品質画像特化
    /// </summary>
    LowQuality
}

/// <summary>
/// 適応的前処理の詳細結果
/// </summary>
public record AdaptivePreprocessingResult
{
    /// <summary>
    /// 最適化されたパラメータ
    /// </summary>
    public AdaptivePreprocessingParameters Parameters { get; init; } = new();
    
    /// <summary>
    /// 画像品質分析結果
    /// </summary>
    public ImageQualityMetrics QualityMetrics { get; init; } = new();
    
    /// <summary>
    /// テキスト密度分析結果
    /// </summary>
    public TextDensityMetrics TextDensityMetrics { get; init; } = new();
    
    /// <summary>
    /// 最適化の理由説明
    /// </summary>
    public string OptimizationReason { get; init; } = "";
    
    /// <summary>
    /// 適用された最適化戦略
    /// </summary>
    public string OptimizationStrategy { get; init; } = "";
    
    /// <summary>
    /// 最適化にかかった時間（ミリ秒）
    /// </summary>
    public long OptimizationTimeMs { get; init; }
    
    /// <summary>
    /// 予想される改善効果 (0.0-1.0)
    /// </summary>
    public double ExpectedImprovement { get; init; }
    
    /// <summary>
    /// パラメータの信頼度 (0.0-1.0)
    /// </summary>
    public double ParameterConfidence { get; init; } = 0.5;
}