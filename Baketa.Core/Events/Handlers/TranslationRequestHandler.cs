using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// 翻訳要求イベントハンドラー
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="translationService">翻訳サービス</param>
/// <param name="eventAggregator">イベント集約インスタンス</param>
/// <param name="logger">ロガー</param>
public class TranslationRequestHandler(
    ITranslationService translationService,
    IEventAggregator eventAggregator,
    ILogger<TranslationRequestHandler> logger) : IEventProcessor<TranslationRequestEvent>
{
    private readonly ITranslationService _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ILogger<TranslationRequestHandler> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
    /// <inheritdoc />
    public int Priority => 100;
        
    /// <inheritdoc />
    public bool SynchronousExecution => false;

    /// <inheritdoc />
    public async Task HandleAsync(TranslationRequestEvent eventData)
    {
        
        // NULLチェック
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger.LogInformation("翻訳要求を処理中: '{Text}' ({SourceLang} → {TargetLang})", 
                eventData.OcrResult.Text, eventData.SourceLanguage, eventData.TargetLanguage);
            Console.WriteLine($"🎯 [DEBUG] TranslationRequestHandler.HandleAsync開始 - テキスト: '{eventData.OcrResult.Text}'");

            // 翻訳サービスの状態確認
            Console.WriteLine($"🔍 [DEBUG] 翻訳サービス: {_translationService?.GetType().Name ?? "null"}");
            
            // 利用可能なエンジンを確認
            var availableEngines = _translationService.GetAvailableEngines();
            Console.WriteLine($"🔍 [DEBUG] 利用可能エンジン数: {availableEngines.Count}");
            foreach (var engine in availableEngines)
            {
                Console.WriteLine($"🔍 [DEBUG] エンジン: {engine.Name} - Ready: {await engine.IsReadyAsync().ConfigureAwait(false)}");
            }
            
            // アクティブエンジンの確認
            Console.WriteLine($"🔍 [DEBUG] アクティブエンジン: {_translationService.ActiveEngine?.Name ?? "null"}");

            // 翻訳実行
            var sourceLanguage = ParseLanguage(eventData.SourceLanguage);
            var targetLanguage = ParseLanguage(eventData.TargetLanguage);
            
            Console.WriteLine($"🔍 [DEBUG] 翻訳言語ペア: {sourceLanguage} → {targetLanguage}");
            Console.WriteLine($"🔍 [DEBUG] 翻訳サービス.TranslateAsync呼び出し開始");
            
            var translationResponse = await _translationService.TranslateAsync(
                eventData.OcrResult.Text,
                sourceLanguage,
                targetLanguage).ConfigureAwait(false);

            Console.WriteLine($"🔍 [DEBUG] 翻訳サービス.TranslateAsync呼び出し完了");
            Console.WriteLine($"🔍 [DEBUG] 翻訳結果: {translationResponse?.TranslatedText ?? "null"}");
            Console.WriteLine($"🔍 [DEBUG] 翻訳成功: {translationResponse?.IsSuccess ?? false}");
            Console.WriteLine($"🔍 [DEBUG] エラー情報: {translationResponse?.Error?.Message ?? "なし"}");

            var translatedText = translationResponse?.TranslatedText ?? string.Empty;
            
            
            _logger.LogInformation("翻訳完了: '{Original}' → '{Translated}'", 
                eventData.OcrResult.Text, translatedText);

            // 座標情報付き翻訳完了イベントを発行
            var completedEvent = new TranslationWithBoundsCompletedEvent(
                sourceText: eventData.OcrResult.Text,
                translatedText: translatedText,
                sourceLanguage: eventData.SourceLanguage,
                targetLanguage: eventData.TargetLanguage,
                bounds: eventData.OcrResult.Bounds,
                confidence: 1.0f,
                engineName: "Translation Service");

            _logger.LogInformation("🎯 TranslationWithBoundsCompletedEvent発行開始 - ID: {EventId}", completedEvent.Id);
            Console.WriteLine($"🎯 [DEBUG] TranslationWithBoundsCompletedEvent発行開始 - ID: {completedEvent.Id}");
            await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
            _logger.LogInformation("🎯 TranslationWithBoundsCompletedEvent発行完了 - ID: {EventId}", completedEvent.Id);
            Console.WriteLine($"🎯 [DEBUG] TranslationWithBoundsCompletedEvent発行完了 - ID: {completedEvent.Id}");
            
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳要求処理中にエラーが発生: '{Text}'", eventData.OcrResult.Text);
            Console.WriteLine($"🔥 [ERROR] TranslationRequestHandler例外発生: {ex.GetType().Name} - {ex.Message}");
            Console.WriteLine($"🔥 [ERROR] スタックトレース: {ex.StackTrace}");
            
            // 翻訳失敗イベントを発行
            var failedEvent = new TranslationFailedEvent(
                sourceText: eventData.OcrResult.Text,
                sourceLanguage: eventData.SourceLanguage,
                targetLanguage: eventData.TargetLanguage,
                engineName: "Translation Service",
                exception: ex,
                errorMessage: ex.Message);
                
            await _eventAggregator.PublishAsync(failedEvent).ConfigureAwait(false);
        }
    }
    
    /// <summary>
    /// 文字列から言語を解析する
    /// </summary>
    /// <param name="languageString">言語文字列</param>
    /// <returns>Language型</returns>
    private static Language ParseLanguage(string languageString)
    {
        return languageString?.ToLowerInvariant() switch
        {
            "ja" or "japanese" => Language.Japanese,
            "en" or "english" => Language.English,
            "auto" => Language.Japanese, // autoの場合はデフォルトで日本語を想定
            _ => Language.English // デフォルトは英語
        };
    }
}