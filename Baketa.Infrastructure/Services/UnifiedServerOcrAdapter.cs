using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Server;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Issue #292: 統合AIサーバーをIOcrServerManager互換で使用するアダプター
/// 既存のSuryaOcrEngineと互換性を維持
/// </summary>
public sealed class UnifiedServerOcrAdapter : IOcrServerManager
{
    private readonly IUnifiedAIServerManager _unifiedServer;
    private readonly ILogger<UnifiedServerOcrAdapter> _logger;
    private bool _disposed;

    public UnifiedServerOcrAdapter(
        IUnifiedAIServerManager unifiedServer,
        ILogger<UnifiedServerOcrAdapter> logger)
    {
        ArgumentNullException.ThrowIfNull(unifiedServer);
        ArgumentNullException.ThrowIfNull(logger);

        _unifiedServer = unifiedServer;
        _logger = logger;

        _logger.LogInformation("[UnifiedOcrAdapter] OCR adapter initialized for unified server");
    }

    /// <inheritdoc/>
    public bool IsReady => _unifiedServer.IsReady;

    /// <inheritdoc/>
    public int Port => _unifiedServer.Port;

    /// <inheritdoc/>
    public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[UnifiedOcrAdapter] StartServerAsync called");

        var success = await _unifiedServer.StartServerAsync(cancellationToken).ConfigureAwait(false);

        if (success)
        {
            _logger.LogInformation("[UnifiedOcrAdapter] Unified server started for OCR on port {Port}", Port);
        }
        else
        {
            _logger.LogError("[UnifiedOcrAdapter] Failed to start unified server for OCR");
        }

        return success;
    }

    /// <inheritdoc/>
    public async Task StopServerAsync()
    {
        _logger.LogInformation("[UnifiedOcrAdapter] StopServerAsync called");

        // 統合サーバーは両方のサービス（翻訳+OCR）を提供するため、
        // OCR側からの停止リクエストは実際には行わない（翻訳側も使用中の可能性があるため）
        // 実際の停止はIUnifiedAIServerManager.DisposeAsync()で行われる
        _logger.LogDebug("[UnifiedOcrAdapter] Unified server stop deferred (may be in use by translation service)");

        await Task.CompletedTask.ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("[UnifiedOcrAdapter] Adapter disposed (server lifecycle managed by IUnifiedAIServerManager)");
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
