using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers;

/// <summary>
/// ResourceMonitoringEventの空ハンドラ
/// Issue #195: 未処理イベント警告を解消するための最小限実装
/// 将来的にリソース監視UIやアラート機能に拡張可能
/// </summary>
public sealed class ResourceMonitoringEventHandler : IEventProcessor<ResourceMonitoringEvent>
{
    private readonly ILogger<ResourceMonitoringEventHandler> _logger;

    public ResourceMonitoringEventHandler(ILogger<ResourceMonitoringEventHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// イベント処理の優先度（低優先度）
    /// </summary>
    public int Priority => 1000;

    /// <summary>
    /// 同期実行設定（非同期処理）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// イベント処理
    /// 現時点ではログ出力のみ（将来的に拡張可能）
    /// </summary>
    public Task HandleAsync(ResourceMonitoringEvent eventData)
    {
        // Issue #195: 過剰ログを防ぐため、Warning以上のイベントのみログ出力
        if (eventData.EventType == ResourceMonitoringEventType.WarningRaised ||
            eventData.EventType == ResourceMonitoringEventType.MonitoringError)
        {
            _logger.LogWarning("[ResourceMonitoring] {EventType}: {Message}",
                eventData.EventType, eventData.Message ?? "No details");
        }
        else
        {
            _logger.LogDebug("[ResourceMonitoring] {EventType} received", eventData.EventType);
        }

        return Task.CompletedTask;
    }
}
