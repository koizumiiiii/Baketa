using Baketa.Core.Models.OCR;

namespace Baketa.Core.Translation.Pipeline;

/// <summary>
/// パイプライン内での翻訳ジョブ情報
/// CoordinateBasedTranslationServiceの処理データとTPL Dataflow要件を統合
/// </summary>
/// <param name="ocrResults">翻訳対象のOCR結果</param>
/// <param name="sourceLanguage">翻訳元言語</param>
/// <param name="targetLanguage">翻訳先言語</param>
/// <param name="displayMode">UI表示モード</param>
/// <param name="coordinateInfo">座標情報（InPlaceモード時必須）</param>
/// <param name="initialJobId">ジョブID（空の場合自動生成）</param>
/// <param name="priority">処理優先度（デフォルト: Normal）</param>
public record TranslationJob(
    IReadOnlyList<OcrResult> OcrResults,
    string SourceLanguage,
    string TargetLanguage,
    TranslationDisplayMode DisplayMode,
    CoordinateInfo? CoordinateInfo = null,
    string initialJobId = "",
    JobPriority Priority = JobPriority.Normal
)
{
    /// <summary>ジョブIDの自動生成</summary>
    public string JobId { get; } = string.IsNullOrEmpty(initialJobId) ? Guid.NewGuid().ToString("N")[..8] : initialJobId;
    
    /// <summary>作成日時</summary>
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    
    /// <summary>ジョブが有効（翻訳実行対象）かどうか</summary>
    public bool IsValid => OcrResults.Count > 0 && 
                          !string.IsNullOrWhiteSpace(SourceLanguage) && 
                          !string.IsNullOrWhiteSpace(TargetLanguage) &&
                          (DisplayMode != TranslationDisplayMode.InPlace || CoordinateInfo != null);
    
    /// <summary>InPlaceモード用座標情報が有効かどうか</summary>
    public bool HasValidCoordinateInfo => CoordinateInfo?.IsValid == true;
    
    /// <summary>翻訳対象テキスト数</summary>
    public int TextCount => OcrResults.Count;
    
    /// <summary>翻訳対象の全テキスト（デバッグ用）</summary>
    public string CombinedText => string.Join(" ", OcrResults.Select(r => r.Text));
    
    /// <summary>バッチサマリー（デバッグ・ログ用）</summary>
    public string BatchSummary => OcrResults.Count > 0 
        ? $"[{string.Join(", ", OcrResults.Take(3).Select(r => r.Text.Length > 10 ? r.Text[..10] + "..." : r.Text))}]{(OcrResults.Count > 3 ? $" +{OcrResults.Count - 3}more" : "")}"
        : "Empty";
    
    /// <summary>
    /// 空のTranslationJobを作成（フィルタリング用）
    /// </summary>
    public static TranslationJob Empty => new(
        OcrResults: Array.Empty<OcrResult>(),
        SourceLanguage: "",
        TargetLanguage: "",
        DisplayMode: TranslationDisplayMode.Default);
    
    /// <summary>
    /// 単一OCR結果からTranslationJobを作成
    /// </summary>
    /// <param name="ocrResult">OCR結果</param>
    /// <param name="sourceLanguage">ソース言語</param>
    /// <param name="targetLanguage">ターゲット言語</param>
    /// <param name="displayMode">表示モード</param>
    /// <param name="coordinateInfo">座標情報</param>
    /// <returns>TranslationJob</returns>
    public static TranslationJob FromSingleResult(
        OcrResult ocrResult,
        string sourceLanguage,
        string targetLanguage,
        TranslationDisplayMode displayMode,
        CoordinateInfo? coordinateInfo = null)
    {
        ArgumentNullException.ThrowIfNull(ocrResult);
        return new TranslationJob(
            OcrResults: [ocrResult],
            SourceLanguage: sourceLanguage,
            TargetLanguage: targetLanguage,
            DisplayMode: displayMode,
            CoordinateInfo: coordinateInfo);
    }
    
    /// <summary>
    /// BatchTranslationRequestEventからTranslationJobを作成
    /// </summary>
    /// <param name="batchEvent">バッチ翻訳要求イベント</param>
    /// <param name="displayMode">表示モード</param>
    /// <param name="coordinateInfo">座標情報</param>
    /// <returns>TranslationJob</returns>
    public static TranslationJob FromBatchEvent(
        Baketa.Core.Events.EventTypes.BatchTranslationRequestEvent batchEvent,
        TranslationDisplayMode displayMode,
        CoordinateInfo? coordinateInfo = null)
    {
        ArgumentNullException.ThrowIfNull(batchEvent);
        return new TranslationJob(
            OcrResults: batchEvent.OcrResults,
            SourceLanguage: batchEvent.SourceLanguage,
            TargetLanguage: batchEvent.TargetLanguage,
            DisplayMode: displayMode,
            CoordinateInfo: coordinateInfo);
    }
}

/// <summary>
/// ジョブ処理優先度
/// </summary>
public enum JobPriority
{
    /// <summary>低優先度</summary>
    Low,
    /// <summary>通常優先度</summary>
    Normal,
    /// <summary>高優先度</summary>
    High,
    /// <summary>緊急優先度</summary>
    Critical
}