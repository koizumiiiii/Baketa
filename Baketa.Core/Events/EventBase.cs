using System;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// イベント基本実装
/// </summary>
public abstract class EventBase : IEvent
{
    /// <summary>
    /// コンストラクタ
    /// </summary>
    protected EventBase()
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
    }

    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public DateTime Timestamp { get; }

    /// <inheritdoc />
    public abstract string Name { get; }

    /// <inheritdoc />
    public abstract string Category { get; }
}
