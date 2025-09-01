using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// キャプチャ完了イベントハンドラー
/// P1: 段階的フィルタリングシステム統合済み
/// </summary>
public class CaptureCompletedHandler : IEventProcessor<CaptureCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly ISmartProcessingPipelineService? _smartPipeline;
        private readonly ILogger<CaptureCompletedHandler>? _logger;
        private readonly IOptionsMonitor<ProcessingPipelineSettings>? _settings;

        public CaptureCompletedHandler(
            IEventAggregator eventAggregator,
            ISmartProcessingPipelineService? smartPipeline = null,
            ILogger<CaptureCompletedHandler>? logger = null,
            IOptionsMonitor<ProcessingPipelineSettings>? settings = null)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _smartPipeline = smartPipeline;
            _logger = logger;
            _settings = settings;
        }
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(CaptureCompletedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            try
            {
                _logger?.LogDebug("キャプチャ完了イベント処理開始 - Image: {Width}x{Height}", 
                    eventData.CapturedImage.Width, eventData.CapturedImage.Height);

                // 🔄 P1: 段階的フィルタリングシステム使用判定
                if (_smartPipeline != null)
                {
                    _logger?.LogDebug("段階的フィルタリングシステム使用開始");
                    await HandleWithStagedFilteringAsync(eventData).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogDebug("従来処理モード使用（段階的フィルタリング無効）");
                    await HandleLegacyModeAsync(eventData).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "CaptureCompletedHandler処理エラー: {ErrorType} - {ErrorMessage}", ex.GetType().Name, ex.Message);
                
                // エラー通知イベントを発行
                var errorNotificationEvent = new NotificationEvent(
                    $"キャプチャ後の処理でエラーが発生しました: {ex.Message}",
                    NotificationType.Error,
                    "処理エラー",
                    displayTime: 5000);
                    
                try
                {
                    await _eventAggregator.PublishAsync(errorNotificationEvent).ConfigureAwait(false);
                }
                catch
                {
                    // 通知イベント発行失敗は無視（ログ出力済み）
                }
                
                // 例外は再スローして上位で処理
                throw;
            }
        }

    /// <summary>
    /// P1: 段階的フィルタリングシステムを使用した処理
    /// </summary>
    private async Task HandleWithStagedFilteringAsync(CaptureCompletedEvent eventData)
    {
        try
        {
            // 段階的処理パイプラインの入力を作成
            var input = new ProcessingPipelineInput
            {
                CapturedImage = eventData.CapturedImage,
                CaptureRegion = eventData.CaptureRegion,
                SourceWindowHandle = IntPtr.Zero, // TODO: eventDataから取得
                CaptureTimestamp = DateTime.UtcNow,
                // TODO: 前回のハッシュやテキストを設定（キャッシュ機構が必要）
                Options = new ProcessingPipelineOptions
                {
                    // Geminiフィードバック反映: 設定から取得（ハードコーディング回避）
                    EnableStaging = _settings?.CurrentValue?.EnableStaging ?? true,
                    EnablePerformanceMetrics = _settings?.CurrentValue?.EnablePerformanceMetrics ?? true,
                    EnableEarlyTermination = _settings?.CurrentValue?.EnableEarlyTermination ?? true
                }
            };

            // 段階的処理パイプライン実行
            var pipelineResult = await _smartPipeline!.ExecuteAsync(input).ConfigureAwait(false);
            
            _logger?.LogDebug("段階的処理完了 - 最終段階: {LastStage}, 総処理時間: {TotalTime}ms, 早期終了: {EarlyTerminated}",
                pipelineResult.LastCompletedStage, pipelineResult.TotalElapsedTime.TotalMilliseconds, pipelineResult.Metrics.EarlyTerminated);

            // 段階別結果に応じたイベント発行
            await PublishStageSpecificEventsAsync(pipelineResult).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "段階的フィルタリングシステム処理エラー");
            
            // フォールバック: 従来処理モード
            _logger?.LogWarning("段階的処理失敗 - 従来処理モードにフォールバック");
            await HandleLegacyModeAsync(eventData).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 段階別結果に応じたイベント発行
    /// </summary>
    private async Task PublishStageSpecificEventsAsync(ProcessingPipelineResult result)
    {
        try
        {
            // キャプチャ完了通知
            var captureNotification = new NotificationEvent(
                $"キャプチャ完了 - 処理時間: {result.TotalElapsedTime.TotalMilliseconds:F1}ms",
                NotificationType.Success,
                "段階的処理",
                displayTime: 2000);
            await _eventAggregator.PublishAsync(captureNotification).ConfigureAwait(false);

            // OCR完了時イベント
            if (result.LastCompletedStage >= ProcessingStageType.OcrExecution && result.OcrResult?.Success == true)
            {
                // モックOcrResultを作成（実装時に実際のデータに置き換え）
                var mockOcrResults = new List<object>(); // TODO: 実際のOcrResultリストを作成
                
                // 一時的にスキップ（OcrResult型が見つからない場合）
                // TODO: 実際のOcrCompletedEventとOcrResultを使用
                _logger?.LogDebug("OCRCompletedEvent発行スキップ - OcrResult型解決が必要");
                _logger?.LogDebug("OCR結果検出 - テキスト長: {TextLength}", result.OcrResult.DetectedText.Length);
            }

            // 翻訳完了時イベント
            if (result.LastCompletedStage == ProcessingStageType.TranslationExecution && result.TranslationResult?.Success == true)
            {
                var translationEvent = new TranslationCompletedEvent(
                    sourceText: result.OcrResult?.DetectedText ?? "",
                    translatedText: result.TranslationResult.TranslatedText,
                    sourceLanguage: "auto", // TODO: 実際のソース言語を設定
                    targetLanguage: "ja",   // TODO: 実際のターゲット言語を設定
                    processingTime: result.TranslationResult.ProcessingTime,
                    engineName: result.TranslationResult.EngineUsed);
                await _eventAggregator.PublishAsync(translationEvent).ConfigureAwait(false);
                
                _logger?.LogDebug("TranslationCompletedEvent発行 - 翻訳テキスト長: {TextLength}", result.TranslationResult.TranslatedText.Length);
            }

            // パフォーマンスメトリクス通知（デバッグ情報）
            if (result.Metrics.EarlyTerminated)
            {
                var performanceNotification = new NotificationEvent(
                    $"性能最適化: {result.Metrics.SkippedStages}段階スキップ、CPU削減: {result.Metrics.EstimatedCpuReduction:P0}",
                    NotificationType.Information,
                    "パフォーマンス",
                    displayTime: 3000);
                await _eventAggregator.PublishAsync(performanceNotification).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "段階別イベント発行エラー");
        }
    }

    /// <summary>
    /// 従来処理モード（段階的フィルタリング無効時）
    /// </summary>
    private async Task HandleLegacyModeAsync(CaptureCompletedEvent eventData)
    {
        // Phase 1: 画像変化検知によるOCR処理制御
        if (eventData.ImageChangeSkipped)
        {
            _logger?.LogDebug("画像変化なし - OCR処理スキップ");
            
            var skipNotification = new NotificationEvent(
                "画像変化なし - OCR処理をスキップしました",
                NotificationType.Information,
                "OCRスキップ",
                displayTime: 1000);
                
            await _eventAggregator.PublishAsync(skipNotification).ConfigureAwait(false);
            return; // OCRRequestEventを発行せずに終了
        }
        
        // キャプチャ完了通知
        var notificationEvent = new NotificationEvent(
            $"キャプチャが完了しました: {eventData.CapturedImage.Width}x{eventData.CapturedImage.Height}",
            NotificationType.Success,
            "キャプチャ完了",
            displayTime: 3000);
            
        await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
        
        // OCR処理要求イベント発行（従来方式）
        _logger?.LogDebug("OCR要求イベント発行 - Image: {Width}x{Height}", 
            eventData.CapturedImage.Width, eventData.CapturedImage.Height);
        
        var ocrRequestEvent = new OcrRequestEvent(
            eventData.CapturedImage,
            eventData.CaptureRegion,
            targetWindowHandle: null
        );
        
        await _eventAggregator.PublishAsync(ocrRequestEvent).ConfigureAwait(false);
        
        _logger?.LogDebug("OcrRequestEvent発行完了");
    }
}
