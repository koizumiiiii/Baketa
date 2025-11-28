namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// トークン有効期限切れを処理するハンドラー
/// HTTP 401検出やトークン期限切れ時の自動ログアウト・クリーンアップを担当
/// </summary>
public interface ITokenExpirationHandler
{
    /// <summary>
    /// トークンが期限切れになったときに発火するイベント
    /// UI層でこのイベントをサブスクライブし、ユーザー通知とナビゲーションを行う
    /// </summary>
    event EventHandler<TokenExpiredEventArgs>? TokenExpired;

    /// <summary>
    /// トークン期限切れを処理する
    /// - 監査ログ記録
    /// - ローカルトークン削除
    /// - TokenExpiredイベント発火
    /// </summary>
    /// <param name="reason">期限切れの理由</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task HandleTokenExpiredAsync(string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// HTTP 401 Unauthorized応答を処理する
    /// </summary>
    /// <param name="statusCode">HTTPステータスコード</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>401応答だった場合true、それ以外はfalse</returns>
    Task<bool> TryHandleUnauthorizedResponseAsync(int statusCode, CancellationToken cancellationToken = default);

    /// <summary>
    /// セッションの有効性を確認し、期限切れの場合は処理を実行
    /// </summary>
    /// <param name="session">確認するセッション</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>セッションが有効な場合true</returns>
    Task<bool> ValidateSessionAsync(AuthSession? session, CancellationToken cancellationToken = default);
}

/// <summary>
/// トークン期限切れイベントの引数
/// </summary>
/// <param name="UserId">ユーザーID（マスク済み推奨）</param>
/// <param name="Reason">期限切れの理由</param>
/// <param name="ExpiredAt">期限切れ検出時刻</param>
public sealed record TokenExpiredEventArgs(
    string? UserId,
    string Reason,
    DateTime ExpiredAt);
