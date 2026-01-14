using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers.Translation;

/// <summary>
/// 翻訳停止要求イベントハンドラ
/// Phase 6.1: Stop押下後も処理継続問題の修正
///
/// StopTranslationRequestEventを受信し、TranslationOrchestrationService.StopAsync()を呼び出して
/// 自動翻訳ループを確実に停止する
/// </summary>
public sealed class StopTranslationRequestEventHandler : IEventProcessor<StopTranslationRequestEvent>
{
    private readonly ITranslationOrchestrationService _orchestrationService;
    private readonly ILogger<StopTranslationRequestEventHandler> _logger;

    public StopTranslationRequestEventHandler(
        ITranslationOrchestrationService orchestrationService,
        ILogger<StopTranslationRequestEventHandler> logger)
    {
        _orchestrationService = orchestrationService ?? throw new ArgumentNullException(nameof(orchestrationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 100; // 高優先度で処理（停止は即座に実行すべき）

    /// <inheritdoc />
    public bool SynchronousExecution => true; // 同期実行で確実に停止を完了させる

    /// <summary>
    /// StopTranslationRequestEventを処理
    /// </summary>
    public async Task HandleAsync(StopTranslationRequestEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("🛑 [STOP_HANDLER] Stop translation request received - EventId: {EventId}", eventData.Id);

        try
        {
            // TranslationOrchestrationService.StopAsync()を呼び出してCancellationTokenをキャンセル
            await _orchestrationService.StopAsync().ConfigureAwait(false);

            _logger.LogInformation("✅ [STOP_HANDLER] Translation stopped successfully - EventId: {EventId}", eventData.Id);
        }
        catch (OperationCanceledException ex)
        {
            // キャンセルは正常な停止処理の一部
            _logger.LogDebug(ex, "⚠️ [STOP_HANDLER] Stop operation was cancelled - EventId: {EventId}", eventData.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [STOP_HANDLER] Failed to stop translation - EventId: {EventId}, ErrorMessage: {ErrorMessage}",
                eventData.Id, ex.Message);
            throw;
        }
    }
}
