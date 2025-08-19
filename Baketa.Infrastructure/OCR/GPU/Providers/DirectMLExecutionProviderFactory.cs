using Microsoft.ML.OnnxRuntime;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.GPU.Providers;

/// <summary>
/// DirectML Execution Provider Factory
/// Windows統一GPU推論API（NVIDIA/AMD/Intel対応）
/// Phase 4.2: DirectML対応実装
/// </summary>
public sealed class DirectMLExecutionProviderFactory(
    ILogger<DirectMLExecutionProviderFactory> logger,
    DirectMLSettings settings) : IExecutionProviderFactory
{
    private readonly ILogger<DirectMLExecutionProviderFactory> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly DirectMLSettings _settings = settings ?? throw new ArgumentNullException(nameof(settings));

    public ExecutionProvider Type => ExecutionProvider.DirectML;

    public bool IsSupported(GpuEnvironmentInfo environment)
    {
        try
        {
            // Windows環境チェック
            if (!OperatingSystem.IsWindows())
            {
                _logger.LogDebug("DirectML requires Windows");
                return false;
            }

            // DirectMLサポート確認（Windows環境では基本的にサポート）
            if (!environment.SupportsDirectML)
            {
                _logger.LogDebug("DirectML not supported by current environment");
                // Windows環境でGPUが利用可能な場合はDirectMLを強制有効化
                if (!environment.IsGpuAvailable)
                {
                    return false;
                }
                _logger.LogInformation("DirectML support forced enabled for GPU environment");
            }

            // DirectX Feature Levelチェック
            if (environment.DirectXFeatureLevel < DirectXFeatureLevel.D3D111)
            {
                _logger.LogDebug("DirectML requires DirectX 11.1 or higher");
                return false;
            }

            // GPU利用可能性確認  
            if (!environment.IsGpuAvailable)
            {
                _logger.LogDebug("No GPU available for DirectML");
                return false;
            }
            
            _logger.LogDebug("DirectML provider supported for {GpuName} (CUDA:{SupportsCuda}, DirectML:{SupportsDirectML})", 
                environment.GpuName, environment.SupportsCuda, environment.SupportsDirectML);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check DirectML support");
            return false;
        }
    }

    public Dictionary<string, string> GetProviderOptions(GpuEnvironmentInfo environment)
    {
        try
        {
            var directmlOptions = new Dictionary<string, string>();

            // GPU デバイス選択
            directmlOptions["device_id"] = environment.GpuDeviceId.ToString();
            
            // メモリ最適化
            if (environment.AvailableMemoryMB > 2048)
            {
                directmlOptions["enable_graph_capture"] = "true";
                directmlOptions["enable_dynamic_graph_fusion"] = "true";
            }

            // 統合GPU最適化
            if (environment.IsIntegratedGpu)
            {
                directmlOptions["disable_metacommands"] = "false"; // 統合GPU向け最適化
                directmlOptions["enable_cpu_sync_spinning"] = "false"; // 電力効率重視
                _logger.LogInformation("DirectML configured for integrated GPU with power efficiency");
            }
            else
            {
                // 専用GPU最適化
                directmlOptions["disable_metacommands"] = "false";
                directmlOptions["enable_cpu_sync_spinning"] = "true"; // 性能優先
                _logger.LogInformation("DirectML configured for dedicated GPU with performance priority");
            }

            // 高性能GPU向け追加最適化
            if (environment.IsHighPerformanceGpu)
            {
                directmlOptions["enable_graph_serialization"] = "true";
                directmlOptions["graph_optimization_level"] = "all";
            }

            _logger.LogInformation("DirectML provider options configured successfully for {GpuName}", environment.GpuName);
            return directmlOptions;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create DirectML provider options");
            throw;
        }
    }

    public int Priority(GpuEnvironmentInfo environment)
    {
        // Gemini推奨: 設定ファイルから基本優先度を取得し、環境に応じて微調整
        var basePriority = _settings.Priority;
        
        // 環境に応じた優先度調整
        if (environment.SupportsCuda)
        {
            // CUDA対応NVIDIAではCUDAを優先（DirectMLは基本優先度-15）
            return Math.Max(basePriority - 15, 10);
        }

        if (IsAmdGpu(environment))
        {
            // AMD GPU: DirectMLが最適（基本優先度＋環境ボーナス）
            var amdBonus = environment.IsHighPerformanceGpu ? 10 : 5;
            return basePriority + amdBonus;
        }

        if (environment.IsIntegratedGpu)
        {
            // Intel統合GPU: OpenVINOとの併用を考慮（基本優先度そのまま）
            return basePriority;
        }

        // その他のGPU: 基本優先度から少し下げる
        return Math.Max(basePriority - 5, 10);
    }

    public string GetProviderInfo(GpuEnvironmentInfo environment)
    {
        var gpuType = environment.IsIntegratedGpu ? "Integrated" : "Dedicated";
        var memoryInfo = $"{environment.AvailableMemoryMB}MB VRAM";
        return $"DirectML Provider on {gpuType} GPU ({memoryInfo})";
    }

    private static bool IsAmdGpu(GpuEnvironmentInfo environment)
    {
        return environment.GpuName.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
               environment.GpuName.Contains("Radeon", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// DirectML設定
/// appsettings.json "DirectML" セクションで設定
/// </summary>
public sealed record DirectMLSettings
{
    public bool Enabled { get; init; } = true;
    public int DeviceId { get; init; } = 0;
    public bool EnableGraphCapture { get; init; } = true;
    public bool EnableDynamicGraphFusion { get; init; } = true;
    public bool EnableMetacommands { get; init; } = true;
    public bool EnableDebugLogging { get; init; } = false;
    public bool EnableFallback { get; init; } = true;
    public int Priority { get; init; } = 75;
}
