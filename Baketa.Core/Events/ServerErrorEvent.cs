using Baketa.Core.Abstractions.Events;

namespace Baketa.Core.Events;

/// <summary>
/// サーバーエラーの重大度
/// </summary>
public enum ErrorSeverity
{
    /// <summary>
    /// 警告 - 機能が完全に使用不可
    /// </summary>
    Warn,

    /// <summary>
    /// 注意 - 機能は継続可能だが品質低下
    /// </summary>
    Caution
}

/// <summary>
/// サーバーエラーの種類
/// </summary>
public static class ServerErrorTypes
{
    // メモリ関連
    public const string MemoryError = "MemoryError";
    public const string CudaOutOfMemory = "CudaOutOfMemory";

    // Python関連
    public const string ModuleNotFound = "ModuleNotFound";
    public const string StartupTimeout = "StartupTimeout";
    public const string ProcessCrash = "ProcessCrash";

    // gRPC関連
    public const string GrpcUnavailable = "GrpcUnavailable";
    public const string GrpcTimeout = "GrpcTimeout";
    public const string GrpcError = "GrpcError";

    // ネットワーク関連
    public const string RateLimited = "RateLimited";
    public const string AuthError = "AuthError";
    public const string NetworkError = "NetworkError";
}

/// <summary>
/// サーバーエラーのソース
/// </summary>
public static class ServerErrorSources
{
    public const string TranslationServer = "TranslationServer";
    public const string OcrServer = "OcrServer";
    public const string TranslationClient = "TranslationClient";
    public const string OcrClient = "OcrClient";
    public const string CloudApi = "CloudApi";
}

/// <summary>
/// サーバーエラーイベント
/// Issue #264: Pythonサーバーエラー時のユーザー通知
/// </summary>
public sealed class ServerErrorEvent : IEvent
{
    /// <inheritdoc />
    public Guid Id { get; }

    /// <inheritdoc />
    public DateTime Timestamp { get; }

    /// <inheritdoc />
    public string Name => "ServerError";

    /// <inheritdoc />
    public string Category => "Server";

    /// <summary>
    /// エラーの重大度
    /// </summary>
    public ErrorSeverity Severity { get; }

    /// <summary>
    /// エラーのソース（どのコンポーネントで発生したか）
    /// </summary>
    public string Source { get; }

    /// <summary>
    /// エラーの種類
    /// </summary>
    public string ErrorType { get; }

    /// <summary>
    /// ユーザー向けメッセージ
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// 推奨アクション（復旧手順）
    /// </summary>
    public string? ActionHint { get; }

    /// <summary>
    /// 技術的な詳細（ログ用）
    /// </summary>
    public string? TechnicalDetail { get; }

    /// <summary>
    /// 関連する例外
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    public ServerErrorEvent(
        ErrorSeverity severity,
        string source,
        string errorType,
        string message,
        string? actionHint = null,
        string? technicalDetail = null,
        Exception? exception = null)
    {
        Id = Guid.NewGuid();
        Timestamp = DateTime.UtcNow;
        Severity = severity;
        Source = source ?? ServerErrorSources.TranslationServer;
        ErrorType = errorType ?? ServerErrorTypes.GrpcError;
        Message = message ?? "サーバーエラーが発生しました";
        ActionHint = actionHint;
        TechnicalDetail = technicalDetail;
        Exception = exception;
    }

    /// <summary>
    /// メモリエラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateMemoryError(
        string source,
        string? technicalDetail = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.MemoryError,
            "メモリ不足が発生しました。",
            "不要なアプリケーションを閉じるか、アプリを再起動してください。",
            technicalDetail);
    }

    /// <summary>
    /// CUDAメモリエラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateCudaMemoryError(
        string source,
        string? technicalDetail = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.CudaOutOfMemory,
            "GPUメモリが不足しています。",
            "他のGPUを使用するアプリケーションを閉じるか、CPU版に切り替えてください。",
            technicalDetail);
    }

    /// <summary>
    /// モジュール不足エラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateModuleNotFoundError(
        string source,
        string moduleName,
        string? technicalDetail = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.ModuleNotFound,
            $"必要なモジュール '{moduleName}' が見つかりません。",
            "アプリケーションを再インストールしてください。",
            technicalDetail);
    }

    /// <summary>
    /// 起動タイムアウトエラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateStartupTimeoutError(
        string source,
        int timeoutSeconds)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.StartupTimeout,
            $"サーバーの起動が{timeoutSeconds}秒以内に完了しませんでした。",
            "アプリケーションを再起動してください。初回起動時はモデルのダウンロードに時間がかかる場合があります。");
    }

    /// <summary>
    /// gRPC接続不可エラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateGrpcUnavailableError(
        string source,
        string? technicalDetail = null,
        Exception? exception = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.GrpcUnavailable,
            "サーバーに接続できません。",
            "しばらく待ってから再試行してください。問題が続く場合はアプリケーションを再起動してください。",
            technicalDetail,
            exception);
    }

    /// <summary>
    /// gRPCタイムアウトエラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateGrpcTimeoutError(
        string source,
        string? technicalDetail = null,
        Exception? exception = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Warn,
            source,
            ServerErrorTypes.GrpcTimeout,
            "サーバーからの応答がタイムアウトしました。",
            "サーバーが高負荷状態の可能性があります。しばらく待ってから再試行してください。",
            technicalDetail,
            exception);
    }

    /// <summary>
    /// レート制限エラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateRateLimitedError(
        string source,
        string? retryAfter = null)
    {
        var actionHint = string.IsNullOrEmpty(retryAfter)
            ? "しばらく待ってから再試行してください。"
            : $"{retryAfter}後に再試行してください。";

        return new ServerErrorEvent(
            ErrorSeverity.Caution,
            source,
            ServerErrorTypes.RateLimited,
            "APIの利用制限に達しました。",
            actionHint);
    }

    /// <summary>
    /// 認証エラーイベントを作成
    /// </summary>
    public static ServerErrorEvent CreateAuthError(
        string source,
        string? technicalDetail = null)
    {
        return new ServerErrorEvent(
            ErrorSeverity.Caution,
            source,
            ServerErrorTypes.AuthError,
            "認証に失敗しました。",
            "再ログインしてください。",
            technicalDetail);
    }
}
