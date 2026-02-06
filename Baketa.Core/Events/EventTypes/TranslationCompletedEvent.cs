using System;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 翻訳完了イベント
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="sourceText">元のテキスト</param>
/// <param name="translatedText">翻訳されたテキスト</param>
/// <param name="sourceLanguage">元言語コード</param>
/// <param name="targetLanguage">翻訳先言語コード</param>
/// <param name="processingTime">翻訳処理時間</param>
/// <param name="engineName">使用された翻訳エンジン名</param>
/// <param name="isBatchAnalytics">バッチ分析用イベントかどうか（UIオーバーレイ表示不要）</param>
/// <exception cref="ArgumentNullException">sourceTextまたはtranslatedTextがnullの場合</exception>
public class TranslationCompletedEvent(
        string sourceText,
        string translatedText,
        string sourceLanguage,
        string targetLanguage,
        TimeSpan processingTime,
        string engineName = "Default",
        bool isBatchAnalytics = false) : EventBase
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
    public string SourceLanguage { get; } = sourceLanguage ?? "en";

    /// <summary>
    /// 翻訳先言語コード
    /// </summary>
    public string TargetLanguage { get; } = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));

    /// <summary>
    /// 翻訳処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; } = processingTime;

    /// <summary>
    /// 使用された翻訳エンジン名
    /// </summary>
    public string EngineName { get; } = engineName ?? "Default";

    /// <summary>
    /// バッチ分析用イベントかどうか（UIオーバーレイ表示不要）
    /// </summary>
    public bool IsBatchAnalytics { get; } = isBatchAnalytics;

    /// <inheritdoc />
    public override string Name => "TranslationCompleted";

    /// <inheritdoc />
    public override string Category => "Translation";
}
