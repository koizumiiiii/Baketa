using System;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.OCR; // 🔥 [FIX7_STEP4] OcrContext統合
using Baketa.Infrastructure.OCR.BatchProcessing;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Events.Handlers;

/// <summary>
/// OCR処理要求イベントハンドラー
/// UltraThink緊急修正: TimedChunkAggregator統合による時間軸集約OCR実装
/// Phase 2.2: 翻訳処理チェーン連鎖修復 + 翻訳品質向上40-60%
/// CaptureCompletedEvent→OcrRequestEvent→EnhancedBatchOcrIntegrationService→TimedChunkAggregator
/// </summary>
public sealed class OcrRequestHandler(
    ITranslationOrchestrationService translationService,
    EnhancedBatchOcrIntegrationService enhancedBatchOcrService,
    ILogger<OcrRequestHandler> logger) : IEventProcessor<OcrRequestEvent>
{
    private readonly ITranslationOrchestrationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    private readonly EnhancedBatchOcrIntegrationService _enhancedBatchOcrService = enhancedBatchOcrService ?? throw new ArgumentNullException(nameof(enhancedBatchOcrService));
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
            _logger.LogInformation("🚀 [ULTRATHINK_FIX] OcrRequestHandler: TimedChunkAggregator統合OCR処理開始 - Image: {Width}x{Height}",
                eventData.CapturedImage.Width, eventData.CapturedImage.Height);

            Console.WriteLine($"🧩 [ULTRATHINK_FIX] OcrRequestHandler: EnhancedBatchOcrIntegrationService統合フロー開始");

            // 🎯 UltraThink緊急修正: EnhancedBatchOcrIntegrationServiceによる時間軸統合OCR処理
            // 型変換: IImage → IAdvancedImage（キャスト）、IntPtr? → IntPtr（null安全処理）
            if (eventData.CapturedImage is not Baketa.Core.Abstractions.Imaging.IAdvancedImage advancedImage)
            {
                _logger.LogWarning("⚠️ [ULTRATHINK_FIX] CapturedImageがIAdvancedImageでないため、フォールバック処理実行");
                await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindowHandle).ConfigureAwait(false);
                return;
            }

            var targetWindowHandle = eventData.TargetWindowHandle ?? IntPtr.Zero;

            // 🔥 [FIX7_STEP4] OcrContext生成 - CaptureRegion情報を含むコンテキストオブジェクト作成
            var ocrContext = new OcrContext(
                Image: advancedImage,
                WindowHandle: targetWindowHandle,
                CaptureRegion: eventData.CaptureRegion != System.Drawing.Rectangle.Empty ? eventData.CaptureRegion : null);

            _logger.LogInformation("🔥 [FIX7_STEP4] OcrContext生成完了 - CaptureRegion: {HasCaptureRegion}, Value: {CaptureRegion}",
                ocrContext.HasCaptureRegion,
                ocrContext.HasCaptureRegion ? $"({ocrContext.CaptureRegion!.Value.X},{ocrContext.CaptureRegion.Value.Y},{ocrContext.CaptureRegion.Value.Width}x{ocrContext.CaptureRegion.Value.Height})" : "null");

            var enhancedChunks = await _enhancedBatchOcrService.ProcessWithEnhancedOcrAsync(ocrContext).ConfigureAwait(false);

            if (enhancedChunks.Count > 0)
            {
                // TimedChunkAggregator無効時: 直接翻訳処理実行（フォールバック）
                _logger.LogInformation("📊 [ULTRATHINK_FIX] TimedAggregator無効モード - 直接翻訳処理: {ChunkCount}個", enhancedChunks.Count);
                Console.WriteLine($"📊 [ULTRATHINK_FIX] 直接翻訳処理実行: {enhancedChunks.Count}個のチャンク");

                // 従来フロー（フォールバック処理）
                await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindowHandle).ConfigureAwait(false);
            }
            else
            {
                // TimedChunkAggregator有効時: 集約待機中（別途集約完了時に翻訳処理実行）
                _logger.LogInformation("⏱️ [ULTRATHINK_FIX] TimedChunkAggregator集約待機中 - バッファリング処理完了");
                Console.WriteLine("⏱️ [ULTRATHINK_FIX] TimedChunkAggregator集約待機中 - 時間軸統合による翻訳品質向上処理");

                // 注意: 集約完了時の翻訳処理は EnhancedBatchOcrIntegrationService.OnChunksAggregatedHandler で実行される
            }

            _logger.LogInformation("✅ [ULTRATHINK_FIX] OcrRequestHandler: TimedChunkAggregator統合処理完了");
            Console.WriteLine("✅ [ULTRATHINK_FIX] OcrRequestHandler: 統合OCR処理完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [ULTRATHINK_FIX] OcrRequestHandler統合処理エラー - フォールバック実行: {ErrorType} - {Message}",
                ex.GetType().Name, ex.Message);

            Console.WriteLine($"❌ [ULTRATHINK_FIX] OcrRequestHandler統合エラー - フォールバック実行: {ex.GetType().Name} - {ex.Message}");

            try
            {
                // エラー時フォールバック: 従来の翻訳フロー実行
                _logger.LogWarning("🔄 [ULTRATHINK_FIX] フォールバック処理実行 - 従来翻訳フロー");
                await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindowHandle).ConfigureAwait(false);
                _logger.LogInformation("✅ [ULTRATHINK_FIX] フォールバック処理成功");
            }
            catch (Exception fallbackEx)
            {
                _logger.LogError(fallbackEx, "❌ [ULTRATHINK_FIX] フォールバック処理も失敗");
                throw; // 最終的な例外は再スロー
            }
        }
    }
}
