namespace Baketa.Core.Abstractions.Auth;

/// <summary>
/// Issue #287: JWT短期トークン認証サービスインターフェース
/// SessionTokenをJWTアクセストークンに交換し、自動リフレッシュを管理
/// </summary>
public interface IJwtTokenService : IDisposable
{
    /// <summary>
    /// 現在のアクセストークンを取得
    /// 期限切れの場合は自動的にリフレッシュを試みる
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>JWTアクセストークン、未認証の場合はnull</returns>
    Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// SessionTokenをJWTトークンペアに交換
    /// Patreon認証完了後に呼び出す
    /// </summary>
    /// <param name="sessionToken">Patreon SessionToken</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>JWTトークンペア、失敗時はnull</returns>
    Task<JwtTokenPair?> ExchangeSessionTokenAsync(string sessionToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// リフレッシュトークンを使用して新しいJWTを取得
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>新しいJWTトークンペア、失敗時はnull</returns>
    Task<JwtTokenPair?> RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 現在のトークンが期限切れ間近かどうか
    /// </summary>
    /// <returns>true: リフレッシュ推奨、false: まだ有効</returns>
    bool IsNearExpiry();

    /// <summary>
    /// 保存されているトークンをクリア（ログアウト時）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    Task ClearTokensAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// JWTが利用可能かどうか
    /// </summary>
    bool HasValidToken { get; }

    /// <summary>
    /// トークンリフレッシュ失敗時のイベント
    /// </summary>
    event EventHandler<JwtRefreshFailedEventArgs>? RefreshFailed;
}

/// <summary>
/// JWTトークンペア
/// </summary>
/// <param name="AccessToken">JWTアクセストークン</param>
/// <param name="RefreshToken">リフレッシュトークン</param>
/// <param name="ExpiresAt">アクセストークン有効期限（UTC）</param>
public sealed record JwtTokenPair(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt);

/// <summary>
/// JWTリフレッシュ失敗イベント引数
/// </summary>
public sealed class JwtRefreshFailedEventArgs : EventArgs
{
    /// <summary>
    /// 失敗理由
    /// </summary>
    public required string Reason { get; init; }

    /// <summary>
    /// 再ログインが必要かどうか
    /// </summary>
    public bool RequiresReLogin { get; init; }

    /// <summary>
    /// 発生日時
    /// </summary>
    public DateTime OccurredAt { get; } = DateTime.UtcNow;
}
