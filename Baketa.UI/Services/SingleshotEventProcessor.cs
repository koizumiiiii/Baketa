using System;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
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

    public SingleshotEventProcessor(
        ILogger<SingleshotEventProcessor> logger,
        IEventAggregator eventAggregator,
        ITranslationOrchestrationService translationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));

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
        _logger.LogInformation("📸 シングルショット翻訳実行開始: {EventId}", eventData.Id);
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
            _logger.LogInformation("Processing singleshot translation request for window: {WindowTitle} (Handle={Handle})",
                eventData.TargetWindow.Title, eventData.TargetWindow.Handle);

            // TranslationOrchestrationServiceの単発翻訳メソッドを呼び出し
            await _translationService.TriggerSingleTranslationAsync(eventData.TargetWindow.Handle).ConfigureAwait(false);

            _logger.LogInformation("✅ シングルショット翻訳実行完了");
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
    }
}
