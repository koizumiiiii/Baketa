using System;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// アプリケーション終了時に発行されるイベント
/// </summary>
public class ApplicationShutdownEvent : IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>
    /// イベント名
    /// </summary>
    public string Name => "ApplicationShutdown";

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    public string Category => "Application";
}
