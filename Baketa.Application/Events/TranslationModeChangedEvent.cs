using Baketa.Application.Models;
using Baketa.Core.Events;

namespace Baketa.Application.Events;

/// <summary>
/// 翻訳モード変更イベント
/// </summary>
public sealed class TranslationModeChangedEvent : EventBase
{
    /// <summary>
    /// 新しいモード
    /// </summary>
    public TranslationMode NewMode { get; }

    /// <summary>
    /// 前のモード
    /// </summary>
    public TranslationMode PreviousMode { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="newMode">新しいモード</param>
    /// <param name="previousMode">前のモード</param>
    public TranslationModeChangedEvent(TranslationMode newMode, TranslationMode previousMode)
    {
        NewMode = newMode;
        PreviousMode = previousMode;
    }

    /// <inheritdoc />
    public override string Name => "TranslationModeChanged";

    /// <inheritdoc />
    public override string Category => "Translation.Mode";
}
