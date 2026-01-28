using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// Issue #287: JWT短期トークン認証サービス実装
/// Relay ServerとのJWT交換・リフレッシュを管理
/// </summary>
/// <param name="httpClient">HTTPクライアント</param>
/// <param name="logger">ロガー</param>
/// <param name="settings">Cloud翻訳設定</param>
public sealed class JwtTokenService(
    HttpClient httpClient,
    ILogger<JwtTokenService> logger,
    IOptions<CloudTranslationSettings> settings) : IJwtTokenService
{
    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<JwtTokenService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly string _relayServerUrl = settings?.Value?.RelayServerUrl
        ?? "https://api.baketa.app";

    private JwtTokenPair? _currentTokenPair;
    private readonly object _tokenLock = new();
    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);
    private bool _disposed;

    /// <summary>
    /// リフレッシュを推奨する残り時間（秒）
    /// </summary>
    private const int RefreshThresholdSeconds = 120; // 2分前

    /// <summary>
    /// トークンリフレッシュ失敗イベント
    /// </summary>
    public event EventHandler<JwtRefreshFailedEventArgs>? RefreshFailed;

    /// <inheritdoc/>
    public bool HasValidToken
    {
        get
        {
            lock (_tokenLock)
            {
                return _currentTokenPair != null && _currentTokenPair.ExpiresAt > DateTime.UtcNow;
            }
        }
    }

    /// <inheritdoc/>
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Fast path: 有効なトークンがあればすぐに返す
        lock (_tokenLock)
        {
            if (_currentTokenPair != null && _currentTokenPair.ExpiresAt > DateTime.UtcNow)
            {
                return _currentTokenPair.AccessToken;
            }
        }

        // レースコンディション対策: SemaphoreSlimで排他制御
        // 複数スレッドが同時にリフレッシュを試みるのを防ぐ
        await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Double-check: セマフォ取得後に再度チェック
            // 他のスレッドがリフレッシュ完了している可能性
            lock (_tokenLock)
            {
                if (_currentTokenPair != null && _currentTokenPair.ExpiresAt > DateTime.UtcNow)
                {
                    return _currentTokenPair.AccessToken;
                }
            }

            // 期限切れの場合はリフレッシュを試みる
            if (_currentTokenPair != null)
            {
                var refreshed = await RefreshAsyncCore(cancellationToken).ConfigureAwait(false);
                return refreshed?.AccessToken;
            }

            return null;
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<JwtTokenPair?> ExchangeSessionTokenAsync(
        string sessionToken,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            _logger.LogInformation("[Issue #287] Exchanging SessionToken for JWT");

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{_relayServerUrl}/api/auth/token");
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", sessionToken);

            var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("[Issue #287] JWT exchange failed: {Status} - {Error}",
                    response.StatusCode, errorContent);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result == null || string.IsNullOrEmpty(result.AccessToken))
            {
                _logger.LogWarning("[Issue #287] JWT exchange returned empty response");
                return null;
            }

            var tokenPair = new JwtTokenPair(
                result.AccessToken,
                result.RefreshToken,
                DateTime.UtcNow.AddSeconds(result.ExpiresIn));

            lock (_tokenLock)
            {
                _currentTokenPair = tokenPair;
            }

            _logger.LogInformation("[Issue #287] JWT exchange successful, expires at {ExpiresAt:u}",
                tokenPair.ExpiresAt);

            return tokenPair;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #287] JWT exchange failed with exception");
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task<JwtTokenPair?> RefreshAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // レースコンディション対策: SemaphoreSlimで排他制御
        await _refreshSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await RefreshAsyncCore(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    /// <summary>
    /// JWT リフレッシュの内部実装（セマフォなし）
    /// </summary>
    /// <remarks>
    /// このメソッドは必ずセマフォ保護下で呼び出すこと。
    /// GetAccessTokenAsync または RefreshAsync から呼び出される。
    /// </remarks>
    private async Task<JwtTokenPair?> RefreshAsyncCore(CancellationToken cancellationToken)
    {
        string? refreshToken;
        lock (_tokenLock)
        {
            refreshToken = _currentTokenPair?.RefreshToken;
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            _logger.LogWarning("[Issue #287] No refresh token available");
            OnRefreshFailed("No refresh token available", requiresReLogin: true);
            return null;
        }

        try
        {
            _logger.LogInformation("[Issue #287] Refreshing JWT");

            var requestBody = new AuthRefreshRequest { RefreshToken = refreshToken };
            var response = await _httpClient.PostAsJsonAsync(
                $"{_relayServerUrl}/api/auth/refresh",
                requestBody,
                JsonOptions,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("[Issue #287] JWT refresh failed: {Status} - {Error}",
                    response.StatusCode, errorContent);

                var requiresReLogin = response.StatusCode == System.Net.HttpStatusCode.Unauthorized;
                OnRefreshFailed($"Refresh failed: {response.StatusCode}", requiresReLogin);

                if (requiresReLogin)
                {
                    ClearTokensAsyncCore();
                }

                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<AuthTokenResponse>(
                JsonOptions, cancellationToken).ConfigureAwait(false);

            if (result == null || string.IsNullOrEmpty(result.AccessToken))
            {
                _logger.LogWarning("[Issue #287] JWT refresh returned empty response");
                OnRefreshFailed("Empty refresh response", requiresReLogin: true);
                return null;
            }

            var tokenPair = new JwtTokenPair(
                result.AccessToken,
                result.RefreshToken,
                DateTime.UtcNow.AddSeconds(result.ExpiresIn));

            lock (_tokenLock)
            {
                _currentTokenPair = tokenPair;
            }

            _logger.LogInformation("[Issue #287] JWT refresh successful, expires at {ExpiresAt:u}",
                tokenPair.ExpiresAt);

            return tokenPair;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #287] JWT refresh failed with exception");
            OnRefreshFailed($"Exception: {ex.Message}", requiresReLogin: false);
            return null;
        }
    }

    /// <inheritdoc/>
    public bool IsNearExpiry()
    {
        lock (_tokenLock)
        {
            if (_currentTokenPair == null)
            {
                return false;
            }

            var remainingSeconds = (_currentTokenPair.ExpiresAt - DateTime.UtcNow).TotalSeconds;
            return remainingSeconds <= RefreshThresholdSeconds;
        }
    }

    /// <inheritdoc/>
    public Task ClearTokensAsync(CancellationToken cancellationToken = default)
    {
        ClearTokensAsyncCore();
        return Task.CompletedTask;
    }

    /// <summary>
    /// トークンクリアの内部実装
    /// </summary>
    private void ClearTokensAsyncCore()
    {
        lock (_tokenLock)
        {
            _currentTokenPair = null;
        }

        _logger.LogInformation("[Issue #287] JWT tokens cleared");
    }

    private void OnRefreshFailed(string reason, bool requiresReLogin)
    {
        RefreshFailed?.Invoke(this, new JwtRefreshFailedEventArgs
        {
            Reason = reason,
            RequiresReLogin = requiresReLogin,
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_tokenLock)
        {
            _currentTokenPair = null;
        }

        _refreshSemaphore.Dispose();
    }

    // JSON serialization options
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Relay Server JWT認証レスポンス
    /// </summary>
    private sealed class AuthTokenResponse
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("tokenType")]
        public string TokenType { get; set; } = "Bearer";
    }

    /// <summary>
    /// JWT リフレッシュリクエスト
    /// </summary>
    private sealed class AuthRefreshRequest
    {
        [JsonPropertyName("refreshToken")]
        public string RefreshToken { get; set; } = string.Empty;
    }
}
