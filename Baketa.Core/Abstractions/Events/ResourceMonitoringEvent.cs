using Baketa.Core.Abstractions.Monitoring;

namespace Baketa.Core.Abstractions.Events;

/// <summary>
/// リソース監視関連イベント
/// システムリソース状況の変化や警告をイベントアグリゲーターを通じて通知
/// </summary>
public sealed class ResourceMonitoringEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; }
    
    /// <inheritdoc />
    public DateTime Timestamp { get; }
    
    /// <inheritdoc />
    public string Name { get; }
    
    /// <inheritdoc />
    public string Category { get; }
    
    /// <summary>
    /// リソース監視イベントの種別
    /// </summary>
    public ResourceMonitoringEventType EventType { get; }
    
    /// <summary>
    /// 現在のリソースメトリクス
    /// </summary>
    public ResourceMetrics CurrentMetrics { get; }
    
    /// <summary>
    /// 前回のリソースメトリクス（変更イベントの場合）
    /// </summary>
    public ResourceMetrics? PreviousMetrics { get; }
    
    /// <summary>
    /// 警告情報（警告イベントの場合）
    /// </summary>
    public ResourceWarning? Warning { get; }
    
    /// <summary>
    /// イベントメッセージ
    /// </summary>
    public string Message { get; }
    
    /// <summary>
    /// リソース監視イベントコンストラクタ
    /// </summary>
    /// <param name="eventType">イベント種別</param>
    /// <param name="currentMetrics">現在のリソースメトリクス</param>
    /// <param name="message">イベントメッセージ</param>
    /// <param name="previousMetrics">前回のリソースメトリクス</param>
    /// <param name="warning">警告情報</param>
    public ResourceMonitoringEvent(
        ResourceMonitoringEventType eventType,
        ResourceMetrics currentMetrics,
        string message,
        ResourceMetrics? previousMetrics = null,
        ResourceWarning? warning = null)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        EventType = eventType;
        CurrentMetrics = currentMetrics;
        Message = message;
        PreviousMetrics = previousMetrics;
        Warning = warning;
        
        Name = eventType switch
        {
            ResourceMonitoringEventType.MetricsChanged => "ResourceMetricsChanged",
            ResourceMonitoringEventType.WarningRaised => "ResourceWarningRaised",
            ResourceMonitoringEventType.MonitoringStarted => "ResourceMonitoringStarted",
            ResourceMonitoringEventType.MonitoringStopped => "ResourceMonitoringStopped",
            ResourceMonitoringEventType.MonitoringError => "ResourceMonitoringError",
            _ => "ResourceMonitoringUnknown"
        };
        
        Category = "ResourceMonitoring";
    }
    
    /// <summary>
    /// リソースメトリクス変更イベント作成
    /// </summary>
    public static ResourceMonitoringEvent CreateMetricsChanged(
        ResourceMetrics currentMetrics, 
        ResourceMetrics? previousMetrics = null)
    {
        var changeDescription = previousMetrics != null 
            ? $"リソース状況変化 - CPU:{previousMetrics.CpuUsagePercent:F1}%→{currentMetrics.CpuUsagePercent:F1}% MEM:{previousMetrics.MemoryUsagePercent:F1}%→{currentMetrics.MemoryUsagePercent:F1}%"
            : $"リソース状況更新 - {currentMetrics}";
            
        return new ResourceMonitoringEvent(
            ResourceMonitoringEventType.MetricsChanged,
            currentMetrics,
            changeDescription,
            previousMetrics);
    }
    
    /// <summary>
    /// リソース警告イベント作成
    /// </summary>
    public static ResourceMonitoringEvent CreateWarning(
        ResourceMetrics currentMetrics,
        ResourceWarning warning)
    {
        return new ResourceMonitoringEvent(
            ResourceMonitoringEventType.WarningRaised,
            currentMetrics,
            $"リソース警告: {warning.Message}",
            warning: warning);
    }
    
    /// <summary>
    /// 監視開始イベント作成
    /// </summary>
    public static ResourceMonitoringEvent CreateMonitoringStarted(ResourceMetrics initialMetrics)
    {
        return new ResourceMonitoringEvent(
            ResourceMonitoringEventType.MonitoringStarted,
            initialMetrics,
            "リソース監視を開始しました");
    }
    
    /// <summary>
    /// 監視停止イベント作成
    /// </summary>
    public static ResourceMonitoringEvent CreateMonitoringStopped(ResourceMetrics finalMetrics)
    {
        return new ResourceMonitoringEvent(
            ResourceMonitoringEventType.MonitoringStopped,
            finalMetrics,
            "リソース監視を停止しました");
    }
    
    /// <summary>
    /// 監視エラーイベント作成
    /// </summary>
    public static ResourceMonitoringEvent CreateMonitoringError(
        ResourceMetrics? currentMetrics,
        Exception exception)
    {
        var fallbackMetrics = currentMetrics ?? new ResourceMetrics(
            DateTime.UtcNow, 0, 0, 0, 0);
            
        return new ResourceMonitoringEvent(
            ResourceMonitoringEventType.MonitoringError,
            fallbackMetrics,
            $"リソース監視エラー: {exception.Message}");
    }
}

/// <summary>
/// リソース監視イベント種別
/// </summary>
public enum ResourceMonitoringEventType
{
    /// <summary>メトリクス変更</summary>
    MetricsChanged,
    /// <summary>警告発生</summary>
    WarningRaised,
    /// <summary>監視開始</summary>
    MonitoringStarted,
    /// <summary>監視停止</summary>
    MonitoringStopped,
    /// <summary>監視エラー</summary>
    MonitoringError
}

/// <summary>
/// リソース警告情報
/// </summary>
/// <param name="Type">警告種別</param>
/// <param name="Message">警告メッセージ</param>
/// <param name="Severity">重要度</param>
/// <param name="Threshold">閾値</param>
/// <param name="CurrentValue">現在値</param>
public sealed record ResourceWarning(
    ResourceWarningType Type,
    string Message,
    ResourceWarningSeverity Severity,
    double Threshold,
    double CurrentValue)
{
    /// <summary>
    /// 警告の文字列表現
    /// </summary>
    public override string ToString() => 
        $"{Type} ({Severity}): {Message} [Current:{CurrentValue:F1}% Threshold:{Threshold:F1}%]";
}

/// <summary>
/// リソース警告の重要度
/// </summary>
public enum ResourceWarningSeverity
{
    /// <summary>情報</summary>
    Info = 0,
    /// <summary>注意</summary>
    Warning = 1,
    /// <summary>エラー</summary>
    Error = 2,
    /// <summary>致命的</summary>
    Critical = 3
}