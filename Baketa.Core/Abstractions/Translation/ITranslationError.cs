namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// 翻訳エラーの詳細情報インターフェース
/// OCRエラーと翻訳エラーを明確に分離するための拡張
/// </summary>
public interface ITranslationError
{
    /// <summary>
    /// エラーの種類
    /// </summary>
    TranslationErrorCategory Category { get; }

    /// <summary>
    /// エラーコード
    /// </summary>
    string ErrorCode { get; }

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    string Message { get; }

    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    string UserFriendlyMessage { get; }

    /// <summary>
    /// リトライ可能かどうか
    /// </summary>
    bool IsRetryable { get; }

    /// <summary>
    /// 推奨されるリトライ遅延（ミリ秒）
    /// </summary>
    int? SuggestedRetryDelayMs { get; }

    /// <summary>
    /// エラーの詳細情報
    /// </summary>
    Dictionary<string, object>? Details { get; }
}

/// <summary>
/// エラーカテゴリー
/// </summary>
public enum TranslationErrorCategory
{
    /// <summary>
    /// OCR処理エラー
    /// </summary>
    OcrProcessing,

    /// <summary>
    /// サーバー起動エラー
    /// </summary>
    ServerStartup,

    /// <summary>
    /// ネットワーク接続エラー
    /// </summary>
    NetworkConnection,

    /// <summary>
    /// 翻訳処理エラー
    /// </summary>
    TranslationProcessing,

    /// <summary>
    /// 設定エラー
    /// </summary>
    Configuration,

    /// <summary>
    /// リソース不足
    /// </summary>
    ResourceExhaustion,

    /// <summary>
    /// タイムアウト
    /// </summary>
    Timeout,

    /// <summary>
    /// 不明なエラー
    /// </summary>
    Unknown
}

/// <summary>
/// 拡張翻訳エラーの実装（Step 1: 即座の応急処置）
/// </summary>
public sealed class EnhancedTranslationError : ITranslationError
{
    public TranslationErrorCategory Category { get; init; }
    public string ErrorCode { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string UserFriendlyMessage { get; init; } = string.Empty;
    public bool IsRetryable { get; init; }
    public int? SuggestedRetryDelayMs { get; init; }
    public Dictionary<string, object>? Details { get; init; }

    /// <summary>
    /// OCRエラー用のファクトリメソッド
    /// </summary>
    public static EnhancedTranslationError CreateOcrError(string message, Exception? innerException = null)
    {
        return new EnhancedTranslationError
        {
            Category = TranslationErrorCategory.OcrProcessing,
            ErrorCode = "OCR_PROCESSING_ERROR",
            Message = message,
            UserFriendlyMessage = "画像からテキストを読み取れませんでした",
            IsRetryable = true,
            SuggestedRetryDelayMs = 2000,
            Details = innerException != null
                ? new Dictionary<string, object> { ["InnerException"] = innerException.ToString() }
                : null
        };
    }

    /// <summary>
    /// サーバー起動エラー用のファクトリメソッド
    /// </summary>
    public static EnhancedTranslationError CreateServerStartupError(string message, int retryCount = 0)
    {
        return new EnhancedTranslationError
        {
            Category = TranslationErrorCategory.ServerStartup,
            ErrorCode = "SERVER_STARTUP_FAILED",
            Message = message,
            UserFriendlyMessage = "翻訳サービスを開始できませんでした。しばらくお待ちください",
            IsRetryable = retryCount < 3,
            SuggestedRetryDelayMs = (int)Math.Pow(2, retryCount) * 1000, // Exponential backoff
            Details = new Dictionary<string, object> { ["RetryCount"] = retryCount }
        };
    }

    /// <summary>
    /// ネットワーク接続エラー用のファクトリメソッド
    /// </summary>
    public static EnhancedTranslationError CreateNetworkError(string host, int port, Exception? innerException = null)
    {
        return new EnhancedTranslationError
        {
            Category = TranslationErrorCategory.NetworkConnection,
            ErrorCode = "NETWORK_CONNECTION_FAILED",
            Message = $"Failed to connect to {host}:{port}",
            UserFriendlyMessage = "翻訳サービスに接続できません",
            IsRetryable = true,
            SuggestedRetryDelayMs = 3000,
            Details = new Dictionary<string, object>
            {
                ["Host"] = host,
                ["Port"] = port,
                ["InnerException"] = innerException?.ToString() ?? string.Empty
            }
        };
    }
}
