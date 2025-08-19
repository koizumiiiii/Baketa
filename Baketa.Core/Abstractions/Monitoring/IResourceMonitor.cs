using Baketa.Core.Abstractions.Common;

namespace Baketa.Core.Abstractions.Monitoring;

/// <summary>
/// システムリソース監視の主要インターフェース
/// CPU、メモリ、GPU使用率などのシステムリソース情報をリアルタイム監視
/// </summary>
public interface IResourceMonitor : IInitializable, IDisposable
{
    /// <summary>
    /// 現在のリソース監視状態
    /// </summary>
    bool IsMonitoring { get; }
    
    /// <summary>
    /// 監視間隔（ミリ秒）
    /// デフォルト: 5000ms (5秒間隔)
    /// </summary>
    int MonitoringIntervalMs { get; set; }
    
    /// <summary>
    /// 最新のリソースメトリクス取得
    /// </summary>
    ResourceMetrics? CurrentMetrics { get; }
    
    /// <summary>
    /// リソース監視開始
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    Task StartMonitoringAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// リソース監視停止
    /// </summary>
    Task StopMonitoringAsync();
    
    /// <summary>
    /// 現在のリソース状況を即座に取得
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>現在のシステムリソースメトリクス</returns>
    Task<ResourceMetrics> GetCurrentMetricsAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// リソース履歴取得（指定期間内）
    /// </summary>
    /// <param name="fromTime">開始時刻</param>
    /// <param name="toTime">終了時刻</param>
    /// <returns>指定期間内のリソースメトリクス履歴</returns>
    IEnumerable<ResourceMetrics> GetMetricsHistory(DateTime fromTime, DateTime toTime);
    
    /// <summary>
    /// リソース状況変化イベント
    /// 閾値を超えた場合や重要な変化が発生した場合に発火
    /// </summary>
    event EventHandler<ResourceMetricsChangedEventArgs>? ResourceMetricsChanged;
    
    /// <summary>
    /// リソース警告イベント
    /// CPU/メモリ使用率が高い場合など警告レベルの状況で発火
    /// </summary>
    event EventHandler<ResourceWarningEventArgs>? ResourceWarning;
}

/// <summary>
/// リソースメトリクス変化イベント引数
/// </summary>
public sealed class ResourceMetricsChangedEventArgs : EventArgs
{
    public ResourceMetrics CurrentMetrics { get; }
    public ResourceMetrics? PreviousMetrics { get; }
    public DateTime Timestamp { get; }
    
    public ResourceMetricsChangedEventArgs(ResourceMetrics currentMetrics, ResourceMetrics? previousMetrics = null)
    {
        CurrentMetrics = currentMetrics;
        PreviousMetrics = previousMetrics;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// リソース警告イベント引数
/// </summary>
public sealed class ResourceWarningEventArgs : EventArgs
{
    public ResourceWarningType WarningType { get; }
    public string Message { get; }
    public ResourceMetrics CurrentMetrics { get; }
    public DateTime Timestamp { get; }
    
    public ResourceWarningEventArgs(
        ResourceWarningType warningType, 
        string message, 
        ResourceMetrics currentMetrics)
    {
        WarningType = warningType;
        Message = message;
        CurrentMetrics = currentMetrics;
        Timestamp = DateTime.UtcNow;
    }
}

/// <summary>
/// リソース警告種別
/// </summary>
public enum ResourceWarningType
{
    /// <summary>CPU使用率高</summary>
    HighCpuUsage,
    /// <summary>メモリ使用率高</summary>
    HighMemoryUsage,
    /// <summary>GPU使用率高</summary>
    HighGpuUsage,
    /// <summary>システム応答性低下</summary>
    SystemUnresponsive,
    /// <summary>リソース監視エラー</summary>
    MonitoringError
}