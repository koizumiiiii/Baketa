using Baketa.Core.Models;

namespace Baketa.Core.Translation.Abstractions;

/// <summary>
/// 画像翻訳レスポンス
/// </summary>
public sealed class ImageTranslationResponse
{
    /// <summary>
    /// リクエストID
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 翻訳成功フラグ
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 検出されたテキスト（オリジナル）
    /// </summary>
    public string? DetectedText { get; init; }

    /// <summary>
    /// 翻訳結果テキスト
    /// </summary>
    public string? TranslatedText { get; init; }

    /// <summary>
    /// 検出された言語コード
    /// </summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>
    /// 使用プロバイダーID
    /// </summary>
    public string? ProviderId { get; init; }

    /// <summary>
    /// 使用トークン数
    /// </summary>
    public TokenUsageDetail? TokenUsage { get; init; }

    /// <summary>
    /// 処理時間
    /// </summary>
    public TimeSpan ProcessingTime { get; init; }

    /// <summary>
    /// エラー情報（失敗時）
    /// </summary>
    public TranslationErrorDetail? Error { get; init; }

    /// <summary>
    /// 複数テキストの翻訳結果（Issue #242: 複数テキスト対応）
    /// </summary>
    /// <remarks>
    /// 複数テキストが検出された場合、このプロパティに全テキストが格納されます。
    /// 後方互換性のため、DetectedText/TranslatedTextには最初のテキストが設定されます。
    /// </remarks>
    public IReadOnlyList<TranslatedTextItem>? Texts { get; init; }

    /// <summary>
    /// [Issue #296] サーバーサイドの月間トークン使用状況
    /// </summary>
    /// <remarks>
    /// Relay Serverから返される月間トークン消費量。
    /// クライアントのローカル追跡をサーバー値と同期するために使用。
    /// </remarks>
    public ServerMonthlyUsage? MonthlyUsage { get; init; }

    /// <summary>
    /// 複数テキストが含まれるかどうか
    /// </summary>
    public bool HasMultipleTexts => Texts is { Count: > 1 };

    /// <summary>
    /// 成功レスポンス作成
    /// </summary>
    public static ImageTranslationResponse Success(
        string requestId,
        string detectedText,
        string translatedText,
        string providerId,
        TokenUsageDetail tokenUsage,
        TimeSpan processingTime,
        string? detectedLanguage = null) => new()
    {
        RequestId = requestId,
        IsSuccess = true,
        DetectedText = detectedText,
        TranslatedText = translatedText,
        DetectedLanguage = detectedLanguage,
        ProviderId = providerId,
        TokenUsage = tokenUsage,
        ProcessingTime = processingTime
    };

    /// <summary>
    /// 失敗レスポンス作成
    /// </summary>
    public static ImageTranslationResponse Failure(
        string requestId,
        TranslationErrorDetail error,
        TimeSpan processingTime) => new()
    {
        RequestId = requestId,
        IsSuccess = false,
        Error = error,
        ProcessingTime = processingTime
    };

    /// <summary>
    /// 複数テキスト成功レスポンス作成（Issue #242）
    /// </summary>
    public static ImageTranslationResponse SuccessWithMultipleTexts(
        string requestId,
        IReadOnlyList<TranslatedTextItem> texts,
        string providerId,
        TokenUsageDetail tokenUsage,
        TimeSpan processingTime,
        string? detectedLanguage = null)
    {
        var firstText = texts.Count > 0 ? texts[0] : null;

        return new()
        {
            RequestId = requestId,
            IsSuccess = true,
            DetectedText = firstText?.Original ?? string.Empty,
            TranslatedText = firstText?.Translation ?? string.Empty,
            DetectedLanguage = detectedLanguage,
            ProviderId = providerId,
            TokenUsage = tokenUsage,
            ProcessingTime = processingTime,
            Texts = texts
        };
    }
}

/// <summary>
/// 翻訳されたテキストアイテム（Issue #242: 複数テキスト対応）
/// </summary>
public sealed class TranslatedTextItem
{
    /// <summary>
    /// 検出された元のテキスト
    /// </summary>
    public required string Original { get; init; }

    /// <summary>
    /// 翻訳されたテキスト
    /// </summary>
    public required string Translation { get; init; }

    /// <summary>
    /// テキストのバウンディングボックス（ピクセル座標）
    /// Phase 2: 座標ベースオーバーレイ表示用
    /// </summary>
    public Int32Rect? BoundingBox { get; init; }

    /// <summary>
    /// バウンディングボックスが設定されているか
    /// </summary>
    public bool HasBoundingBox => BoundingBox.HasValue;
}

/// <summary>
/// トークン使用量詳細
/// </summary>
public sealed class TokenUsageDetail
{
    /// <summary>
    /// 入力トークン数
    /// </summary>
    public int InputTokens { get; init; }

    /// <summary>
    /// 出力トークン数
    /// </summary>
    public int OutputTokens { get; init; }

    /// <summary>
    /// 合計トークン数
    /// </summary>
    public int TotalTokens => InputTokens + OutputTokens;

    /// <summary>
    /// 画像トークン数（推定）
    /// </summary>
    public int ImageTokens { get; init; }

    /// <summary>
    /// 空のトークン使用量
    /// </summary>
    public static TokenUsageDetail Empty => new()
    {
        InputTokens = 0,
        OutputTokens = 0,
        ImageTokens = 0
    };
}

/// <summary>
/// 翻訳エラー詳細
/// </summary>
public sealed class TranslationErrorDetail
{
    /// <summary>
    /// エラーコード
    /// </summary>
    public required string Code { get; init; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// リトライ可能か
    /// </summary>
    public bool IsRetryable { get; init; }

    /// <summary>
    /// 一般的なエラーコード
    /// </summary>
    public static class Codes
    {
        public const string NetworkError = "NETWORK_ERROR";
        public const string TokenLimitExceeded = "TOKEN_LIMIT_EXCEEDED";
        public const string SessionInvalid = "SESSION_INVALID";
        public const string PlanNotSupported = "PLAN_NOT_SUPPORTED";
        public const string RateLimited = "RATE_LIMITED";
        public const string ApiError = "API_ERROR";
        public const string Timeout = "TIMEOUT";
        public const string NotImplemented = "NOT_IMPLEMENTED";
        public const string InternalError = "INTERNAL_ERROR";
    }
}

/// <summary>
/// [Issue #296] サーバーサイドの月間トークン使用状況
/// </summary>
public sealed class ServerMonthlyUsage
{
    /// <summary>
    /// 対象年月（YYYY-MM形式）
    /// </summary>
    public required string YearMonth { get; init; }

    /// <summary>
    /// 使用済みトークン数
    /// </summary>
    public long TokensUsed { get; init; }

    /// <summary>
    /// 月間上限トークン数
    /// </summary>
    public long TokensLimit { get; init; }

    /// <summary>
    /// 残りトークン数
    /// </summary>
    public long RemainingTokens => Math.Max(0, TokensLimit - TokensUsed);

    /// <summary>
    /// 使用率（0.0〜1.0）
    /// </summary>
    public double UsagePercentage => TokensLimit > 0 ? (double)TokensUsed / TokensLimit : 0.0;
}
