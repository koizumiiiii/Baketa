using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳フロー統合のイベントプロセッサー
/// UI層とApplication層の翻訳フローを統合
/// </summary>
public class TranslationFlowEventProcessor(
    ILogger<TranslationFlowEventProcessor> logger,
    IEventAggregator eventAggregator,
    TranslationResultOverlayManager overlayManager,
    ICaptureService captureService,
    TranslationOrchestrationService translationService) : 
    IEventProcessor<StartTranslationRequestEvent>,
    IEventProcessor<StopTranslationRequestEvent>,
    IEventProcessor<ToggleTranslationDisplayRequestEvent>,
    IEventProcessor<SettingsChangedEvent>
{
    private readonly ILogger<TranslationFlowEventProcessor> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly TranslationResultOverlayManager _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
    private readonly ICaptureService _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
    private readonly TranslationOrchestrationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));

    public int Priority => 100;
    public bool SynchronousExecution => false;

    /// <summary>
    /// 翻訳開始要求イベントの処理
    /// </summary>
    public async Task HandleAsync(StartTranslationRequestEvent eventData)
    {
        try
        {
            _logger.LogInformation("🚀 翻訳開始要求を処理中: ウィンドウ={WindowTitle} (Handle={Handle})", 
                eventData.TargetWindow.Title, eventData.TargetWindow.Handle);

            // 1. 翻訳状態を「キャプチャ中」に変更
            _logger.LogDebug("📊 翻訳状態をキャプチャ中に変更");
            var statusEvent = new TranslationStatusChangedEvent(TranslationStatus.Capturing);
            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            // 2. オーバーレイマネージャーを初期化
            _logger.LogDebug("🖼️ オーバーレイマネージャーを初期化");
            await _overlayManager.InitializeAsync().ConfigureAwait(false);

            // 3. 実際の翻訳処理を開始
            _logger.LogDebug("⚙️ 翻訳処理開始");
            await ProcessTranslationAsync(eventData.TargetWindow).ConfigureAwait(false);

            _logger.LogInformation("✅ 翻訳開始処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 翻訳開始処理中にエラーが発生しました: {ErrorMessage}", ex.Message);
            
            // エラー状態に変更
            var errorEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
            await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 翻訳停止要求イベントの処理
    /// </summary>
    public async Task HandleAsync(StopTranslationRequestEvent eventData)
    {
        try
        {
            _logger.LogInformation("翻訳停止要求を処理中");

            // 1. 翻訳状態を「待機中」に変更
            var statusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            // 2. オーバーレイを非表示
            await _overlayManager.HideAsync().ConfigureAwait(false);

            // 3. 実際の翻訳停止処理
            await _translationService.StopAutomaticTranslationAsync().ConfigureAwait(false);

            _logger.LogInformation("翻訳停止処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳停止処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 翻訳表示切り替え要求イベントの処理
    /// </summary>
    public async Task HandleAsync(ToggleTranslationDisplayRequestEvent eventData)
    {
        try
        {
            _logger.LogDebug("翻訳表示切り替え要求を処理中: IsVisible={IsVisible}", eventData.IsVisible);

            // オーバーレイの表示/非表示を切り替え
            if (eventData.IsVisible)
            {
                await _overlayManager.ShowAsync().ConfigureAwait(false);
            }
            else
            {
                await _overlayManager.HideAsync().ConfigureAwait(false);
            }

            // 表示状態変更イベントを発行
            var visibilityEvent = new TranslationDisplayVisibilityChangedEvent(eventData.IsVisible);
            await _eventAggregator.PublishAsync(visibilityEvent).ConfigureAwait(false);

            _logger.LogDebug("翻訳表示切り替えが完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳表示切り替え処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 設定変更イベントの処理
    /// </summary>
    public async Task HandleAsync(SettingsChangedEvent eventData)
    {
        try
        {
            _logger.LogInformation("設定変更を適用中");

            // オーバーレイ設定を更新
            _overlayManager.SetOpacity(eventData.OverlayOpacity);
            
            // フォントサイズに基づいて最大幅を調整
            var maxWidth = eventData.FontSize * 25; // フォントサイズの25倍を最大幅とする
            _overlayManager.SetMaxWidth(maxWidth);

            // TODO: Application層の設定サービスと統合
            // var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            // await settingsService.UpdateSettingsAsync(eventData);

            _logger.LogInformation("設定変更が適用されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定変更処理中にエラーが発生しました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 実際の翻訳処理を実行
    /// </summary>
    private async Task ProcessTranslationAsync(WindowInfo targetWindow)
    {
        try
        {
            _logger.LogInformation("🔄 翻訳処理を開始: ウィンドウ={WindowTitle} (Handle={Handle})", 
                targetWindow.Title, targetWindow.Handle);

            // 1. 翻訳中状態に変更
            _logger.LogDebug("📊 翻訳状態を翻訳中に変更");
            var translatingEvent = new TranslationStatusChangedEvent(TranslationStatus.Translating);
            await _eventAggregator.PublishAsync(translatingEvent).ConfigureAwait(false);

            // 2. ウィンドウキャプチャ
            _logger.LogDebug("📸 ウィンドウキャプチャを開始: Handle={Handle}", targetWindow.Handle);
            var captureResult = await _captureService.CaptureWindowAsync(targetWindow.Handle).ConfigureAwait(false);
            
            if (captureResult == null)
            {
                _logger.LogWarning("⚠️ ウィンドウキャプチャに失敗しました");
                await DisplayFallbackTranslationAsync().ConfigureAwait(false);
                return;
            }
            
            _logger.LogDebug("✅ ウィンドウキャプチャ成功");

            // 3. 単発翻訳実行
            _logger.LogDebug("🌐 翻訳サービスによる翻訳処理を開始");
            
            // TranslationResultsのObservableを購読して結果を取得
            TranslationResult? translationResult = null;
            var resultReceived = false;
            
            using var subscription = _translationService.TranslationResults
                .Subscribe(result => 
                {
                    _logger.LogDebug("📥 翻訳結果を受信: {Original} -> {Translated}", 
                        result.OriginalText, result.TranslatedText);
                    translationResult = result;
                    resultReceived = true;
                });
            
            _logger.LogDebug("🎯 単発翻訳をトリガー");
            await _translationService.TriggerSingleTranslationAsync().ConfigureAwait(false);
            
            // 翻訳結果の完了まで待機（最大5秒）
            var maxWaitTime = 5000;
            var waited = 0;
            while (!resultReceived && waited < maxWaitTime)
            {
                await Task.Delay(100).ConfigureAwait(false);
                waited += 100;
            }
            
            _logger.LogDebug("⏱️ 翻訳処理待機時間: {WaitTime}ms", waited);

            // 4. 翻訳結果を表示
            if (translationResult != null)
            {
                _logger.LogInformation("📄 翻訳結果表示: '{Original}' -> '{Translated}' (信頼度: {Confidence})", 
                    translationResult.OriginalText, translationResult.TranslatedText, translationResult.Confidence);
                    
                var displayEvent = new TranslationResultDisplayEvent
                {
                    OriginalText = translationResult.OriginalText,
                    TranslatedText = translationResult.TranslatedText,
                    DetectedPosition = new System.Drawing.Point(100, 200) // 固定位置
                };

                await _eventAggregator.PublishAsync(displayEvent).ConfigureAwait(false);
                _logger.LogDebug("✅ 翻訳結果表示イベントを発行");
            }
            else
            {
                _logger.LogInformation("❓ 翻訳対象のテキストが検出されませんでした（待機時間: {WaitTime}ms）", waited);
                await DisplayNoTextFoundMessageAsync().ConfigureAwait(false);
            }

            // 5. 翻訳完了状態に変更
            _logger.LogDebug("📊 翻訳状態を完了に変更");
            var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
            await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);

            _logger.LogInformation("🏁 翻訳処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "💥 翻訳処理中にエラーが発生しました: {ErrorMessage}", ex.Message);
            await DisplayErrorMessageAsync(ex).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// フォールバック翻訳結果を表示（キャプチャ失敗時）
    /// </summary>
    private async Task DisplayFallbackTranslationAsync()
    {
        var fallbackEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(キャプチャ失敗)",
            TranslatedText = "ウィンドウキャプチャに失敗しました",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(fallbackEvent).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// テキスト未検出メッセージを表示
    /// </summary>
    private async Task DisplayNoTextFoundMessageAsync()
    {
        var noTextEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(テキスト未検出)",
            TranslatedText = "翻訳対象のテキストが見つかりませんでした",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(noTextEvent).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    private async Task DisplayErrorMessageAsync(Exception exception)
    {
        var errorEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(エラー)",
            TranslatedText = $"翻訳処理中にエラーが発生しました: {exception.Message}",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

        var errorStatusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
        await _eventAggregator.PublishAsync(errorStatusEvent).ConfigureAwait(false);
    }
}
