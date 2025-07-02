using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

    /// <summary>
    /// OCR失敗イベントハンドラー
    /// </summary>
    public class OcrFailedHandler(IEventAggregator eventAggregator) : IEventProcessor<OcrFailedEvent>
    {
        private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;
        
        /// <inheritdoc />
        public async Task HandleAsync(OcrFailedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            // エラー発生を通知
            var notificationEvent = new NotificationEvent(
                message: $"OCR処理中にエラーが発生しました: {eventData.ErrorMessage}",
                type: NotificationType.Error,
                title: "OCRエラー",
                displayTime: 5000,
                relatedError: eventData.Exception);
                
            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
            
            // ロギングやエラーリカバリー処理などをここに実装
            // 例: OCRエンジンの再起動、別のOCRエンジンでの再試行など
            
            // 開発モードの場合は詳細なエラー情報をデバッグ出力など
            // これは実際のアプリケーションコードで実装
        }
    }
