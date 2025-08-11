using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Settings;
using Baketa.Core.Models;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Baketa.Core.Events.Handlers;

/// <summary>
/// OCR完了イベントハンドラー
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="eventAggregator">イベント集約インスタンス</param>
/// <param name="appSettingsOptions">アプリケーション設定</param>
public class OcrCompletedHandler(IEventAggregator eventAggregator, IOptions<AppSettings> appSettingsOptions) : IEventProcessor<OcrCompletedEvent>
    {
        private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        private readonly AppSettings _appSettings = appSettingsOptions?.Value ?? throw new ArgumentNullException(nameof(appSettingsOptions));
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task HandleAsync(OcrCompletedEvent eventData)
    {
        // デバッグログ: ハンドラー呼び出し確認
        Console.WriteLine($"🔥 [DEBUG] OcrCompletedHandler.HandleAsync 呼び出し開始: Results={eventData?.Results?.Count ?? 0}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] OcrCompletedHandler.HandleAsync 呼び出し開始: Results={eventData?.Results?.Count ?? 0}{Environment.NewLine}");
        
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
        
        // 🚀 [PHASE_2_2_OPTIMIZATION] バッチ並列翻訳処理実装
        Console.WriteLine($"🚀 [PHASE_2_2] 並列翻訳要求イベント発行開始: {eventData.Results.Count}個のテキスト");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_2_2] 並列翻訳要求イベント発行開始: {eventData.Results.Count}個のテキスト{Environment.NewLine}");
        
        // 各テキスト領域に対して翻訳要求イベントを並列で発行
        var translationTasks = eventData.Results.Select(async result =>
        {
            try
            {
                Console.WriteLine($"🚀 [PHASE_2_2] 翻訳要求イベント準備: '{result.Text}'");
                
                // 翻訳設定から言語情報を取得
                var sourceLanguageCode = _appSettings.Translation.AutoDetectSourceLanguage 
                    ? "auto" 
                    : _appSettings.Translation.DefaultSourceLanguage;
                
                var targetLanguageCode = _appSettings.Translation.DefaultTargetLanguage;

                Console.WriteLine($"🌍 [LANGUAGE_SETTING_FIXED] 設定取得: {sourceLanguageCode} → {targetLanguageCode} (自動検出: {_appSettings.Translation.AutoDetectSourceLanguage})");

                // 翻訳要求イベントを発行
                var translationRequestEvent = new TranslationRequestEvent(
                    ocrResult: result,
                    sourceLanguage: sourceLanguageCode,
                    targetLanguage: targetLanguageCode);
                
                Console.WriteLine($"🚀 [PHASE_2_2] EventAggregator.PublishAsync並列実行: '{result.Text}'");
                await _eventAggregator.PublishAsync(translationRequestEvent).ConfigureAwait(false);
                Console.WriteLine($"🚀 [PHASE_2_2] EventAggregator.PublishAsync並列完了: '{result.Text}'");
                
                return true; // 成功
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🔥 [ERROR] 並列翻訳要求でエラー: '{result.Text}' - {ex.GetType().Name}: {ex.Message}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] 並列翻訳要求でエラー: '{result.Text}' - {ex.GetType().Name}: {ex.Message}{Environment.NewLine}");
                return false; // 失敗
            }
        });

        // すべての翻訳要求イベントが完了するまで待機
        var results = await Task.WhenAll(translationTasks).ConfigureAwait(false);
        
        // 実行結果をログ出力
        var successCount = results.Count(r => r);
        var failureCount = results.Length - successCount;
        
        Console.WriteLine($"🚀 [PHASE_2_2] 並列翻訳要求イベント発行完了 - 成功: {successCount}, 失敗: {failureCount}");
        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 [PHASE_2_2] 並列翻訳要求イベント発行完了 - 成功: {successCount}, 失敗: {failureCount}{Environment.NewLine}");
    }
    }
