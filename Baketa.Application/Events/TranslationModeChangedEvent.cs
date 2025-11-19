using Baketa.Core.Events;
using TranslationMode = Baketa.Core.Abstractions.Services.TranslationMode;

namespace Baketa.Application.Events;

/// <summary>
/// 翻訳モード変更イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="newMode">新しいモード</param>
/// <param name="previousMode">前のモード</param>
public sealed class TranslationModeChangedEvent(TranslationMode newMode, TranslationMode previousMode) : EventBase
{
    /// <summary>
    /// 新しいモード
    /// </summary>
    public TranslationMode NewMode { get; } = newMode;

    /// <summary>
    /// 前のモード
    /// </summary>
    public TranslationMode PreviousMode { get; } = previousMode;

    /// <inheritdoc />
    public override string Name => "TranslationModeChanged";

    /// <inheritdoc />
    public override string Category => "Translation.Mode";
}
