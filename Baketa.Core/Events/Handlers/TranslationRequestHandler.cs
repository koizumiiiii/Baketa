using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
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
    Console.WriteLine($"🎯 [DEBUG] ⭐⭐⭐ TranslationRequestHandler.HandleAsync 呼び出された！ ⭐⭐⭐");
    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DEBUG] ⭐⭐⭐ TranslationRequestHandler.HandleAsync 呼び出された！ ⭐⭐⭐{Environment.NewLine}");
    
    // NULLチェック
    ArgumentNullException.ThrowIfNull(eventData);

    // 🚀 [PHASE_2_3] BaketaExceptionHandler統合 - フォールバック戦略実装
    Console.WriteLine($"🚀 [PHASE_2_3] BaketaExceptionHandler統合開始: '{eventData.OcrResult.Text}'");
    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_2_3] BaketaExceptionHandler統合開始: '{eventData.OcrResult.Text}'{Environment.NewLine}");

    // プライマリ翻訳処理とフォールバック戦略
    var translationResult = await BaketaExceptionHandler.HandleWithFallbackAsync(
        primary: async () =>
        {
            Console.WriteLine($"🎯 [PHASE_2_3] プライマリ翻訳処理開始: '{eventData.OcrResult.Text}'");
            
            _logger.LogInformation("翻訳要求を処理中: '{Text}' ({SourceLang} → {TargetLang})", 
                eventData.OcrResult.Text, eventData.SourceLanguage, eventData.TargetLanguage);

            // 翻訳サービスの状態確認
            if (_translationService == null)
            {
                throw new InvalidOperationException("翻訳サービスが初期化されていません。");
            }

            // 利用可能なエンジンを確認
            var availableEngines = _translationService.GetAvailableEngines();
            Console.WriteLine($"🔍 [PHASE_2_3] 利用可能エンジン数: {availableEngines.Count}");

            // 翻訳実行
            var sourceLanguage = ParseLanguage(eventData.SourceLanguage);
            var targetLanguage = ParseLanguage(eventData.TargetLanguage);
            
            Console.WriteLine($"🔍 [PHASE_2_3] 翻訳言語ペア: {sourceLanguage} → {targetLanguage}");
            
            var translationResponse = await _translationService.TranslateAsync(
                eventData.OcrResult.Text,
                sourceLanguage,
                targetLanguage).ConfigureAwait(false);

            Console.WriteLine($"🎯 [PHASE_2_3] 翻訳結果: {translationResponse?.TranslatedText ?? "null"}");
            Console.WriteLine($"🎯 [PHASE_2_3] 翻訳成功: {translationResponse?.IsSuccess ?? false}");

            if (!translationResponse?.IsSuccess ?? true)
            {
                throw new InvalidOperationException($"翻訳処理が失敗しました: {translationResponse?.Error?.Message ?? "不明なエラー"}");
            }

            return translationResponse.TranslatedText ?? string.Empty;
        },
        fallback: async () =>
        {
            Console.WriteLine($"🔄 [PHASE_2_3] フォールバック処理開始: '{eventData.OcrResult.Text}'");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [PHASE_2_3] フォールバック処理開始{Environment.NewLine}");
            
            _logger.LogWarning("プライマリ翻訳が失敗、フォールバック処理を実行中: '{Text}'", eventData.OcrResult.Text);
            
            // フォールバック戦略: 元のテキストをそのまま返す
            await Task.Delay(100).ConfigureAwait(false); // 軽微な遅延でリトライ効果
            return eventData.OcrResult.Text; // 翻訳失敗時は元テキストを返す
        },
        onError: async (ex) =>
        {
            Console.WriteLine($"🔥 [PHASE_2_3] エラー処理実行: {ex.GetType().Name} - {ex.Message}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [PHASE_2_3] エラー処理実行: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
            
            _logger.LogError(ex, "翻訳要求処理中にエラーが発生: '{Text}'", eventData.OcrResult.Text);
            
            // ユーザーフレンドリーなエラーメッセージ生成
            var userFriendlyMessage = BaketaExceptionHandler.GetUserFriendlyErrorMessage(ex, "翻訳処理");
            
            // エラー通知イベント発行
            var errorNotificationEvent = new NotificationEvent(
                userFriendlyMessage,
                NotificationType.Error,
                "翻訳エラー",
                displayTime: 5000);
                
            await _eventAggregator.PublishAsync(errorNotificationEvent).ConfigureAwait(false);
        }).ConfigureAwait(false);

    Console.WriteLine($"🚀 [PHASE_2_3] BaketaExceptionHandler処理完了: '{translationResult}'");
    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_2_3] BaketaExceptionHandler処理完了: '{translationResult}'{Environment.NewLine}");

    _logger.LogInformation("翻訳完了: '{Original}' → '{Translated}'", 
        eventData.OcrResult.Text, translationResult);

    // 座標情報付き翻訳完了イベントを発行
    var completedEvent = new TranslationWithBoundsCompletedEvent(
        sourceText: eventData.OcrResult.Text,
        translatedText: translationResult,
        sourceLanguage: eventData.SourceLanguage,
        targetLanguage: eventData.TargetLanguage,
        bounds: eventData.OcrResult.Bounds,
        confidence: 1.0f,
        engineName: "Translation Service (Phase 2.3 Enhanced)");

    _logger.LogInformation("🎯 TranslationWithBoundsCompletedEvent発行開始 - ID: {EventId}", completedEvent.Id);
    Console.WriteLine($"🎯 [PHASE_2_3] TranslationWithBoundsCompletedEvent発行開始 - ID: {completedEvent.Id}");
    
    await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    
    _logger.LogInformation("🎯 TranslationWithBoundsCompletedEvent発行完了 - ID: {EventId}", completedEvent.Id);
    Console.WriteLine($"🎯 [PHASE_2_3] TranslationWithBoundsCompletedEvent発行完了 - ID: {completedEvent.Id}");
}
    
    /// <summary>
    /// 文字列から言語を解析する
    /// </summary>
    /// <param name="languageString">言語文字列</param>
    /// <returns>Language型</returns>
    private static Language ParseLanguage(string languageString)
    {
        if (string.IsNullOrEmpty(languageString))
            return Language.English;
            
        var normalizedLang = languageString.ToLowerInvariant();
        
        return normalizedLang switch
        {
            "ja" or "japanese" or "ja-jp" => Language.Japanese,
            "en" or "english" or "en-us" => Language.English,
            "auto" => Language.English, // ✅ 重大バグ修正: autoの場合は英語想定（英→日翻訳が目的）
            _ => Language.English // デフォルトは英語
        };
    }
}