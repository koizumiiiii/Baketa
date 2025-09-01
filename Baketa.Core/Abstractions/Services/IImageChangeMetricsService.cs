namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// 画像変化検知メトリクス収集サービス
/// パフォーマンス測定と効果分析用
/// </summary>
public interface IImageChangeMetricsService
{
    /// <summary>
    /// OCR処理がスキップされた回数を記録
    /// </summary>
    /// <param name="changePercentage">変化率</param>
    /// <param name="processingTime">処理時間</param>
    void RecordOcrSkipped(float changePercentage, TimeSpan processingTime);
    
    /// <summary>
    /// OCR処理が実行された回数を記録
    /// </summary>
    /// <param name="changePercentage">変化率</param>
    /// <param name="processingTime">処理時間</param>
    void RecordOcrExecuted(float changePercentage, TimeSpan processingTime);
    
    /// <summary>
    /// 現在のメトリクス統計を取得
    /// </summary>
    /// <returns>メトリクス統計情報</returns>
    ImageChangeMetrics GetMetrics();
    
    /// <summary>
    /// メトリクスをリセット
    /// </summary>
    void ResetMetrics();
}

/// <summary>
/// 画像変化検知のメトリクス統計
/// </summary>
public class ImageChangeMetrics
{
    /// <summary>
    /// スキップされたOCR処理の総回数
    /// </summary>
    public long TotalSkippedCount { get; init; }
    
    /// <summary>
    /// 実行されたOCR処理の総回数
    /// </summary>
    public long TotalExecutedCount { get; init; }
    
    /// <summary>
    /// スキップ率 (0.0-1.0)
    /// </summary>
    public double SkipRate => TotalSkippedCount + TotalExecutedCount > 0 
        ? (double)TotalSkippedCount / (TotalSkippedCount + TotalExecutedCount) 
        : 0.0;
    
    /// <summary>
    /// 平均変化率（スキップ時）
    /// </summary>
    public float AverageSkippedChangePercentage { get; init; }
    
    /// <summary>
    /// 平均変化率（実行時）
    /// </summary>
    public float AverageExecutedChangePercentage { get; init; }
    
    /// <summary>
    /// 平均処理時間（変化検知）
    /// </summary>
    public TimeSpan AverageProcessingTime { get; init; }
    
    /// <summary>
    /// メトリクス収集開始時刻
    /// </summary>
    public DateTime CollectionStartTime { get; init; }
    
    /// <summary>
    /// 最終更新時刻
    /// </summary>
    public DateTime LastUpdated { get; init; }
}