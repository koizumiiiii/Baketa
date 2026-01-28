using System.Runtime.Versioning;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Hardware;
using Baketa.Core.Models.Hardware;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// [Issue #335] ハードウェアスペックチェッカー実装
/// [Fix] WMI（System.Management）からIGpuEnvironmentDetector（DXGI/NVML）に移行
/// - IL Trimming対応（System.Managementへの依存を削除）
/// - 正確なVRAM（64-bit対応、4GB制限なし）
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class HardwareChecker : IHardwareChecker
{
    private readonly ILogger<HardwareChecker> _logger;
    private readonly IGpuEnvironmentDetector? _gpuEnvironmentDetector;

    public HardwareChecker(
        ILogger<HardwareChecker> logger,
        IGpuEnvironmentDetector? gpuEnvironmentDetector = null)
    {
        _logger = logger;
        _gpuEnvironmentDetector = gpuEnvironmentDetector;
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
        // IGpuEnvironmentDetectorが利用可能な場合は、キャッシュされた情報を使用
        // （DXGI/NVMLベースで正確なVRAM情報を取得）
        if (_gpuEnvironmentDetector != null)
        {
            try
            {
                var cachedEnv = _gpuEnvironmentDetector.GetCachedEnvironment();
                if (cachedEnv != null)
                {
                    _logger.LogDebug("[Issue #335] GPU情報をキャッシュから取得: {GpuName}, VRAM={VramMb}MB",
                        cachedEnv.GpuName, cachedEnv.AvailableMemoryMB);
                    return (cachedEnv.GpuName, (int)cachedEnv.AvailableMemoryMB);
                }

                // キャッシュがない場合は同期的に検出を試みる
                // 注: DetectEnvironmentAsyncは非同期だが、ここでは同期的に待機
                var environment = _gpuEnvironmentDetector.DetectEnvironmentAsync().GetAwaiter().GetResult();
                _logger.LogDebug("[Issue #335] GPU情報を検出: {GpuName}, VRAM={VramMb}MB",
                    environment.GpuName, environment.AvailableMemoryMB);
                return (environment.GpuName, (int)environment.AvailableMemoryMB);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Issue #335] IGpuEnvironmentDetector経由のGPU情報取得失敗");
            }
        }

        // フォールバック: GPU情報が取得できない場合
        _logger.LogWarning("[Issue #335] GPU情報を取得できません（IGpuEnvironmentDetector未登録またはエラー）");
        return ("Unknown GPU", 0);
    }
}
