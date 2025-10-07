using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// オーバーレイ診断イベント発行を担当するサービス実装
/// Phase 4.1: InPlaceTranslationOverlayManagerから診断ロジックを抽出
/// </summary>
public class OverlayDiagnosticService(
    IEventAggregator eventAggregator,
    ILogger<OverlayDiagnosticService> logger) : IOverlayDiagnosticService
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<OverlayDiagnosticService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// オーバーレイ表示開始時の診断イベントを発行します
    /// </summary>
    public async Task PublishOverlayStartedAsync(
        TextChunk textChunk,
        string sessionId,
        bool isInitialized,
        bool isDisposed)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(sessionId);

        try
        {
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "Overlay",
                IsSuccess = true,
                ProcessingTimeMs = 0,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"オーバーレイ表示開始: ChunkId={textChunk.ChunkId}, テキスト長={textChunk.TranslatedText?.Length ?? 0}",
                Metrics = new Dictionary<string, object>
                {
                    { "ChunkId", textChunk.ChunkId },
                    { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                    { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                    { "BoundsX", textChunk.CombinedBounds.X },
                    { "BoundsY", textChunk.CombinedBounds.Y },
                    { "BoundsWidth", textChunk.CombinedBounds.Width },
                    { "BoundsHeight", textChunk.CombinedBounds.Height },
                    { "CanShowInPlace", textChunk.CanShowInPlace() },
                    { "IsInitialized", isInitialized },
                    { "IsDisposed", isDisposed }
                }
            }).ConfigureAwait(false);

            _logger.LogDebug("診断イベント発行: オーバーレイ表示開始 - ChunkId: {ChunkId}", textChunk.ChunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "診断イベント発行エラー: オーバーレイ表示開始 - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// オーバーレイ表示成功時の診断イベントを発行します
    /// </summary>
    public async Task PublishOverlaySuccessAsync(
        TextChunk textChunk,
        string sessionId,
        long processingTimeMs,
        int activeOverlaysCount,
        bool isUpdate)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(sessionId);

        try
        {
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "Overlay",
                IsSuccess = true,
                ProcessingTimeMs = processingTimeMs,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Information,
                Message = $"オーバーレイ表示成功: ChunkId={textChunk.ChunkId}, 処理時間={processingTimeMs}ms",
                Metrics = new Dictionary<string, object>
                {
                    { "ChunkId", textChunk.ChunkId },
                    { "ProcessingTimeMs", processingTimeMs },
                    { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                    { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                    { "BoundsArea", textChunk.CombinedBounds.Width * textChunk.CombinedBounds.Height },
                    { "ActiveOverlaysCount", activeOverlaysCount },
                    { "IsUpdate", isUpdate },
                    { "DisplayType", "InPlace" }
                }
            }).ConfigureAwait(false);

            _logger.LogDebug("診断イベント発行: オーバーレイ表示成功 - ChunkId: {ChunkId}, 処理時間: {ProcessingTimeMs}ms",
                textChunk.ChunkId, processingTimeMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "診断イベント発行エラー: オーバーレイ表示成功 - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }

    /// <summary>
    /// オーバーレイ表示失敗時の診断イベントを発行します
    /// </summary>
    public async Task PublishOverlayFailedAsync(
        TextChunk textChunk,
        string sessionId,
        long processingTimeMs,
        Exception exception,
        bool isInitialized,
        bool isDisposed,
        int activeOverlaysCount)
    {
        ArgumentNullException.ThrowIfNull(textChunk);
        ArgumentNullException.ThrowIfNull(sessionId);
        ArgumentNullException.ThrowIfNull(exception);

        try
        {
            await _eventAggregator.PublishAsync(new PipelineDiagnosticEvent
            {
                Stage = "Overlay",
                IsSuccess = false,
                ProcessingTimeMs = processingTimeMs,
                ErrorMessage = exception.Message,
                SessionId = sessionId,
                Severity = DiagnosticSeverity.Error,
                Message = $"オーバーレイ表示失敗: ChunkId={textChunk.ChunkId}, エラー={exception.GetType().Name}: {exception.Message}",
                Metrics = new Dictionary<string, object>
                {
                    { "ChunkId", textChunk.ChunkId },
                    { "ProcessingTimeMs", processingTimeMs },
                    { "ErrorType", exception.GetType().Name },
                    { "CombinedTextLength", textChunk.CombinedText?.Length ?? 0 },
                    { "TranslatedTextLength", textChunk.TranslatedText?.Length ?? 0 },
                    { "IsInitialized", isInitialized },
                    { "IsDisposed", isDisposed },
                    { "ActiveOverlaysCount", activeOverlaysCount }
                }
            }).ConfigureAwait(false);

            _logger.LogError(exception, "診断イベント発行: オーバーレイ表示失敗 - ChunkId: {ChunkId}", textChunk.ChunkId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "診断イベント発行エラー: オーバーレイ表示失敗 - ChunkId: {ChunkId}", textChunk.ChunkId);
            throw;
        }
    }
}
