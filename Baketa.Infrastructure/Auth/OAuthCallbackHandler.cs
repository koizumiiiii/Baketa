using System.Net;
using System.Text;
using System.Web;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// OAuth callback handler for desktop applications
/// Starts a local HTTP server to receive OAuth callbacks and exchange codes for sessions
/// Implements PKCE flow with CSRF protection via state parameter
/// </summary>
public sealed class OAuthCallbackHandler : IOAuthCallbackHandler, IAsyncDisposable, IDisposable
{
    private readonly ILogger<OAuthCallbackHandler> _logger;
    private readonly SupabaseAuthService _authService;
    private readonly ITokenStorage _tokenStorage;
    private readonly AuthSettings _authSettings;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private OAuthFlowState? _currentFlowState;
    private TaskCompletionSource<AuthResult>? _callbackTcs;
    // 🔥 [ISSUE#167] リスナー準備完了シグナル（レースコンディション対策）
    private TaskCompletionSource<bool>? _listenerReadyTcs;
    private bool _disposed;

    public OAuthCallbackHandler(
        SupabaseAuthService authService,
        ITokenStorage tokenStorage,
        IOptions<AuthSettings> authSettings,
        ILogger<OAuthCallbackHandler> logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _authSettings = authSettings?.Value ?? throw new ArgumentNullException(nameof(authSettings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start OAuth flow with the specified provider
    /// Opens browser for authentication and waits for callback
    /// Implements PKCE flow with CSRF protection
    /// </summary>
    /// <param name="provider">OAuth provider</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Authentication result</returns>
    public async Task<AuthResult> StartOAuthFlowAsync(AuthProvider provider, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.LogInformation("[OAUTH_DEBUG] StartOAuthFlowAsync開始: Provider={Provider}", provider);

            // Create a new completion source for this OAuth flow
            _callbackTcs = new TaskCompletionSource<AuthResult>();

            // Start HTTP listener for callback
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync呼び出し開始");
            await StartListenerAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync完了");

            // Initiate OAuth flow - get PKCE verifier and CSRF state
            _logger.LogDebug("[OAUTH_DEBUG] InitiateOAuthFlowAsync呼び出し開始");
            _currentFlowState = await _authService.InitiateOAuthFlowAsync(
                provider,
                _authSettings.OAuthCallbackPort,
                cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("[OAUTH_DEBUG] InitiateOAuthFlowAsync完了: State={State}", _currentFlowState != null ? "OK" : "NULL");

            if (_currentFlowState == null)
            {
                _logger.LogWarning("[OAUTH_DEBUG] OAuth flow state is NULL! Failed to initiate OAuth flow for provider: {Provider}", provider);
                return new AuthFailure(AuthErrorCodes.OAuthError, "OAuth認証の開始に失敗しました。");
            }

            _logger.LogDebug("[OAUTH_DEBUG] OAuth flow state created: PKCE verifier length={PkceLength}, State length={StateLength}",
                _currentFlowState.PkceVerifier.Length, _currentFlowState.StateParameter.Length);

            // Open the OAuth URL in the default browser
            _logger.LogInformation("[OAUTH_DEBUG] ブラウザを開く: {Uri}", _currentFlowState.Uri);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _currentFlowState.Uri.ToString(),
                UseShellExecute = true
            });

            _logger.LogInformation("[OAUTH_DEBUG] ブラウザ起動完了、コールバック待機中...");

            // Wait for callback with timeout
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(_authSettings.OAuthCallbackTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await _callbackTcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("OAuth callback timed out after {Timeout} seconds", _authSettings.OAuthCallbackTimeoutSeconds);
                return new AuthFailure(AuthErrorCodes.OAuthError, "OAuth認証がタイムアウトしました。再度お試しください。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during OAuth flow for provider: {Provider}", provider);
            return new AuthFailure(AuthErrorCodes.UnexpectedError, $"OAuth認証中にエラーが発生しました: {ex.Message}");
        }
        finally
        {
            await StopListenerAsync().ConfigureAwait(false);
            _currentFlowState = null;
        }
    }

    /// <summary>
    /// Start the HTTP listener for OAuth callbacks
    /// </summary>
    private async Task StartListenerAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: HttpListener作成開始");
            _httpListener = new HttpListener();
            // 🔥 [ISSUE#196] localhostのみでリッスン
            // 127.0.0.1も同時登録するとHTTP.sys競合でエラー183が発生する環境がある
            _httpListener.Prefixes.Add($"http://localhost:{_authSettings.OAuthCallbackPort}/");
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: Prefixes追加完了, Port={Port}", _authSettings.OAuthCallbackPort);
            _httpListener.Start();
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: HttpListener.Start()完了, IsListening={IsListening}", _httpListener.IsListening);

            _listenerCts = new CancellationTokenSource();
            // 🔥 [ISSUE#167] リスナー準備完了シグナルを初期化
            _listenerReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _logger.LogInformation("[OAUTH_DEBUG] OAuth callback listener started on port {Port} (localhost)", _authSettings.OAuthCallbackPort);

            // Start listening for requests in the background with proper task tracking
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: ListenForCallbackAsync開始");
            _listenerTask = ListenForCallbackAsync(_listenerCts.Token);
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: ListenForCallbackAsync呼び出し完了（バックグラウンド実行中）");

            // 🔥 [ISSUE#167] GetContextAsync()が待機状態になるまで待つ（最大3秒）
            _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: シグナル待機開始（最大3秒）");
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await _listenerReadyTcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
                _logger.LogDebug("[OAUTH_DEBUG] StartListenerAsync: シグナル受信完了");
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("[OAUTH_DEBUG] StartListenerAsync: シグナル待機タイムアウト（3秒）、続行します");
            }
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5) // Access denied
        {
            _logger.LogError(ex, "Failed to start HTTP listener. Port {Port} may be in use or requires elevation.", _authSettings.OAuthCallbackPort);
            throw new InvalidOperationException($"ポート {_authSettings.OAuthCallbackPort} でHTTPリスナーを開始できませんでした。", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start HTTP listener");
            throw;
        }
    }

