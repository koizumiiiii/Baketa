using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Models.Processing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace Baketa.Application.EventHandlers;

/// <summary>
/// TranslationCompletedEventを受信してTranslationWithBoundsCompletedEventに変換する中継ハンドラー
/// UltraThink調査により判明した翻訳結果表示問題の修正
/// </summary>
public class TranslationCompletedHandler(
    IEventAggregator eventAggregator,
    ILogger<TranslationCompletedHandler> logger,
    IConfiguration configuration) : IEventProcessor<TranslationCompletedEvent>
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<TranslationCompletedHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IConfiguration _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

    /// <summary>
    /// イベント処理の優先度（低優先度でUI表示優先度を下げる）
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// 同期実行設定（非同期処理でパフォーマンス最適化）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// TranslationCompletedEventを受信してTranslationWithBoundsCompletedEventに変換
    /// </summary>
    public async Task HandleAsync(TranslationCompletedEvent eventData)
    {
        try
        {
            _logger.LogInformation("🔄 [中継] TranslationCompletedEvent受信 - ID: {EventId}, テキスト長: {TextLength}", 
                eventData?.Id, eventData?.TranslatedText?.Length ?? 0);
            Console.WriteLine($"🔄 [中継] TranslationCompletedEvent受信 - ID: {eventData?.Id}");

            if (eventData == null)
            {
                _logger.LogWarning("⚠️ TranslationCompletedEvent is null - スキップ");
                return;
            }

            // 設定から言語を動的取得
            var defaultSourceLanguage = _configuration.GetValue<string>("Translation:DefaultSourceLanguage", "en");
            var defaultTargetLanguage = _configuration.GetValue<string>("Translation:DefaultTargetLanguage", "ja");

            // TranslationWithBoundsCompletedEventに変換
            // TranslationCompletedEventにはBounds情報がないため、空のRectangleを使用
            var boundsEvent = new TranslationWithBoundsCompletedEvent(
                sourceText: eventData.SourceText ?? "",
                translatedText: eventData.TranslatedText ?? "",
                sourceLanguage: eventData.SourceLanguage ?? defaultSourceLanguage,
                targetLanguage: eventData.TargetLanguage ?? defaultTargetLanguage,
                bounds: System.Drawing.Rectangle.Empty, // Bounds情報なし
                confidence: 0.95f, // デフォルト信頼度
                engineName: eventData.EngineName ?? "Default"
            );

            _logger.LogInformation("🎯 [中継] TranslationWithBoundsCompletedEvent発行 - ID: {EventId}, Bounds: ({X},{Y},{W},{H})", 
                boundsEvent.Id, boundsEvent.Bounds.X, boundsEvent.Bounds.Y, 
                boundsEvent.Bounds.Width, boundsEvent.Bounds.Height);
            Console.WriteLine($"🎯 [中継] TranslationWithBoundsCompletedEvent発行 - ID: {boundsEvent.Id}");

            // UI表示用イベントを発行
            await _eventAggregator.PublishAsync(boundsEvent).ConfigureAwait(false);
            
            _logger.LogInformation("✅ [中継] TranslationCompletedEvent → TranslationWithBoundsCompletedEvent変換完了");
            Console.WriteLine($"✅ [中継] イベント変換完了 - {eventData.Id} → {boundsEvent.Id}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ [中継] TranslationCompletedEvent変換処理でエラー");
            Console.WriteLine($"❌ [中継] エラー: {ex.Message}");
        }
    }
}