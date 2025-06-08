using System.Drawing;
using Sdcb.PaddleOCR;

namespace Baketa.Infrastructure.OCR.PaddleOCR.Results;

/// <summary>
/// OCR処理の結果を表すクラス
/// </summary>
public class OcrResult
{
    /// <summary>
    /// 認識されたテキスト
    /// </summary>
    public string Text { get; }
    
    /// <summary>
    /// 信頼度スコア（0.0 - 1.0）
    /// </summary>
    public float Confidence { get; }
    
    /// <summary>
    /// テキストの境界ボックス
    /// </summary>
    public Rectangle BoundingBox { get; }
    
    /// <summary>
    /// テキストの詳細領域（4つの角の座標）
    /// </summary>
    public PointF[] Region { get; }
    
    /// <summary>
    /// 回転角度（度）
    /// </summary>
    public float Angle { get; }

    public OcrResult(string text, float confidence, Rectangle boundingBox)
    {
        // パラメータ検証を追加
        if (confidence < 0.0f || confidence > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0.0 and 1.0");
        
        if (boundingBox.Width < 0 || boundingBox.Height < 0)
            throw new ArgumentException("BoundingBox dimensions must be non-negative", nameof(boundingBox));
        
        Text = text ?? string.Empty;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Region = [];
        Angle = 0f;
    }
    
    public OcrResult(string text, float confidence, Rectangle boundingBox, PointF[] region, float angle = 0f)
    {
        // パラメータ検証を追加
        if (confidence < 0.0f || confidence > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(confidence), "Confidence must be between 0.0 and 1.0");
        
        if (boundingBox.Width < 0 || boundingBox.Height < 0)
            throw new ArgumentException("BoundingBox dimensions must be non-negative", nameof(boundingBox));
        
        Text = text ?? string.Empty;
        Confidence = confidence;
        BoundingBox = boundingBox;
        Region = region ?? [];
        Angle = angle;
    }

    /// <summary>
    /// PaddleOCRの結果からOcrResultを作成
    /// 注意: 実際のPaddleOcrResult APIの確認後に修正が必要
    /// </summary>
    /// <param name="paddleResult">PaddleOCRの結果</param>
    /// <returns>変換されたOcrResult</returns>
    public static OcrResult FromPaddleResult(PaddleOcrResult paddleResult)
    {
        ArgumentNullException.ThrowIfNull(paddleResult);

        // TODO: 実際のPaddleOcrResultのAPIを確認して修正
        // 現在はコンパイルエラー回避のための仮実装
        
        var region = Array.Empty<PointF>();
        var boundingBox = Rectangle.Empty;
        var confidence = 0.5f;
        var text = "[PaddleOCR Result]"; // 仮のテキスト
        
        // 実際のAPIが確認でき次第、以下のコメントを解除して修正
        /*
        try 
        {
            text = paddleResult.Text ?? string.Empty;
            confidence = paddleResult.Score; // または paddleResult.Confidence
            region = paddleResult.Region ?? Array.Empty<PointF>();
            boundingBox = CalculateBoundingBox(region);
        }
        catch (Exception ex)
        {
            // APIプロパティ名が不正な場合のフォールバック
            Console.WriteLine($"PaddleOcrResult API Error: {ex.Message}");
        }
        */

        return new OcrResult(
            text: text,
            confidence: confidence,
            boundingBox: boundingBox,
            region: region,
            angle: 0f
        );
    }

    /// <summary>
    /// 複数のPaddleOCR結果をOcrResultの配列に変換
    /// </summary>
    /// <param name="paddleResults">PaddleOCRの結果配列</param>
    /// <returns>変換されたOcrResult配列</returns>
    public static OcrResult[] FromPaddleResults(PaddleOcrResult[]? paddleResults)
    {
        if (paddleResults == null)
            return [];

        return [.. paddleResults
            .Where(result => result != null && !string.IsNullOrWhiteSpace(result.Text))
            .Select(FromPaddleResult)];
    }

    /// <summary>
    /// 領域から境界ボックスを計算
    /// </summary>
    private static Rectangle CalculateBoundingBox(PointF[] region)
    {
        if (region == null || region.Length == 0)
        {
            return Rectangle.Empty;
        }

        var minX = region.Min(p => p.X);
        var maxX = region.Max(p => p.X);
        var minY = region.Min(p => p.Y);
        var maxY = region.Max(p => p.Y);

        return new Rectangle(
            x: (int)Math.Floor(minX),
            y: (int)Math.Floor(minY),
            width: (int)Math.Ceiling(maxX - minX),
            height: (int)Math.Ceiling(maxY - minY)
        );
    }

    /// <summary>
    /// 文字列表現
    /// </summary>
    public override string ToString()
    {
        var confidencePercent = (Confidence * 100).ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
        return $"Text: '{Text}', Confidence: {confidencePercent}%, BoundingBox: {BoundingBox}";
    }
}

/// <summary>
/// 複数のOCR結果をまとめて管理するクラス
/// </summary>
public class OcrResultCollection
{
    /// <summary>
    /// OCR結果の配列
    /// </summary>
    public OcrResult[] Results { get; }
    
    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; }
    
    /// <summary>
    /// 使用された言語
    /// </summary>
    public string Language { get; }
    
    /// <summary>
    /// 処理対象だった画像のサイズ
    /// </summary>
    public Size ImageSize { get; }

    public OcrResultCollection(
        OcrResult[] results, 
        TimeSpan processingTime, 
        string language, 
        Size imageSize)
    {
        // パラメータ検証を追加
        ArgumentNullException.ThrowIfNull(results);
        
        if (string.IsNullOrEmpty(language))
            throw new ArgumentException("Language cannot be null or empty", nameof(language));
            
        if (processingTime < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(processingTime), "Processing time cannot be negative");
            
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
            throw new ArgumentException("Image size must have positive dimensions", nameof(imageSize));
        
        Results = results;
        ProcessingTime = processingTime;
        Language = language;
        ImageSize = imageSize;
    }

    /// <summary>
    /// 全テキストを結合した文字列
    /// </summary>
    public string CombinedText => string.Join(" ", Results.Select(r => r.Text));
    
    /// <summary>
    /// 平均信頼度
    /// </summary>
    public float AverageConfidence => Results.Length > 0 ? Results.Average(r => r.Confidence) : 0f;
    
    /// <summary>
    /// 高信頼度の結果のみを取得
    /// </summary>
    /// <param name="minConfidence">最小信頼度</param>
    /// <returns>フィルタリングされた結果</returns>
    public IEnumerable<OcrResult> GetHighConfidenceResults(float minConfidence = 0.7f)
    {
        if (minConfidence < 0.0f || minConfidence > 1.0f)
            throw new ArgumentOutOfRangeException(nameof(minConfidence), "Confidence must be between 0.0 and 1.0");
            
        return Results.Where(r => r.Confidence >= minConfidence);
    }

    /// <summary>
    /// 指定された領域内の結果のみを取得
    /// </summary>
    /// <param name="region">検索領域</param>
    /// <returns>領域内の結果</returns>
    public IEnumerable<OcrResult> GetResultsInRegion(Rectangle region)
    {
        return Results.Where(r => region.IntersectsWith(r.BoundingBox));
    }

    /// <summary>
    /// 結果の個数
    /// </summary>
    public int ResultCount => Results.Length;
    
    /// <summary>
    /// 文字列表現
    /// </summary>
    public override string ToString()
    {
        return $"Results: {Results.Length}, Language: {Language}, ProcessingTime: {ProcessingTime.TotalMilliseconds}ms, AvgConfidence: {AverageConfidence:F3}";
    }
}
