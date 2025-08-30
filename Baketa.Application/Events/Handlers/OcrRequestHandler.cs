using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Application.Services.Translation;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Baketa.Application.Events.Handlers;

/// <summary>
/// OCR処理要求イベントハンドラー
/// Phase 2.2: 翻訳処理チェーン連鎖修復
/// CaptureCompletedEvent→OcrRequestEvent→TriggerSingleTranslationAsync
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
    public async Task HandleAsync(OcrRequestEvent eventData)
    {
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger.LogInformation("🚀 [PHASE2_FIX] OcrRequestHandler: OCR処理開始 - Image: {Width}x{Height}", 
                eventData.CapturedImage.Width, eventData.CapturedImage.Height);
            
            Console.WriteLine($"🔥 [PHASE2_FIX] OcrRequestHandler: TriggerSingleTranslationAsync呼び出し開始");

            // ITranslationOrchestrationService経由でOCR→翻訳処理を開始
            await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindowHandle).ConfigureAwait(false);

            _logger.LogInformation("✅ [PHASE2_FIX] OcrRequestHandler: OCR→翻訳処理開始完了");
            Console.WriteLine("✅ [PHASE2_FIX] OcrRequestHandler: TriggerSingleTranslationAsync完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [PHASE2_FIX] OcrRequestHandler処理エラー: {ErrorType} - {Message}", 
                ex.GetType().Name, ex.Message);
            
            Console.WriteLine($"❌ [PHASE2_FIX] OcrRequestHandler処理エラー: {ex.GetType().Name} - {ex.Message}");
            
            // 例外は再スローして上位で処理
            throw;
        }
    }
}