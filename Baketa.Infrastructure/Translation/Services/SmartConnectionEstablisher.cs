using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// 接続確立戦略のインターフェース
/// </summary>
public interface IConnectionStrategy
{
    /// <summary>
    /// サーバーの準備状況を確認
    /// </summary>
    Task<bool> IsServerReady(int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// 戦略の名前
    /// </summary>
    string StrategyName { get; }
}

/// <summary>
/// TCP ポートリスニング確認戦略
/// </summary>
public class TcpPortListeningStrategy : IConnectionStrategy
{
    private readonly ILogger<TcpPortListeningStrategy> _logger;

    public string StrategyName => "TcpPortListening";

    public TcpPortListeningStrategy(ILogger<TcpPortListeningStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsServerReady(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcpClient = new System.Net.Sockets.TcpClient();
            await tcpClient.ConnectAsync("127.0.0.1", port, cancellationToken);
            _logger.LogDebug("🔗 TCP接続成功: Port {Port}", port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("🔌 TCP接続失敗: Port {Port}, Error: {Error}", port, ex.Message);
            return false;
        }
    }
}

/// <summary>
/// HTTP ヘルスチェック戦略
/// </summary>
public class HttpHealthCheckStrategy : IConnectionStrategy, IDisposable
{
    private readonly ILogger<HttpHealthCheckStrategy> _logger;
    private readonly HttpClient _httpClient;
    private bool _disposed;

    public string StrategyName => "HttpHealthCheck";

