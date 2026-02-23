using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// トークン有効期限切れを処理するハンドラー実装
/// HTTP 401検出やトークン期限切れ時の自動ログアウト・クリーンアップを担当
/// </summary>
public sealed class TokenExpirationHandler : ITokenExpirationHandler, IDisposable
{
    private readonly ITokenStorage _tokenStorage;
    private readonly ITokenAuditLogger _auditLogger;
    private readonly ILogger<TokenExpirationHandler> _logger;
    private readonly SemaphoreSlim _handlerLock = new(1, 1);
    private bool _disposed;
    private bool _isHandling;

    /// <inheritdoc/>
    public event EventHandler<TokenExpiredEventArgs>? TokenExpired;

    public TokenExpirationHandler(
        ITokenStorage tokenStorage,
        ITokenAuditLogger auditLogger,
        ILogger<TokenExpirationHandler> logger)
    {
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task HandleTokenExpiredAsync(string reason, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // 重複実行を防止
        if (_isHandling)
        {
            _logger.LogDebug("Token expiration handling already in progress, skipping");
            return;
        }

        await _handlerLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ダブルチェック
            if (_isHandling)
            {
                _logger.LogDebug("Token expiration handling already in progress (double-check), skipping");
                return;
            }

            _isHandling = true;
            _logger.LogWarning("Processing token expiration: {Reason}", reason);

            string? userId = null;
            try
            {
                // 1. 現在のトークンを取得（監査ログ用のユーザーID取得）
                var tokens = await _tokenStorage.RetrieveTokensAsync(cancellationToken).ConfigureAwait(false);
                if (tokens.HasValue)
                {
                    // JWTからユーザーIDを抽出（簡易実装）
                    userId = ExtractUserIdFromToken(tokens.Value.AccessToken);
                }

                // 2. 監査ログ記録
                if (!string.IsNullOrEmpty(userId))
                {
                    await _auditLogger.LogTokenRevokedAsync(userId, reason, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await _auditLogger.LogTokenValidationFailedAsync($"Token expired: {reason}", cancellationToken).ConfigureAwait(false);
                }

                // 3. ローカルトークンを削除
                _logger.LogWarning("[TOKEN_CLEAR][Path-1A] Infrastructure.TokenExpirationHandler: HandleTokenExpiredAsync normal flow. Reason={Reason}, UserId={UserId}", reason, MaskUserId(userId));
                var cleared = await _tokenStorage.ClearTokensAsync(cancellationToken).ConfigureAwait(false);
                if (cleared)
                {
                    _logger.LogInformation("[TOKEN_CLEAR][Path-1A] Local tokens cleared successfully");
                }
                else
                {
                    _logger.LogWarning("[TOKEN_CLEAR][Path-1A] Failed to clear local tokens or no tokens existed");
                }

                // 4. イベント発火（UI層でハンドリング）
                var eventArgs = new TokenExpiredEventArgs(
                    MaskUserId(userId),
                    reason,
                    DateTime.UtcNow);

                _logger.LogInformation("Raising TokenExpired event for user {UserId}", MaskUserId(userId));
                TokenExpired?.Invoke(this, eventArgs);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Token expiration handling was cancelled");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token expiration handling");

                // エラーが発生してもトークンは確実に削除
                try
                {
                    _logger.LogWarning("[TOKEN_CLEAR][Path-1B] Infrastructure.TokenExpirationHandler: Error recovery in HandleTokenExpiredAsync. OriginalReason={Reason}, Error={Error}", reason, ex.Message);
                    await _tokenStorage.ClearTokensAsync(CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception clearEx)
                {
                    _logger.LogError(clearEx, "[TOKEN_CLEAR][Path-1B] Failed to clear tokens during error recovery");
                }

                // エラー時もイベントは発火（UI側で適切に処理させる）
                TokenExpired?.Invoke(this, new TokenExpiredEventArgs(
                    MaskUserId(userId),
                    $"Error during handling: {reason}",
                    DateTime.UtcNow));
            }
        }
        finally
        {
            _isHandling = false;
            _handlerLock.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> TryHandleUnauthorizedResponseAsync(int statusCode, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (statusCode != 401)
        {
            return false;
        }

        _logger.LogWarning("HTTP 401 Unauthorized response detected, initiating token expiration handling");
        await HandleTokenExpiredAsync("HTTP 401 Unauthorized response", cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <inheritdoc/>
    public async Task<bool> ValidateSessionAsync(AuthSession? session, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (session == null)
        {
            _logger.LogDebug("Session is null, validation failed");
            return false;
        }

        if (!session.IsValid)
        {
            _logger.LogWarning("Session expired at {ExpiresAt}", session.ExpiresAt);
            await HandleTokenExpiredAsync($"Session expired at {session.ExpiresAt:O}", cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (session.IsNearExpiry)
        {
            _logger.LogInformation("Session is near expiry ({ExpiresAt}), consider refreshing", session.ExpiresAt);
            // 期限切れ間近の場合は警告のみ、まだ有効なのでtrueを返す
        }

        return true;
    }

    /// <summary>
    /// JWTからユーザーIDを抽出する（System.Text.Jsonを使用した安全な実装）
    /// </summary>
    private static string? ExtractUserIdFromToken(string? accessToken)
    {
        if (string.IsNullOrEmpty(accessToken))
        {
            return null;
        }

        try
        {
            // JWTは3つのパートに分かれている: header.payload.signature
            var parts = accessToken.Split('.');
            if (parts.Length != 3)
            {
                return null;
            }

            // payloadをBase64Urlデコード
            var payload = parts[1];
            // Base64Url → Base64 変換
            payload = payload.Replace('-', '+').Replace('_', '/');
            // Base64 padding調整
            var paddingNeeded = (4 - payload.Length % 4) % 4;
            if (paddingNeeded < 4)
            {
                payload += new string('=', paddingNeeded);
            }

            var payloadBytes = Convert.FromBase64String(payload);
            var payloadJson = System.Text.Encoding.UTF8.GetString(payloadBytes);

            // System.Text.Jsonで安全にパース
            using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
            if (document.RootElement.TryGetProperty("sub", out var subElement))
            {
                return subElement.GetString();
            }

            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            // 不正なJSONフォーマット
            return null;
        }
        catch (FormatException)
        {
            // 不正なBase64
            return null;
        }
        catch
        {
            // その他のエラー
            return null;
        }
    }

    /// <summary>
    /// ユーザーIDをマスクする（プライバシー保護）
    /// </summary>
    private static string? MaskUserId(string? userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        if (userId.Length <= 8)
        {
            return $"{userId[..Math.Min(2, userId.Length)]}****";
        }

        return $"{userId[..4]}****{userId[^4..]}";
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _handlerLock.Dispose();
    }
}
