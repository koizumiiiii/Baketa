using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.OCR.Results;
using System.Globalization;

namespace Baketa.Infrastructure.OCR.BatchProcessing;

/// <summary>
/// バッチOCR処理の実装クラス
/// Phase 2-B: OCRバッチ処理最適化とパフォーマンス向上
/// </summary>
public sealed class BatchOcrProcessor : IBatchOcrProcessor, IDisposable
{
    private readonly IOcrEngine _ocrEngine;
    private readonly ILogger<BatchOcrProcessor>? _logger;
    
    private BatchOcrOptions _options = new();
    private readonly ConcurrentQueue<ProcessingMetric> _processingHistory = new();
    private bool _disposed;
    
    // パフォーマンス統計
    private long _totalProcessedCount;
    private double _totalProcessingTime;
    private readonly DateTime _startTime = DateTime.UtcNow;
    private int _errorCount;
    private readonly ConcurrentDictionary<int, TextChunk> _chunkCache = new();
    private readonly object _configLock = new();

    public BatchOcrProcessor(IOcrEngine ocrEngine, ILogger<BatchOcrProcessor>? logger = null)
    {
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _logger = logger;
    }

    /// <summary>
    /// 画像をバッチ処理してテキストチャンクを取得
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessBatchAsync(
        IAdvancedImage image, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        var stopwatch = Stopwatch.StartNew();
        var processingStartTime = DateTime.UtcNow;
        
        try
        {
            _logger?.LogInformation("📦 バッチOCR処理開始 - 画像: {Width}x{Height}, ウィンドウ: {Handle}", 
                image.Width, image.Height, windowHandle.ToString("X", CultureInfo.InvariantCulture));

            // 1. 前処理: 画像品質分析
            var qualityMetrics = await AnalyzeImageQualityAsync(image, cancellationToken).ConfigureAwait(false);
            _logger?.LogDebug("🔍 画像品質分析完了: スコア={Score:F2}, 推奨処理={ProcessingType}", 
                qualityMetrics.QualityScore, qualityMetrics.RecommendedProcessing);

            // 2. OCR実行
            var ocrResults = await ExecuteOcrWithOptimizationsAsync(image, qualityMetrics, cancellationToken).ConfigureAwait(false);
            
            // 3. テキストチャンクのグルーピング
            var textChunks = await GroupTextIntoChunksAsync(ocrResults, windowHandle, cancellationToken).ConfigureAwait(false);
            
            stopwatch.Stop();
            
            // 4. パフォーマンス統計更新
            UpdatePerformanceMetrics(processingStartTime, stopwatch.Elapsed, textChunks.Count, true);
            
            _logger?.LogInformation("✅ バッチOCR処理完了 - 処理時間: {ElapsedMs}ms, チャンク数: {ChunkCount}", 
                stopwatch.ElapsedMilliseconds, textChunks.Count);

            return textChunks;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            UpdatePerformanceMetrics(processingStartTime, stopwatch.Elapsed, 0, false);
            
            _logger?.LogError(ex, "❌ バッチOCR処理エラー - 画像: {Width}x{Height}", image.Width, image.Height);
            throw;
        }
    }

