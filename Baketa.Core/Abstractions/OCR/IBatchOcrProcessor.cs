using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Core.Abstractions.OCR;

/// <summary>
/// バッチOCR処理の抽象インターフェース
/// Phase 2: OCRバッチ処理最適化と座標ベース翻訳のための基盤
/// </summary>
public interface IBatchOcrProcessor
{
    /// <summary>
    /// 画像をバッチ処理してテキストチャンクを取得
    /// ユーザー要求: 認識したテキストとともに座標位置もログで確認
    /// </summary>
    /// <param name="image">処理対象の画像</param>
    /// <param name="windowHandle">ソースウィンドウハンドル</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>座標情報付きテキストチャンクのリスト</returns>
    Task<IReadOnlyList<TextChunk>> ProcessBatchAsync(
        IAdvancedImage image, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// バッチ処理の設定を更新
    /// 並列度、チャンクサイズなどの動的調整
    /// </summary>
    /// <param name="options">バッチ処理オプション</param>
    Task ConfigureBatchProcessingAsync(BatchOcrOptions options);
    
    /// <summary>
    /// バッチ処理のパフォーマンスメトリクスを取得
    /// </summary>
    /// <returns>処理性能統計</returns>
    BatchOcrMetrics GetPerformanceMetrics();
    
    /// <summary>
    /// バッチ処理キャッシュをクリア
    /// メモリ効率化のため
    /// </summary>
    Task ClearCacheAsync();
}

/// <summary>
/// バッチOCR処理のオプション設定
/// </summary>
public sealed class BatchOcrOptions
{
    /// <summary>並列処理の最大度合い</summary>
    public int MaxParallelism { get; init; } = Environment.ProcessorCount;
    
    /// <summary>テキスト領域の最小サイズ（ピクセル）</summary>
    public int MinTextRegionSize { get; init; } = 10;
    
    /// <summary>テキスト領域の最大サイズ（ピクセル）</summary>
    public int MaxTextRegionSize { get; init; } = 2000;
    
    /// <summary>チャンクグループ化の距離閾値（ピクセル）</summary>
    public double ChunkGroupingDistance { get; init; } = 50.0;
    
    /// <summary>低解像度スキャンのスケール比率</summary>
    public float LowResolutionScale { get; init; } = 0.25f;
    
    /// <summary>OCR信頼度の最小閾値</summary>
    public float MinConfidenceThreshold { get; init; } = 0.1f;
    
    /// <summary>前処理パイプラインを有効化</summary>
    public bool EnablePreprocessing { get; init; } = true;
    
    /// <summary>GPU加速を有効化</summary>
    public bool EnableGpuAcceleration { get; init; } = true;
    
    /// <summary>タイムアウト時間（ミリ秒）</summary>
    public int TimeoutMs { get; init; } = 30000;
    
    /// <summary>OCR処理時のタイルサイズ（ピクセル）</summary>
    public int TileSize { get; init; } = 1024;
}

/// <summary>
/// バッチOCR処理のパフォーマンスメトリクス
/// </summary>
public sealed class BatchOcrMetrics
{
    /// <summary>総処理回数</summary>
    public long TotalProcessedCount { get; init; }
    
    /// <summary>平均処理時間（ミリ秒）</summary>
    public double AverageProcessingTimeMs { get; init; }
    
    /// <summary>最後の処理時間（ミリ秒）</summary>
    public double LastProcessingTimeMs { get; init; }
    
    /// <summary>検出されたテキスト数の平均</summary>
    public double AverageTextCount { get; init; }
    
    /// <summary>平均信頼度</summary>
    public double AverageConfidence { get; init; }
    
    /// <summary>並列処理効率（0.0-1.0）</summary>
    public double ParallelEfficiency { get; init; }
    
    /// <summary>キャッシュヒット率（0.0-1.0）</summary>
    public double CacheHitRate { get; init; }
    
    /// <summary>メモリ使用量（MB）</summary>
    public double MemoryUsageMB { get; init; }
    
    /// <summary>エラー率（0.0-1.0）</summary>
    public double ErrorRate { get; init; }
    
    /// <summary>GPU使用率（0.0-1.0、GPU使用時のみ）</summary>
    public double? GpuUtilization { get; init; }
    
    /// <summary>前処理にかかった時間の割合（0.0-1.0）</summary>
    public double PreprocessingRatio { get; init; }
    
    /// <summary>OCR処理にかかった時間の割合（0.0-1.0）</summary>
    public double OcrProcessingRatio { get; init; }
    
    /// <summary>後処理にかかった時間の割合（0.0-1.0）</summary>
    public double PostprocessingRatio { get; init; }
    
    /// <summary>
    /// メトリクスのログ出力用文字列
    /// </summary>
    public string ToLogString() =>
        $"Processed: {TotalProcessedCount} | AvgTime: {AverageProcessingTimeMs:F1}ms | " +
        $"AvgTexts: {AverageTextCount:F1} | Confidence: {AverageConfidence:F3} | " +
        $"Efficiency: {ParallelEfficiency:F2} | Cache: {CacheHitRate:F2} | " +
        $"Memory: {MemoryUsageMB:F1}MB | Errors: {ErrorRate:F3}";
}