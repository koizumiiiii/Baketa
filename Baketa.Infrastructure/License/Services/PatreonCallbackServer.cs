using System.Net;
using System.Text;
using System.Web;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Infrastructure.License.Services;

/// <summary>
/// Patreon OAuth コールバックを受け取るローカルHTTPサーバー
/// localhost:8080/patreon/callback でリッスンし、認証コードを処理する
/// </summary>
public sealed class PatreonCallbackServer : IAsyncDisposable, IDisposable
{
    private readonly IPatreonOAuthService _oauthService;
    private readonly PatreonSettings _settings;
    private readonly ILogger<PatreonCallbackServer> _logger;

    private HttpListener? _httpListener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private TaskCompletionSource<PatreonAuthResult>? _callbackTcs;
    private TaskCompletionSource<bool>? _listenerReadyTcs;
    private bool _disposed;

    /// <summary>
    /// コールバックサーバーのデフォルトポート
    /// </summary>
    public const int DefaultCallbackPort = 8080;

    /// <summary>
    /// コールバックパス
    /// </summary>
    public const string CallbackPath = "/patreon/callback";

    /// <summary>
    /// コールバックタイムアウト（秒）
    /// </summary>
    public int CallbackTimeoutSeconds { get; set; } = 300;

    public PatreonCallbackServer(
        IPatreonOAuthService oauthService,
        IOptions<PatreonSettings> settings,
        ILogger<PatreonCallbackServer> logger)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// OAuth認証フローを開始し、コールバックを待機する
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>認証結果</returns>
    public async Task<PatreonAuthResult> StartAndWaitForCallbackAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        try
        {
            _logger.LogInformation("[PATREON_CALLBACK] OAuth認証フロー開始");

            // コールバック完了シグナル
            _callbackTcs = new TaskCompletionSource<PatreonAuthResult>();

            // HTTPリスナー開始
            await StartListenerAsync(cancellationToken).ConfigureAwait(false);

            // タイムアウト付きでコールバックを待機
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(CallbackTimeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            try
            {
                return await _callbackTcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("[PATREON_CALLBACK] コールバックがタイムアウトしました（{Timeout}秒）", CallbackTimeoutSeconds);
                return PatreonAuthResult.CreateFailure("TIMEOUT", "認証がタイムアウトしました。再度お試しください。");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PATREON_CALLBACK] OAuth認証フロー中にエラー");
            return PatreonAuthResult.CreateFailure("ERROR", $"認証中にエラーが発生しました: {ex.Message}");
        }
        finally
        {
            await StopListenerAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// HTTPリスナーを開始
    /// </summary>
    private async Task StartListenerAsync(CancellationToken cancellationToken)
    {
        try
        {
            var port = GetPortFromRedirectUri();
            _logger.LogDebug("[PATREON_CALLBACK] HTTPリスナー開始: ポート={Port}", port);

            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://localhost:{port}/");
            _httpListener.Start();

            _listenerCts = new CancellationTokenSource();
            _listenerReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _logger.LogInformation("[PATREON_CALLBACK] HTTPリスナー開始完了: http://localhost:{Port}/", port);

            // バックグラウンドでリクエストをリッスン
            _listenerTask = ListenForCallbackAsync(_listenerCts.Token);

            // リスナー準備完了を待機（最大3秒）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
            try
            {
                await _listenerReadyTcs.Task.WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                _logger.LogWarning("[PATREON_CALLBACK] リスナー準備シグナルがタイムアウト、続行します");
            }
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 5)
        {
            _logger.LogError(ex, "[PATREON_CALLBACK] HTTPリスナー開始失敗: ポートが使用中または権限不足");
            throw new InvalidOperationException("OAuthコールバックサーバーを開始できませんでした。ポートが使用中の可能性があります。", ex);
        }
    }

    /// <summary>
    /// RedirectUriからポート番号を抽出
    /// </summary>
    private int GetPortFromRedirectUri()
    {
        if (Uri.TryCreate(_settings.RedirectUri, UriKind.Absolute, out var uri))
        {
            return uri.Port > 0 ? uri.Port : DefaultCallbackPort;
        }
        return DefaultCallbackPort;
    }

    /// <summary>
    /// コールバックリクエストをリッスン
    /// </summary>
    private async Task ListenForCallbackAsync(CancellationToken cancellationToken)
    {
        bool isFirstIteration = true;

        while (!cancellationToken.IsCancellationRequested && _httpListener?.IsListening == true)
        {
            try
            {
                if (isFirstIteration)
                {
                    isFirstIteration = false;
                    _listenerReadyTcs?.TrySetResult(true);
                }

                var context = await _httpListener.GetContextAsync().ConfigureAwait(false);
                await HandleCallbackAsync(context).ConfigureAwait(false);
            }
            catch (HttpListenerException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PATREON_CALLBACK] リスナーループでエラー");
            }
        }
    }

    /// <summary>
    /// コールバックリクエストを処理
    /// </summary>
    private async Task HandleCallbackAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        try
        {
            // ログインジェクション対策: ユーザー入力をサニタイズ
            var sanitizedPath = SanitizeForLog(request.Url?.LocalPath);
            _logger.LogDebug("[PATREON_CALLBACK] リクエスト受信: {Path}", sanitizedPath);

            // パスを検証
            if (request.Url?.LocalPath != CallbackPath)
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

            // Patreonからのエラーチェック
            if (!string.IsNullOrEmpty(error))
            {
                // ログインジェクション対策: ユーザー入力をサニタイズ
                var sanitizedError = SanitizeForLog(error);
                var sanitizedDescription = SanitizeForLog(errorDescription);
                _logger.LogWarning("[PATREON_CALLBACK] Patreonエラー: {Error} - {Description}", sanitizedError, sanitizedDescription);

                // XSS対策: HTMLエンコードされた安全なメッセージを使用
                var safeMessage = string.IsNullOrEmpty(errorDescription)
                    ? "Patreonで認証がキャンセルされました。"
                    : "認証エラーが発生しました。再度お試しください。";
                await SendResponseAsync(response, "認証エラー", safeMessage, false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(PatreonAuthResult.CreateFailure($"PATREON_{error.ToUpperInvariant()}", errorDescription ?? "認証エラー"));
                return;
            }

            // 必須パラメータチェック
            if (string.IsNullOrEmpty(code))
            {
                _logger.LogWarning("[PATREON_CALLBACK] 認証コードがありません");
                await SendResponseAsync(response, "認証エラー", "認証コードが見つかりませんでした。", false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(PatreonAuthResult.CreateFailure("MISSING_CODE", "認証コードがありません"));
                return;
            }

            if (string.IsNullOrEmpty(state))
            {
                _logger.LogWarning("[PATREON_CALLBACK] stateパラメータがありません");
                await SendResponseAsync(response, "認証エラー", "セキュリティパラメータが見つかりませんでした。", false).ConfigureAwait(false);
                _callbackTcs?.TrySetResult(PatreonAuthResult.CreateFailure("MISSING_STATE", "stateパラメータがありません"));
                return;
            }

            _logger.LogInformation("[PATREON_CALLBACK] 認証コード受信、トークン交換開始");

            // OAuthサービスでトークン交換
            var result = await _oauthService.HandleCallbackAsync(code, state, CancellationToken.None).ConfigureAwait(false);

            if (result.Success)
            {
                _logger.LogInformation("[PATREON_CALLBACK] ✅ 認証成功: Plan={Plan}", result.Plan);
                await SendResponseAsync(response, "認証成功",
                    $"Patreon連携が完了しました！（{result.Plan}プラン）\nこのウィンドウを閉じてアプリに戻ってください。", true).ConfigureAwait(false);
            }
            else
            {
                _logger.LogWarning("[PATREON_CALLBACK] 認証失敗: {Error}", result.ErrorMessage);
                await SendResponseAsync(response, "認証エラー", result.ErrorMessage ?? "認証に失敗しました。", false).ConfigureAwait(false);
            }

            _callbackTcs?.TrySetResult(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PATREON_CALLBACK] コールバック処理中にエラー");
            response.StatusCode = 500;
            await SendResponseAsync(response, "エラー", "内部エラーが発生しました。", false).ConfigureAwait(false);
            _callbackTcs?.TrySetResult(PatreonAuthResult.CreateFailure("INTERNAL_ERROR", ex.Message));
        }
    }

    /// <summary>
    /// HTMLレスポンスを送信
    /// </summary>
    private static async Task SendResponseAsync(HttpListenerResponse response, string title, string message, bool success)
    {
        var statusColor = success ? "#4CAF50" : "#f44336";
        var statusIcon = success ? "✓" : "✗";

        // XSS対策: HTMLエンコード
        var safeTitle = WebUtility.HtmlEncode(title);
        var safeMessage = WebUtility.HtmlEncode(message);

        var html = $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <meta charset="UTF-8">
                <title>Baketa - {{safeTitle}}</title>
                <style>
                    body {
                        font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
                        display: flex;
                        justify-content: center;
                        align-items: center;
                        height: 100vh;
                        margin: 0;
                        background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
                    }
                    .container {
                        text-align: center;
                        padding: 40px 60px;
                        background: white;
                        border-radius: 16px;
                        box-shadow: 0 10px 40px rgba(0,0,0,0.2);
                        max-width: 450px;
                    }
                    .status {
                        font-size: 64px;
                        margin-bottom: 20px;
                        color: {{statusColor}};
                    }
                    h1 {
                        color: {{statusColor}};
                        margin-bottom: 16px;
                        font-size: 28px;
                    }
                    p {
                        color: #666;
                        line-height: 1.8;
                        font-size: 16px;
                        white-space: pre-line;
                    }
                    .logo {
                        font-size: 24px;
                        color: #764ba2;
                        margin-bottom: 20px;
                        font-weight: bold;
                    }
                </style>
            </head>
            <body>
                <div class="container">
                    <div class="logo">🎮 Baketa</div>
                    <div class="status">{{statusIcon}}</div>
                    <h1>{{safeTitle}}</h1>
                    <p>{{safeMessage}}</p>
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
    /// HTTPリスナーを停止
    /// </summary>
    private async Task StopListenerAsync()
    {
        try
        {
            _listenerCts?.Cancel();

            if (_listenerTask != null)
            {
                try
                {
                    await _listenerTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    _logger.LogWarning("[PATREON_CALLBACK] リスナータスクがタイムアウト");
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
            _listenerReadyTcs = null;

            _logger.LogDebug("[PATREON_CALLBACK] HTTPリスナー停止完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PATREON_CALLBACK] HTTPリスナー停止中にエラー");
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// ログインジェクション対策: ユーザー入力をサニタイズ
    /// 改行、制御文字を除去し、長さを制限
    /// </summary>
    private static string? SanitizeForLog(string? input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // 制御文字と改行を除去
        var sanitized = new string(input
            .Where(c => !char.IsControl(c) && c != '\r' && c != '\n')
            .ToArray());

        // 長さを制限（ログ肥大化防止）
        const int maxLength = 200;
        if (sanitized.Length > maxLength)
            sanitized = sanitized[..maxLength] + "...";

        return sanitized;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopListenerAsync().ConfigureAwait(false);
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        try
        {
            StopListenerAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PATREON_CALLBACK] Dispose中にエラー");
        }
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
