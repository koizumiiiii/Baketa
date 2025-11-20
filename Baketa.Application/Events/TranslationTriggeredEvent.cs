using System;
using Baketa.Core.Events;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

namespace Baketa.Application.Events;

/// <summary>
/// 翻訳実行トリガーイベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="mode">翻訳モード</param>
/// <param name="triggeredAt">トリガーされた時刻</param>
public sealed class TranslationTriggeredEvent(TranslationMode mode, DateTime triggeredAt) : EventBase
{
    /// <summary>
    /// 翻訳モード
    /// </summary>
    public TranslationMode Mode { get; } = mode;

    /// <summary>
    /// トリガーされた時刻
    /// </summary>
    public DateTime TriggeredAt { get; } = triggeredAt;

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
