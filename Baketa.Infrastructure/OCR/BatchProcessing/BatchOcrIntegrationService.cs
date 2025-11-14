using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.OCR; // 🔥 [FIX7_STEP2] OcrContext統合
using Baketa.Infrastructure.ResourceManagement;
using Microsoft.Extensions.Logging;

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
    private readonly IResourceManager _resourceManager;

    private readonly SemaphoreSlim _processingSemaphore;
    private bool _disposed;

    public BatchOcrIntegrationService(
        IBatchOcrProcessor batchOcrProcessor,
        IOcrEngine fallbackOcrEngine,
        IResourceManager resourceManager,
        ILogger<BatchOcrIntegrationService>? logger = null)
    {
        _batchOcrProcessor = batchOcrProcessor ?? throw new ArgumentNullException(nameof(batchOcrProcessor));
        _fallbackOcrEngine = fallbackOcrEngine ?? throw new ArgumentNullException(nameof(fallbackOcrEngine));
        _resourceManager = resourceManager ?? throw new ArgumentNullException(nameof(resourceManager));
        _logger = logger;

        // 並列処理制限（CPUコア数に基づく）- HybridResourceManagerでの制御に段階的移行予定
        var maxConcurrency = Math.Max(1, Environment.ProcessorCount - 1);
        _processingSemaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
    }

    /// <summary>
    /// 統合OCR処理 - バッチ処理とフォールバックの組み合わせ
    /// Phase 2統合: HybridResourceManager経由でリソース制御付き処理を実行
    /// FIX7 Step2: OcrContext対応 - CaptureRegion情報を保持
    /// </summary>
    public async Task<IReadOnlyList<TextChunk>> ProcessWithIntegratedOcrAsync(
        OcrContext context)
    {
        ThrowIfDisposed();

        _logger?.LogInformation("🔥 [FIX7_STEP2] ProcessWithIntegratedOcrAsync開始 - CaptureRegion: {HasCaptureRegion}, Value: {CaptureRegion}",
            context.HasCaptureRegion,
            context.HasCaptureRegion ? $"({context.CaptureRegion.Value.X},{context.CaptureRegion.Value.Y},{context.CaptureRegion.Value.Width}x{context.CaptureRegion.Value.Height})" : "null");

        // HybridResourceManager経由でリソース制御付きOCR処理を実行
        var request = new ProcessingRequest(
            ImagePath: $"InMemory_{DateTime.UtcNow:yyyyMMddHHmmssfff}",
            OperationId: Guid.NewGuid().ToString(),
            Timestamp: DateTime.UtcNow
        );

        return await _resourceManager.ProcessOcrAsync(
            async (req, ct) =>
            {
                _logger?.LogInformation("🔄 [HybridResourceManager] 統合OCR処理開始 - 画像: {Width}x{Height}, OperationId: {OperationId}",
                    context.Image.Width, context.Image.Height, req.OperationId);

                // 1. バッチOCR処理を試行（レガシーセマフォア制御付き）
                await _processingSemaphore.WaitAsync(ct).ConfigureAwait(false);

                try
                {
                    var chunks = await TryBatchOcrProcessingAsync(context, ct).ConfigureAwait(false);

                    // 2. バッチ処理結果の検証
                    if (IsValidOcrResult(chunks))
                    {
                        _logger?.LogInformation("✅ [HybridResourceManager] バッチOCR処理成功 - チャンク数: {ChunkCount}", chunks.Count);
                        return chunks;
                    }

                    // 3. フォールバック処理
                    _logger?.LogWarning("⚠️ [HybridResourceManager] バッチOCR結果不十分、フォールバック処理実行");
                    return await ExecuteFallbackOcrAsync(context, ct).ConfigureAwait(false);
                }
                finally
                {
                    _processingSemaphore.Release();
                }
            },
            request,
            context.CancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 複数画像の並列バッチ処理
    /// FIX7 Step2: OcrContext対応
    /// </summary>
    public async Task<IReadOnlyList<IReadOnlyList<TextChunk>>> ProcessMultipleImagesAsync(
        IReadOnlyList<OcrContext> contexts,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (contexts.Count == 0)
            return [];

        _logger?.LogInformation("📦 複数画像並列処理開始 - 画像数: {ImageCount}", contexts.Count);

        // 並列処理タスクを作成
        var tasks = contexts.Select(async context =>
        {
            try
            {
                return await ProcessWithIntegratedOcrAsync(context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "❌ 画像処理エラー - サイズ: {Width}x{Height}",
                    context.Image.Width, context.Image.Height);
                return (IReadOnlyList<TextChunk>)[];
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
    /// FIX7 Step2: OcrContext対応
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> TryBatchOcrProcessingAsync(
        OcrContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            // 画像サイズに基づく最適化
            await OptimizeBatchPerformanceAsync(context.Image.Width, context.Image.Height, cancellationToken).ConfigureAwait(false);

            // 🎯 [OPTION_B_PHASE2] IImage → IAdvancedImage キャスト
            if (context.Image is not IAdvancedImage advancedImage)
            {
                throw new InvalidOperationException($"バッチOCR処理にはIAdvancedImageが必要です（実際の型: {context.Image.GetType().Name}）");
            }

            // バッチ処理実行
            return await _batchOcrProcessor.ProcessBatchAsync(advancedImage, context.WindowHandle, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ バッチOCR処理エラー");
            return [];
        }
    }

    /// <summary>
    /// フォールバックOCR処理
    /// FIX7 Step2: OcrContext対応 - **ROOT CAUSE FIX**: CaptureRegionをTextChunkに設定
    /// </summary>
    private async Task<IReadOnlyList<TextChunk>> ExecuteFallbackOcrAsync(
        OcrContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var ocrResults = await _fallbackOcrEngine.RecognizeAsync(context.Image, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!ocrResults.HasText)
                return [];

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

                // 🔥 [FIX7_ROOT_CAUSE_FIX] CaptureRegionをTextChunkに設定 - これがFIX7の根本原因修正
                var chunk = new TextChunk
                {
                    ChunkId = i,
                    TextResults = [positionedResult],
                    CombinedBounds = region.Bounds,
                    CombinedText = region.Text,
                    SourceWindowHandle = context.WindowHandle,
                    DetectedLanguage = ocrResults.LanguageCode,
                    CaptureRegion = context.CaptureRegion // ✅ [FIX7_CRITICAL] ROI座標ズレ問題の根本原因修正
                };

                chunks.Add(chunk);
            }

            _logger?.LogInformation("🔥 [FIX7_STEP2] フォールバックOCR完了 - チャンク数: {ChunkCount}, CaptureRegion設定: {HasCaptureRegion}",
                chunks.Count, context.HasCaptureRegion);

            return chunks;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "❌ フォールバックOCR処理エラー");
            return [];
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