    /// <summary>
    /// バッチ処理の設定を更新
    /// </summary>
    public async Task ConfigureBatchProcessingAsync(BatchOcrOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ThrowIfDisposed();
        
        await Task.Run(() =>
        {
            lock (_configLock)
            {
                _options = options;
                _logger?.LogInformation("⚙️ バッチOCR設定更新 - 並列度: {Parallelism}, GPU: {GpuEnabled}", 
                    options.MaxParallelism, options.EnableGpuAcceleration);
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// バッチ処理のパフォーマンスメトリクスを取得
    /// </summary>
    public BatchOcrMetrics GetPerformanceMetrics()
    {
        ThrowIfDisposed();
        
        lock (_configLock)
        {
            var totalProcessed = Interlocked.Read(ref _totalProcessedCount);
            var totalTime = _totalProcessingTime;
            var errorCount = _errorCount;
            var uptime = DateTime.UtcNow - _startTime;
            
            // 最近の処理履歴から統計計算
            var recentMetrics = _processingHistory.TakeLast(100).ToList();
            var successfulMetrics = recentMetrics.Where(m => m.Success).ToList();
            
            return new BatchOcrMetrics
            {
                TotalProcessedCount = totalProcessed,
                AverageProcessingTimeMs = totalProcessed > 0 ? totalTime / totalProcessed : 0,
                LastProcessingTimeMs = recentMetrics.LastOrDefault()?.ProcessingTimeMs ?? 0,
                AverageTextCount = successfulMetrics.Count > 0 ? successfulMetrics.Average(m => m.TextCount) : 0,
                AverageConfidence = successfulMetrics.Count > 0 ? successfulMetrics.Average(m => m.AverageConfidence) : 0,
                ParallelEfficiency = CalculateParallelEfficiency(),
                CacheHitRate = CalculateCacheHitRate(),
                MemoryUsageMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0),
                ErrorRate = totalProcessed > 0 ? (double)errorCount / totalProcessed : 0,
                GpuUtilization = _options.EnableGpuAcceleration ? 0.8 : null, // TODO: 実際のGPU使用率取得
                PreprocessingRatio = 0.15, // TODO: 実際の前処理時間比率
                OcrProcessingRatio = 0.70, // TODO: 実際のOCR処理時間比率
                PostprocessingRatio = 0.15  // TODO: 実際の後処理時間比率
            };
        }
    }

    /// <summary>
    /// バッチ処理キャッシュをクリア
    /// </summary>
    public async Task ClearCacheAsync()
    {
        ThrowIfDisposed();
        
        await Task.Run(() =>
        {
            _chunkCache.Clear();
            _logger?.LogInformation("🧹 バッチOCRキャッシュクリア完了");
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像品質分析
    /// </summary>
    private async Task<ImageQualityMetrics> AnalyzeImageQualityAsync(IAdvancedImage image, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            // 簡易品質分析（実際の実装では詳細な画像分析を行う）
            var resolution = image.Width * image.Height;
            var aspectRatio = (double)image.Width / image.Height;
            
            var qualityScore = resolution switch
            {
                > 2000000 => 0.9, // 高解像度
                > 500000 => 0.7,  // 中解像度
                _ => 0.5           // 低解像度
            };

            // アスペクト比による調整
            if (aspectRatio is < 0.5 or > 3.0)
                qualityScore *= 0.8; // 極端なアスペクト比は品質を下げる

            var recommendedProcessing = qualityScore switch
            {
                >= 0.8 => ImageProcessingType.Standard,
                >= 0.6 => ImageProcessingType.Enhanced,
                _ => ImageProcessingType.Aggressive
            };

            return new ImageQualityMetrics(qualityScore, recommendedProcessing);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 最適化されたOCR実行
    /// </summary>
    private async Task<OcrResults> ExecuteOcrWithOptimizationsAsync(
        IAdvancedImage image, 
        ImageQualityMetrics qualityMetrics, 
        CancellationToken cancellationToken)
    {
        // 品質に応じた前処理パラメータ調整
        var processingOptions = qualityMetrics.RecommendedProcessing switch
        {
            ImageProcessingType.Aggressive => new { Threshold = 0.1, Enhancement = true },
            ImageProcessingType.Enhanced => new { Threshold = 0.15, Enhancement = true },
            _ => new { Threshold = 0.25, Enhancement = false }
        };

        _logger?.LogDebug("🔧 OCR前処理設定 - 閾値: {Threshold}, 強化: {Enhancement}", 
            processingOptions.Threshold, processingOptions.Enhancement);

        // OCR設定の動的調整
        var currentSettings = _ocrEngine.GetSettings();
        var optimizedSettings = currentSettings.Clone();
        optimizedSettings.DetectionThreshold = processingOptions.Threshold;

        await _ocrEngine.ApplySettingsAsync(optimizedSettings, cancellationToken).ConfigureAwait(false);

        try
        {
            return await _ocrEngine.RecognizeAsync(image, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // 設定を元に戻す
            await _ocrEngine.ApplySettingsAsync(currentSettings, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// テキストをチャンクにグルーピング
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> GroupTextIntoChunksAsync(
        OcrResults ocrResults, 
        IntPtr windowHandle, 
        CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            if (!ocrResults.HasText || ocrResults.TextRegions.Count == 0)
            {
                _logger?.LogDebug("📝 テキスト領域なし - 空のチャンクリストを返却");
                return (IReadOnlyList<TextChunk>)Array.Empty<TextChunk>();
            }

            var chunks = new List<TextChunk>();
            var processedRegions = new HashSet<OcrTextRegion>();
            var chunkId = 0;

            foreach (var region in ocrResults.TextRegions)
            {
                if (processedRegions.Contains(region))
                    continue;

                // 近接テキスト領域をグループ化
                var groupedRegions = FindNearbyRegions(region, ocrResults.TextRegions, processedRegions);
                processedRegions.UnionWith(groupedRegions);

                // PositionedTextResultに変換
                var positionedResults = groupedRegions.Select(r => new PositionedTextResult
                {
                    Text = r.Text,
                    BoundingBox = r.Bounds,
                    Confidence = (float)r.Confidence,
                    ChunkId = chunkId,
                    ProcessingTime = ocrResults.ProcessingTime,
                    DetectedLanguage = ocrResults.LanguageCode
                }).ToList();

                // チャンクのバウンディングボックス計算
                var combinedBounds = CalculateCombinedBounds(groupedRegions);
                var combinedText = string.Join(" ", groupedRegions.Select(r => r.Text));

                var chunk = new TextChunk
                {
                    ChunkId = chunkId++,
                    TextResults = positionedResults,
                    CombinedBounds = combinedBounds,
                    CombinedText = combinedText,
                    SourceWindowHandle = windowHandle,
                    DetectedLanguage = ocrResults.LanguageCode
                };

                chunks.Add(chunk);

                _logger?.LogDebug("📦 チャンク作成 - ID: {ChunkId}, テキスト: '{Text}', 領域数: {RegionCount}", 
                    chunk.ChunkId, chunk.CombinedText, groupedRegions.Count);
            }

            _logger?.LogInformation("📊 チャンクグルーピング完了 - 総チャンク数: {ChunkCount}, 総テキスト領域数: {RegionCount}", 
                chunks.Count, ocrResults.TextRegions.Count);

            return (IReadOnlyList<TextChunk>)chunks.AsReadOnly();
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 近接テキスト領域を検索
    /// </summary>
    private List<OcrTextRegion> FindNearbyRegions(
        OcrTextRegion baseRegion, 
        IReadOnlyList<OcrTextRegion> allRegions, 
        HashSet<OcrTextRegion> processedRegions)
    {
        var nearbyRegions = new List<OcrTextRegion> { baseRegion };
        var searchQueue = new Queue<OcrTextRegion>();
        searchQueue.Enqueue(baseRegion);

        while (searchQueue.Count > 0)
        {
            var currentRegion = searchQueue.Dequeue();

            foreach (var region in allRegions)
            {
                if (processedRegions.Contains(region) || nearbyRegions.Contains(region))
                    continue;

                var distance = CalculateDistance(currentRegion.Bounds, region.Bounds);
                if (distance <= _options.ChunkGroupingDistance)
                {
                    nearbyRegions.Add(region);
                    searchQueue.Enqueue(region);
                }
            }
        }

        return nearbyRegions;
    }

    /// <summary>
    /// 2つのテキスト領域間の距離を計算
    /// </summary>
    private static double CalculateDistance(Rectangle rect1, Rectangle rect2)
    {
        var center1 = new Point(rect1.X + rect1.Width / 2, rect1.Y + rect1.Height / 2);
        var center2 = new Point(rect2.X + rect2.Width / 2, rect2.Y + rect2.Height / 2);
        
        var dx = center1.X - center2.X;
        var dy = center1.Y - center2.Y;
        
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// 複数の領域の結合バウンディングボックスを計算
    /// </summary>
    private static Rectangle CalculateCombinedBounds(List<OcrTextRegion> regions)
    {
        if (regions.Count == 0)
            return Rectangle.Empty;

        var minX = regions.Min(r => r.Bounds.X);
        var minY = regions.Min(r => r.Bounds.Y);
        var maxX = regions.Max(r => r.Bounds.Right);
        var maxY = regions.Max(r => r.Bounds.Bottom);

        return new Rectangle(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary>
    /// パフォーマンス統計を更新
    /// </summary>
    private void UpdatePerformanceMetrics(DateTime startTime, TimeSpan processingTime, int textCount, bool success)
    {
        lock (_configLock)
        {
            Interlocked.Increment(ref _totalProcessedCount);
            _totalProcessingTime += processingTime.TotalMilliseconds;
            
            if (!success)
                Interlocked.Increment(ref _errorCount);

            var metric = new ProcessingMetric
            {
                Timestamp = startTime,
                ProcessingTimeMs = processingTime.TotalMilliseconds,
                TextCount = textCount,
                Success = success,
                AverageConfidence = success ? 0.85 : 0 // TODO: 実際の信頼度
            };

            _processingHistory.Enqueue(metric);

            // 履歴のサイズ制限
            while (_processingHistory.Count > 1000)
                _processingHistory.TryDequeue(out _);
        }
    }

    /// <summary>
    /// 並列処理効率を計算
    /// </summary>
    private double CalculateParallelEfficiency()
    {
        // TODO: 実際の並列処理効率測定
        return Math.Min(1.0, _options.MaxParallelism / (double)Environment.ProcessorCount);
    }

    /// <summary>
    /// キャッシュヒット率を計算
    /// </summary>
    private double CalculateCacheHitRate()
    {
        // TODO: 実際のキャッシュ統計
        return 0.15; // 仮の値
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _chunkCache.Clear();
        _disposed = true;
        
        _logger?.LogInformation("🧹 BatchOcrProcessor リソース解放完了");
    }
}

/// <summary>
/// 画像品質メトリクス
/// </summary>
internal sealed record ImageQualityMetrics(double QualityScore, ImageProcessingType RecommendedProcessing);

/// <summary>
/// 画像処理タイプ
/// </summary>
internal enum ImageProcessingType
{
    Standard,   // 標準処理
    Enhanced,   // 強化処理
    Aggressive  // 積極的処理
}

/// <summary>
/// 処理メトリック
/// </summary>
internal sealed record ProcessingMetric
{
    public DateTime Timestamp { get; init; }
    public double ProcessingTimeMs { get; init; }
    public int TextCount { get; init; }
    public bool Success { get; init; }
    public double AverageConfidence { get; init; }
}