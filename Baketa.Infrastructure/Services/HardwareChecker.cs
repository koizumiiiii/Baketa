using System.Management;
using System.Runtime.Versioning;
using Baketa.Core.Abstractions.Hardware;
using Baketa.Core.Models.Hardware;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// [Issue #335] ハードウェアスペックチェッカー実装
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareChecker : IHardwareChecker
{
    private readonly ILogger<HardwareChecker> _logger;

    public HardwareChecker(ILogger<HardwareChecker> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public HardwareCheckResult Check()
    {
        var cpuCores = Environment.ProcessorCount;
        var totalRamGb = GetTotalRamGb();
        var (gpuName, vramMb) = GetGpuInfo();
        var warnings = new List<string>();

        // 最低要件チェック
        var meetsMinimum = cpuCores >= HardwareRequirements.Minimum.CpuCores &&
                          totalRamGb >= HardwareRequirements.Minimum.RamGb &&
                          vramMb >= HardwareRequirements.Minimum.VramMb;

        // 推奨要件チェック
        var meetsRecommended = cpuCores >= HardwareRequirements.Recommended.CpuCores &&
                              totalRamGb >= HardwareRequirements.Recommended.RamGb &&
                              vramMb >= HardwareRequirements.Recommended.VramMb;

        // 警告レベル判定
        var warningLevel = HardwareWarningLevel.Ok;

        if (totalRamGb < HardwareRequirements.Critical.MinRamGb ||
            vramMb < HardwareRequirements.Critical.MinVramMb)
        {
            warningLevel = HardwareWarningLevel.Critical;
        }
        else if (!meetsMinimum)
        {
            warningLevel = HardwareWarningLevel.Warning;
        }
        else if (!meetsRecommended)
        {
            warningLevel = HardwareWarningLevel.Info;
        }

        // 警告メッセージ生成
        if (vramMb < HardwareRequirements.Critical.MinVramMb)
        {
            warnings.Add($"VRAM: {vramMb}MB (最低: {HardwareRequirements.Critical.MinVramMb}MB)");
        }
        else if (vramMb < HardwareRequirements.Minimum.VramMb)
        {
            warnings.Add($"VRAM: {vramMb}MB (推奨: {HardwareRequirements.Minimum.VramMb}MB以上)");
        }

        if (totalRamGb < HardwareRequirements.Critical.MinRamGb)
        {
            warnings.Add($"RAM: {totalRamGb}GB (最低: {HardwareRequirements.Critical.MinRamGb}GB)");
        }
        else if (totalRamGb < HardwareRequirements.Minimum.RamGb)
        {
            warnings.Add($"RAM: {totalRamGb}GB (推奨: {HardwareRequirements.Minimum.RamGb}GB以上)");
        }

        if (cpuCores < HardwareRequirements.Minimum.CpuCores)
        {
            warnings.Add($"CPU: {cpuCores}コア (推奨: {HardwareRequirements.Minimum.CpuCores}コア以上)");
        }

        _logger.LogInformation(
            "[Issue #335] ハードウェアチェック完了: CPU={CpuCores}コア, RAM={RamGb}GB, GPU={GpuName}, VRAM={VramMb}MB, Level={Level}",
            cpuCores, totalRamGb, gpuName, vramMb, warningLevel);

        return new HardwareCheckResult
        {
            CpuCores = cpuCores,
            TotalRamGb = totalRamGb,
            GpuName = gpuName,
            VramMb = vramMb,
            MeetsMinimum = meetsMinimum,
            MeetsRecommended = meetsRecommended,
            WarningLevel = warningLevel,
            Warnings = warnings
        };
    }

    private static int GetTotalRamGb()
    {
        try
        {
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var totalMemoryBytes = gcMemoryInfo.TotalAvailableMemoryBytes;
            return (int)(totalMemoryBytes / (1024L * 1024 * 1024));
        }
        catch
        {
            return 0;
        }
    }

    private (string GpuName, int VramMb) GetGpuInfo()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? "Unknown GPU";
                var adapterRam = obj["AdapterRAM"];

                // AdapterRAM is in bytes, convert to MB
                // Note: Win32_VideoController.AdapterRAM is limited to 4GB (uint32)
                // For GPUs with more VRAM, we need alternative detection
                var vramMb = 0;
                if (adapterRam != null)
                {
                    var ramBytes = Convert.ToUInt64(adapterRam);
                    vramMb = (int)(ramBytes / (1024 * 1024));

                    // If VRAM is exactly 4GB (4294967296 bytes / 1MB = 4096MB),
                    // it might be capped, try to estimate from GPU name
                    if (vramMb == 4095 || vramMb == 4096)
                    {
                        vramMb = EstimateVramFromGpuName(name);
                    }
                }

                // Return the first discrete GPU found (usually NVIDIA/AMD)
                if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("GeForce", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("[Issue #335] Discrete GPU detected: {Name}, VRAM: {VramMb}MB", name, vramMb);
                    return (name, vramMb);
                }
            }

            // Fallback: return the first GPU found
            using var fallbackSearcher = new ManagementObjectSearcher("SELECT Name, AdapterRAM FROM Win32_VideoController");
            var first = fallbackSearcher.Get().Cast<ManagementObject>().FirstOrDefault();
            if (first != null)
            {
                var name = first["Name"]?.ToString() ?? "Unknown GPU";
                var ramBytes = Convert.ToUInt64(first["AdapterRAM"] ?? 0);
                var vramMb = (int)(ramBytes / (1024 * 1024));
                return (name, vramMb);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #335] GPU情報取得失敗");
        }

        return ("Unknown GPU", 0);
    }

    /// <summary>
    /// GPU名からVRAMを推定（Win32_VideoController.AdapterRAMの4GB制限対策）
    /// </summary>
    private static int EstimateVramFromGpuName(string gpuName)
    {
        var upperName = gpuName.ToUpperInvariant();

        // NVIDIA RTX 40 series
        if (upperName.Contains("4090")) return 24576;  // 24GB
        if (upperName.Contains("4080")) return 16384;  // 16GB
        if (upperName.Contains("4070 TI SUPER")) return 16384;  // 16GB
        if (upperName.Contains("4070 TI")) return 12288;  // 12GB
        if (upperName.Contains("4070 SUPER")) return 12288;  // 12GB
        if (upperName.Contains("4070")) return 12288;  // 12GB
        if (upperName.Contains("4060 TI")) return 8192;  // 8GB
        if (upperName.Contains("4060")) return 8192;  // 8GB

        // NVIDIA RTX 30 series
        if (upperName.Contains("3090")) return 24576;  // 24GB
        if (upperName.Contains("3080 TI")) return 12288;  // 12GB
        if (upperName.Contains("3080")) return 10240;  // 10GB
        if (upperName.Contains("3070 TI")) return 8192;  // 8GB
        if (upperName.Contains("3070")) return 8192;  // 8GB
        if (upperName.Contains("3060 TI")) return 8192;  // 8GB
        if (upperName.Contains("3060")) return 12288;  // 12GB (desktop)
        if (upperName.Contains("3050")) return 8192;  // 8GB

        // NVIDIA RTX 20 series
        if (upperName.Contains("2080 TI")) return 11264;  // 11GB
        if (upperName.Contains("2080 SUPER")) return 8192;  // 8GB
        if (upperName.Contains("2080")) return 8192;  // 8GB
        if (upperName.Contains("2070 SUPER")) return 8192;  // 8GB
        if (upperName.Contains("2070")) return 8192;  // 8GB
        if (upperName.Contains("2060 SUPER")) return 8192;  // 8GB
        if (upperName.Contains("2060")) return 6144;  // 6GB

        // NVIDIA GTX 16 series
        if (upperName.Contains("1660 TI")) return 6144;  // 6GB
        if (upperName.Contains("1660 SUPER")) return 6144;  // 6GB
        if (upperName.Contains("1660")) return 6144;  // 6GB
        if (upperName.Contains("1650 SUPER")) return 4096;  // 4GB
        if (upperName.Contains("1650")) return 4096;  // 4GB

        // NVIDIA GTX 10 series
        if (upperName.Contains("1080 TI")) return 11264;  // 11GB
        if (upperName.Contains("1080")) return 8192;  // 8GB
        if (upperName.Contains("1070 TI")) return 8192;  // 8GB
        if (upperName.Contains("1070")) return 8192;  // 8GB
        if (upperName.Contains("1060 6GB")) return 6144;  // 6GB
        if (upperName.Contains("1060 3GB")) return 3072;  // 3GB
        if (upperName.Contains("1060")) return 6144;  // Default to 6GB version
        if (upperName.Contains("1050 TI")) return 4096;  // 4GB
        if (upperName.Contains("1050")) return 2048;  // 2GB

        // AMD RX 7000 series
        if (upperName.Contains("7900 XTX")) return 24576;  // 24GB
        if (upperName.Contains("7900 XT")) return 20480;  // 20GB
        if (upperName.Contains("7800 XT")) return 16384;  // 16GB
        if (upperName.Contains("7700 XT")) return 12288;  // 12GB
        if (upperName.Contains("7600")) return 8192;  // 8GB

        // AMD RX 6000 series
        if (upperName.Contains("6900 XT")) return 16384;  // 16GB
        if (upperName.Contains("6800 XT")) return 16384;  // 16GB
        if (upperName.Contains("6800")) return 16384;  // 16GB
        if (upperName.Contains("6700 XT")) return 12288;  // 12GB
        if (upperName.Contains("6600 XT")) return 8192;  // 8GB
        if (upperName.Contains("6600")) return 8192;  // 8GB

        // Default: return 4GB (the capped value)
        return 4096;
    }
}
