namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// Cloud AI翻訳のフォールバック制御を担当
/// Primary → Secondary → Local の3段階フォールバック
/// </summary>
public interface IFallbackOrchestrator
{
    /// <summary>
    /// フォールバック付き画像翻訳を実行
    /// </summary>
    Task<FallbackTranslationResult> TranslateWithFallbackAsync(
        ImageTranslationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のフォールバック状態を取得
    /// </summary>
    FallbackStatus GetCurrentStatus();
}

/// <summary>
/// フォールバック翻訳結果
/// </summary>
public sealed class FallbackTranslationResult
{
    /// <summary>
    /// 翻訳成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 翻訳結果
    /// </summary>
    public ImageTranslationResponse? Response { get; init; }

    /// <summary>
    /// 使用されたエンジン
    /// </summary>
    public FallbackLevel UsedEngine { get; init; }

    /// <summary>
    /// フォールバック履歴（試行順）
    /// </summary>
    public IReadOnlyList<FallbackAttempt> Attempts { get; init; } = [];

    /// <summary>
    /// 最終エラー（全エンジン失敗時）
    /// </summary>
    public TranslationErrorDetail? FinalError { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static FallbackTranslationResult Success(
        ImageTranslationResponse response,
        FallbackLevel usedEngine,
        IReadOnlyList<FallbackAttempt> attempts) => new()
    {
        IsSuccess = true,
        Response = response,
        UsedEngine = usedEngine,
        Attempts = attempts
    };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static FallbackTranslationResult Failure(
        TranslationErrorDetail error,
        IReadOnlyList<FallbackAttempt> attempts) => new()
    {
        IsSuccess = false,
        UsedEngine = FallbackLevel.None,
        Attempts = attempts,
        FinalError = error
    };
}

/// <summary>
/// フォールバックレベル
/// </summary>
public enum FallbackLevel
{
    /// <summary>Primary Cloud (Gemini)</summary>
    Primary,

    /// <summary>Secondary Cloud (GPT-4.1-nano)</summary>
    Secondary,

    /// <summary>Local (NLLB-200)</summary>
    Local,

    /// <summary>すべて失敗</summary>
    None
}

/// <summary>
/// フォールバック試行記録
/// </summary>
public sealed class FallbackAttempt
{
    /// <summary>
    /// 試行したレベル
    /// </summary>
    public required FallbackLevel Level { get; init; }

    /// <summary>
    /// プロバイダーID
    /// </summary>
    public required string ProviderId { get; init; }

    /// <summary>
    /// 成功したか
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// フォールバック状態
/// </summary>
public sealed class FallbackStatus
{
    /// <summary>
    /// Primaryエンジンが利用可能か
    /// </summary>
    public bool PrimaryAvailable { get; init; }

    /// <summary>
    /// Secondaryエンジンが利用可能か
    /// </summary>
    public bool SecondaryAvailable { get; init; }

    /// <summary>
    /// Localエンジンが利用可能か
    /// </summary>
    public bool LocalAvailable { get; init; } = true;

    /// <summary>
    /// Primary次回再試行時刻
    /// </summary>
    public DateTime? PrimaryNextRetry { get; init; }

    /// <summary>
    /// Secondary次回再試行時刻
    /// </summary>
    public DateTime? SecondaryNextRetry { get; init; }
}
