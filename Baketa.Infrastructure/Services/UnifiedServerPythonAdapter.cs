using Baketa.Core.Abstractions.Server;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Issue #292: 統合AIサーバーをIPythonServerManager互換で使用するアダプター
/// 既存のGrpcTranslationEngineAdapterと互換性を維持
/// </summary>
public sealed class UnifiedServerPythonAdapter : IPythonServerManager
{
    private readonly IUnifiedAIServerManager _unifiedServer;
    private readonly ILogger<UnifiedServerPythonAdapter> _logger;
    private readonly object _lock = new();
    private bool _disposed;
    private UnifiedServerInfo? _serverInfo;

    public UnifiedServerPythonAdapter(
        IUnifiedAIServerManager unifiedServer,
        ILogger<UnifiedServerPythonAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(unifiedServer);
        ArgumentNullException.ThrowIfNull(logger);

        _unifiedServer = unifiedServer;
        _logger = logger;

        _logger.LogInformation("[UnifiedAdapter] Python adapter initialized for unified server");
    }

    /// <inheritdoc/>
    public async Task<IPythonServerInfo> StartServerAsync(string languagePair)
    {
        _logger.LogInformation("[UnifiedAdapter] StartServerAsync called for languagePair: {LanguagePair}", languagePair);

        // 統合サーバーを起動
        var success = await _unifiedServer.StartServerAsync().ConfigureAwait(false);

        if (!success)
        {
            _logger.LogError("[UnifiedAdapter] Failed to start unified server");
            throw new InvalidOperationException("Failed to start unified AI server");
        }

        lock (_lock)
        {
            _serverInfo = new UnifiedServerInfo(
                Port: _unifiedServer.Port,
                LanguagePair: languagePair,
                StartedAt: DateTime.UtcNow,
                IsHealthy: _unifiedServer.IsReady);
        }

        _logger.LogInformation("[UnifiedAdapter] Unified server started on port {Port}", _unifiedServer.Port);
        return _serverInfo;
    }

    /// <inheritdoc/>
    public async Task StopServerAsync(int port)
    {
        _logger.LogInformation("[UnifiedAdapter] StopServerAsync called for port: {Port}", port);

        // 統合サーバーは単一ポートなので、ポートが一致する場合のみ停止
        if (port == _unifiedServer.Port)
        {
            await _unifiedServer.StopServerAsync().ConfigureAwait(false);
            lock (_lock)
            {
                _serverInfo = null;
            }
        }
        else
        {
            _logger.LogWarning("[UnifiedAdapter] Port mismatch: requested {Requested}, actual {Actual}",
                port, _unifiedServer.Port);
        }
    }

    /// <inheritdoc/>
    public async Task StopServerAsync(string languagePair)
    {
        _logger.LogInformation("[UnifiedAdapter] StopServerAsync called for languagePair: {LanguagePair}", languagePair);

        // 統合サーバーは言語ペアに関係なく単一サーバーなので、停止リクエストを処理
        // ただし、統合サーバーは両方のサービス（翻訳+OCR）を提供するため、
        // 翻訳側からの停止は実際には行わない（OCR側も使用中の可能性があるため）
        _logger.LogDebug("[UnifiedAdapter] Unified server stop deferred (may be in use by OCR service)");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IPythonServerInfo>> GetActiveServersAsync()
    {
        lock (_lock)
        {
            if (_serverInfo != null && _unifiedServer.IsReady)
            {
                // サーバー情報を最新状態で更新
                _serverInfo = _serverInfo with { IsHealthy = _unifiedServer.IsReady };
                return Task.FromResult<IReadOnlyList<IPythonServerInfo>>(new[] { _serverInfo });
            }

            return Task.FromResult<IReadOnlyList<IPythonServerInfo>>(Array.Empty<IPythonServerInfo>());
        }
    }

    /// <inheritdoc/>
    public Task<IPythonServerInfo?> GetServerAsync(string languagePair)
    {
        lock (_lock)
        {
            if (_serverInfo != null && _unifiedServer.IsReady)
            {
                // 統合サーバーは全言語対応なので、常に返す
                _serverInfo = _serverInfo with
                {
                    LanguagePair = languagePair,
                    IsHealthy = _unifiedServer.IsReady
                };
                return Task.FromResult<IPythonServerInfo?>(_serverInfo);
            }

            return Task.FromResult<IPythonServerInfo?>(null);
        }
    }

    /// <inheritdoc/>
    public async Task PerformHealthCheckAsync()
    {
        _logger.LogDebug("[UnifiedAdapter] Performing health check");

        var isHealthy = await _unifiedServer.CheckServerHealthAsync().ConfigureAwait(false);

        lock (_lock)
        {
            if (_serverInfo != null)
            {
                _serverInfo = _serverInfo with { IsHealthy = isHealthy };
            }
        }

        _logger.LogDebug("[UnifiedAdapter] Health check result: {IsHealthy}", isHealthy);
    }

    /// <inheritdoc/>
    public void InitializeHealthCheckTimer()
    {
        // 統合サーバーは独自のヘルスチェックを持つため、ここでは何もしない
        _logger.LogDebug("[UnifiedAdapter] Health check timer initialization delegated to unified server");
    }

    /// <inheritdoc/>
    public async Task<IPythonServerInfo?> RestartServerAsync(string languagePair)
    {
        _logger.LogInformation("[UnifiedAdapter] RestartServerAsync called for languagePair: {LanguagePair}", languagePair);

        // 停止して再起動
        await _unifiedServer.StopServerAsync().ConfigureAwait(false);

        lock (_lock)
        {
            _serverInfo = null;
        }

        // 少し待機してから再起動
        await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);

        try
        {
            return await StartServerAsync(languagePair).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UnifiedAdapter] Failed to restart server");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 統合サーバーの破棄はIUnifiedAIServerManager側の責任
        _logger.LogDebug("[UnifiedAdapter] Adapter disposed (server lifecycle managed by IUnifiedAIServerManager)");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("[UnifiedAdapter] Adapter disposed async");
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 統合サーバー用のサーバー情報レコード
    /// </summary>
    private sealed record UnifiedServerInfo(
        int Port,
        string LanguagePair,
        DateTime StartedAt,
        bool IsHealthy) : IPythonServerInfo
    {
        public TimeSpan Uptime => DateTime.UtcNow - StartedAt;
    }
}
