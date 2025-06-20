using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

    /// <summary>
    /// キャプチャ完了イベントハンドラー
    /// </summary>
    public class CaptureCompletedHandler : IEventProcessor<CaptureCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator;
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;
        
        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="eventAggregator">イベント集約インスタンス</param>
        public CaptureCompletedHandler(IEventAggregator eventAggregator)
        {
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        }
        
        /// <inheritdoc />
        public async Task HandleAsync(CaptureCompletedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            // キャプチャが完了したことを通知するイベントを発行
            var notificationEvent = new NotificationEvent(
                $"キャプチャが完了しました: {eventData.CapturedImage.Width}x{eventData.CapturedImage.Height}",
                NotificationType.Success,
                "キャプチャ完了",
                displayTime: 3000);
                
            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
            
            // ここで次のステップ（OCR処理など）を開始するコードを追加することも可能
        }
    }
