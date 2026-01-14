using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR; // OcrContext統合
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Events.Handlers;

/// <summary>
/// OCR処理要求イベントハンドラー
/// PP-OCRv5削除後: TimedChunkAggregator統合はITextChunkAggregatorServiceで実施
/// CaptureCompletedEvent→OcrRequestEvent→TranslationOrchestrationService
/// </summary>
public sealed class OcrRequestHandler(
    ITranslationOrchestrationService translationService,
    ILogger<OcrRequestHandler> logger) : IEventProcessor<OcrRequestEvent>
{
    private readonly ITranslationOrchestrationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    private readonly ILogger<OcrRequestHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(OcrRequestEvent eventData, CancellationToken cancellationToken = default)
    {
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger.LogInformation("🚀 [PP-OCRv5削除] OcrRequestHandler: 翻訳処理開始 - Image: {Width}x{Height}",
                eventData.CapturedImage.Width, eventData.CapturedImage.Height);

            Console.WriteLine($"🧩 [PP-OCRv5削除] OcrRequestHandler: TranslationOrchestrationServiceフロー開始");

            // NOTE: [PP-OCRv5削除] EnhancedBatchOcrIntegrationServiceを削除
            // 直接TranslationOrchestrationServiceに委譲
            await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindowHandle).ConfigureAwait(false);

            _logger.LogInformation("✅ [PP-OCRv5削除] OcrRequestHandler: 翻訳処理完了");
            Console.WriteLine("✅ [PP-OCRv5削除] OcrRequestHandler: 翻訳処理完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PP-OCRv5削除] OcrRequestHandler処理エラー: {ErrorType} - {Message}",
                ex.GetType().Name, ex.Message);

            Console.WriteLine($"❌ [PP-OCRv5削除] OcrRequestHandler処理エラー: {ex.GetType().Name} - {ex.Message}");

            // エラーを伝播させず、ログに記録のみ
            // UIの安定性を優先
        }
    }
}
