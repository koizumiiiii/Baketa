using System;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// シングルショット翻訳実行イベントプロセッサー
/// ボタン押下時に1回だけキャプチャ→OCR→翻訳→オーバーレイ表示を実行
/// TranslationOrchestrationServiceに処理を委譲
/// </summary>
public class SingleshotEventProcessor : IEventProcessor<ExecuteSingleshotRequestEvent>
{
    private readonly ILogger<SingleshotEventProcessor> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly ITranslationOrchestrationService _translationService;
    private readonly ITranslationModeService _translationModeService;

    public SingleshotEventProcessor(
        ILogger<SingleshotEventProcessor> logger,
        IEventAggregator eventAggregator,
        ITranslationOrchestrationService translationService,
        ITranslationModeService translationModeService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));

        _logger.LogDebug("SingleshotEventProcessor instance created: Hash={Hash}", GetHashCode());
    }

    public int Priority => 100;
    public bool SynchronousExecution => false;

    /// <summary>
    /// シングルショット翻訳実行要求イベントの処理
    /// TranslationOrchestrationService.TriggerSingleTranslationAsyncに委譲
    /// </summary>
    public async Task HandleAsync(ExecuteSingleshotRequestEvent eventData)
    {
        var processorId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("📸 [PROCESSOR] シングルショット翻訳イベント受信: EventId={EventId}, ProcessorId={ProcessorId}",
            eventData.Id, processorId);
        _logger.LogInformation("🎯 ターゲットウィンドウ: {WindowTitle} (Handle={Handle})",
            eventData.TargetWindow?.Title ?? "null", eventData.TargetWindow?.Handle ?? IntPtr.Zero);

        // イベントデータの妥当性チェック
        if (eventData.TargetWindow == null)
        {
            var errorMessage = "ターゲットウィンドウがnullです";
            _logger.LogError("{ErrorMessage}", errorMessage);
            return;
        }

        try
        {
            // 🔧 [SINGLESHOT_FIX] Singleshotモードに切り替え - CaptureCompletedHandlerで早期終了を無効化
            _logger.LogInformation("🔄 [PROCESSOR:{ProcessorId}] Singleshotモードに切り替え開始", processorId);
            await _translationModeService.SwitchToSingleshotModeAsync().ConfigureAwait(false);
            _logger.LogInformation("✅ [PROCESSOR:{ProcessorId}] Singleshotモード切り替え完了", processorId);

            _logger.LogInformation("[PROCESSOR:{ProcessorId}] TriggerSingleTranslationAsync呼び出し開始: {WindowTitle}",
                processorId, eventData.TargetWindow.Title);

            // TranslationOrchestrationServiceの単発翻訳メソッドを呼び出し
            await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindow.Handle).ConfigureAwait(false);

            _logger.LogInformation("✅ [PROCESSOR:{ProcessorId}] シングルショット翻訳実行完了", processorId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "シングルショット翻訳処理中にエラーが発生しました: {ErrorMessage}", ex.Message);

            // 🔥 [GEMINI_REVIEW] エラー状態を明示的に通知（TranslationStatus.Error使用）
            try
            {
                var errorEvent = new TranslationStatusChangedEvent(TranslationStatus.Error);
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
                _logger.LogDebug("TranslationStatus.Errorイベント発行完了");
            }
            catch (Exception eventEx)
            {
                _logger.LogError(eventEx, "エラー状態イベント発行失敗");
            }
        }
        finally
        {
            // 🔧 [SINGLESHOT_FIX] 翻訳完了後、モードをリセット
            try
            {
                _logger.LogInformation("🔄 [PROCESSOR:{ProcessorId}] 翻訳モードリセット開始", processorId);
                await _translationModeService.ResetModeAsync().ConfigureAwait(false);
                _logger.LogInformation("✅ [PROCESSOR:{ProcessorId}] 翻訳モードリセット完了", processorId);
            }
            catch (Exception resetEx)
            {
                _logger.LogError(resetEx, "[PROCESSOR:{ProcessorId}] 翻訳モードリセット失敗: {ErrorMessage}",
                    processorId, resetEx.Message);
            }
        }
    }
}
