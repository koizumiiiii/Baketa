using Microsoft.Extensions.Logging;
using System.Drawing;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.StickyRoi;

namespace Baketa.Infrastructure.OCR;

/// <summary>
/// SimpleOcrEngineAdapterをIGpuOcrEngineとして使用するための暫定アダプター
/// Sprint 2 Phase 1: Mock除去とROI統合基盤
/// </summary>
public sealed class SimpleOcrEngineGpuAdapter : IGpuOcrEngine
{
    private readonly SimpleOcrEngineAdapter _simpleOcrEngineAdapter;
    private readonly ILogger<SimpleOcrEngineGpuAdapter> _logger;
    private bool _disposed;

    public SimpleOcrEngineGpuAdapter(
        SimpleOcrEngineAdapter simpleOcrEngineAdapter,
        ILogger<SimpleOcrEngineGpuAdapter> logger)
    {
        _simpleOcrEngineAdapter = simpleOcrEngineAdapter ?? throw new ArgumentNullException(nameof(simpleOcrEngineAdapter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _logger.LogInformation("🔌 SimpleOcrEngineGpuAdapter初期化完了 - Sprint 2 Phase 1暫定実装");
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return _simpleOcrEngineAdapter.IsAvailableAsync(cancellationToken);
    }

    public async Task<Baketa.Core.Abstractions.OCR.OcrResult> RecognizeTextAsync(byte[] imageData, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("🔌 SimpleOcrEngine GPU Adapter経由でOCR実行 - データサイズ: {Size}B", imageData.Length);
            
            // SimpleOcrEngineAdapterに処理を委譲
            var result = await _simpleOcrEngineAdapter.RecognizeTextAsync(imageData, cancellationToken);
            
            // GPU Adapter固有のメタデータ追加（init専用のため新しいインスタンス作成）
            var enhancedMetadata = new Dictionary<string, object>(result.Metadata ?? new Dictionary<string, object>())
            {
                ["GpuAdapterMode"] = "SimpleOcrEngine",
                ["Sprint2Phase"] = "Mock除去完了",
                ["ActualPaddleOCR"] = "有効"
            };
            
            result = new OcrResult
            {
                DetectedTexts = result.DetectedTexts,
                IsSuccessful = result.IsSuccessful,
                ProcessingTime = result.ProcessingTime,
                ErrorMessage = result.ErrorMessage,
                Metadata = enhancedMetadata
            };
            
            _logger.LogDebug("✅ SimpleOcrEngine GPU Adapter処理完了 - 検出数: {Count}", result.DetectedTexts.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SimpleOcrEngine GPU Adapter処理失敗");
            throw;
        }
    }

    public Task<GpuEnvironmentInfo> GetGpuEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        // 暫定的なGPU環境情報を返却
        var gpuInfo = new GpuEnvironmentInfo
        {
            IsDedicatedGpu = true,
            SupportsCuda = true,  // 設定依存
            SupportsDirectML = true,
            SupportsOpenCL = false,
            SupportsOpenVINO = false,
            SupportsTensorRT = false,
            AvailableMemoryMB = 4096, // 推定値
            GpuName = "PaddleOCR via SimpleOcrEngine",
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Compute75,
            RecommendedProviders = [ExecutionProvider.CUDA, ExecutionProvider.CPU],
            MaximumTexture2DDimension = 16384,
            DirectXFeatureLevel = DirectXFeatureLevel.D3D120
        };

        _logger.LogDebug("🔧 GPU環境情報取得 - Mode: {Mode}", "SimpleOcrEngine");
        
        return Task.FromResult(gpuInfo);
    }

    public Task<bool> UpdateExecutionProviderAsync(
        ExecutionProviderType providerType, 
        string? deviceId = null, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🔧 実行プロバイダー更新要求: {Provider}, デバイス: {Device}", 
            providerType, deviceId ?? "default");
        
        // SimpleOcrEngineAdapterは設定ファイルベースでGPU/CPU切り替えを行うため、
        // 動的変更は制限される。常に成功として返答
        
        return Task.FromResult(true);
    }

    public Task<long> GetMemoryUsageAsync(CancellationToken cancellationToken = default)
    {
        // 推定メモリ使用量
        var estimatedMemoryMB = 2048L; // 2GB推定
        
        _logger.LogDebug("📊 推定メモリ使用量: {Memory}MB", estimatedMemoryMB);
        
        return Task.FromResult(estimatedMemoryMB);
    }

    public Task<GpuOcrStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        // 暫定的な統計情報
        var stats = new GpuOcrStatistics
        {
            TotalExecutions = 0,      // 実際の統計は今後実装
            SuccessfulExecutions = 0,
            AverageExecutionTime = TimeSpan.FromMilliseconds(500), // 推定値
            PeakMemoryUsageMB = 2048,
            GpuUtilization = 0.5,     // 推定値
            ErrorCount = 0,
            LastUpdated = DateTime.UtcNow
        };

        _logger.LogDebug("📊 統計情報取得 - Mode: SimpleOcrEngine暫定");
        
        return Task.FromResult(stats);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _simpleOcrEngineAdapter?.Dispose();
            _logger.LogInformation("🧹 SimpleOcrEngineGpuAdapter解放完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ SimpleOcrEngine GPU Adapter解放エラー");
        }
        
        _disposed = true;
    }
}