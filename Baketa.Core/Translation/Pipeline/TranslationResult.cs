namespace Baketa.Core.Translation.Pipeline;

/// <summary>
/// 翻訳完了結果（UI更新用）
/// CoordinateBasedTranslationServiceのTextChunk表示ロジックと
/// TPL Dataflow処理結果を統合
/// </summary>
/// <param name="originalText">原文</param>
/// <param name="translatedText">訳文</param>
/// <param name="displayMode">表示モード</param>
/// <param name="coordinateInfo">座標情報</param>
/// <param name="jobId">ジョブID（トレーシング用）</param>
/// <param name="processingTime">翻訳処理時間</param>
/// <param name="confidence">翻訳信頼度</param>
public record TranslationResult(
    string OriginalText,
    string TranslatedText,
    TranslationDisplayMode DisplayMode,
    CoordinateInfo? CoordinateInfo,
    string JobId,
    TimeSpan ProcessingTime = default,
    float Confidence = 1.0f
)
{
    /// <summary>翻訳完了時刻</summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;
    
    /// <summary>翻訳成功フラグ（TranslationValidator統合）</summary>
    public bool IsSuccess => Baketa.Core.Utilities.TranslationValidator.IsValid(TranslatedText, OriginalText);
    
    /// <summary>表示可能フラグ（成功 かつ 座標情報有効）</summary>
    public bool CanDisplay => IsSuccess && (DisplayMode != TranslationDisplayMode.InPlace || HasValidCoordinateInfo);
    
    /// <summary>座標情報が有効かどうか</summary>
    public bool HasValidCoordinateInfo => CoordinateInfo?.IsValid == true;
    
    /// <summary>処理結果サマリー（デバッグ用）</summary>
    public string ResultSummary => IsSuccess 
        ? $"✅ '{OriginalText[..Math.Min(20, OriginalText.Length)]}...' → '{TranslatedText[..Math.Min(20, TranslatedText.Length)]}...'"
        : $"❌ '{OriginalText[..Math.Min(20, OriginalText.Length)]}...' → Error";
    
    /// <summary>
    /// エラー結果を作成
    /// </summary>
    /// <param name="originalText">原文</param>
    /// <param name="errorMessage">エラーメッセージ</param>
    /// <param name="jobId">ジョブID</param>
    /// <param name="displayMode">表示モード</param>
    /// <param name="coordinateInfo">座標情報</param>
    /// <returns>エラー結果</returns>
    public static TranslationResult CreateError(
        string originalText,
        string errorMessage,
        string jobId,
        TranslationDisplayMode displayMode = TranslationDisplayMode.Default,
        CoordinateInfo? coordinateInfo = null)
    {
        return new TranslationResult(
            OriginalText: originalText ?? "",
            TranslatedText: errorMessage ?? "[翻訳エラー]",
            DisplayMode: displayMode,
            CoordinateInfo: coordinateInfo,
            JobId: jobId);
    }
    
    /// <summary>
    /// TranslationJobから結果テンプレートを作成
    /// </summary>
    /// <param name="job">翻訳ジョブ</param>
    /// <param name="translatedText">翻訳結果</param>
    /// <param name="processingTime">処理時間</param>
    /// <param name="confidence">信頼度</param>
    /// <returns>TranslationResult</returns>
    public static TranslationResult FromJob(
        TranslationJob job,
        string translatedText,
        TimeSpan processingTime = default,
        float confidence = 1.0f)
    {
        ArgumentNullException.ThrowIfNull(job);
        return new TranslationResult(
            OriginalText: job.CombinedText,
            TranslatedText: translatedText ?? "",
            DisplayMode: job.DisplayMode,
            CoordinateInfo: job.CoordinateInfo,
            JobId: job.JobId,
            ProcessingTime: processingTime,
            Confidence: confidence);
    }
    
    /// <summary>
    /// バッチ結果から複数のTranslationResultを作成
    /// </summary>
    /// <param name="job">元のジョブ</param>
    /// <param name="translatedTexts">翻訳結果配列</param>
    /// <param name="processingTime">バッチ処理時間</param>
    /// <returns>TranslationResult配列</returns>
    /// <exception cref="ArgumentException">OCR結果数と翻訳結果数が一致しない場合</exception>
    public static IReadOnlyList<TranslationResult> FromBatchJob(
        TranslationJob job,
        IReadOnlyList<string> translatedTexts,
        TimeSpan processingTime = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(translatedTexts);
        
        // カウント不一致チェック（データ整合性保証）
        if (job.OcrResults.Count != translatedTexts.Count)
        {
            throw new ArgumentException(
                $"OCR result count ({job.OcrResults.Count}) does not match translated text count ({translatedTexts.Count}). JobId: {job.JobId}",
                nameof(translatedTexts));
        }
        
        var results = new List<TranslationResult>();
        for (int i = 0; i < job.OcrResults.Count; i++)
        {
            var ocrResult = job.OcrResults[i];
            var translatedText = translatedTexts[i];
            
            // 個別結果の座標情報（InPlaceモード時）
            var individualCoordinateInfo = job.DisplayMode == TranslationDisplayMode.InPlace
                ? new CoordinateInfo(ocrResult.Bounds, job.CoordinateInfo?.WindowHandle ?? IntPtr.Zero)
                : null;
            
            results.Add(new TranslationResult(
                OriginalText: ocrResult.Text,
                TranslatedText: translatedText,
                DisplayMode: job.DisplayMode,
                CoordinateInfo: individualCoordinateInfo,
                JobId: $"{job.JobId}_{i}",
                ProcessingTime: processingTime,
                Confidence: ocrResult.Confidence));
        }
        
        return results.AsReadOnly();
    }
    
    /// <summary>
    /// TextChunk用データを作成（ShowInPlaceOverlayAsync互換性）
    /// </summary>
    /// <returns>TextChunk互換データ（座標・テキスト情報）</returns>
    public (string translatedText, System.Drawing.Rectangle bounds, IntPtr windowHandle) ToTextChunkData()
    {
        return (
            translatedText: TranslatedText,
            bounds: CoordinateInfo?.Bounds ?? System.Drawing.Rectangle.Empty,
            windowHandle: CoordinateInfo?.WindowHandle ?? IntPtr.Zero
        );
    }
}