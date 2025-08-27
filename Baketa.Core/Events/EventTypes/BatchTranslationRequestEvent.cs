using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Models.OCR;

namespace Baketa.Core.Events.EventTypes;

/// <summary>
/// バッチ翻訳要求イベント
/// TPL Dataflowによる制御された並列処理のために複数のOCR結果をバッチ処理
/// </summary>
/// <param name="ocrResults">翻訳対象のOCR結果のバッチ</param>
/// <param name="sourceLanguage">ソース言語</param>
/// <param name="targetLanguage">ターゲット言語</param>
public class BatchTranslationRequestEvent(IReadOnlyList<OcrResult> ocrResults, string sourceLanguage, string targetLanguage) : EventBase
{
    /// <summary>
    /// 翻訳対象のOCR結果のバッチ
    /// </summary>
    public IReadOnlyList<OcrResult> OcrResults { get; } = ocrResults ?? throw new ArgumentNullException(nameof(ocrResults));

    /// <summary>
    /// ソース言語
    /// </summary>
    public string SourceLanguage { get; } = sourceLanguage ?? throw new ArgumentNullException(nameof(sourceLanguage));

    /// <summary>
    /// ターゲット言語
    /// </summary>
    public string TargetLanguage { get; } = targetLanguage ?? throw new ArgumentNullException(nameof(targetLanguage));

    /// <summary>
    /// バッチサイズ
    /// </summary>
    public int BatchSize => OcrResults.Count;

    /// <summary>
    /// バッチ内のテキストの概要（デバッグ用）
    /// </summary>
    public string BatchSummary => OcrResults.Count > 0 
        ? $"[{string.Join(", ", OcrResults.Take(3).Select(r => r.Text[..Math.Min(10, r.Text.Length)]))}]{(OcrResults.Count > 3 ? $" and {OcrResults.Count - 3} more" : "")}"
        : "Empty batch";

    /// <inheritdoc />
    public override string Name => "BatchTranslationRequest";
        
    /// <inheritdoc />
    public override string Category => "Translation";

    /// <summary>
    /// イベントの詳細な文字列表現（デバッグ用）
    /// </summary>
    /// <returns>詳細な文字列表現</returns>
    public override string ToString()
    {
        return $"{Name}: {BatchSize} items, {SourceLanguage} -> {TargetLanguage}, Summary: {BatchSummary}";
    }
}