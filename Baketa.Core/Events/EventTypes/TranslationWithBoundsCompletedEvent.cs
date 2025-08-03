using System;
using System.Drawing;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 座標情報付き翻訳完了イベント
/// オーバーレイ表示のための座標情報を含む翻訳完了イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="sourceText">元のテキスト</param>
/// <param name="translatedText">翻訳されたテキスト</param>
/// <param name="sourceLanguage">元言語コード</param>
/// <param name="targetLanguage">翻訳先言語コード</param>
/// <param name="bounds">テキストの座標範囲</param>
/// <param name="confidence">翻訳の信頼度（0.0-1.0）</param>
/// <param name="engineName">使用された翻訳エンジン名</param>
/// <exception cref="ArgumentNullException">sourceTextまたはtranslatedTextがnullの場合</exception>
public class TranslationWithBoundsCompletedEvent(
    string sourceText,
    string translatedText,
    string sourceLanguage,
    string targetLanguage,
    Rectangle bounds,
    float confidence = 1.0f,
    string engineName = "Default") : EventBase
{
    /// <summary>
    /// 元のテキスト
    /// </summary>
    public string SourceText { get; } = sourceText ?? throw new ArgumentNullException(nameof(sourceText));

    /// <summary>
    /// 翻訳されたテキスト
    /// </summary>
    public string TranslatedText { get; } = translatedText ?? throw new ArgumentNullException(nameof(translatedText));

    /// <summary>
    /// 元言語コード
    /// </summary>
    public string SourceLanguage { get; } = sourceLanguage ?? "auto";

    /// <summary>
    /// 翻訳先言語コード
    /// </summary>
    public string TargetLanguage { get; } = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));

    /// <summary>
    /// テキストの座標範囲
    /// </summary>
    public Rectangle Bounds { get; } = bounds;

    /// <summary>
    /// 翻訳の信頼度（0.0-1.0）
    /// </summary>
    public float Confidence { get; } = Math.Clamp(confidence, 0.0f, 1.0f);

    /// <summary>
    /// 使用された翻訳エンジン名
    /// </summary>
    public string EngineName { get; } = engineName ?? "Default";

    /// <inheritdoc />
    public override string Name => "TranslationWithBoundsCompleted";
        
    /// <inheritdoc />
    public override string Category => "Translation";
}