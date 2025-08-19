using Microsoft.ML.OnnxRuntime;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Baketa.Infrastructure.OCR.GPU.Providers;

/// <summary>
/// OpenVINO Execution Provider Factory
/// Intel CPU/GPU最適化推論エンジンの統合
/// Phase 4.1: OpenVINO統合実装
/// </summary>
public sealed class OpenVINOExecutionProviderFactory(
    ILogger<OpenVINOExecutionProviderFactory> logger,
    OpenVINOSettings settings) : IExecutionProviderFactory
{
    private readonly ILogger<OpenVINOExecutionProviderFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly OpenVINOSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public ExecutionProvider Type => ExecutionProvider.OpenVINO;

    public bool IsSupported(GpuEnvironmentInfo environment)
    {
        try
        {
            // Intel CPUまたはGPUでOpenVINOサポート確認
            if (!environment.SupportsOpenVINO)
            {
                _logger.LogDebug("OpenVINO not supported by current environment");
                return false;
            }

            // Intel CPU優先、統合GPUでも利用可能
            var isIntelEnvironment = IsIntelEnvironment();
            if (!isIntelEnvironment)
            {
                _logger.LogDebug("OpenVINO optimized for Intel hardware, current environment may have limited performance");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check OpenVINO support");
            return false;
        }
    }

    public Dictionary<string, string> GetProviderOptions(GpuEnvironmentInfo environment)
    {
        try
        {
            var openvinoOptions = new Dictionary<string, string>();

            // デバイス設定（CPU優先、GPU利用可能な場合は併用）
            if (environment.IsIntegratedGpu && IsIntelGpu())
            {
                openvinoOptions["device_type"] = "GPU";
                _logger.LogInformation("OpenVINO configured for Intel integrated GPU");
            }
            else
            {
                openvinoOptions["device_type"] = "CPU";
                _logger.LogInformation("OpenVINO configured for CPU inference");
            }

            // パフォーマンス最適化設定
            openvinoOptions["num_of_threads"] = _settings.NumThreads.ToString();
            openvinoOptions["cache_dir"] = _settings.CacheDirectory;
            
            if (_settings.EnableDynamicShapes)
            {
                openvinoOptions["enable_dynamic_shapes"] = "true";
            }

            // Intel CPU最適化
            if (_settings.EnableCpuOptimization)
            {
                openvinoOptions["enable_cpu_mem_arena"] = "true";
                openvinoOptions["intra_op_num_threads"] = Environment.ProcessorCount.ToString();
            }

            // GPU最適化（Intel統合GPU）
            if (environment.IsIntegratedGpu && _settings.EnableGpuOptimization)
            {
                openvinoOptions["enable_opencl_throttling"] = "false";
                openvinoOptions["gpu_throughput_streams"] = "1";
            }

            _logger.LogInformation("OpenVINO provider options configured successfully");
            return openvinoOptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create OpenVINO provider options");
            throw;
        }
    }

    public int Priority(GpuEnvironmentInfo environment)
    {
        // Gemini推奨: 設定ファイルから基本優先度を取得し、環境に応じて微調整
        var basePriority = _settings.Priority;
        
        if (IsIntelEnvironment())
        {
            // Intel環境: 統合GPU vs CPU の違いを考慮
            var intelBonus = environment.IsIntegratedGpu ? 5 : 0;
            return basePriority + intelBonus;
        }
        
        // AMD/その他環境では大幅に優先度を下げる
        return Math.Max(basePriority - 50, 10);
    }

    public string GetProviderInfo(GpuEnvironmentInfo environment)
    {
        var deviceType = environment.IsIntegratedGpu && IsIntelGpu() ? "Intel iGPU" : "Intel CPU";
        return $"OpenVINO Provider on {deviceType}";
    }

    private static bool IsIntelEnvironment()
    {
        try
        {
            // Intel CPU判定（簡易版）
            var processorName = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? string.Empty;
            return processorName.Contains("Intel", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsIntelGpu()
    {
        // Intel GPU判定ロジック（WindowsGpuEnvironmentDetector連携想定）
        // 実装詳細は環境検出システムに委譲
        return true; // 簡易実装
    }
}

/// <summary>
/// OpenVINO設定
/// appsettings.json "OpenVINO" セクションで設定
/// </summary>
public sealed record OpenVINOSettings
{
    public bool Enabled { get; init; } = true;
    public int NumThreads { get; init; } = Environment.ProcessorCount;
    public string CacheDirectory { get; init; } = Path.Combine(Path.GetTempPath(), "openvino_cache");
    public bool EnableDynamicShapes { get; init; } = false;
    public bool EnableCpuOptimization { get; init; } = true;
    public bool EnableGpuOptimization { get; init; } = true;
    public int Priority { get; init; } = 80;
    public bool EnableFallback { get; init; } = true;
}
