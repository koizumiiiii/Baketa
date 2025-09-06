using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.UI.Overlay;
using Baketa.Core.Events.EventTypes;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Baketa.Application.EventHandlers;

/// <summary>
/// 座標情報付き翻訳完了イベントハンドラー
/// 翻訳完了後にオーバーレイ表示を行う
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
/// <param name="overlayOrchestrator">新しいオーバーレイオーケストレーター（Phase 15対応）</param>
/// <param name="logger">ロガー</param>
public class TranslationWithBoundsCompletedHandler(
    IEventAggregator eventAggregator,
    IOverlayOrchestrator? overlayOrchestrator,
    ILogger<TranslationWithBoundsCompletedHandler> logger) : IEventProcessor<TranslationWithBoundsCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly IOverlayOrchestrator? _overlayOrchestrator = overlayOrchestrator;
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

            // 🔍 翻訳成功判定：空文字や空白文字の場合は翻訳失敗とみなす
            var isTranslationSuccessful = !string.IsNullOrWhiteSpace(eventData.TranslatedText);

            // 🏗️ Phase 15: 新しいオーバーレイオーケストレーターを使用（利用可能な場合）
            if (_overlayOrchestrator != null && isTranslationSuccessful)
            {
                _logger.LogDebug("🚀 [PHASE15_HANDLER] 新しいOverlayOrchestrator使用開始 - ID: {Id}", eventData.Id);
                Console.WriteLine($"🚀 [PHASE15_HANDLER] 新しいOverlayOrchestrator使用 - EventId: {eventData.Id}");
                
                try
                {
                    // TranslationResultに変換
                    var translationResult = new TranslationResult
                    {
                        Id = eventData.Id.ToString(),
                        TranslatedText = eventData.TranslatedText,
                        OriginalText = eventData.SourceText,
                        DisplayArea = eventData.Bounds,
                        SourceLanguage = eventData.SourceLanguage,
                        TargetLanguage = eventData.TargetLanguage,
                        EngineName = eventData.EngineName,
                        Timestamp = DateTimeOffset.UtcNow
                    };

                    // 新しいオーケストレーターで処理
                    var displayResult = await _overlayOrchestrator.HandleTranslationResultAsync(translationResult).ConfigureAwait(false);
                    
                    if (displayResult)
                    {
                        _logger.LogInformation("✅ [PHASE15_HANDLER] 新システムでオーバーレイ表示成功 - ID: {Id}, Text: '{Text}'", 
                            eventData.Id, eventData.TranslatedText.Substring(0, Math.Min(30, eventData.TranslatedText.Length)));
                        Console.WriteLine($"✅ [PHASE15_HANDLER] 新システム表示成功 - ID: {eventData.Id}");
                    }
                    else
                    {
                        _logger.LogDebug("🚫 [PHASE15_HANDLER] 新システムで表示スキップ（重複検出） - ID: {Id}", eventData.Id);
                        Console.WriteLine($"🚫 [PHASE15_HANDLER] 新システム表示スキップ - ID: {eventData.Id}");
                    }
                }
                catch (Exception orchestratorEx)
                {
                    _logger.LogError(orchestratorEx, "❌ [PHASE15_HANDLER] 新しいオーケストレーター処理中にエラー発生 - ID: {Id}", eventData.Id);
                    Console.WriteLine($"❌ [PHASE15_HANDLER] 新システムエラー - ID: {eventData.Id}");
                    
                    // 新システムでエラーが発生した場合は既存システムにフォールバック
                    _logger.LogWarning("⚠️ [PHASE15_HANDLER] 既存システムにフォールバック実行");
                    await PublishLegacyOverlayEvent();
                }
            }
            else
            {
                // 既存システムを使用（新システム無効 or 翻訳失敗）
                _logger.LogDebug("🔄 [LEGACY_HANDLER] 既存システム使用 - NewOrchestrator: {HasOrchestrator}, Success: {Success}", 
                    _overlayOrchestrator != null, isTranslationSuccessful);
                await PublishLegacyOverlayEvent();
            }

            async Task PublishLegacyOverlayEvent()
            {
                // 🔍 [DEBUG] オーバーレイソース特定と翻訳成功判定
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TranslationWithBoundsCompletedHandler → OverlayUpdateEvent発行");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] EventId: {eventData.Id}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] SourceText: '{eventData.SourceText}'");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TranslatedText: '{eventData.TranslatedText}'");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] SourceLanguage: {eventData.SourceLanguage}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] TargetLanguage: {eventData.TargetLanguage}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] EngineName: {eventData.EngineName}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] Bounds: {eventData.Bounds}");
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] IsTranslationSuccessful: {isTranslationSuccessful}");

                // オーバーレイ更新イベントを発行
                var overlayEvent = new OverlayUpdateEvent(
                    text: eventData.TranslatedText,
                    displayArea: eventData.Bounds,
                    originalText: eventData.SourceText,
                    sourceLanguage: eventData.SourceLanguage,
                    targetLanguage: eventData.TargetLanguage,
                    isTranslationResult: isTranslationSuccessful);
                    
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] OverlayUpdateEvent発行中 - ID: {overlayEvent.Id}");
                await _eventAggregator.PublishAsync(overlayEvent).ConfigureAwait(false);
                Console.WriteLine($"🎯 [LEGACY_OVERLAY_SOURCE] OverlayUpdateEvent発行完了 - ID: {overlayEvent.Id}");
            }

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