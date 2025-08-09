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
            
            // 各テキスト領域に対して翻訳要求イベントを発行
            Console.WriteLine($"🔥 [DEBUG] 翻訳要求イベント発行開始: {eventData.Results.Count}個のテキスト");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] 翻訳要求イベント発行開始: {eventData.Results.Count}個のテキスト{Environment.NewLine}");
            
            foreach (var result in eventData.Results)
            {
                Console.WriteLine($"🔥 [DEBUG] 翻訳要求イベント発行: '{result.Text}'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] 翻訳要求イベント発行: '{result.Text}'{Environment.NewLine}");
                
                // 翻訳設定から言語情報を取得
                Console.WriteLine($"🔍 [SETTINGS_DEBUG] _appSettings != null: {_appSettings != null}");
                Console.WriteLine($"🔍 [SETTINGS_DEBUG] _appSettings.Translation != null: {_appSettings?.Translation != null}");
                Console.WriteLine($"🔍 [SETTINGS_DEBUG] AutoDetectSourceLanguage値: {_appSettings?.Translation?.AutoDetectSourceLanguage}");
                Console.WriteLine($"🔍 [SETTINGS_DEBUG] DefaultSourceLanguage値: '{_appSettings?.Translation?.DefaultSourceLanguage}'");
                Console.WriteLine($"🔍 [SETTINGS_DEBUG] DefaultTargetLanguage値: '{_appSettings?.Translation?.DefaultTargetLanguage}'");
                
                var sourceLanguageCode = _appSettings.Translation.AutoDetectSourceLanguage 
                    ? "auto" 
                    : _appSettings.Translation.DefaultSourceLanguage;
                
                var targetLanguageCode = _appSettings.Translation.DefaultTargetLanguage;

                Console.WriteLine($"🌍 [LANGUAGE_SETTING_FIXED] 設定取得: {sourceLanguageCode} → {targetLanguageCode} (自動検出: {_appSettings.Translation.AutoDetectSourceLanguage})");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🌍 [LANGUAGE_SETTING_FIXED] 設定取得: {sourceLanguageCode} → {targetLanguageCode} (自動検出: {_appSettings.Translation.AutoDetectSourceLanguage}){Environment.NewLine}");

                // 翻訳要求イベントを発行
                var translationRequestEvent = new TranslationRequestEvent(
                    ocrResult: result,
                    sourceLanguage: sourceLanguageCode,
                    targetLanguage: targetLanguageCode);
                
                Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync呼び出し直前: '{result.Text}'");
                try
                {
                    await _eventAggregator.PublishAsync(translationRequestEvent).ConfigureAwait(false);
                    Console.WriteLine($"🔥 [DEBUG] EventAggregator.PublishAsync成功: '{result.Text}'");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"🔥 [ERROR] EventAggregator.PublishAsync例外: {ex.GetType().Name} - {ex.Message}");
                    Console.WriteLine($"🔥 [ERROR] スタックトレース: {ex.StackTrace}");
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] EventAggregator.PublishAsync例外: {ex.GetType().Name} - {ex.Message}{Environment.NewLine}");
                    throw;
                }
                
                Console.WriteLine($"🔥 [DEBUG] 翻訳要求イベント発行完了: '{result.Text}'");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] 翻訳要求イベント発行完了: '{result.Text}'{Environment.NewLine}");
            }
        }
    }
