using System;
using System.IO;
using System.Linq;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;

namespace Baketa.Integration.Tests.GPU;

/// <summary>
/// GPU環境テスト用のヘルパークラス
/// 実機でのテストとモックテストの橋渡し
/// </summary>
public static class GPUEnvironmentTestHelper
{
    /// <summary>
    /// 現在の実行環境のGPU特性を推定
    /// </summary>
    public static GPUEnvironmentCategory EstimateCurrentEnvironment()
    {
        // 実機での簡易GPU検出（詳細な検出は実際のGPUEnvironmentDetectorで実行）
        // CIやテスト環境での実行を想定した簡易判定
        
        var hasNvidiaCuda = HasNvidiaRuntime();
        var hasAmdAcceleration = HasAmdAcceleration();
        var availableMemory = GetApproximateVideoMemory();

        if (hasNvidiaCuda || hasAmdAcceleration)
        {
            return availableMemory > 4000 ? 
                GPUEnvironmentCategory.HighEndDedicated : 
                GPUEnvironmentCategory.MidRangeDedicated;
        }

        // 統合GPUと推定
        return availableMemory > 1000 ? 
            GPUEnvironmentCategory.ModernIntegrated : 
            GPUEnvironmentCategory.LegacyIntegrated;
    }

    /// <summary>
    /// テスト環境に応じた推奨キャプチャ戦略
    /// </summary>
    public static string GetRecommendedStrategy(GPUEnvironmentCategory category)
    {
        return category switch
        {
            GPUEnvironmentCategory.HighEndDedicated => "ROIBased",
            GPUEnvironmentCategory.MidRangeDedicated => "ROIBased",
            GPUEnvironmentCategory.ModernIntegrated => "DirectFullScreen",
            GPUEnvironmentCategory.LegacyIntegrated => "GDIFallback",
            _ => "GDIFallback"
        };
    }

