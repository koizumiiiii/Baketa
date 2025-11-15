using System;
using Baketa.Core.Events;
using Baketa.Core.Models.OCR;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// 翻訳要求イベント
/// OCR完了後の個別テキストに対する翻訳要求
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="ocrResult">翻訳対象のOCR結果</param>
/// <param name="sourceLanguage">ソース言語（例: "ja", "auto"）</param>
/// <param name="targetLanguage">ターゲット言語（例: "en"）</param>
/// <exception cref="ArgumentNullException">ocrResultがnullの場合</exception>
public class TranslationRequestEvent(OcrResult ocrResult, string sourceLanguage, string targetLanguage) : EventBase
{
    /// <summary>
    /// 翻訳対象のOCR結果
    /// </summary>
    public OcrResult OcrResult { get; } = ocrResult ?? throw new ArgumentNullException(nameof(ocrResult));

    /// <summary>
    /// ソース言語
    /// </summary>
    public string SourceLanguage { get; } = sourceLanguage ?? throw new ArgumentNullException(nameof(sourceLanguage));

    /// <summary>
    /// ターゲット言語
    /// </summary>
    public string TargetLanguage { get; } = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));

    /// <inheritdoc />
    public override string Name => "TranslationRequest";

    /// <inheritdoc />
    public override string Category => "Translation";
}
