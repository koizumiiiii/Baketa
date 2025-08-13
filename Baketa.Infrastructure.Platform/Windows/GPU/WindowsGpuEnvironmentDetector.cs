using Baketa.Core.Abstractions.GPU;
using Microsoft.Extensions.Logging;
using System.Management;
using System.Runtime.InteropServices;

namespace Baketa.Infrastructure.Platform.Windows.GPU;

/// <summary>
/// Windows環境でのGPU検出実装
/// WMI + DirectX APIを使用したハードウェア情報取得
/// </summary>
public sealed class WindowsGpuEnvironmentDetector : IGpuEnvironmentDetector, IDisposable
{
    private readonly ILogger<WindowsGpuEnvironmentDetector> _logger;
    private readonly object _lockObject = new();
    private GpuEnvironmentInfo? _cachedEnvironment;
    private bool _disposed;

    public WindowsGpuEnvironmentDetector(ILogger<WindowsGpuEnvironmentDetector> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<GpuEnvironmentInfo> DetectEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        lock (_lockObject)
        {
            if (_cachedEnvironment != null)
            {
                _logger.LogDebug("キャッシュ済みGPU環境情報を返却: {GpuName}", _cachedEnvironment.GpuName);
                return _cachedEnvironment;
            }
        }

        _logger.LogInformation("GPU環境検出開始");
        
        try
        {
            var environment = await Task.Run(() => DetectGpuEnvironmentInternal(), cancellationToken).ConfigureAwait(false);
            
            lock (_lockObject)
            {
                _cachedEnvironment = environment;
            }
            
            _logger.LogInformation("GPU環境検出完了: {GpuName}, CUDA:{SupportsCuda}, DirectML:{SupportsDirectML}, VRAM:{AvailableMemoryMB}MB", 
                environment.GpuName, environment.SupportsCuda, environment.SupportsDirectML, environment.AvailableMemoryMB);
            
            return environment;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GPU環境検出失敗");
            
            // フォールバック環境（CPU専用）を返却
            var fallbackEnvironment = CreateFallbackEnvironment();
            
            lock (_lockObject)
            {
                _cachedEnvironment = fallbackEnvironment;
            }
            
            return fallbackEnvironment;
        }
    }

    public GpuEnvironmentInfo? GetCachedEnvironment()
    {
        lock (_lockObject)
        {
            return _cachedEnvironment;
        }
    }

    public async Task<GpuEnvironmentInfo> RefreshEnvironmentAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("GPU環境情報強制更新");
        
        lock (_lockObject)
        {
            _cachedEnvironment = null;
        }
        
