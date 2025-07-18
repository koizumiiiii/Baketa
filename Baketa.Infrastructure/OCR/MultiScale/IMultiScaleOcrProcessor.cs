using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;

namespace Baketa.Infrastructure.OCR.MultiScale;

/// <summary>
/// マルチスケールOCR処理を行うインターフェース
/// 異なるスケールで画像を処理し、小さいテキストの認識精度を向上させる
/// </summary>
public interface IMultiScaleOcrProcessor
{
    /// <summary>
    /// 使用するスケールファクター（例: [1.0, 1.5, 2.0, 3.0]）
    /// </summary>
    IReadOnlyList<float> ScaleFactors { get; }
    
    /// <summary>
    /// 動的にスケールを決定するかどうか
    /// </summary>
    bool UseDynamicScaling { get; set; }
    
    /// <summary>
    /// マルチスケールOCR処理を実行
    /// </summary>
    /// <param name="image">処理対象の画像</param>
    /// <param name="ocrEngine">使用するOCRエンジン</param>
    /// <returns>統合されたOCR結果</returns>
    Task<OcrResults> ProcessAsync(IAdvancedImage image, IOcrEngine ocrEngine);
    
    /// <summary>
    /// マルチスケールOCR処理を実行（詳細結果付き）
    /// </summary>
    /// <param name="image">処理対象の画像</param>
    /// <param name="ocrEngine">使用するOCRエンジン</param>
    /// <returns>各スケールの結果を含む詳細な結果</returns>
    Task<MultiScaleOcrResult> ProcessWithDetailsAsync(IAdvancedImage image, IOcrEngine ocrEngine);
    
    /// <summary>
    /// 画像特性に基づいて最適なスケールファクターを決定
    /// </summary>
    /// <param name="image">分析対象の画像</param>
    /// <returns>推奨されるスケールファクター</returns>
    Task<IReadOnlyList<float>> DetermineOptimalScalesAsync(IAdvancedImage image);
}

/// <summary>
/// マルチスケールOCR処理の詳細結果
/// </summary>
public class MultiScaleOcrResult
{
    /// <summary>
    /// 各スケールでの処理結果
    /// </summary>
    public IReadOnlyList<ScaleProcessingResult> ScaleResults { get; init; } = new List<ScaleProcessingResult>();
    
    /// <summary>
    /// 統合された最終結果
    /// </summary>
    public OcrResults MergedResult { get; init; } = null!;
    
    /// <summary>
    /// 処理統計情報
    /// </summary>
    public MultiScaleProcessingStats Stats { get; init; } = new();
}

/// <summary>
/// 特定スケールでの処理結果
/// </summary>
public class ScaleProcessingResult
{
    /// <summary>
    /// 使用したスケールファクター
    /// </summary>
    public float ScaleFactor { get; init; }
    
    /// <summary>
    /// OCR結果
    /// </summary>
    public OcrResults OcrResult { get; init; } = null!;
    
    /// <summary>
    /// 処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 検出されたテキストリージョン数
    /// </summary>
    public int DetectedRegions { get; init; }
    
    /// <summary>
    /// 平均信頼度スコア
    /// </summary>
    public float AverageConfidence { get; init; }
}

/// <summary>
/// マルチスケール処理の統計情報
/// </summary>
public class MultiScaleProcessingStats
{
    /// <summary>
    /// 総処理時間（ミリ秒）
    /// </summary>
    public long TotalProcessingTimeMs { get; init; }
    
    /// <summary>
    /// 使用されたスケール数
    /// </summary>
    public int ScalesUsed { get; init; }
    
    /// <summary>
    /// 統合前の総テキストリージョン数
    /// </summary>
    public int TotalRegionsBeforeMerge { get; init; }
    
    /// <summary>
    /// 統合後の総テキストリージョン数
    /// </summary>
    public int TotalRegionsAfterMerge { get; init; }
    
    /// <summary>
    /// 小さいテキストとして検出されたリージョン数
    /// </summary>
    public int SmallTextRegions { get; init; }
    
    /// <summary>
    /// 結果の改善度（0.0-1.0）
    /// </summary>
    public float ImprovementScore { get; init; }
}