using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 座標情報付き翻訳完了イベントハンドラー
/// 翻訳完了後にオーバーレイ表示を行う
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
/// <param name="logger">ロガー</param>
public class TranslationWithBoundsCompletedHandler(
    IEventAggregator eventAggregator,
    ILogger<TranslationWithBoundsCompletedHandler> logger) : IEventProcessor<TranslationWithBoundsCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<TranslationWithBoundsCompletedHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
    /// <inheritdoc />
    public int Priority => 200;
        
    /// <inheritdoc />
    public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(TranslationWithBoundsCompletedEvent eventData)
    {
        // デバッグログ: ハンドラー呼び出し確認
        _logger.LogInformation("🎯 TranslationWithBoundsCompletedHandler.HandleAsync 呼び出し開始 - ID: {EventId}", eventData?.Id);
        Console.WriteLine($"🎯 [DEBUG] TranslationWithBoundsCompletedHandler.HandleAsync 呼び出し開始 - ID: {eventData?.Id}");
        Console.WriteLine($"🎯 [DEBUG] SourceText: '{eventData?.SourceText}'");
        Console.WriteLine($"🎯 [DEBUG] TranslatedText: '{eventData?.TranslatedText}'");
        Console.WriteLine($"🎯 [DEBUG] Bounds: {eventData?.Bounds}");
        
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger.LogInformation("座標情報付き翻訳完了: '{Original}' → '{Translated}' (Bounds: {Bounds})", 
                eventData.SourceText, eventData.TranslatedText, eventData.Bounds);

            // 🔍 [DEBUG] 重複表示デバッグ：オーバーレイソース特定
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] TranslationWithBoundsCompletedHandler → OverlayUpdateEvent発行");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] EventId: {eventData.Id}");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] SourceText: '{eventData.SourceText}'");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] TranslatedText: '{eventData.TranslatedText}'");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] SourceLanguage: {eventData.SourceLanguage}");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] TargetLanguage: {eventData.TargetLanguage}");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] EngineName: {eventData.EngineName}");
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] Bounds: {eventData.Bounds}");

            // オーバーレイ更新イベントを発行
            var overlayEvent = new OverlayUpdateEvent(
                text: eventData.TranslatedText,
                displayArea: eventData.Bounds,
                originalText: eventData.SourceText,
                sourceLanguage: eventData.SourceLanguage,
                targetLanguage: eventData.TargetLanguage,
                isTranslationResult: true);
                
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] OverlayUpdateEvent発行中 - ID: {overlayEvent.Id}");
            await _eventAggregator.PublishAsync(overlayEvent).ConfigureAwait(false);
            Console.WriteLine($"🎯 [OVERLAY_SOURCE] OverlayUpdateEvent発行完了 - ID: {overlayEvent.Id}");

            // 翻訳成功通知
            var notificationEvent = new NotificationEvent(
                $"翻訳完了: {eventData.EngineName}",
                NotificationType.Success,
                "翻訳",
                displayTime: 2000);
                
            await _eventAggregator.PublishAsync(notificationEvent).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "座標情報付き翻訳完了イベント処理中にエラーが発生: '{Text}'", eventData.SourceText);
        }
    }
}