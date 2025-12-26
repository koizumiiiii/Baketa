using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Models.Validation;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 並列翻訳オーケストレーターインターフェース
/// ローカル翻訳とCloud AI翻訳を並列実行し、相互検証で統合
/// </summary>
/// <remarks>
/// Issue #78 Phase 4: 並列翻訳オーケストレーション
///
/// 実行フロー:
/// 1. ローカルOCR結果を受け取る
/// 2. ローカル翻訳とCloud AI翻訳を並列実行
/// 3. 両方完了後、CrossValidatorで相互検証・統合
/// 4. 検証済み結果を返却
///
/// フォールバック:
/// - Cloud AI失敗時: ローカル翻訳結果のみを使用
/// - ローカル翻訳失敗時: Cloud AI結果のみを使用（座標情報なし）
/// </remarks>
public interface IParallelTranslationOrchestrator
{
    /// <summary>
    /// 並列翻訳を実行
    /// </summary>
    /// <param name="request">並列翻訳リクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>並列翻訳結果</returns>
    Task<ParallelTranslationResult> TranslateAsync(
        ParallelTranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cloud AI翻訳が利用可能かどうか
    /// </summary>
    bool IsCloudTranslationAvailable { get; }

    /// <summary>
    /// 現在のエンジン状態を取得
    /// </summary>
    ParallelTranslationStatus GetStatus();
}

/// <summary>
/// 並列翻訳リクエスト
/// </summary>
public sealed class ParallelTranslationRequest
{
    /// <summary>
    /// リクエストID
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString();

    /// <summary>
    /// OCR結果のテキストチャンク（座標情報付き）
    /// </summary>
    public required IReadOnlyList<TextChunk> OcrChunks { get; init; }

    /// <summary>
    /// 画像データ（Cloud AI翻訳用、Base64エンコード）
    /// </summary>
    public required string ImageBase64 { get; init; }

    /// <summary>
    /// 画像MIMEタイプ
    /// </summary>
    public string MimeType { get; init; } = "image/png";

    /// <summary>
    /// 画像幅
    /// </summary>
    public int ImageWidth { get; init; }

    /// <summary>
    /// 画像高さ
    /// </summary>
    public int ImageHeight { get; init; }

    /// <summary>
    /// ソース言語コード
    /// </summary>
    public string SourceLanguage { get; init; } = "auto";

    /// <summary>
    /// ターゲット言語コード
    /// </summary>
    public required string TargetLanguage { get; init; }

    /// <summary>
    /// セッショントークン（Cloud AI認証用）
    /// </summary>
    public string? SessionToken { get; init; }

    /// <summary>
    /// 翻訳コンテキスト（ゲームジャンル等）
    /// </summary>
    public string? Context { get; init; }

    /// <summary>
    /// Cloud AI翻訳を使用するか（Pro/Premiaプラン）
    /// </summary>
    public bool UseCloudTranslation { get; init; }

    /// <summary>
    /// 相互検証を実行するか
    /// </summary>
    public bool EnableCrossValidation { get; init; } = true;
}

/// <summary>
/// 並列翻訳結果
/// </summary>
public sealed class ParallelTranslationResult
{
    /// <summary>
    /// 成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 検証済みテキストチャンク（相互検証後）
    /// </summary>
    public IReadOnlyList<ValidatedTextChunk> ValidatedChunks { get; init; } = [];

    /// <summary>
    /// ローカル翻訳結果（相互検証前）
    /// </summary>
    public IReadOnlyList<TextChunk>? LocalTranslationChunks { get; init; }

    /// <summary>
    /// Cloud AI翻訳レスポンス（相互検証前）
    /// </summary>
    public ImageTranslationResponse? CloudTranslationResponse { get; init; }

    /// <summary>
    /// 使用されたエンジン
    /// </summary>
    public TranslationEngineUsed EngineUsed { get; init; }

    /// <summary>
    /// 相互検証統計（実行された場合）
    /// </summary>
    public CrossValidationStatistics? ValidationStatistics { get; init; }

    /// <summary>
    /// 処理時間詳細
    /// </summary>
    public ParallelTranslationTiming Timing { get; init; } = new();

    /// <summary>
    /// エラー情報（失敗時）
    /// </summary>
    public TranslationErrorDetail? Error { get; init; }

    /// <summary>
    /// ローカル翻訳のみの成功結果を作成
    /// </summary>
    public static ParallelTranslationResult LocalOnlySuccess(
        IReadOnlyList<TextChunk> localChunks,
        TimeSpan localDuration)
    {
        var validatedChunks = localChunks
            .Select(c => ValidatedTextChunk.LocalOnly(c, c.TranslatedText ?? c.CombinedText))
            .ToList();

        return new ParallelTranslationResult
        {
            IsSuccess = true,
            ValidatedChunks = validatedChunks,
            LocalTranslationChunks = localChunks,
            EngineUsed = TranslationEngineUsed.LocalOnly,
            Timing = new ParallelTranslationTiming
            {
                LocalTranslationDuration = localDuration,
                TotalDuration = localDuration
            }
        };
    }

