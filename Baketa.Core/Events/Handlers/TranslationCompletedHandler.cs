using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 翻訳完了イベントハンドラー
/// </summary>
/// <inheritdoc />
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
public class TranslationCompletedHandler(IEventAggregator eventAggregator) : IEventProcessor<TranslationCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(TranslationCompletedEvent eventData)
        {
            // NULLチェック
            ArgumentNullException.ThrowIfNull(eventData);

            // 翻訳完了の通知（実際のアプリケーションでは、これは開発モードでのみ表示するなどの制御が必要）
            var notificationEvent = new NotificationEvent(
                $"翻訳が完了しました ({eventData.SourceLanguage} → {eventData.TargetLanguage})",
                NotificationType.Information,
                "翻訳完了",
                displayTime: 2000);
                
            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
            
            // 実際のオーバーレイ表示処理
            // ここでは仮のオーバーレイ領域を設定。実際のアプリケーションでは、
            // OCR結果のバウンディングボックスなど、適切な位置情報が必要
            var overlayEvent = new OverlayUpdateEvent(
                text: eventData.TranslatedText,
                displayArea: new Rectangle(0, 0, 400, 100), // 仮の位置と大きさ
                originalText: eventData.SourceText,
                sourceLanguage: eventData.SourceLanguage,
                targetLanguage: eventData.TargetLanguage,
                isTranslationResult: true);
                
            await _eventAggregator.PublishAsync(overlayEvent).ConfigureAwait(false);
        }
    }
