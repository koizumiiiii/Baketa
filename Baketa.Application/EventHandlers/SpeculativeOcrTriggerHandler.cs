using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.EventHandlers;

/// <summary>
/// 投機的OCR実行トリガーハンドラ
/// </summary>
/// <remarks>
/// Issue #293: 投機的実行とリソース適応
/// - CaptureCompletedEventを購読
/// - Live翻訳が無効な時（Shotモード待機中）にキャプチャ完了で投機的OCRを試行
/// - GPU余裕がある場合のみOCRを先行実行してキャッシュ
/// </remarks>
public sealed class SpeculativeOcrTriggerHandler : IEventProcessor<CaptureCompletedEvent>
{
    private readonly ISpeculativeOcrService? _speculativeOcrService;
    private readonly ITranslationModeService _translationModeService;
    private readonly ILogger<SpeculativeOcrTriggerHandler> _logger;

    public SpeculativeOcrTriggerHandler(
        ISpeculativeOcrService? speculativeOcrService,
        ITranslationModeService translationModeService,
        ILogger<SpeculativeOcrTriggerHandler> logger)
    {
        _speculativeOcrService = speculativeOcrService;
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public int Priority => 100; // 低優先度（通常のハンドラの後に実行）

    /// <inheritdoc />
    public bool SynchronousExecution => false; // 非同期で実行（メインフローをブロックしない）

    /// <inheritdoc />
    public async Task HandleAsync(CaptureCompletedEvent eventData, CancellationToken cancellationToken = default)
    {
        // 投機的OCRサービスが無効な場合は何もしない
        if (_speculativeOcrService?.IsEnabled != true)
        {
            return;
        }

        // Live翻訳中は投機的OCRを実行しない（既にOCRが実行されるため）
        if (_translationModeService.CurrentMode == TranslationMode.Live)
        {
            return;
        }

        // 投機的OCRが実行中の場合はスキップ
        if (_speculativeOcrService.IsExecuting)
        {
            _logger.LogDebug("🔄 [Issue #293] 投機的OCRは既に実行中のためスキップ");
            return;
        }

        // キャプチャ画像がない場合はスキップ
        if (eventData.CapturedImage == null)
        {
            return;
        }

        try
        {
            _logger.LogDebug("🚀 [Issue #293] 投機的OCR実行を試行: ImageSize={Width}x{Height}",
                eventData.CapturedImage.Width, eventData.CapturedImage.Height);

            // 投機的OCRを試行（GPU余裕がない場合は内部でスキップされる）
            var executed = await _speculativeOcrService.TryExecuteSpeculativeOcrAsync(
                eventData.CapturedImage,
                imageHash: null, // ハッシュは内部で計算
                cancellationToken).ConfigureAwait(false);

            if (executed)
            {
                _logger.LogInformation("✅ [Issue #293] 投機的OCR完了 - キャッシュ作成済み");
            }
            else
            {
                _logger.LogDebug("⏸️ [Issue #293] 投機的OCRスキップ（リソース不足または実行中）");
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("🛑 [Issue #293] 投機的OCRがキャンセルされました");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [Issue #293] 投機的OCR実行中にエラーが発生");
        }
    }
}