    /// <summary>
    /// 相互検証付き成功結果を作成
    /// </summary>
    public static ParallelTranslationResult CrossValidatedSuccess(
        IReadOnlyList<ValidatedTextChunk> validatedChunks,
        IReadOnlyList<TextChunk> localChunks,
        ImageTranslationResponse cloudResponse,
        CrossValidationStatistics statistics,
        ParallelTranslationTiming timing)
        => new()
        {
            IsSuccess = true,
            ValidatedChunks = validatedChunks,
            LocalTranslationChunks = localChunks,
            CloudTranslationResponse = cloudResponse,
            EngineUsed = TranslationEngineUsed.BothWithValidation,
            ValidationStatistics = statistics,
            Timing = timing
        };

    /// <summary>
    /// Cloud AIフォールバック（ローカル失敗時）
    /// </summary>
    public static ParallelTranslationResult CloudFallbackSuccess(
        ImageTranslationResponse cloudResponse,
        TimeSpan cloudDuration)
        => new()
        {
            IsSuccess = true,
            ValidatedChunks = [],
            CloudTranslationResponse = cloudResponse,
            EngineUsed = TranslationEngineUsed.CloudOnly,
            Timing = new ParallelTranslationTiming
            {
                CloudTranslationDuration = cloudDuration,
                TotalDuration = cloudDuration
            }
        };

    /// <summary>
    /// 相互検証失敗時のフォールバック成功結果を作成
    /// 両方の翻訳は成功したが、相互検証で例外が発生しローカル結果にフォールバック
    /// </summary>
    public static ParallelTranslationResult CrossValidationFailedSuccess(
        IReadOnlyList<TextChunk> localChunks,
        ImageTranslationResponse cloudResponse,
        ParallelTranslationTiming timing,
        string validationErrorMessage)
    {
        var validatedChunks = localChunks
            .Select(c => ValidatedTextChunk.LocalOnly(c, c.TranslatedText ?? c.CombinedText))
            .ToList();

        return new ParallelTranslationResult
        {
            IsSuccess = true,
            ValidatedChunks = validatedChunks,
            LocalTranslationChunks = localChunks,
            CloudTranslationResponse = cloudResponse,
            EngineUsed = TranslationEngineUsed.BothWithValidationFailed,
            Timing = timing,
            Error = new TranslationErrorDetail
            {
                Code = "CROSS_VALIDATION_FAILED",
                Message = validationErrorMessage,
                IsRetryable = true
            }
        };
    }

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static ParallelTranslationResult Failure(
        TranslationErrorDetail error,
        ParallelTranslationTiming? timing = null)
        => new()
        {
            IsSuccess = false,
            Error = error,
            EngineUsed = TranslationEngineUsed.None,
            Timing = timing ?? new()
        };
}

/// <summary>
/// 使用された翻訳エンジン
/// </summary>
public enum TranslationEngineUsed
{
    /// <summary>翻訳未実行</summary>
    None,

    /// <summary>ローカル翻訳のみ（Free/Standardプラン）</summary>
    LocalOnly,

    /// <summary>Cloud AIのみ（ローカル失敗時フォールバック）</summary>
    CloudOnly,

    /// <summary>両方実行・相互検証あり（Pro/Premiaプラン）</summary>
    BothWithValidation,

    /// <summary>両方実行・相互検証なし</summary>
    BothWithoutValidation,

    /// <summary>両方実行・相互検証失敗（ローカル結果にフォールバック）</summary>
    BothWithValidationFailed
}

/// <summary>
/// 並列翻訳の処理時間詳細
/// </summary>
public sealed class ParallelTranslationTiming
{
    /// <summary>
    /// ローカル翻訳の処理時間
    /// </summary>
    public TimeSpan LocalTranslationDuration { get; init; }

    /// <summary>
    /// Cloud AI翻訳の処理時間
    /// </summary>
    public TimeSpan CloudTranslationDuration { get; init; }

    /// <summary>
    /// 相互検証の処理時間
    /// </summary>
    public TimeSpan CrossValidationDuration { get; init; }

    /// <summary>
    /// 合計処理時間
    /// </summary>
    public TimeSpan TotalDuration { get; init; }

    /// <summary>
    /// 並列実行による節約時間（Sequential - Actual）
    /// </summary>
    public TimeSpan ParallelSavings =>
        LocalTranslationDuration + CloudTranslationDuration + CrossValidationDuration - TotalDuration;
}

/// <summary>
/// 並列翻訳の状態
/// </summary>
public sealed class ParallelTranslationStatus
{
    /// <summary>
    /// ローカル翻訳エンジンが利用可能か
    /// </summary>
    public bool LocalEngineAvailable { get; init; } = true;

    /// <summary>
    /// Cloud AI翻訳が利用可能か
    /// </summary>
    public bool CloudEngineAvailable { get; init; }

    /// <summary>
    /// 相互検証が有効か
    /// </summary>
    public bool CrossValidationEnabled { get; init; }

    /// <summary>
    /// フォールバック状態
    /// </summary>
    public FallbackStatus? FallbackStatus { get; init; }

    /// <summary>
    /// 最後のエラー（ある場合）
    /// </summary>
    public TranslationErrorDetail? LastError { get; init; }
}