    public HttpHealthCheckStrategy(ILogger<HttpHealthCheckStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<bool> IsServerReady(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        try
        {
            var response = await _httpClient.GetAsync($"http://127.0.0.1:{port}/health", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogDebug("💚 ヘルスチェック成功: Port {Port}, Response: {Response}", port, content);
                return true;
            }

            _logger.LogDebug("💛 ヘルスチェック応答異常: Port {Port}, Status: {Status}", port, response.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("💔 ヘルスチェック失敗: Port {Port}, Error: {Error}", port, ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _httpClient.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// TCP ハンドシェイク戦略
/// </summary>
public class TcpHandshakeStrategy : IConnectionStrategy
{
    private readonly ILogger<TcpHandshakeStrategy> _logger;

    public string StrategyName => "TcpHandshake";

    public TcpHandshakeStrategy(ILogger<TcpHandshakeStrategy> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsServerReady(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            using var tcpClient = new System.Net.Sockets.TcpClient();
            // 🔧 UltraThink Phase 4.8: タイムアウト設定追加（5秒）
            tcpClient.ReceiveTimeout = 5000;
            tcpClient.SendTimeout = 5000;

            await tcpClient.ConnectAsync("127.0.0.1", port, cancellationToken);

            var stream = tcpClient.GetStream();
            var writer = new StreamWriter(stream, System.Text.Encoding.UTF8);
            var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            // 🚀 UltraThink Phase 4.8: 翻訳テストリクエスト（Python翻訳サーバー対応）
            var testRequest = "{\"text\":\"test\",\"source_lang\":\"en\",\"target_lang\":\"ja\"}";
            await writer.WriteLineAsync(testRequest);
            await writer.FlushAsync();

            // タイムアウト付きレスポンス読み取り
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var response = await reader.ReadLineAsync();
            if (!string.IsNullOrEmpty(response))
            {
                // 🎯 成功レスポンス確認（JSONに"success":trueが含まれているかチェック）
                if (response.Contains("\"success\":true") || response.Contains("\"success\": true"))
                {
                    _logger.LogDebug("🤝 翻訳テスト成功: Port {Port}, Response: {Response}", port, response?.Substring(0, Math.Min(100, response.Length)));
                    return true;
                }
                else
                {
                    _logger.LogDebug("⚠️ 翻訳テスト応答異常: Port {Port}, Response: {Response}", port, response?.Substring(0, Math.Min(100, response.Length)));
                    return false;
                }
            }

            _logger.LogDebug("🤷 翻訳テスト無応答: Port {Port}", port);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("❌ 翻訳テスト失敗: Port {Port}, Error: {Error}", port, ex.Message);
            return false;
        }
    }
}

/// <summary>
/// スマート接続確立サービス
/// 複数の戦略を用いて接続の信頼性を向上させます
/// </summary>
public sealed class SmartConnectionEstablisher : IDisposable
{
    private readonly ILogger<SmartConnectionEstablisher> _logger;
    private readonly IConnectionStrategy[] _strategies;
    private bool _disposed;

    public SmartConnectionEstablisher(ILogger<SmartConnectionEstablisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 戦略インスタンス作成（ロガー共有で軽量化）
        _strategies = new IConnectionStrategy[]
        {
            new TcpPortListeningStrategy(logger as ILogger<TcpPortListeningStrategy> ?? new NullLogger<TcpPortListeningStrategy>()),
            new HttpHealthCheckStrategy(logger as ILogger<HttpHealthCheckStrategy> ?? new NullLogger<HttpHealthCheckStrategy>()),
            new TcpHandshakeStrategy(logger as ILogger<TcpHandshakeStrategy> ?? new NullLogger<TcpHandshakeStrategy>())
        };

        _logger.LogDebug("🧠 SmartConnectionEstablisher初期化: {StrategyCount}戦略", _strategies.Length);
    }

    /// <summary>
    /// サーバーの準備完了を待機（複数戦略 + ウォームアップ期間）
    /// </summary>
    /// <param name="port">接続ポート</param>
    /// <param name="timeout">最大待機時間</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>準備完了の場合true</returns>
    public async Task<bool> WaitForServerReady(int port, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var startTime = DateTime.UtcNow;
        var endTime = startTime + timeout;
        var retryCount = 0;

        _logger.LogInformation("⏳ サーバー準備完了待機開始: Port {Port}, Timeout {Timeout}秒",
            port, timeout.TotalSeconds);

        while (DateTime.UtcNow < endTime && !cancellationToken.IsCancellationRequested)
        {
            // 全戦略を順次実行
            foreach (var strategy in _strategies)
            {
                try
                {
                    if (await strategy.IsServerReady(port, cancellationToken))
                    {
                        _logger.LogDebug("✅ 戦略成功: {Strategy} (Port {Port})", strategy.StrategyName, port);

                        // Gemini推奨: 2秒ウォームアップ期間
                        _logger.LogDebug("🔥 ウォームアップ期間待機: 2秒");
                        await Task.Delay(2000, cancellationToken);

                        var elapsed = DateTime.UtcNow - startTime;
                        _logger.LogInformation("🚀 サーバー準備完了: Port {Port}, 経過時間 {Elapsed:F1}秒, 成功戦略: {Strategy}",
                            port, elapsed.TotalSeconds, strategy.StrategyName);

                        return true;
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("⏹️ サーバー準備完了待機がキャンセルされました");
                    return false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ 戦略実行エラー: {Strategy} (Port {Port})", strategy.StrategyName, port);
                }
            }

            // Exponential Backoff (最大5秒)
            var delay = Math.Min(5000, (int)Math.Pow(2, retryCount) * 500);
            _logger.LogDebug("⏱️ リトライ待機: {Delay}ms (試行回数: {RetryCount})", delay, retryCount + 1);

            await Task.Delay(delay, cancellationToken);
            retryCount++;
        }

        var totalElapsed = DateTime.UtcNow - startTime;
        _logger.LogWarning("❌ サーバー準備完了タイムアウト: Port {Port}, 経過時間 {Elapsed:F1}秒",
            port, totalElapsed.TotalSeconds);

        return false;
    }

    /// <summary>
    /// 単発の接続準備確認（ウォームアップなし）
    /// </summary>
    /// <param name="port">接続ポート</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>準備完了の場合true</returns>
    public async Task<bool> IsServerReady(int port, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        foreach (var strategy in _strategies)
        {
            try
            {
                if (await strategy.IsServerReady(port, cancellationToken))
                {
                    _logger.LogDebug("✅ サーバー準備確認成功: {Strategy} (Port {Port})", strategy.StrategyName, port);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "❌ サーバー準備確認失敗: {Strategy} (Port {Port})", strategy.StrategyName, port);
            }
        }

        _logger.LogDebug("❌ サーバー準備未完了: Port {Port} (全戦略失敗)", port);
        return false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // IDisposable実装戦略のDispose
        foreach (var strategy in _strategies)
        {
            if (strategy is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        _disposed = true;
    }
}
