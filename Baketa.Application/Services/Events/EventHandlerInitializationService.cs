using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.EventHandlers;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Events.Handlers;
using Baketa.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
    private readonly LoggingSettings _loggingSettings = InitializeLoggingSettings(serviceProvider);

    private static LoggingSettings InitializeLoggingSettings(IServiceProvider serviceProvider)
    {
        try
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            if (configuration != null)
            {
                return new LoggingSettings
                {
                    DebugLogPath = configuration.GetValue<string>("Logging:DebugLogPath") ?? "debug_app_logs.txt",
                    EnableDebugFileLogging = configuration.GetValue<bool>("Logging:EnableDebugFileLogging", true),
                    MaxDebugLogFileSizeMB = configuration.GetValue<int>("Logging:MaxDebugLogFileSizeMB", 10),
                    DebugLogRetentionDays = configuration.GetValue<int>("Logging:DebugLogRetentionDays", 7)
                };
            }
        }
        catch
        {
            // 設定取得失敗時はデフォルトを使用
        }
        return LoggingSettings.CreateDevelopmentSettings();
    }

    /// <summary>
    /// イベントハンドラーを初期化します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>初期化タスク</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        // 🚨 最重要: メソッド開始の即座ログ出力（確実な記録）
        var startTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        Console.WriteLine("🚨🚨🚨 [INIT_START] EventHandlerInitializationService.InitializeAsync() 実行開始！");
        System.Diagnostics.Debug.WriteLine("🚨🚨🚨 [INIT_START] EventHandlerInitializationService.InitializeAsync() 実行開始！");

        // 確実なファイル記録
        try
        {
            System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                $"{startTimestamp}→🚨🚨🚨 [INIT_START] EventHandlerInitializationService.InitializeAsync() 実行開始！{Environment.NewLine}");
        }
        catch { /* ファイル出力失敗は無視 */ }

        _logger.LogInformation("イベントハンドラー初期化を開始します");
        Console.WriteLine("🔥 [INIT_LOG] _logger.LogInformation実行完了");

        // ファイル記録
        try
        {
            System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→🔥 [INIT_LOG] _logger.LogInformation実行完了{Environment.NewLine}");
        }
        catch { /* ファイル出力失敗は無視 */ }

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

            // ⚡ [PHASE_2_FIX] CaptureCompletedHandlerの登録
            try
            {
                var captureCompletedHandler = _serviceProvider.GetRequiredService<IEventProcessor<CaptureCompletedEvent>>();
                eventAggregator.Subscribe<CaptureCompletedEvent>(captureCompletedHandler);
                _logger.LogInformation("CaptureCompletedHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] CaptureCompletedHandlerを登録しました");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [SUCCESS] CaptureCompletedHandlerを登録しました{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "CaptureCompletedHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] CaptureCompletedHandlerの登録失敗: {ex.Message}");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] CaptureCompletedHandler登録失敗: {ex.Message}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // 🔥 [PHASE5] ROIImageCapturedEventHandler削除 - ROI廃止により不要

            // ⚡ [PHASE_2_FIX] OcrRequestHandlerの登録 - 翻訳パイプライン連鎖修復
            try
            {
                var ocrRequestHandler = _serviceProvider.GetRequiredService<IEventProcessor<OcrRequestEvent>>();
                eventAggregator.Subscribe<OcrRequestEvent>(ocrRequestHandler);
                _logger.LogInformation("OcrRequestHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] OcrRequestHandlerを登録しました");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [SUCCESS] OcrRequestHandler (翻訳パイプライン連鎖) を登録しました{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "OcrRequestHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] OcrRequestHandlerの登録失敗: {ex.Message}");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] OcrRequestHandler登録失敗: {ex.Message}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // TranslationRequestHandlerの登録
            try
            {
                var translationRequestHandler = _serviceProvider.GetRequiredService<TranslationRequestHandler>();
                eventAggregator.Subscribe<TranslationRequestEvent>(translationRequestHandler);
                _logger.LogInformation("TranslationRequestHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] TranslationRequestHandlerを登録しました");
                // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] TranslationRequestHandlerを登録しました{Environment.NewLine}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranslationRequestHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] TranslationRequestHandlerの登録失敗: {ex.Message}");
                // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [ERROR] TranslationRequestHandlerの登録失敗: {ex.Message}{Environment.NewLine}");
            }

            // BatchTranslationRequestHandlerの登録
            try
            {
                var batchTranslationRequestHandler = _serviceProvider.GetRequiredService<BatchTranslationRequestHandler>();
                eventAggregator.Subscribe<BatchTranslationRequestEvent>(batchTranslationRequestHandler);
                _logger.LogInformation("BatchTranslationRequestHandlerを登録しました");
                Console.WriteLine("🔥 [DEBUG] BatchTranslationRequestHandlerを登録しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BatchTranslationRequestHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] BatchTranslationRequestHandlerの登録失敗: {ex.Message}");
            }

            // 🔄 [DUPLICATE_TRANSLATION_FIX] TranslationCompletedHandler登録を無効化
            // このハンドラは、新しいアーキテクチャの完了イベントを古いUI表示イベントに変換する
            // ブリッジとして機能しており、二重翻訳の根本原因となっていたため登録を停止する。
            // try
            // {
            //     var translationCompletedHandler = _serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationCompletedEvent>>();
            //     eventAggregator.Subscribe<Baketa.Core.Events.EventTypes.TranslationCompletedEvent>(translationCompletedHandler);
            //     _logger.LogInformation("TranslationCompletedHandlerを登録しました - 翻訳完了イベント中継修復");
            //     Console.WriteLine("🔄 [FIX] TranslationCompletedHandlerを登録しました - 翻訳完了イベント中継修復");
            //     
            //     // 確実なファイル記録
            //     try
            //     {
            //         System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(), 
            //             $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [FIX] TranslationCompletedHandler登録 - 翻訳完了イベント中継修復{Environment.NewLine}");
            //     }
            //     catch { /* ファイル出力失敗は無視 */ }
            // }
            // catch (Exception ex)
            // {
            //     _logger.LogError(ex, "TranslationCompletedHandlerの登録に失敗しました");
            //     Console.WriteLine($"🔥 [ERROR] TranslationCompletedHandlerの登録失敗: {ex.Message}");
            //     
            //     // 確実なファイル記録
            //     try
            //     {
            //         System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(), 
            //             $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] TranslationCompletedHandler登録失敗: {ex.Message}{Environment.NewLine}");
            //     }
            //     catch { /* ファイル出力失敗は無視 */ }
            // }

            // 🔄 [FIX] TranslationWithBoundsCompletedHandler復活 - 翻訳結果をTextChunkに反映するため必須
            try
            {
                var translationWithBoundsCompletedHandler = _serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationWithBoundsCompletedEvent>>();
                eventAggregator.Subscribe<Baketa.Core.Events.EventTypes.TranslationWithBoundsCompletedEvent>(translationWithBoundsCompletedHandler);
                _logger.LogInformation("TranslationWithBoundsCompletedHandlerを登録しました - 翻訳結果反映修復");
                Console.WriteLine("🔄 [FIX] TranslationWithBoundsCompletedHandlerを登録しました - 翻訳結果反映修復");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [FIX] TranslationWithBoundsCompletedHandler復活 - 翻訳結果反映修復{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TranslationWithBoundsCompletedHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] TranslationWithBoundsCompletedHandlerの登録失敗: {ex.Message}");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] TranslationWithBoundsCompletedHandler登録失敗: {ex.Message}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // 🎉 [PHASE12.2] AggregatedChunksReadyEventHandler登録 - 2重翻訳アーキテクチャ排除
            try
            {
                var aggregatedChunksReadyHandler = _serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.Translation.AggregatedChunksReadyEvent>>();
                eventAggregator.Subscribe<Baketa.Core.Events.Translation.AggregatedChunksReadyEvent>(aggregatedChunksReadyHandler);
                _logger.LogInformation("🎉 AggregatedChunksReadyHandlerを登録しました - TimedChunkAggregatorイベント駆動処理");
                Console.WriteLine("🎉 [PHASE12.2] AggregatedChunksReadyHandlerを登録しました - TimedChunkAggregatorイベント駆動処理");
                _logger?.LogDebug("🎉 [PHASE12.2] AggregatedChunksReadyHandlerを登録しました - TimedChunkAggregatorイベント駆動処理");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [PHASE12.2] AggregatedChunksReadyHandler登録完了 - TimedChunkAggregatorイベント駆動処理{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AggregatedChunksReadyHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] AggregatedChunksReadyHandler登録失敗: {ex.Message}");
                _logger?.LogDebug($"🔥 [ERROR] AggregatedChunksReadyHandler登録失敗: {ex.Message}");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] AggregatedChunksReadyHandler登録失敗: {ex.Message}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // 🛑 [PHASE6.1] StopTranslationRequestEventHandler登録 - Stop処理問題修正
            try
            {
                // 🔥 [PHASE6.1_EVENTAG_INSTANCE_CHECK] EventAggregatorインスタンス確認
                var eventAggregatorHash = eventAggregator?.GetHashCode() ?? -1;
                Console.WriteLine($"🔍 [INSTANCE_CHECK] EventHandlerInitializationService - EventAggregator HashCode: {eventAggregatorHash}");

                var stopTranslationHandler = _serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.EventTypes.StopTranslationRequestEvent>>();
                eventAggregator.Subscribe<Baketa.Core.Events.EventTypes.StopTranslationRequestEvent>(stopTranslationHandler);
                _logger.LogInformation("🛑 StopTranslationRequestHandlerを登録しました - Stop押下後も処理継続問題の修正");
                Console.WriteLine("🛑 [PHASE6.1] StopTranslationRequestHandlerを登録しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ StopTranslationRequestHandlerの登録に失敗しました");
                Console.WriteLine($"❌ [ERROR] StopTranslationRequestHandler登録失敗: {ex.Message}");
            }

            // 🔥 [CRITICAL_FIX] PriorityAwareOcrCompletedHandlerの登録 - 統合翻訳処理実現
            try
            {
                var priorityAwareOcrHandler = _serviceProvider.GetRequiredService<IEventProcessor<OcrCompletedEvent>>();
                eventAggregator.Subscribe<OcrCompletedEvent>(priorityAwareOcrHandler);
                _logger.LogInformation("🔥 PriorityAwareOcrCompletedHandlerを登録しました - 統合翻訳処理実現");
                Console.WriteLine("🔥 [CRITICAL_FIX] PriorityAwareOcrCompletedHandlerを登録しました - 分離表示問題解決");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→✅ [SUCCESS] PriorityAwareOcrCompletedHandler登録完了 - 統合翻訳処理実現{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PriorityAwareOcrCompletedHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] PriorityAwareOcrCompletedHandler登録失敗: {ex.Message}");

                // 確実なファイル記録
                try
                {
                    System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                        $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")}→❌ [ERROR] PriorityAwareOcrCompletedHandler登録失敗: {ex.Message}{Environment.NewLine}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }

            // DiagnosticEventProcessorの登録
            try
            {
                var diagnosticEventProcessor = _serviceProvider.GetRequiredService<IEventProcessor<PipelineDiagnosticEvent>>();
                eventAggregator.Subscribe<PipelineDiagnosticEvent>(diagnosticEventProcessor);
                _logger.LogInformation("DiagnosticEventProcessorを登録しました");
                Console.WriteLine("🔥 [DEBUG] DiagnosticEventProcessorを登録しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DiagnosticEventProcessorの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] DiagnosticEventProcessorの登録失敗: {ex.Message}");
            }

            // 🔧 [Issue #195] ResourceMonitoringEventHandlerの登録 - 未処理イベント警告を解消
            try
            {
                var resourceMonitoringHandler = _serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Abstractions.Events.ResourceMonitoringEvent>>();
                eventAggregator.Subscribe<Baketa.Core.Abstractions.Events.ResourceMonitoringEvent>(resourceMonitoringHandler);
                _logger.LogInformation("ResourceMonitoringEventHandlerを登録しました");
                Console.WriteLine("🔧 [Issue #195] ResourceMonitoringEventHandlerを登録しました");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ResourceMonitoringEventHandlerの登録に失敗しました");
                Console.WriteLine($"🔥 [ERROR] ResourceMonitoringEventHandler登録失敗: {ex.Message}");
            }

            // 🔥 [ISSUE#163] SingleshotEventProcessorの登録はUIModule/TranslationFlowModuleで実施
            // (UI層イベントのためApplication層では登録できない)

            _logger.LogInformation("🔥 イベントハンドラー初期化が完了しました");
            Console.WriteLine("🔥 [DEBUG] イベントハンドラー初期化が完了しました");
            // System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
            //     $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔥 [DEBUG] イベントハンドラー初期化が完了しました{Environment.NewLine}");

            await Task.Delay(1, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨🚨🚨 [INIT_EXCEPTION] EventHandlerInitializationService例外発生！");
            Console.WriteLine($"🚨 [INIT_EXCEPTION] Type: {ex.GetType().FullName}");
            Console.WriteLine($"🚨 [INIT_EXCEPTION] Message: {ex.Message}");
            Console.WriteLine($"🚨 [INIT_EXCEPTION] StackTrace: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"🚨🚨🚨 [INIT_EXCEPTION] EventHandlerInitializationService例外発生！");
            System.Diagnostics.Debug.WriteLine($"🚨 [INIT_EXCEPTION] Type: {ex.GetType().FullName}");
            System.Diagnostics.Debug.WriteLine($"🚨 [INIT_EXCEPTION] Message: {ex.Message}");

            _logger.LogError(ex, "イベントハンドラー初期化中にエラーが発生しました");
            Console.WriteLine($"🔥 [ERROR] イベントハンドラー初期化エラー: {ex.Message}");

            // ファイルにも記録
            try
            {
                System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [INIT_EXCEPTION] {ex.GetType().FullName}: {ex.Message}{Environment.NewLine}");
                System.IO.File.AppendAllText(_loggingSettings.GetFullDebugLogPath(),
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 [INIT_EXCEPTION_STACK] {ex.StackTrace}{Environment.NewLine}");
            }
            catch { /* ファイル出力失敗は無視 */ }

            throw;
        }
    }

}
