using Baketa.Core.Events.EventTypes;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

    /// <summary>
    /// OCR完了イベントハンドラー
    /// </summary>
    public class OcrCompletedHandler : IEventProcessor<OcrCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="eventAggregator">イベント集約インスタンス</param>
        public OcrCompletedHandler(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }
        
        /// <inheritdoc />
        public async Task HandleAsync(OcrCompletedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            // OCR結果が存在しない場合
            if (eventData.Results == null || !eventData.Results.Any())
            {
                var notificationEvent = new NotificationEvent(
                    "OCR処理は完了しましたが、テキストは検出されませんでした。",
                    NotificationType.Information,
                    "OCR完了");
                    
                await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
                return;
            }
            
            // OCR結果を通知
            var successNotificationEvent = new NotificationEvent(
            $"OCR処理が完了しました: {eventData.Results.Count}個のテキスト領域を検出",
            NotificationType.Success,
            "OCR完了",
            displayTime: 3000);
            
            await _eventAggregator.PublishAsync(successNotificationEvent).ConfigureAwait(false);
            
            // 各テキスト領域に対して翻訳処理を開始
            // 実際の実装では、ここで翻訳サービスを呼び出すか、翻訳要求イベントを発行
            foreach (var result in eventData.Results)
            {
                // 実際のオーバーレイ表示処理
                var overlayEvent = new OverlayUpdateEvent(
                    text: result.Text,
                    displayArea: result.Bounds,
                    originalText: result.Text,
                    sourceLanguage: "auto");
                    
                await _eventAggregator.PublishAsync(overlayEvent).ConfigureAwait(false);
                
                // 翻訳処理のためのフローを別途実装
            }
        }
    }
