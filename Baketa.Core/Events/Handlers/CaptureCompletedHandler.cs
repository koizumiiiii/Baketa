using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// キャプチャ完了イベントハンドラー
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
public class CaptureCompletedHandler(IEventAggregator eventAggregator) : IEventProcessor<CaptureCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        
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
                // 🔄 Phase 1: 画像変化検知によるOCR処理制御
                if (eventData.ImageChangeSkipped)
                {
                    Console.WriteLine("⚡ [CHANGE_DETECTION] CaptureCompletedHandler: 画像変化なし - OCR処理スキップ");
                    
                    // スキップ通知イベント発行
                    var skipNotification = new NotificationEvent(
                        "画像変化なし - OCR処理をスキップしました",
                        NotificationType.Information,
                        "OCRスキップ",
                        displayTime: 1000);
                        
                    await _eventAggregator.PublishAsync(skipNotification).ConfigureAwait(false);
                    
                    Console.WriteLine("✅ [CHANGE_DETECTION] CaptureCompletedHandler: スキップ通知完了");
                    return; // OCRRequestEventを発行せずに終了
                }
                
                // キャプチャが完了したことを通知するイベントを発行
                var notificationEvent = new NotificationEvent(
                    $"キャプチャが完了しました: {eventData.CapturedImage.Width}x{eventData.CapturedImage.Height}",
                    NotificationType.Success,
                    "キャプチャ完了",
                    displayTime: 3000);
                    
                await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
                
                // ⚡ [PHASE_2_FIX] OCR処理要求イベント発行 - 翻訳パイプライン連鎖修復
                Console.WriteLine($"🔥 [PHASE2_FIX] CaptureCompletedHandler: OCR要求イベント発行 - Image: {eventData.CapturedImage.Width}x{eventData.CapturedImage.Height}");
                
                // OCR処理要求イベントを発行（Applicationレイヤーで処理）
                var ocrRequestEvent = new OcrRequestEvent(
                    eventData.CapturedImage,
                    eventData.CaptureRegion,
                    targetWindowHandle: null
                );
                
                await _eventAggregator.PublishAsync(ocrRequestEvent).ConfigureAwait(false);
                
                Console.WriteLine("✅ [PHASE2_FIX] CaptureCompletedHandler: OcrRequestEvent発行完了");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [PHASE2_FIX] CaptureCompletedHandler処理エラー: {ex.GetType().Name} - {ex.Message}");
                
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
    }
