using System;

namespace Baketa.Core.Abstractions.Events;

/// <summary>
/// 基本イベントインターフェース
/// </summary>
public interface IEvent
{
    /// <summary>
    /// イベントID
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// イベント発生時刻
    /// </summary>
    DateTime Timestamp { get; }

    /// <summary>
    /// イベント名
    /// </summary>
    string Name { get; }

    /// <summary>
    /// イベントカテゴリ
    /// </summary>
    string Category { get; }
}
