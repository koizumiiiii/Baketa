using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Performance;

/// <summary>
/// GPUメモリ使用状況
/// </summary>
public class GpuMemoryInfo
{
    /// <summary>
    /// 使用中メモリ量（MB）
    /// </summary>
    public long UsedMemoryMB { get; init; }
    
    /// <summary>
    /// 総メモリ量（MB）
    /// </summary>
    public long TotalMemoryMB { get; init; }
    
    /// <summary>
    /// 使用率（0.0～1.0）
    /// </summary>
    public double UsageRatio => TotalMemoryMB > 0 ? (double)UsedMemoryMB / TotalMemoryMB : 0.0;
    
    /// <summary>
    /// 利用可能メモリ量（MB）
    /// </summary>
    public long AvailableMemoryMB => TotalMemoryMB - UsedMemoryMB;
    
    /// <summary>
    /// GPUデバイス名
    /// </summary>
    public string DeviceName { get; init; } = string.Empty;
    
    /// <summary>
    /// 測定時刻
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// GPUメモリ制限設定
/// </summary>
public class GpuMemoryLimits
{
    /// <summary>
    /// 最大使用量（MB）
    /// </summary>
    public int MaxUsageMB { get; set; } = 2048;
    
    /// <summary>
    /// 警告閾値（使用率）
    /// </summary>
    public double WarningThreshold { get; set; } = 0.8;
    
    /// <summary>
    /// 制限強制モード
    /// </summary>
    public bool EnforceLimit { get; set; } = true;
    
    /// <summary>
    /// 監視間隔（秒）
    /// </summary>
    public int MonitoringIntervalSeconds { get; set; } = 30;
}

/// <summary>
/// GPUメモリ管理インターフェース
/// </summary>
public interface IGpuMemoryManager : IDisposable
{
    /// <summary>
    /// 監視が有効かどうか
    /// </summary>
    bool IsMonitoringEnabled { get; }
    
    /// <summary>
    /// 現在のGPUメモリ情報を取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>GPUメモリ情報</returns>
    Task<GpuMemoryInfo> GetCurrentMemoryInfoAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// メモリ制限設定を適用
    /// </summary>
    /// <param name="limits">制限設定</param>
    Task ApplyLimitsAsync(GpuMemoryLimits limits);
    
    /// <summary>
    /// 監視開始
    /// </summary>
    /// <param name="limits">制限設定</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task StartMonitoringAsync(GpuMemoryLimits limits, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 監視停止
    /// </summary>
    Task StopMonitoringAsync();
    
    /// <summary>
    /// 指定メモリ量が利用可能かチェック
    /// </summary>
    /// <param name="requiredMemoryMB">必要メモリ量（MB）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>利用可能な場合true</returns>
    Task<bool> IsMemoryAvailableAsync(int requiredMemoryMB, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// GPUメモリ使用量警告イベント
    /// </summary>
    event EventHandler<GpuMemoryInfo>? MemoryUsageWarning;
    
    /// <summary>
    /// GPUメモリ制限超過イベント
    /// </summary>
    event EventHandler<GpuMemoryInfo>? MemoryLimitExceeded;
}