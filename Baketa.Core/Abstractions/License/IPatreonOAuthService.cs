using Baketa.Core.License.Models;

namespace Baketa.Core.Abstractions.License;

/// <summary>
/// Patreon OAuth 認証サービスのインターフェース
/// </summary>
public interface IPatreonOAuthService
{
    /// <summary>
    /// Patreon認証が設定されているかどうか
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// 現在の同期ステータス
    /// </summary>
    PatreonSyncStatus SyncStatus { get; }

    /// <summary>
    /// 最後に同期した日時
    /// </summary>
    DateTime? LastSyncTime { get; }

    /// <summary>
    /// 認証済みのPatreonユーザーID
    /// </summary>
    string? PatreonUserId { get; }

    /// <summary>
    /// OAuth認証URLを生成
    /// </summary>
    /// <param name="state">CSRF対策用のstate値</param>
    /// <returns>ブラウザで開くべきURL</returns>
    string GenerateAuthorizationUrl(string state);

    /// <summary>
    /// OAuth認証コールバックを処理し、トークンを取得
    /// </summary>
    /// <param name="authorizationCode">認証コード</param>
    /// <param name="state">CSRF対策用のstate値</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>認証結果</returns>
    Task<PatreonAuthResult> HandleCallbackAsync(
        string authorizationCode,
        string state,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Patreon連携を解除
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存された認証情報を読み込む
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>認証情報（未設定の場合はnull）</returns>
    Task<PatreonLocalCredentials?> LoadCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Patreonライセンス状態を同期
    /// </summary>
    /// <param name="forceRefresh">強制的にAPIから取得するかどうか</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>同期結果</returns>
    Task<PatreonSyncResult> SyncLicenseAsync(
        bool forceRefresh = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// ステータス変更イベント
    /// </summary>
    event EventHandler<PatreonStatusChangedEventArgs>? StatusChanged;
}

/// <summary>
/// Patreon認証結果
/// </summary>
public sealed record PatreonAuthResult
{
    /// <summary>
    /// 成功したかどうか
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Patreon ユーザーID
    /// </summary>
    public string? PatreonUserId { get; init; }

    /// <summary>
    /// ユーザー名
    /// </summary>
    public string? UserName { get; init; }

    /// <summary>
    /// メールアドレス
    /// </summary>
    public string? Email { get; init; }

    /// <summary>
    /// 判定されたプラン
    /// </summary>
    public PlanType Plan { get; init; } = PlanType.Free;

    /// <summary>
    /// エラーメッセージ
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// エラーコード
    /// </summary>
    public string? ErrorCode { get; init; }

    /// <summary>
    /// 成功結果を作成
    /// </summary>
    public static PatreonAuthResult CreateSuccess(string userId, string? userName, string? email, PlanType plan) => new()
    {
        Success = true,
        PatreonUserId = userId,
        UserName = userName,
        Email = email,
        Plan = plan
    };

    /// <summary>
    /// 失敗結果を作成
    /// </summary>
    public static PatreonAuthResult CreateFailure(string errorCode, string errorMessage) => new()
    {
        Success = false,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage
    };
}

/// <summary>
/// Patreonステータス変更イベント引数
/// </summary>
public sealed class PatreonStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 新しい同期ステータス
    /// </summary>
    public required PatreonSyncStatus NewStatus { get; init; }

    /// <summary>
    /// 以前の同期ステータス
    /// </summary>
    public PatreonSyncStatus? PreviousStatus { get; init; }

    /// <summary>
    /// 新しいプラン
    /// </summary>
    public PlanType Plan { get; init; } = PlanType.Free;

    /// <summary>
    /// 最終同期日時
    /// </summary>
    public DateTime? LastSyncTime { get; init; }

    /// <summary>
    /// エラーメッセージ（エラー時のみ）
    /// </summary>
    public string? ErrorMessage { get; init; }
}
