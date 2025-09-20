using Baketa.Core.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Processing;

/// <summary>
/// パイプライン実行の排他制御を管理する実装クラス
/// Strategy A: 並行パイプライン実行を防ぎ、SafeImage競合を根絶
///
/// UltraThink分析結果:
/// - 並行パイプライン実行による参照カウント競合が根本原因
/// - SemaphoreSlim(1,1)により確実に単一実行を保証
/// - Phase 3.2B二重参照戦略より遥かにシンプルで効果的
/// </summary>
public sealed class PipelineExecutionManager : IPipelineExecutionManager, IDisposable
{
    private readonly ILogger<PipelineExecutionManager> _logger;
    private readonly SemaphoreSlim _executionSemaphore;
    private volatile bool _isExecuting;
    private bool _disposed;

    public PipelineExecutionManager(ILogger<PipelineExecutionManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _executionSemaphore = new SemaphoreSlim(1, 1);

        _logger.LogInformation("🎯 [STRATEGY_A] PipelineExecutionManager初期化 - 排他制御による並行実行防止");
    }

    /// <inheritdoc />
    public bool IsExecuting => _isExecuting;

    /// <inheritdoc />
    public async Task<T> ExecuteExclusivelyAsync<T>(Func<CancellationToken, Task<T>> pipelineFunc, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(pipelineFunc);

        var pipelineId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogDebug("🎯 [STRATEGY_A] パイプライン実行要求 - ID: {PipelineId}, 待機開始", pipelineId);

        await _executionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            _isExecuting = true;
            _logger.LogInformation("🎯 [STRATEGY_A] パイプライン排他実行開始 - ID: {PipelineId}", pipelineId);

            var result = await pipelineFunc(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("🎯 [STRATEGY_A] パイプライン排他実行完了 - ID: {PipelineId}", pipelineId);
            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("🎯 [STRATEGY_A] パイプライン実行はキャンセルされました - ID: {PipelineId}", pipelineId);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "🚨 [STRATEGY_A] パイプライン実行エラー - ID: {PipelineId}", pipelineId);
            throw;
        }
        finally
        {
            _isExecuting = false;
            _executionSemaphore.Release();
            _logger.LogDebug("🎯 [STRATEGY_A] パイプライン実行完了、セマフォ解放 - ID: {PipelineId}", pipelineId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _logger.LogInformation("🎯 [STRATEGY_A] PipelineExecutionManager破棄開始");

        _executionSemaphore?.Dispose();
        _disposed = true;

        _logger.LogInformation("🎯 [STRATEGY_A] PipelineExecutionManager破棄完了");
    }
}