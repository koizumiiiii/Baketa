using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.Infrastructure.OCR.Services;

/// <summary>
/// バッチOCR処理のNo-Op実装
/// NOTE: [PP-OCRv5削除] PP-OCRv5削除後のDI解決用スタブ実装
/// Surya OCRでは異なるパイプラインを使用するため、このインターフェースは実質的に不要
/// TranslationProcessingFacade等の既存DIコンテナ要件を満たすために存在
/// </summary>
public sealed class NoOpBatchOcrProcessor : IBatchOcrProcessor, IOcrFailureManager
{
    private DateTime? _lastResetTime;

    /// <inheritdoc />
    public bool IsOcrAvailable => true;

    /// <inheritdoc />
    public int MaxFailureThreshold => 3;

    /// <inheritdoc />
    public DateTime? LastResetTime => _lastResetTime;

    /// <inheritdoc />
    public void ResetFailureCounter()
    {
        _lastResetTime = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public int GetFailureCount() => 0;

    /// <inheritdoc />
    public Task<IReadOnlyList<TextChunk>> ProcessBatchAsync(
        IAdvancedImage image,
        IntPtr windowHandle,
        CancellationToken cancellationToken = default)
    {
        // NOTE: [PP-OCRv5削除] Surya OCRでは TranslationOrchestrationService 経由で処理されるため、
        // このメソッドは呼び出されない。互換性のため空リストを返す。
        return Task.FromResult<IReadOnlyList<TextChunk>>([]);
    }

    /// <inheritdoc />
    public Task ConfigureBatchProcessingAsync(BatchOcrOptions options)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public BatchOcrMetrics GetPerformanceMetrics()
    {
        return new BatchOcrMetrics
        {
            TotalProcessedCount = 0,
            AverageProcessingTimeMs = 0,
            LastProcessingTimeMs = 0,
            AverageTextCount = 0,
            AverageConfidence = 0,
            ParallelEfficiency = 0,
            CacheHitRate = 0,
            MemoryUsageMB = 0,
            ErrorRate = 0
        };
    }

    /// <inheritdoc />
    public Task ClearCacheAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public IReadOnlyList<RoiImageInfo> GetCurrentSessionRoiImages()
    {
        return [];
    }

    /// <inheritdoc />
    public void ClearRoiImageInfo()
    {
        // No-op
    }
}
