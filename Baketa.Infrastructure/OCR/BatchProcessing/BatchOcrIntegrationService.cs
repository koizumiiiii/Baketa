using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.OCR.Results;

namespace Baketa.Infrastructure.OCR.BatchProcessing;

/// <summary>
/// バッチOCR統合サービス
/// Phase 2-B: 既存OCRシステムとBatchOcrProcessorの統合
/// </summary>
public sealed class BatchOcrIntegrationService : IDisposable
{
    private readonly IBatchOcrProcessor _batchOcrProcessor;
    private readonly IOcrEngine _fallbackOcrEngine;
    private readonly ILogger<BatchOcrIntegrationService>? _logger;
    
    private readonly SemaphoreSlim _processingSemaphore;
    private bool _disposed;

    public BatchOcrIntegrationService(
        IBatchOcrProcessor batchOcrProcessor,
        IOcrEngine fallbackOcrEngine,
        ILogger<BatchOcrIntegrationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _fallbackOcrEngine = fallbackOcrEngine ?? throw new ArgumentNullException(nameof(fallbackOcrEngine));
        _logger = logger;
        
        // 並列処理制限（CPUコア数に基づく）
        var maxConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
        _processingSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    /// 統合OCR処理 - バッチ処理とフォールバックの組み合わせ
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessWithIntegratedOcrAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        await _processingSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        try
        {
            _logger?.LogInformation("🔄 統合OCR処理開始 - 画像: {Width}x{Height}", image.Width, image.Height);

            // 1. バッチOCR処理を試行
            var chunks = await TryBatchOcrProcessingAsync(image, windowHandle, cancellationToken).ConfigureAwait(false);
            
            // 2. バッチ処理結果の検証
            if (IsValidOcrResult(chunks))
            {
                _logger?.LogInformation("✅ バッチOCR処理成功 - チャンク数: {ChunkCount}", chunks.Count);
                return chunks;
            }

            // 3. フォールバック処理
            _logger?.LogWarning("⚠️ バッチOCR結果不十分、フォールバック処理実行");
            return await ExecuteFallbackOcrAsync(image, windowHandle, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _processingSemaphore.Release();
        }
    }

    /// <summary>
    /// 複数画像の並列バッチ処理
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyList<TextChunk>>> ProcessMultipleImagesAsync(
        IReadOnlyList<(IAdvancedImage Image, IntPtr WindowHandle)> imageData,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        
        if (imageData.Count == 0)
            return Array.Empty<IReadOnlyList<TextChunk>>();

        _logger?.LogInformation("📦 複数画像並列処理開始 - 画像数: {ImageCount}", imageData.Count);

        // 並列処理タスクを作成
        var tasks = imageData.Select(async data =>
        {
            try
            {
                return await ProcessWithIntegratedOcrAsync(
                    data.Image, 
                    data.WindowHandle, 
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ 画像処理エラー - サイズ: {Width}x{Height}", 
                    data.Image.Width, data.Image.Height);
                return Array.Empty<TextChunk>();
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);
        
        _logger?.LogInformation("✅ 複数画像並列処理完了 - 総チャンク数: {TotalChunks}", 
            results.Sum(r => r.Count));

        return results;
    }

    /// <summary>
    /// バッチ処理性能の最適化設定
    /// </summary>
    public async Task OptimizeBatchPerformanceAsync(
        int imageWidth, 
        int imageHeight, 
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        // 画像サイズに基づく最適化設定
        var options = new BatchOcrOptions
        {
            MaxParallelism = CalculateOptimalParallelism(imageWidth, imageHeight),
            MinTextRegionSize = CalculateMinTextRegionSize(imageWidth, imageHeight),
            ChunkGroupingDistance = CalculateChunkGroupingDistance(imageWidth, imageHeight),
            LowResolutionScale = CalculateLowResolutionScale(imageWidth, imageHeight),
            EnablePreprocessing = imageWidth * imageHeight > 1000000, // 高解像度では前処理有効
            EnableGpuAcceleration = true,
            TimeoutMs = CalculateTimeout(imageWidth, imageHeight)
        };

        await _batchOcrProcessor.ConfigureBatchProcessingAsync(options).ConfigureAwait(false);
        
        // cancellationTokenが要求された場合の処理
        cancellationToken.ThrowIfCancellationRequested();
        
        _logger?.LogInformation("⚙️ バッチ性能最適化完了 - 並列度: {Parallelism}, 前処理: {Preprocessing}", 
            options.MaxParallelism, options.EnablePreprocessing);
    }

    /// <summary>
    /// バッチOCR処理を試行
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> TryBatchOcrProcessingAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            // 画像サイズに基づく最適化
            await OptimizeBatchPerformanceAsync(image.Width, image.Height, cancellationToken).ConfigureAwait(false);
            
            // バッチ処理実行
            return await _batchOcrProcessor.ProcessBatchAsync(image, windowHandle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ バッチOCR処理エラー");
            return Array.Empty<TextChunk>();
        }
    }

    /// <summary>
    /// フォールバックOCR処理
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> ExecuteFallbackOcrAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        CancellationToken cancellationToken)
    {
        try
        {
            var ocrResults = await _fallbackOcrEngine.RecognizeAsync(image, cancellationToken: cancellationToken).ConfigureAwait(false);
            
            if (!ocrResults.HasText)
                return Array.Empty<TextChunk>();

            // シンプルなチャンク変換（フォールバック用）
            var chunks = new List<TextChunk>();
            for (int i = 0; i < ocrResults.TextRegions.Count; i++)
            {
                var region = ocrResults.TextRegions[i];
                var positionedResult = new PositionedTextResult
                {
                    Text = region.Text,
                    BoundingBox = region.Bounds,
                    Confidence = (float)region.Confidence,
                    ChunkId = i,
                    ProcessingTime = ocrResults.ProcessingTime,
                    DetectedLanguage = ocrResults.LanguageCode
                };

                var chunk = new TextChunk
                {
                    ChunkId = i,
                    TextResults = [positionedResult],
                    CombinedBounds = region.Bounds,
                    CombinedText = region.Text,
                    SourceWindowHandle = windowHandle,
                    DetectedLanguage = ocrResults.LanguageCode
                };

                chunks.Add(chunk);
            }

            _logger?.LogInformation("🔄 フォールバックOCR完了 - チャンク数: {ChunkCount}", chunks.Count);
            return chunks;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ フォールバックOCR処理エラー");
            return Array.Empty<TextChunk>();
        }
    }

    /// <summary>
    /// OCR結果の妥当性検証
    /// </summary>
    private static bool IsValidOcrResult(IReadOnlyList<TextChunk> chunks)
    {
        if (chunks.Count == 0)
            return false;

        // 有効なテキストを含むチャンクが存在するかチェック
        var validChunks = chunks.Count(c => 
            !string.IsNullOrWhiteSpace(c.CombinedText) && 
            c.AverageConfidence >= 0.1);

        return validChunks > 0;
    }

    /// <summary>
    /// 最適な並列度を計算
    /// </summary>
    private static int CalculateOptimalParallelism(int width, int height)
    {
        var pixelCount = width * height;
        var baseParallelism = Environment.ProcessorCount;

        return pixelCount switch
        {
            > 4000000 => Math.Max(1, baseParallelism - 2), // 超高解像度：保守的
            > 2000000 => Math.Max(1, baseParallelism - 1), // 高解像度：やや保守的
            > 1000000 => baseParallelism,                   // 中解像度：フル活用
            _ => Math.Min(baseParallelism, 4)               // 低解像度：制限
        };
    }

    /// <summary>
    /// 最小テキスト領域サイズを計算
    /// </summary>
    private static int CalculateMinTextRegionSize(int width, int height)
    {
        var resolution = width * height;
        return resolution switch
        {
            > 2000000 => 20, // 高解像度：大きめの最小サイズ
            > 1000000 => 15, // 中解像度：標準
            _ => 10          // 低解像度：小さめ
        };
    }

    /// <summary>
    /// チャンクグルーピング距離を計算
    /// </summary>
    private static double CalculateChunkGroupingDistance(int width, int height)
    {
        var diagonalLength = Math.Sqrt(width * width + height * height);
        return diagonalLength * 0.02; // 対角線長の2%
    }

    /// <summary>
    /// 低解像度スケールを計算
    /// </summary>
    private static float CalculateLowResolutionScale(int width, int height)
    {
        var pixelCount = width * height;
        return pixelCount switch
        {
            > 4000000 => 0.2f, // 超高解像度：大幅縮小
            > 2000000 => 0.25f, // 高解像度：標準縮小
            > 1000000 => 0.3f,  // 中解像度：軽微縮小
            _ => 0.5f           // 低解像度：最小縮小
        };
    }

    /// <summary>
    /// タイムアウトを計算
    /// </summary>
    private static int CalculateTimeout(int width, int height)
    {
        var pixelCount = width * height;
        var baseTimeout = 15000; // 15秒

        return pixelCount switch
        {
            > 4000000 => baseTimeout * 3, // 45秒
            > 2000000 => baseTimeout * 2, // 30秒
            > 1000000 => (int)(baseTimeout * 1.5), // 22.5秒
            _ => baseTimeout // 15秒
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;

        _processingSemaphore?.Dispose();
        // BatchOcrProcessorがIDisposableを実装しているため、キャストしてDispose
        if (_batchOcrProcessor is IDisposable disposableBatchProcessor)
        {
            disposableBatchProcessor.Dispose();
        }
        _disposed = true;

        _logger?.LogInformation("🧹 BatchOcrIntegrationService リソース解放完了");
    }
}