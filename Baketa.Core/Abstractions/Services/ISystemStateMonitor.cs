using System;

namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// システム状態の監視・判定を抽象化
/// Gemini改善提案: プラットフォーム固有ロジック分離
/// </summary>
public interface ISystemStateMonitor
{
    /// <summary>
    /// システムがアイドル状態かどうか
    /// </summary>
    bool IsSystemIdle();

    /// <summary>
    /// 現在のシステムリソース状況
    /// </summary>
    SystemResourceState GetCurrentResourceState();

    /// <summary>
    /// バッテリー駆動中かどうか（ラップトップ等）
    /// </summary>
    bool IsOnBatteryPower();

    /// <summary>
    /// システム状態変化イベント
    /// </summary>
    event EventHandler<SystemStateChangedEventArgs>? SystemStateChanged;
}

/// <summary>
/// システムリソース状態
/// </summary>
public record SystemResourceState(
    double CpuUsagePercent,
    double MemoryUsagePercent,
    double VramUsagePercent,
    bool IsHighPerformanceMode,
    DateTime CapturedAt
);

/// <summary>
/// システム状態変化イベント引数  
/// </summary>
public class SystemStateChangedEventArgs : EventArgs
{
    public SystemResourceState CurrentState { get; }
    public bool BatteryStatusChanged { get; }
    public bool PerformanceModeChanged { get; }

    public SystemStateChangedEventArgs(
        SystemResourceState currentState,
        bool batteryStatusChanged = false,
        bool performanceModeChanged = false)
    {
        CurrentState = currentState;
        BatteryStatusChanged = batteryStatusChanged;
        PerformanceModeChanged = performanceModeChanged;
    }
}