        return await DetectEnvironmentAsync(cancellationToken).ConfigureAwait(false);
    }

    private GpuEnvironmentInfo DetectGpuEnvironmentInternal()
    {
        var gpuInfo = DetectPrimaryGpu();
        var directXLevel = DetectDirectXFeatureLevel();
        var (AvailableMemoryMB, MaxTexture2DDimension) = DetectGpuMemory(gpuInfo.IsIntegratedGpu);
        var capabilities = DetectGpuCapabilities(gpuInfo);
        
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = gpuInfo.IsIntegratedGpu,
            IsDedicatedGpu = gpuInfo.IsDedicatedGpu,
            SupportsCuda = capabilities.SupportsCuda,
            SupportsOpenCL = capabilities.SupportsOpenCL,
            SupportsDirectML = capabilities.SupportsDirectML,
            SupportsOpenVINO = capabilities.SupportsOpenVINO,
            SupportsTensorRT = capabilities.SupportsTensorRT,
            AvailableMemoryMB = AvailableMemoryMB,
            MaximumTexture2DDimension = MaxTexture2DDimension,
            DirectXFeatureLevel = directXLevel,
            GpuName = gpuInfo.GpuName,
            ComputeCapability = capabilities.ComputeCapability,
            RecommendedProviders = DetermineRecommendedProviders(capabilities, gpuInfo)
        };
    }

    private (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) DetectPrimaryGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_VideoController WHERE Availability = 3");
            using var results = searcher.Get();
            
            var gpus = new List<(string Name, uint AdapterRAM, string PNPDeviceID)>();
            
            foreach (ManagementObject gpu in results.Cast<ManagementObject>())
            {
                var name = gpu["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRAM = Convert.ToUInt32(gpu["AdapterRAM"] ?? 0);
                var pnpDeviceID = gpu["PNPDeviceID"]?.ToString() ?? "";
                
                gpus.Add((name, adapterRAM, pnpDeviceID));
            }
            
            // 専用GPU優先（VRAM容量で判定）
            var (Name, AdapterRAM, PNPDeviceID) = gpus.OrderByDescending(g => g.AdapterRAM).First();
            var isIntegrated = IsIntegratedGpu(Name, PNPDeviceID);
            
            return (Name, isIntegrated, !isIntegrated);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WMI GPU検出失敗");
            return ("Unknown GPU", false, false);
        }
    }

    private static bool IsIntegratedGpu(string gpuName, string pnpDeviceID)
    {
        string[] integratedIndicators =
        [
            "Intel", "UHD", "Iris", "HD Graphics",
            "AMD Radeon Graphics", "Vega",
            "Microsoft Basic"
        ];
        
        return integratedIndicators.Any(indicator => 
            gpuName.Contains(indicator, StringComparison.OrdinalIgnoreCase)) ||
               pnpDeviceID.Contains("VEN_8086", StringComparison.OrdinalIgnoreCase); // Intel Vendor ID
    }

    private DirectXFeatureLevel DetectDirectXFeatureLevel()
    {
        try
        {
            // D3D11CreateDevice を使用してフィーチャーレベル検出
            var featureLevel = NativeMethods.GetDirectXFeatureLevel();
            return featureLevel switch
            {
                0xc100 => DirectXFeatureLevel.D3D122,
                0xc000 => DirectXFeatureLevel.D3D121,
                0xb100 => DirectXFeatureLevel.D3D120,
                0xb000 => DirectXFeatureLevel.D3D111,
                0xa100 => DirectXFeatureLevel.D3D111,
                0xa000 => DirectXFeatureLevel.D3D110,
                _ => DirectXFeatureLevel.Unknown
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DirectXフィーチャーレベル検出失敗");
            return DirectXFeatureLevel.D3D110; // 安全なフォールバック
        }
    }

    private (long AvailableMemoryMB, int MaxTexture2DDimension) DetectGpuMemory(bool isIntegratedGpu)
    {
        try
        {
            if (isIntegratedGpu)
            {
                // 統合GPU: システムRAMの1/4を利用可能とみなす
                var totalRamMB = GC.GetTotalMemory(false) / (1024 * 1024);
                var availableMemoryMB = Math.Max(1024, totalRamMB / 4); // 最低1GB
                return (availableMemoryMB, 8192); // 統合GPUは通常8Kテクスチャまで
            }
            else
            {
                // 専用GPU: WMIからVRAM容量取得
                using var searcher = new ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController WHERE Availability = 3");
                using var results = searcher.Get();
                
                var maxVram = results.Cast<ManagementObject>()
                    .Select(gpu => Convert.ToUInt64(gpu["AdapterRAM"] ?? 0))
                    .Max();
                
                var availableMemoryMB = maxVram > 0 ? (long)(maxVram / (1024 * 1024)) : 4096; // フォールバック4GB
                var maxTexture = availableMemoryMB >= 8192 ? 16384 : 8192; // 8GB以上なら16Kテクスチャ
                
                return (availableMemoryMB, maxTexture);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GPU メモリ情報取得失敗");
            return (4096, 8192); // 安全なフォールバック
        }
    }

    private (bool SupportsCuda, bool SupportsOpenCL, bool SupportsDirectML, bool SupportsOpenVINO, bool SupportsTensorRT, ComputeCapability ComputeCapability) DetectGpuCapabilities(
        (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) gpuInfo)
    {
        var capabilities = (
            SupportsCuda: false,
            SupportsOpenCL: false,
            SupportsDirectML: true, // Windows 10以降は基本的に対応
            SupportsOpenVINO: false,
            SupportsTensorRT: false,
            ComputeCapability: ComputeCapability.Unknown
        );

        var gpuName = gpuInfo.GpuName.ToLowerInvariant();
        
        // NVIDIA GPU検出
        if (gpuName.Contains("nvidia") || gpuName.Contains("geforce") || gpuName.Contains("rtx") || gpuName.Contains("gtx"))
        {
            capabilities.SupportsCuda = true;
            capabilities.SupportsOpenCL = true;
            capabilities.ComputeCapability = DetectNvidiaComputeCapability(gpuName);
            
            // RTXシリーズはTensorRT対応
            if (gpuName.Contains("rtx"))
            {
                capabilities.SupportsTensorRT = true;
            }
        }
        // Intel GPU検出  
        else if (gpuName.Contains("intel") || gpuName.Contains("uhd") || gpuName.Contains("iris"))
        {
            capabilities.SupportsOpenCL = true;
            capabilities.SupportsOpenVINO = true; // Intel最適化
        }
        // AMD GPU検出
        else if (gpuName.Contains("amd") || gpuName.Contains("radeon"))
        {
            capabilities.SupportsOpenCL = true;
        }

        return capabilities;
    }

    private static ComputeCapability DetectNvidiaComputeCapability(string gpuName)
    {
        // RTX 4000シリーズ (Ada Lovelace) - RTX4070等
        if (gpuName.Contains("rtx 40") || gpuName.Contains("rtx40"))
            return ComputeCapability.Compute89;
        
        // RTX 3000シリーズ (Ampere)
        if (gpuName.Contains("rtx 30") || gpuName.Contains("rtx30"))
            return ComputeCapability.Compute86;
        
        // RTX 2000シリーズ (Turing)
        if (gpuName.Contains("rtx 20") || gpuName.Contains("rtx20"))
            return ComputeCapability.Compute75;
        
        // GTX 1000シリーズ (Pascal)  
        if (gpuName.Contains("gtx 16") || gpuName.Contains("gtx16") || 
            gpuName.Contains("gtx 10") || gpuName.Contains("gtx10"))
            return ComputeCapability.Compute61;
        
        // GTX 900シリーズ (Maxwell)
        if (gpuName.Contains("gtx 9") || gpuName.Contains("gtx9"))
            return ComputeCapability.Compute50;
        
        return ComputeCapability.Unknown;
    }

    private static IReadOnlyList<ExecutionProvider> DetermineRecommendedProviders(
        (bool SupportsCuda, bool SupportsOpenCL, bool SupportsDirectML, bool SupportsOpenVINO, bool SupportsTensorRT, ComputeCapability ComputeCapability) capabilities,
        (string GpuName, bool IsIntegratedGpu, bool IsDedicatedGpu) gpuInfo)
    {
        var providers = new List<ExecutionProvider>();
        
        if (gpuInfo.IsDedicatedGpu)
        {
            // RTX専用GPU - 最高性能順
            if (capabilities.SupportsTensorRT)
                providers.Add(ExecutionProvider.TensorRT);
            
            if (capabilities.SupportsCuda)
                providers.Add(ExecutionProvider.CUDA);
        }
        
        if (gpuInfo.IsIntegratedGpu)
        {
            // Intel統合GPU最適化
            if (capabilities.SupportsOpenVINO)
                providers.Add(ExecutionProvider.OpenVINO);
        }
        
        // 共通プロバイダー
        if (capabilities.SupportsDirectML)
            providers.Add(ExecutionProvider.DirectML);
        
        if (capabilities.SupportsOpenCL)
            providers.Add(ExecutionProvider.OpenCL);
        
        // 最終フォールバック
        providers.Add(ExecutionProvider.CPU);
        
        return providers.AsReadOnly();
    }

    private static GpuEnvironmentInfo CreateFallbackEnvironment()
    {
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = false,
            IsDedicatedGpu = false,
            SupportsCuda = false,
            SupportsOpenCL = false,
            SupportsDirectML = false,
            SupportsOpenVINO = false,
            SupportsTensorRT = false,
            AvailableMemoryMB = 0,
            MaximumTexture2DDimension = 4096,
            DirectXFeatureLevel = DirectXFeatureLevel.Unknown,
            GpuName = "Fallback CPU",
            ComputeCapability = ComputeCapability.Unknown,
            RecommendedProviders = [ExecutionProvider.CPU]
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// Windows Native API呼び出し
/// </summary>
internal static class NativeMethods
{
    [DllImport("d3d11.dll", SetLastError = true)]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out IntPtr ppDevice,
        out uint pFeatureLevel,
        out IntPtr ppImmediateContext);

    internal static uint GetDirectXFeatureLevel()
    {
        try
        {
            var hr = D3D11CreateDevice(
                IntPtr.Zero, 0, IntPtr.Zero, 0,
                IntPtr.Zero, 0, 7,
                out var device, out var featureLevel, out var context);
            
            if (hr >= 0)
            {
                // COM リソース解放
                if (device != IntPtr.Zero) Marshal.Release(device);
                if (context != IntPtr.Zero) Marshal.Release(context);
                
                return featureLevel;
            }
        }
        catch
        {
            // 失敗時は安全な値を返す
        }
        
        return 0xa000; // D3D_FEATURE_LEVEL_10_0 フォールバック
    }
}