    /// <summary>
    /// テスト実行環境がCI環境かどうか
    /// </summary>
    public static bool IsRunningInCI()
    {
        var ciIndicators = new[]
        {
            "CI",
            "CONTINUOUS_INTEGRATION", 
            "GITHUB_ACTIONS",
            "AZURE_DEVOPS",
            "JENKINS_URL",
            "TEAMCITY_VERSION"
        };

        return ciIndicators.Any(indicator => 
            !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(indicator)));
    }

    /// <summary>
    /// 統合テスト用の適切なタイムアウト値を取得
    /// </summary>
    public static TimeSpan GetTestTimeout(GPUEnvironmentCategory category)
    {
        if (IsRunningInCI())
        {
            // CI環境では長めのタイムアウト
            return TimeSpan.FromSeconds(30);
        }

        return category switch
        {
            GPUEnvironmentCategory.HighEndDedicated => TimeSpan.FromSeconds(5),
            GPUEnvironmentCategory.MidRangeDedicated => TimeSpan.FromSeconds(10),
            GPUEnvironmentCategory.ModernIntegrated => TimeSpan.FromSeconds(15),
            GPUEnvironmentCategory.LegacyIntegrated => TimeSpan.FromSeconds(30),
            _ => TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// テスト用のモック戦略ファクトリ作成
    /// </summary>
    public static GpuEnvironmentInfo CreateTestEnvironment(GPUEnvironmentCategory category)
    {
        return category switch
        {
            GPUEnvironmentCategory.HighEndDedicated => new GpuEnvironmentInfo
            {
                IsIntegratedGpu = false,
                IsDedicatedGpu = true,
                SupportsCuda = true,
                SupportsOpenCL = true,
                SupportsDirectML = true,
                SupportsOpenVINO = false,
                SupportsTensorRT = true,
                MaximumTexture2DDimension = 16384,
                AvailableMemoryMB = 8192,
                GpuName = "High-End Dedicated GPU (Test)",
                DirectXFeatureLevel = DirectXFeatureLevel.D3D121,
                GpuDeviceId = 1,
                ComputeCapability = ComputeCapability.Compute75,
                RecommendedProviders = [ExecutionProvider.CUDA, ExecutionProvider.TensorRT]
            },
            
            GPUEnvironmentCategory.MidRangeDedicated => new GpuEnvironmentInfo
            {
                IsIntegratedGpu = false,
                IsDedicatedGpu = true,
                SupportsCuda = true,
                SupportsOpenCL = true,
                SupportsDirectML = true,
                SupportsOpenVINO = false,
                SupportsTensorRT = false,
                MaximumTexture2DDimension = 8192,
                AvailableMemoryMB = 4096,
                GpuName = "Mid-Range Dedicated GPU (Test)",
                DirectXFeatureLevel = DirectXFeatureLevel.D3D111,
                GpuDeviceId = 1,
                ComputeCapability = ComputeCapability.Compute61,
                RecommendedProviders = [ExecutionProvider.CUDA, ExecutionProvider.DirectML]
            },
            
            GPUEnvironmentCategory.ModernIntegrated => new GpuEnvironmentInfo
            {
                IsIntegratedGpu = true,
                IsDedicatedGpu = false,
                SupportsCuda = false,
                SupportsOpenCL = true,
                SupportsDirectML = true,
                SupportsOpenVINO = true,
                SupportsTensorRT = false,
                MaximumTexture2DDimension = 4096,
                AvailableMemoryMB = 2048,
                GpuName = "Modern Integrated GPU (Test)",
                DirectXFeatureLevel = DirectXFeatureLevel.D3D111,
                GpuDeviceId = 0,
                ComputeCapability = ComputeCapability.Unknown,
                RecommendedProviders = [ExecutionProvider.DirectML, ExecutionProvider.CPU]
            },
            
            GPUEnvironmentCategory.LegacyIntegrated => new GpuEnvironmentInfo
            {
                IsIntegratedGpu = true,
                IsDedicatedGpu = false,
                SupportsCuda = false,
                SupportsOpenCL = false,
                SupportsDirectML = true,
                SupportsOpenVINO = false,
                SupportsTensorRT = false,
                MaximumTexture2DDimension = 2048,
                AvailableMemoryMB = 512,
                GpuName = "Legacy Integrated GPU (Test)",
                DirectXFeatureLevel = DirectXFeatureLevel.D3D110,
                GpuDeviceId = 0,
                ComputeCapability = ComputeCapability.Unknown,
                RecommendedProviders = [ExecutionProvider.DirectML, ExecutionProvider.CPU]
            },
            
            _ => throw new ArgumentException($"Unsupported GPU category: {category}")
        };
    }

    private static bool HasNvidiaRuntime()
    {
        try
        {
            // CUDA や NVIDIA ドライバの存在チェック（簡易版）
            var nvidiaFiles = new[]
            {
                @"C:\Program Files\NVIDIA Corporation\NVSMI\nvidia-smi.exe",
                @"C:\Windows\System32\nvcuda.dll"
            };

            return nvidiaFiles.Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasAmdAcceleration()
    {
        try
        {
            // AMD GPU加速の存在チェック（簡易版）
            var amdFiles = new[]
            {
                @"C:\Windows\System32\amdocl64.dll",
                @"C:\Windows\System32\atiadlxx.dll"
            };

            return amdFiles.Any(File.Exists);
        }
        catch
        {
            return false;
        }
    }

    private static long GetApproximateVideoMemory()
    {
        try
        {
            // システムメモリベースの簡易推定
            // 実際のビデオメモリ検出は複雑なため、システムメモリから推定
            var totalMemory = GC.GetTotalMemory(false) / (1024 * 1024); // MB
            
            // システムメモリベースでビデオメモリを推定
            if (totalMemory > 16000) return 4096; // 16GB以上 → 4GB VRAM推定
            if (totalMemory > 8000) return 2048;  // 8GB以上 → 2GB VRAM推定
            if (totalMemory > 4000) return 1024;  // 4GB以上 → 1GB VRAM推定
            
            return 512; // それ以下 → 512MB推定
        }
        catch
        {
            return 512; // エラー時はデフォルト値
        }
    }
}

/// <summary>
/// GPU環境のカテゴリ分類
/// </summary>
public enum GPUEnvironmentCategory
{
    HighEndDedicated,    // 高性能専用GPU (RTX 3070+, RX 6700+)
    MidRangeDedicated,   // 中性能専用GPU (GTX 1660+, RX 5500+)
    ModernIntegrated,    // 現代的統合GPU (Intel UHD, AMD Vega)
    LegacyIntegrated     // レガシー統合GPU (Intel HD 4000)
}