    /// <summary>
    /// Listen for incoming OAuth callback requests
    /// </summary>
    private async Task ListenForCallbackAsync(CancellationToken cancellationToken)
    {
        // 🔥 [ISSUE#167] 最初のGetContextAsync()呼び出し前にシグナルを発火
        bool isFirstIteration = true;

        _logger.LogDebug("[OAUTH_DEBUG] ListenForCallbackAsync開始: IsCancelled={IsCancelled}, IsListening={IsListening}",
            cancellationToken.IsCancellationRequested, _httpListener?.IsListening);

        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                // 🔥 [ISSUE#167] GetContextAsync()が待機状態になる直前にシグナルを発火
                if (isFirstIteration)
                {
                    isFirstIteration = false;
                    _logger.LogDebug("[OAUTH_DEBUG] シグナル発火直前");
                    _listenerReadyTcs?.TrySetResult(true);
                    _logger.LogDebug("[OAUTH_DEBUG] シグナル発火完了");
                }

                _logger.LogDebug("[OAUTH_DEBUG] GetContextAsync()呼び出し開始");
                var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                _logger.LogDebug("[OAUTH_DEBUG] HTTPリクエスト受信");
                // Handle callback in a separate task to avoid blocking the listener
                await HandleCallbackSafelyAsync(context).ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                // Expected when stopping the listener
                break;
            }
            catch (ObjectDisposedException)
            {
                // Expected when stopping the listener
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OAuth callback listener loop");
            }
        }

        _logger.LogDebug("OAuth callback listener loop ended");
    }

    /// <summary>
    /// Safely handle callback with exception logging
    /// </summary>
    private async Task HandleCallbackSafelyAsync(HttpListenerContext context)
    {
        try
        {
            await HandleCallbackAsync(context).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in OAuth callback handler");
            _callbackTcs?.TrySetResult(new AuthFailure(AuthErrorCodes.UnexpectedError, "コールバック処理中にエラーが発生しました。"));
        }
    }

    /// <summary>
    /// Handle an incoming OAuth callback request
    /// Validates CSRF state parameter and exchanges code for session using PKCE verifier
    /// </summary>
    private async Task HandleCallbackAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            _logger.LogDebug("OAuthコールバックリクエスト処理開始");

            // Check if this is the OAuth callback path
            if (request.Url?.LocalPath != "/oauth/callback")
            {
                response.StatusCode = 404;
                await SendResponseAsync(response, "Not Found", "ページが見つかりませんでした。", false).ConfigureAwait(false);
                return;
            }

            var queryParams = HttpUtility.ParseQueryString(request.Url.Query);
            var code = queryParams["code"];
            var state = queryParams["state"];
            var error = queryParams["error"];
            var errorDescription = queryParams["error_description"];

            // Handle OAuth provider errors
            if (!string.IsNullOrEmpty(error))
            {
                _logger.LogWarning(
                    "OAuthプロバイダーからエラーが返されました: Error={Error}, Description={Description}",
                    error, errorDescription ?? "(なし)");

                // エラーコード別のユーザーフレンドリーメッセージ
                var (userMessage, errorCode) = error.ToLowerInvariant() switch
                {
                    "access_denied" => (
                        "アクセスが拒否されました。認証をキャンセルしたか、アクセス権限がありません。",
                        AuthErrorCodes.OAuthAccessDenied),
                    "invalid_request" => (
                        "認証リクエストが無効です。Redirect URLがSupabaseに登録されているか確認してください。",
                        AuthErrorCodes.OAuthInvalidRequest),
                    "unauthorized_client" => (
                        "OAuthクライアントが承認されていません。Supabaseの認証設定を確認してください。",
                        AuthErrorCodes.OAuthUnauthorizedClient),
                    "server_error" => (
                        "認証サーバーでエラーが発生しました。しばらく待ってから再度お試しください。",
                        AuthErrorCodes.OAuthServerError),
                    _ => (
                        !string.IsNullOrEmpty(errorDescription)
                            ? $"認証エラー: {errorDescription}"
                            : "認証プロバイダーでエラーが発生しました。再度お試しください。",
                        AuthErrorCodes.OAuthError)
                };

                await SendResponseAsync(response, "認証エラー", userMessage, false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(new AuthFailure(errorCode, $"OAuth Error: {error} - {errorDescription}"));
                return;
            }

            // 🔥 [ISSUE#167] PKCEフローではPKCE自体がCSRF保護を提供するため、
            // カスタムstate検証は不要。アクティブなフロー状態のみ確認。
            if (_currentFlowState == null)
            {
                _logger.LogWarning("OAuth callback received but no active flow state");
                await SendResponseAsync(response, "認証エラー", "認証セッションが見つかりません。再度お試しください。", false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(new AuthFailure(AuthErrorCodes.OAuthError, "認証セッションが見つかりません。"));
                return;
            }

            // state パラメータの存在確認のみログ（セキュリティ: 実際の値は出力しない）
            _logger.LogDebug("OAuth callback state parameter present: {HasState}", !string.IsNullOrEmpty(state));

            // Validate authorization code
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("OAuth callback missing authorization code");
                await SendResponseAsync(response, "認証エラー", "認証コードが見つかりませんでした。", false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(new AuthFailure(AuthErrorCodes.OAuthError, "認証コードが見つかりませんでした。"));
                return;
            }

            _logger.LogInformation("OAuth authorization code received, exchanging for session using PKCE...");

            // Exchange code for session using PKCE verifier
            var result = await _authService.ExchangeCodeForSessionAsync(
                _currentFlowState.PkceVerifier,
                code).ConfigureAwait(false);

            if (result is AuthSuccess success)
            {
                // Store tokens securely
                var tokensStored = await _tokenStorage.StoreTokensAsync(
                    success.Session.AccessToken,
                    success.Session.RefreshToken).ConfigureAwait(false);

                if (!tokensStored)
                {
                    _logger.LogWarning("OAuth authentication successful but token storage failed - session will not persist");
                }
                else
                {
                    _logger.LogInformation("OAuth authentication successful, tokens stored");
                }

                await SendResponseAsync(response, "認証成功", "ログインに成功しました！このウィンドウを閉じてアプリケーションに戻ってください。", true).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(result);
            }
            else
            {
                var failure = result as AuthFailure;
                _logger.LogWarning("OAuth code exchange failed: {ErrorCode}", failure?.ErrorCode ?? "unknown");
                await SendResponseAsync(response, "認証エラー", failure?.Message ?? "セッションの取得に失敗しました。", false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(result);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing OAuth callback");
            response.StatusCode = 500;
            await SendResponseAsync(response, "エラー", "内部エラーが発生しました。", false).ConfigureAwait(false);
            _callbackTcs?.TrySetResult(new AuthFailure(AuthErrorCodes.UnexpectedError, ex.Message));
        }
    }

    /// <summary>
    /// Send an HTML response to the browser
    /// </summary>
    private static async Task SendResponseAsync(HttpListenerResponse response, string title, string message, bool success)
    {
        var statusColor = success ? "#4CAF50" : "#f44336";
        var statusIcon = success ? "✓" : "✗";
        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>Baketa - {{title}}</title>
                <style>
                    body {
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                        background-color: #f5f5f5;
                    }
                    .container {
                        text-align: center;
                        padding: 40px;
                        background: white;
                        border-radius: 8px;
                        box-shadow: 0 2px 10px rgba(0,0,0,0.1);
                        max-width: 400px;
                    }
                    .status {
                        font-size: 48px;
                        margin-bottom: 20px;
                    }
                    h1 {
                        color: {{statusColor}};
                        margin-bottom: 16px;
                    }
                    p {
                        color: #666;
                        line-height: 1.6;
                    }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="status">{{statusIcon}}</div>
                    <h1>{{title}}</h1>
                    <p>{{message}}</p>
                </div>
            </body>
            </html>
            """;

        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer).ConfigureAwait(false);
        response.Close();
    }

    /// <summary>
    /// Stop the HTTP listener and clean up resources
    /// </summary>
    private async Task StopListenerAsync()
    {
        try
        {
            _listenerCts?.Cancel();

            // Wait for listener task to complete with timeout
            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("Listener task did not complete within timeout");
                }
                catch (OperationCanceledException)
                {
                    // Expected
                }
            }

            if (_httpListener?.IsListening == true)
            {
                _httpListener.Stop();
            }

            _httpListener = null;
            _listenerCts?.Dispose();
            _listenerCts = null;
            _listenerTask = null;
            _currentFlowState = null;
            // 🔥 [ISSUE#167] リスナー準備完了シグナルをクリーンアップ
            _listenerReadyTcs = null;

            _logger.LogDebug("OAuth callback listener stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping HTTP listener");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// Async dispose implementation
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        await StopListenerAsync().ConfigureAwait(false);
        _disposed = true;

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Sync dispose implementation (calls async dispose)
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        // Sync dispose - use GetAwaiter().GetResult() as fallback
        try
        {
            StopListenerAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during sync dispose");
        }

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
