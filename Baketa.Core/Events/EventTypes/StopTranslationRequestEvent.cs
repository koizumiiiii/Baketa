using System;
using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 翻訳停止要求イベント
/// Phase 6.1: Stop押下後も処理継続問題の修正
/// </summary>
public class StopTranslationRequestEvent : IEvent
{
    public Guid Id { get; } = Guid.NewGuid();
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    public string Name { get; } = nameof(StopTranslationRequestEvent);
    public string Category { get; } = "Translation";
}
