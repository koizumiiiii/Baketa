using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Platform.Windows.Services;

/// <summary>
/// Windows固有のシステム状態監視実装
/// Gemini改善提案: プラットフォーム固有ロジック分離
/// </summary>
public sealed class WindowsSystemStateMonitor : ISystemStateMonitor
{
    private readonly ILogger<WindowsSystemStateMonitor> _logger;

    private SystemResourceState? _lastResourceState;
    private DateTime _lastCheck = DateTime.MinValue;
    private readonly TimeSpan _checkInterval = TimeSpan.FromSeconds(2); // キャッシュ間隔

    public WindowsSystemStateMonitor(ILogger<WindowsSystemStateMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// システム状態変化イベント
    /// </summary>
    public event EventHandler<SystemStateChangedEventArgs>? SystemStateChanged;

    /// <summary>
    /// システムがアイドル状態かどうか
    /// </summary>
    public bool IsSystemIdle()
    {
        var resourceState = GetCurrentResourceState();

        // アイドル判定条件: CPU < 10%, メモリ < 70%
        return resourceState.CpuUsagePercent < 10.0 &&
               resourceState.MemoryUsagePercent < 70.0;
    }

    /// <summary>
    /// 現在のシステムリソース状況
    /// </summary>
    public SystemResourceState GetCurrentResourceState()
    {
        var now = DateTime.UtcNow;

        // キャッシュ間隔チェック（頻繁なリソース取得を避ける）
        if (_lastResourceState != null && now - _lastCheck < _checkInterval)
        {
            return _lastResourceState;
        }

        _lastCheck = now;

        try
        {
            var cpuUsage = GetCpuUsage();
            var memoryUsage = GetMemoryUsage();
            var vramUsage = GetVramUsage();
            var isHighPerformance = IsHighPerformanceMode();

            var currentState = new SystemResourceState(
                CpuUsagePercent: cpuUsage,
                MemoryUsagePercent: memoryUsage,
                VramUsagePercent: vramUsage,
                IsHighPerformanceMode: isHighPerformance,
                CapturedAt: now
            );

            // 状態変化の検出とイベント発行
            if (_lastResourceState != null && HasSignificantChange(_lastResourceState, currentState))
            {
                var eventArgs = new SystemStateChangedEventArgs(
                    currentState,
                    batteryStatusChanged: false, // TODO: バッテリー状態変化検出
                    performanceModeChanged: _lastResourceState.IsHighPerformanceMode != isHighPerformance
                );

                SystemStateChanged?.Invoke(this, eventArgs);

                _logger.LogDebug("📊 システム状態変化: CPU={CpuUsage:F1}%, Memory={MemoryUsage:F1}%, VRAM={VramUsage:F1}%",
                    cpuUsage, memoryUsage, vramUsage);
            }

            _lastResourceState = currentState;
            return currentState;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ システムリソース状態取得エラー: {ErrorMessage}", ex.Message);

            // エラー時のフォールバック値
            return new SystemResourceState(0, 0, 0, false, now);
        }
    }

    /// <summary>
    /// バッテリー駆動中かどうか
    /// </summary>
    public bool IsOnBatteryPower()
    {
        try
        {
            var status = GetSystemPowerStatus(out var powerStatus);
            if (status)
            {
                // 0x01 = バッテリー駆動, 0x00 = AC電源
                return powerStatus.ACLineStatus == 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace("バッテリー状態取得エラー: {ErrorMessage}", ex.Message);
        }

        return false; // デフォルトはAC電源
    }

    /// <summary>
    /// CPU使用率取得
    /// </summary>
    private double GetCpuUsage()
    {
        try
        {
            var process = Process.GetCurrentProcess();
            var startTime = DateTime.UtcNow;
            var startCpuUsage = process.TotalProcessorTime;

            // 短時間待機してCPU使用率を計算
            System.Threading.Thread.Sleep(100);

            var endTime = DateTime.UtcNow;
            var endCpuUsage = process.TotalProcessorTime;

            var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
            var totalMsPassed = (endTime - startTime).TotalMilliseconds;
            var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

            return Math.Min(100.0, cpuUsageTotal * 100.0);
        }
        catch
        {
            return 0.0; // エラー時は0%として扱う
        }
    }

    /// <summary>
    /// メモリ使用率取得
    /// </summary>
    private double GetMemoryUsage()
    {
        try
        {
            var memStatus = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(memStatus))
            {
                return (double)(memStatus.ullTotalPhys - memStatus.ullAvailPhys) / memStatus.ullTotalPhys * 100.0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogTrace("メモリ使用率取得エラー: {ErrorMessage}", ex.Message);
        }

        return 0.0;
    }

    /// <summary>
    /// VRAM使用率取得（簡易版）
    /// </summary>
    private double GetVramUsage()
    {
        try
        {
            // TODO: NVML または Windows Performance Counters を使用した実装
            // 現在は模擬値を返す
            var random = new Random();
            return random.NextDouble() * 50.0; // 0-50%の範囲で模擬
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>
    /// 高性能モードかどうか判定
    /// </summary>
    private bool IsHighPerformanceMode()
    {
        try
        {
            // Windowsの電源プランから判定
            // TODO: より詳細な実装
            return !IsOnBatteryPower(); // 簡易実装: AC電源時は高性能モード
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 有意な変化があったかどうか判定
    /// </summary>
    private static bool HasSignificantChange(SystemResourceState previous, SystemResourceState current)
    {
        const double threshold = 10.0; // 10%以上の変化で有意とする

        return Math.Abs(previous.CpuUsagePercent - current.CpuUsagePercent) > threshold ||
               Math.Abs(previous.MemoryUsagePercent - current.MemoryUsagePercent) > threshold ||
               Math.Abs(previous.VramUsagePercent - current.VramUsagePercent) > threshold ||
               previous.IsHighPerformanceMode != current.IsHighPerformanceMode;
    }

    #region Win32 API

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte Reserved1;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    #endregion
}
