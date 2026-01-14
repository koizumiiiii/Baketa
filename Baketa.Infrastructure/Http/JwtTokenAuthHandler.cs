using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Http;

/// <summary>
/// Issue #287: JWT認証用DelegatingHandler
/// HttpClientのリクエストに自動的にJWTを付与し、401応答時に自動リフレッシュを試みる
/// </summary>
/// <param name="tokenService">JWTトークンサービス</param>
/// <param name="logger">ロガー</param>
public sealed class JwtTokenAuthHandler(
    IJwtTokenService tokenService,
    ILogger<JwtTokenAuthHandler> logger) : DelegatingHandler
{
    private readonly IJwtTokenService _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
    private readonly ILogger<JwtTokenAuthHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>
    /// リフレッシュを試みる前の最小残り時間（秒）
    /// </summary>
    private const int RefreshThresholdSeconds = 60;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 1. JWTを取得してAuthorizationヘッダーを設定
        var jwt = await _tokenService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(jwt))
        {
            // 期限切れ間近ならバックグラウンドでリフレッシュを試みる
            if (_tokenService.IsNearExpiry())
            {
                _ = RefreshTokenInBackgroundAsync(cancellationToken);
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            _logger.LogDebug("[Issue #287] JWT added to request: {Url}", request.RequestUri);
        }
        else
        {
            _logger.LogDebug("[Issue #287] No JWT available for request: {Url}", request.RequestUri);
        }

        // 2. リクエスト送信
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // 3. 401応答時: リフレッシュを試みて再送
        if (response.StatusCode == HttpStatusCode.Unauthorized && _tokenService.HasValidToken)
        {
            _logger.LogInformation("[Issue #287] 401 Unauthorized received, attempting token refresh");

            var newJwt = await RefreshTokenWithLockAsync(cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(newJwt))
            {
                // 新しいリクエストを作成（元のリクエストは消費済みの可能性）
                var retryRequest = await CloneRequestAsync(request).ConfigureAwait(false);
                retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", newJwt);

                _logger.LogInformation("[Issue #287] Retrying request with refreshed JWT");
                response.Dispose();
                response = await base.SendAsync(retryRequest, cancellationToken).ConfigureAwait(false);
            }
        }

        return response;
    }

    /// <summary>
    /// 排他制御付きでトークンをリフレッシュ
    /// 複数リクエストが同時に401を受けた場合のrace conditionを防止
    /// </summary>
    private async Task<string?> RefreshTokenWithLockAsync(CancellationToken cancellationToken)
    {
        // 既にリフレッシュ中なら待機
        if (!await _refreshLock.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false))
        {
            _logger.LogWarning("[Issue #287] Token refresh lock timeout");
            return null;
        }

        try
        {
            // リフレッシュ後のトークンを確認（他のスレッドがリフレッシュ済みの可能性）
            if (!_tokenService.IsNearExpiry())
            {
                return await _tokenService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
            }

            var result = await _tokenService.RefreshAsync(cancellationToken).ConfigureAwait(false);
            return result?.AccessToken;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    /// バックグラウンドでトークンをリフレッシュ（メインリクエストをブロックしない）
    /// </summary>
    private async Task RefreshTokenInBackgroundAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshTokenWithLockAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Issue #287] Background token refresh failed");
        }
    }

    /// <summary>
    /// HttpRequestMessageを複製（Content含む）
    /// </summary>
    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
    {
        var clone = new HttpRequestMessage(original.Method, original.RequestUri)
        {
            Version = original.Version,
        };

        // ヘッダーをコピー（Authorizationは上書きされるので除外）
        foreach (var header in original.Headers)
        {
            if (!header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Contentをコピー
        if (original.Content != null)
        {
            var content = await original.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(content);

            foreach (var header in original.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        // Propertiesをコピー
        foreach (var prop in original.Options)
        {
            clone.Options.TryAdd(prop.Key, prop.Value);
        }

        return clone;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _refreshLock.Dispose();
        }
        base.Dispose(disposing);
    }
}
