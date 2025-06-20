using System;
using Baketa.Application.Models;
using Baketa.Core.Events;

namespace Baketa.Application.Events;

/// <summary>
/// 翻訳実行トリガーイベント
/// </summary>
public sealed class TranslationTriggeredEvent : EventBase
{
    /// <summary>
    /// 翻訳モード
    /// </summary>
    public TranslationMode Mode { get; }

    /// <summary>
    /// トリガーされた時刻
    /// </summary>
    public DateTime TriggeredAt { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="mode">翻訳モード</param>
    /// <param name="triggeredAt">トリガーされた時刻</param>
    public TranslationTriggeredEvent(TranslationMode mode, DateTime triggeredAt)
    {
        Mode = mode;
        TriggeredAt = triggeredAt;
    }

    /// <summary>
    /// コンストラクタ - TriggeredAtを現在時刻で初期化
    /// </summary>
    /// <param name="mode">翻訳モード</param>
    public TranslationTriggeredEvent(TranslationMode mode) 
        : this(mode, DateTime.UtcNow)
    {
    }

    /// <inheritdoc />
    public override string Name => "TranslationTriggered";

    /// <inheritdoc />
    public override string Category => "Translation.Execution";
}
