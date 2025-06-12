using System;

namespace Baketa.UI.Framework.Events;

    /// <summary>
    /// イベント集約機構で使用される全てのイベントの基底インターフェース
    /// </summary>
    internal interface IEvent : Baketa.Core.Abstractions.Events.IEvent
    {
        // Core.Abstractions.Events.IEventを継承し、互換性を確保
        // Core.Abstractions.Events.IEventのプロパティ：
        // Guid Id { get; }
        // DateTime Timestamp { get; }
        // string Name { get; }
        // string Category { get; }
    }
