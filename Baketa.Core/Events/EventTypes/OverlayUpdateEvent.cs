using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// オーバーレイ更新イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="text">更新されたテキスト</param>
/// <param name="displayArea">表示位置</param>
/// <param name="originalText">元テキスト（利用可能な場合）</param>
/// <param name="sourceLanguage">翻訳元言語（利用可能な場合）</param>
/// <param name="targetLanguage">翻訳先言語</param>
/// <exception cref="ArgumentNullException">textがnullの場合</exception>
public class OverlayUpdateEvent(
        string text,
        Rectangle displayArea,
        string? originalText = null,
        string? sourceLanguage = null,
        string? targetLanguage = null,
        bool isTranslationResult = false) : EventBase
{
    /// <summary>
    /// 更新されたテキスト
    /// </summary>
    public string Text { get; } = text ?? throw new ArgumentNullException(nameof(text));

    /// <summary>
    /// 表示位置
    /// </summary>
    public Rectangle DisplayArea { get; } = displayArea;

    /// <summary>
    /// 元テキスト（利用可能な場合）
    /// </summary>
    public string? OriginalText { get; } = originalText ?? string.Empty;

    /// <summary>
    /// 翻訳元言語（利用可能な場合）
    /// </summary>
    public string? SourceLanguage { get; } = sourceLanguage ?? string.Empty;

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string? TargetLanguage { get; } = targetLanguage ?? string.Empty;

    /// <summary>
    /// 翻訳結果かどうか（falseの場合はOCR結果）
    /// </summary>
    public bool IsTranslationResult { get; } = isTranslationResult;

    /// <inheritdoc />
    public override string Name => "OverlayUpdate";

    /// <inheritdoc />
    public override string Category => "UI";
}
