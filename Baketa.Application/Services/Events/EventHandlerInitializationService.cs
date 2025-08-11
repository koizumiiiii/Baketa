using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Handlers;

namespace Baketa.Application.Services.Events;

/// <summary>
/// イベントハンドラー初期化サービス
/// </summary>
/// <remarks>
/// サービスを初期化します
/// </remarks>
/// <param name="serviceProvider">サービスプロバイダー</param>
/// <param name="logger">ロガー</param>
public sealed class EventHandlerInitializationService(
    IServiceProvider serviceProvider,
    ILogger<EventHandlerInitializationService> logger)
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<EventHandlerInitializationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// イベントハンドラーを初期化します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>初期化タスク</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("イベントハンドラー初期化を開始します");

        try
        {
            // EventAggregatorの取得
            var eventAggregator = _serviceProvider.GetRequiredService<IEventAggregator>();
            _logger.LogInformation("EventAggregator取得成功");
            
            // EventAggregator DI取得詳細デバッグ
            Console.WriteLine($"🔥 [DI_DEBUG] EventHandlerInitializationService - EventAggregator取得");
            Console.WriteLine($"🔥 [DI_DEBUG] EventAggregator型: {eventAggregator.GetType().FullName}");
            Console.WriteLine($"🔥 [DI_DEBUG] EventAggregatorハッシュ: {eventAggregator.GetHashCode()}");
            Console.WriteLine($"🔥 [DI_DEBUG] EventAggregator参照: {eventAggregator}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DI_DEBUG] EventHandlerInitializationService - EventAggregator型: {eventAggregator.GetType().FullName}, ハッシュ: {eventAggregator.GetHashCode()}{Environment.NewLine}");
    
            // OcrCompletedHandlerの登録
            try
            {
                var ocrCompletedHandler = _serviceProvider.GetRequiredService<OcrCompletedHandler>();
                eventAggregator.Subscribe<OcrCompletedEvent>(ocrCompletedHandler);
                _logger.LogInformation("OcrCompletedHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] OcrCompletedHandlerを登録しました");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] OcrCompletedHandlerを登録しました{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OcrCompletedHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] OcrCompletedHandlerの登録失敗: {ex.Message}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] OcrCompletedHandlerの登録失敗: {ex.Message}{Environment.NewLine}");
            }

            // TranslationRequestHandlerの登録
            try
            {
                var translationRequestHandler = _serviceProvider.GetRequiredService<TranslationRequestHandler>();
                eventAggregator.Subscribe<TranslationRequestEvent>(translationRequestHandler);
                _logger.LogInformation("TranslationRequestHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] TranslationRequestHandlerを登録しました");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] TranslationRequestHandlerを登録しました{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranslationRequestHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] TranslationRequestHandlerの登録失敗: {ex.Message}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] TranslationRequestHandlerの登録失敗: {ex.Message}{Environment.NewLine}");
            }

            // TranslationWithBoundsCompletedHandlerの登録
            try
            {
                var translationWithBoundsCompletedHandler = _serviceProvider.GetRequiredService<TranslationWithBoundsCompletedHandler>();
                eventAggregator.Subscribe<TranslationWithBoundsCompletedEvent>(translationWithBoundsCompletedHandler);
                _logger.LogInformation("TranslationWithBoundsCompletedHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] TranslationWithBoundsCompletedHandlerを登録しました");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] TranslationWithBoundsCompletedHandlerを登録しました{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranslationWithBoundsCompletedHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] TranslationWithBoundsCompletedHandlerの登録失敗: {ex.Message}");
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] TranslationWithBoundsCompletedHandlerの登録失敗: {ex.Message}{Environment.NewLine}");
            }

            _logger.LogInformation("🔥 イベントハンドラー初期化が完了しました");
            Console.WriteLine("🔥 [DEBUG] イベントハンドラー初期化が完了しました");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] イベントハンドラー初期化が完了しました{Environment.NewLine}");

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "イベントハンドラー初期化中にエラーが発生しました");
            Console.WriteLine($"🔥 [ERROR] イベントハンドラー初期化エラー: {ex.Message}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] イベントハンドラー初期化エラー: {ex.Message}{Environment.NewLine}");
            throw;
        }
    }

}