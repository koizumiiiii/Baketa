using System.Drawing;

namespace Baketa.Core.Models.OCR;

/// <summary>
/// OCR処理の基本結果を表すクラス（後方互換性のため保持）
/// </summary>
public class OcrResult
{
    /// <summary>
    /// 検出されたテキスト
    /// </summary>
    public string Text { get; }
    
    /// <summary>
    /// テキストの位置と範囲
    /// </summary>
    public Rectangle Bounds { get; }
    
    /// <summary>
    /// 信頼度スコア (0.0〜1.0)
    /// </summary>
    public float Confidence { get; }
    
    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="text">検出されたテキスト</param>
    /// <param name="bounds">テキストの位置と範囲</param>
    /// <param name="confidence">信頼度スコア</param>
    public OcrResult(string text, Rectangle bounds, float confidence)
    {
        Text = text ?? string.Empty;
        Bounds = bounds;
        Confidence = Math.Clamp(confidence, 0.0f, 1.0f);
    }
    
    /// <summary>
    /// 結果が有効かどうか
    /// </summary>
    public bool IsValid => !string.IsNullOrWhiteSpace(Text) && Confidence > 0.0f;
    
    /// <summary>
    /// 文字列表現
    /// </summary>
    /// <returns>テキスト内容</returns>
    public override string ToString() => Text;
    
    /// <summary>
    /// OcrTextRegionから変換
    /// </summary>
    /// <param name="textRegion">変換元のテキスト領域</param>
    /// <returns>OcrResult</returns>
    public static OcrResult FromTextRegion(Baketa.Core.Abstractions.OCR.OcrTextRegion textRegion)
    {
        ArgumentNullException.ThrowIfNull(textRegion);
            
        return new OcrResult(
            textRegion.Text,
            textRegion.Bounds,
            (float)textRegion.Confidence
        );
    }
    
    /// <summary>
    /// OcrTextRegionに変換
    /// </summary>
    /// <returns>OcrTextRegion</returns>
    public Baketa.Core.Abstractions.OCR.OcrTextRegion ToTextRegion()
    {
        return new Baketa.Core.Abstractions.OCR.OcrTextRegion(
            Text,
            Bounds,
            Confidence
        );
    }
}

/// <summary>
/// OCR処理のパフォーマンス情報
/// </summary>
public class OcrPerformanceInfo
{
    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }
    
    /// <summary>
    /// 処理した画像のサイズ（ピクセル数）
    /// </summary>
    public int ProcessedPixels { get; init; }
    
    /// <summary>
    /// 秒間処理ピクセル数
    /// </summary>
    public double PixelsPerSecond => ProcessingTime.TotalSeconds > 0 
        ? ProcessedPixels / ProcessingTime.TotalSeconds 
        : 0.0;
    
    /// <summary>
    /// メモリ使用量（バイト）
    /// </summary>
    public long MemoryUsed { get; init; }
    
    /// <summary>
    /// 検出されたテキスト領域数
    /// </summary>
    public int DetectedRegions { get; init; }
    
    public OcrPerformanceInfo(
        TimeSpan processingTime, 
        int processedPixels, 
        long memoryUsed = 0, 
        int detectedRegions = 0)
    {
        ProcessingTime = processingTime;
        ProcessedPixels = Math.Max(0, processedPixels);
        MemoryUsed = Math.Max(0, memoryUsed);
        DetectedRegions = Math.Max(0, detectedRegions);
    }
}

/// <summary>
/// OCR結果の統計情報
/// </summary>
public class OcrResultStatistics(
    int totalCharacters,
    double averageConfidence,
    double maxConfidence,
    double minConfidence,
    int validRegions,
    Size imageSize,
    bool usedRegionOfInterest = false)
{
    /// <summary>
    /// 総テキスト文字数
    /// </summary>
    public int TotalCharacters { get; init; } = Math.Max(0, totalCharacters);

    /// <summary>
    /// 平均信頼度
    /// </summary>
    public double AverageConfidence { get; init; } = Math.Clamp(averageConfidence, 0.0, 1.0);

    /// <summary>
    /// 最高信頼度
    /// </summary>
    public double MaxConfidence { get; init; } = Math.Clamp(maxConfidence, 0.0, 1.0);

    /// <summary>
    /// 最低信頼度
    /// </summary>
    public double MinConfidence { get; init; } = Math.Clamp(minConfidence, 0.0, 1.0);

    /// <summary>
    /// 有効なテキスト領域数
    /// </summary>
    public int ValidRegions { get; init; } = Math.Max(0, validRegions);

    /// <summary>
    /// 処理対象画像のサイズ
    /// </summary>
    public Size ImageSize { get; init; } = imageSize;

    /// <summary>
    /// ROIが使用されたかどうか
    /// </summary>
    public bool UsedRegionOfInterest { get; init; } = usedRegionOfInterest;
}
