using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Common;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;
using System;
using System.Drawing;
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
    // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
    //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DEBUG] ⭐⭐⭐ TranslationRequestHandler.HandleAsync 呼び出された！ ⭐⭐⭐{Environment.NewLine}");
    
    // NULLチェック
    ArgumentNullException.ThrowIfNull(eventData);

    // 🚀 [PHASE_0.2] 同言語検出フィルターによる早期終了処理
    var sourceLanguage = ParseLanguage(eventData.SourceLanguage);
    var targetLanguage = ParseLanguage(eventData.TargetLanguage);
    
    var tempRequest = TranslationRequest.Create(
        eventData.OcrResult.Text,
        sourceLanguage,
        targetLanguage
    );

    if (tempRequest.ShouldSkipTranslation())
    {
        Console.WriteLine($"🚀 [PHASE_0.2] 同言語検出: '{eventData.SourceLanguage}' → '{eventData.TargetLanguage}' - 翻訳をスキップしています");
        // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
        //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_0.2] 同言語検出: '{eventData.SourceLanguage}' → '{eventData.TargetLanguage}' - 翻訳をスキップしています{Environment.NewLine}");

        _logger.LogInformation("翻訳スキップ: 同言語ペア検出 '{SourceLang}' → '{TargetLang}', テキスト: '{Text}'", 
            eventData.SourceLanguage, eventData.TargetLanguage, eventData.OcrResult.Text);

        // 🚫 [DUPLICATE_DISPLAY_FIX] 同言語の場合は翻訳結果を空文字で非表示にする
        var skippedResult = string.Empty; // 重複表示防止：同言語では非表示
        
        // 🎯 [COORDINATE_FIX] ROI座標から画面座標への変換を実装
        var skipScreenBounds = ConvertRoiToScreenCoordinates(eventData.OcrResult.Bounds);
        Console.WriteLine($"🎯 [COORDINATE_DEBUG_SKIP] ROI座標: {eventData.OcrResult.Bounds} → 画面座標: {skipScreenBounds}");

        // 翻訳完了イベントを即座に発行（処理時間0ms）
        var skipCompletedEvent = new TranslationWithBoundsCompletedEvent(
            sourceText: eventData.OcrResult.Text,
            translatedText: skippedResult, // 🚫 空文字で非表示設定
            sourceLanguage: eventData.SourceLanguage,
            targetLanguage: eventData.TargetLanguage,
            bounds: skipScreenBounds, // 🎯 変換済み画面座標を使用
            confidence: 1.0f,
            engineName: "Same Language Filter (Phase 0.2 - Hidden)");

        Console.WriteLine($"🚀 [PHASE_0.2] 同言語スキップ完了 - 即座にTranslationWithBoundsCompletedEvent発行");
        // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
        //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_0.2] 同言語スキップ完了 - ID: {skipCompletedEvent.Id}{Environment.NewLine}");

        await _eventAggregator.PublishAsync(skipCompletedEvent).ConfigureAwait(false);
        
        _logger.LogInformation("同言語翻訳スキップ完了: '{Original}' (スキップ処理時間: <1ms)", eventData.OcrResult.Text);
        return; // 🚀 早期終了 - 以降の翻訳処理は実行されない
    }

    // 🚀 [PHASE_2_3] BaketaExceptionHandler統合 - フォールバック戦略実装
    Console.WriteLine($"🚀 [PHASE_2_3] BaketaExceptionHandler統合開始: '{eventData.OcrResult.Text}'");
    // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
    //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_2_3] BaketaExceptionHandler統合開始: '{eventData.OcrResult.Text}'{Environment.NewLine}");

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

            if (translationResponse == null || !translationResponse.IsSuccess)
            {
                // 詳細なエラー情報をログ出力
                var errorDetails = translationResponse == null 
                    ? "翻訳レスポンスがnull" 
                    : $"IsSuccess={translationResponse.IsSuccess}, Error={translationResponse.Error?.Message ?? "null"}, ErrorType={translationResponse.Error?.ErrorType}, TranslatedText={translationResponse.TranslatedText ?? "null"}";
                
                Console.WriteLine($"🔥 [PHASE_2_3] エラー詳細: {errorDetails}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_translation_errors.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} エラー詳細: {errorDetails}\n");
                
                throw new InvalidOperationException($"翻訳処理が失敗しました: {translationResponse?.Error?.Message ?? "不明なエラー"}");
            }

            return translationResponse.TranslatedText ?? string.Empty;
        },
        fallback: async () =>
        {
            Console.WriteLine($"🔄 [PHASE_2_3] フォールバック処理開始: '{eventData.OcrResult.Text}'");
            // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔄 [PHASE_2_3] フォールバック処理開始{Environment.NewLine}");
            
            _logger.LogWarning("プライマリ翻訳が失敗、フォールバック処理を実行中: '{Text}'", eventData.OcrResult.Text);
            
            // フォールバック戦略: 空文字を返して非表示にする（元テキスト表示を防止）
            await Task.Delay(100).ConfigureAwait(false); // 軽微な遅延でリトライ効果
            return string.Empty; // 翻訳失敗時は空文字で非表示
        },
        onError: async (ex) =>
        {
            Console.WriteLine($"🔥 [PHASE_2_3] エラー処理実行: {ex.GetType().Name} - {ex.Message}");
            // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [PHASE_2_3] エラー処理実行: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
            
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
    Console.WriteLine($"🎯 [COORDINATE_REACH_CHECK] 座標変換処理に到達しました！");
    
    _logger.LogInformation("翻訳完了: '{Original}' → '{Translated}'", 
        eventData.OcrResult.Text, translationResult);

    // 🎯 [COORDINATE_FIX] ROI座標から画面座標への変換を実装
    // ROI画像の座標はキャプチャ領域内の相対座標のため、実際の画面座標に変換する必要がある
    Console.WriteLine($"🎯 [COORDINATE_BEFORE] 変換前のOcrResult.Bounds: {eventData.OcrResult.Bounds}");
    var screenBounds = ConvertRoiToScreenCoordinates(eventData.OcrResult.Bounds);
    Console.WriteLine($"🎯 [COORDINATE_DEBUG] ROI座標: {eventData.OcrResult.Bounds} → 画面座標: {screenBounds}");
    
    // 座標情報付き翻訳完了イベントを発行
    var completedEvent = new TranslationWithBoundsCompletedEvent(
        sourceText: eventData.OcrResult.Text,
        translatedText: translationResult,
        sourceLanguage: eventData.SourceLanguage,
        targetLanguage: eventData.TargetLanguage,
        bounds: screenBounds, // 🎯 変換済み画面座標を使用
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
            "auto" => Language.Auto, // 🚀 [CRITICAL_BUG_FIX] autoは正しくLanguage.Autoとして処理
            _ => Language.English // デフォルトは英語
        };
    }
    
    /// <summary>
    /// ROI座標を実際の画面座標に変換
    /// 🎯 [DIRECT_COORDINATE_FIX] ROI座標をそのまま画面座標として使用
    /// </summary>
    /// <param name="roiBounds">ROI画像内の座標</param>
    /// <returns>画面上の座標（変換なし）</returns>
    private static System.Drawing.Rectangle ConvertRoiToScreenCoordinates(System.Drawing.Rectangle roiBounds)
    {
        // 🎯 [DIRECT_USE] ROI座標をそのまま画面座標として使用
        // オフセット補正を削除し、OCR検出座標を直接使用
        
        Console.WriteLine($"🎯 [DIRECT_COORDINATE] ROI座標をそのまま使用: {roiBounds}");
        
        return roiBounds; // 変換せずそのまま返す
    }
}