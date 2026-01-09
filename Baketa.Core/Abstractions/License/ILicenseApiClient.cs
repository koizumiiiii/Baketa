using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// ライセンスサーバーとの通信を担当するクライアントインターフェース
/// </summary>
public interface ILicenseApiClient
{
    /// <summary>
    /// サーバーからライセンス状態を取得
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="sessionToken">セッショントークン</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>ライセンス状態（取得失敗時はnull）</returns>
    Task<LicenseApiResponse?> GetLicenseStateAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// トークン消費を記録
    /// </summary>
    /// <param name="request">消費リクエスト</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>消費結果</returns>
    Task<TokenConsumptionApiResponse> ConsumeTokensAsync(
        TokenConsumptionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// セッションの有効性を検証
    /// </summary>
    /// <param name="userId">ユーザーID</param>
    /// <param name="sessionToken">セッショントークン</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>有効な場合true</returns>
    Task<SessionValidationResult> ValidateSessionAsync(
        string userId,
        string sessionToken,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// APIが利用可能かどうか（オンライン状態）
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// ユーザー認証情報（userId/sessionToken）が必要かどうか
    /// Patreonなど独自の認証管理を持つクライアントはfalseを返す
    /// </summary>
    bool RequiresCredentials { get; }
}

/// <summary>
/// ライセンスAPI応答
/// </summary>
public sealed record LicenseApiResponse
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// ライセンス状態
    /// </summary>
    public LicenseState? LicenseState { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 成功応答を作成
    /// </summary>
    public static LicenseApiResponse CreateSuccess(LicenseState state) => new()
    {
        Success = true,
        LicenseState = state
    };

    /// <summary>
    /// 失敗応答を作成
    /// </summary>
    public static LicenseApiResponse CreateFailure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// トークン消費リクエスト
/// </summary>
public sealed record TokenConsumptionRequest
{
    /// <summary>
    /// ユーザーID
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// セッショントークン
    /// </summary>
    public required string SessionToken { get; init; }

    /// <summary>
    /// 消費するトークン数
    /// </summary>
    public required int TokenCount { get; init; }

    /// <summary>
    /// Idempotency Key（二重消費防止）
    /// </summary>
    public required string IdempotencyKey { get; init; }

    /// <summary>
    /// リクエストメタデータ（監査ログ用）
    /// </summary>
    public TokenConsumptionMetadata? Metadata { get; init; }
}

/// <summary>
/// トークン消費メタデータ（監査ログ用）
/// </summary>
public sealed record TokenConsumptionMetadata
{
    /// <summary>
    /// 画像サイズ
    /// </summary>
    public string? ImageSize { get; init; }

    /// <summary>
    /// 検出されたテキスト長
    /// </summary>
    public int? DetectedTextLength { get; init; }

    /// <summary>
    /// 翻訳元言語
    /// </summary>
    public string? SourceLanguage { get; init; }

    /// <summary>
    /// 翻訳先言語
    /// </summary>
    public string? TargetLanguage { get; init; }
}

/// <summary>
/// トークン消費API応答
/// </summary>
public sealed record TokenConsumptionApiResponse
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// 新しい合計使用量
    /// </summary>
    public long NewUsageTotal { get; init; }

    /// <summary>
    /// 残りトークン数
    /// </summary>
    public long RemainingTokens { get; init; }

    /// <summary>
    /// エラーコード（失敗時）
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// エラーメッセージ（失敗時）
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Idempotency Keyが既に使用済みかどうか
    /// </summary>
    public bool WasIdempotent { get; init; }
}

/// <summary>
/// セッション検証結果
/// </summary>
public sealed record SessionValidationResult
{
    /// <summary>
    /// セッションが有効かどうか
    /// </summary>
    public required bool IsValid { get; init; }

    /// <summary>
    /// 無効化理由（無効な場合）
    /// </summary>
    public string? InvalidationReason { get; init; }

    /// <summary>
    /// 新しいデバイス情報（別デバイスログイン時）
    /// </summary>
    public string? NewDeviceInfo { get; init; }

    /// <summary>
    /// 有効な結果を作成
    /// </summary>
    public static SessionValidationResult Valid => new() { IsValid = true };

    /// <summary>
    /// 無効な結果を作成
    /// </summary>
    public static SessionValidationResult Invalid(string reason, string? newDeviceInfo = null) => new()
    {
        IsValid = false,
        InvalidationReason = reason,
        NewDeviceInfo = newDeviceInfo
    };
}
