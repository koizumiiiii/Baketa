using Baketa.Core.Abstractions.GPU;
using Microsoft.Extensions.Logging;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.OCR.GPU.Providers;

/// <summary>
/// CPU Execution Provider Factory
/// フォールバック用CPU推論プロバイダー
/// Phase 4: 確実なフォールバック推論環境提供
/// </summary>
public sealed class CpuExecutionProviderFactory(ILogger<CpuExecutionProviderFactory> logger) : IExecutionProviderFactory
{
    private readonly ILogger<CpuExecutionProviderFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public ExecutionProvider Type => ExecutionProvider.CPU;

    public bool IsSupported(GpuEnvironmentInfo environment)
    {
        // CPUは常にサポート
        _logger.LogDebug("CPU provider always supported as fallback");
        return true;
    }

    public Dictionary<string, string> GetProviderOptions(GpuEnvironmentInfo environment)
    {
        var cpuOptions = new Dictionary<string, string>
        {
            // CPU最適化設定
            ["intra_op_num_threads"] = Environment.ProcessorCount.ToString(),
            ["inter_op_num_threads"] = Math.Max(1, Environment.ProcessorCount / 2).ToString()
        };

        _logger.LogDebug("CPU provider options configured: {ThreadCount} threads", Environment.ProcessorCount);
        return cpuOptions;
    }

    public int Priority(GpuEnvironmentInfo environment)
    {
        // CPU は最低優先度（フォールバック）
        return 10;
    }

    public string GetProviderInfo(GpuEnvironmentInfo environment)
    {
        return $"CPU Provider ({Environment.ProcessorCount} threads)";
    }
}
