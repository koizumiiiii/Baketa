using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳フロー統合のイベントプロセッサー
/// UI層とApplication層の翻訳フローを統合
/// </summary>
public class TranslationFlowEventProcessor : 
    IEventProcessor<StartTranslationRequestEvent>,
    IEventProcessor<StopTranslationRequestEvent>,
    IEventProcessor<ToggleTranslationDisplayRequestEvent>,
    IEventProcessor<SettingsChangedEvent>
{
    private readonly ILogger<TranslationFlowEventProcessor> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly TranslationResultOverlayManager _overlayManager;
    private readonly ICaptureService _captureService;
    private readonly ITranslationOrchestrationService _translationService;
    
    // 重複処理防止用
    private readonly HashSet<string> _processedEventIds = [];
    private readonly HashSet<IntPtr> _processingWindows = [];
    private readonly object _processedEventLock = new();
    
    // 継続的翻訳結果購読管理
    private IDisposable? _continuousTranslationSubscription;
    

    public TranslationFlowEventProcessor(
        ILogger<TranslationFlowEventProcessor> logger,
        IEventAggregator eventAggregator,
        TranslationResultOverlayManager overlayManager,
        ICaptureService captureService,
        ITranslationOrchestrationService translationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        
        _logger.LogDebug("TranslationFlowEventProcessor instance created: Hash={Hash}", GetHashCode());
    }

    public int Priority => 100;
    public bool SynchronousExecution => false;

    /// <summary>
    /// 翻訳開始要求イベントの処理
    /// </summary>
    public async Task HandleAsync(StartTranslationRequestEvent eventData)
    {
        Console.WriteLine($"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}{Environment.NewLine}");
        
        _logger.LogInformation("🚀 HandleAsync(StartTranslationRequestEvent) 呼び出し開始: {EventId}", eventData.Id);
        _logger.LogInformation("🎯 ターゲットウィンドウ: {WindowTitle} (Handle={Handle})", 
            eventData.TargetWindow.Title, eventData.TargetWindow.Handle);
        
        // 重複処理防止チェック（ウィンドウハンドルベース）
        lock (_processedEventLock)
        {
            _logger.LogInformation("🔍 重複チェック: 現在処理中のウィンドウ数={Count}", _processingWindows.Count);
            if (_processingWindows.Contains(eventData.TargetWindow.Handle))
            {
                _logger.LogWarning("⚠️ 重複処理をスキップ: {WindowTitle} (Handle={Handle})", 
                    eventData.TargetWindow.Title, eventData.TargetWindow.Handle);
                return;
            }
            _processingWindows.Add(eventData.TargetWindow.Handle);
            _logger.LogInformation("✅ ウィンドウを処理中リストに追加: {Handle}", eventData.TargetWindow.Handle);
        }
        
        try
        {
            _logger.LogInformation("Processing translation start request for window: {WindowTitle} (Handle={Handle})", 
                eventData.TargetWindow.Title, eventData.TargetWindow.Handle);

            // 1. 翻訳状態を「キャプチャ中」に変更
            _logger.LogDebug("Changing translation status to capturing");
            var statusEvent = new TranslationStatusChangedEvent(TranslationStatus.Capturing);
            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            // 2. オーバーレイマネージャーを初期化
            _logger.LogDebug("Initializing overlay manager");
            await _overlayManager.InitializeAsync().ConfigureAwait(false);

            // 3. 実際の翻訳処理を開始
            _logger.LogDebug("Starting translation process");
            await ProcessTranslationAsync(eventData.TargetWindow).ConfigureAwait(false);

            _logger.LogInformation("✅ 翻訳開始処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during translation start processing: {ErrorMessage}", ex.Message);
            
            // エラー時のみ処理中リストから削除
            lock (_processedEventLock)
            {
                _processingWindows.Remove(eventData.TargetWindow.Handle);
                _logger.LogDebug("Translation processing error cleanup for window handle: {Handle}", eventData.TargetWindow.Handle);
            }
            
            // エラー状態に変更
            var errorEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
            await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
        }
        // 注意: finallyブロックを削除 - 継続的翻訳では処理中状態をStop時まで維持
    }

    /// <summary>
    /// 翻訳停止要求イベントの処理
    /// </summary>
    public async Task HandleAsync(StopTranslationRequestEvent eventData)
    {
        try
        {
            _logger.LogInformation("翻訳停止要求を処理中");

            // 1. 翻訳状態を「待機中」に変更
            var statusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
            await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);

            // 2. オーバーレイを非表示
            await _overlayManager.HideAsync().ConfigureAwait(false);

            // 3. 実際の翻訳停止処理
            await _translationService.StopAutomaticTranslationAsync().ConfigureAwait(false);

            // 4. 継続的翻訳結果購読を停止
            if (_continuousTranslationSubscription != null)
            {
                _continuousTranslationSubscription.Dispose();
                _continuousTranslationSubscription = null;
                _logger.LogInformation("継続的翻訳結果購読を停止");
                Console.WriteLine("🛑 継続的翻訳結果購読を停止");
                System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🛑 継続的翻訳結果購読を停止{Environment.NewLine}");
            }

            // 5. 処理中ウィンドウリストをクリア
            lock (_processedEventLock)
            {
                var processingCount = _processingWindows.Count;
                _processingWindows.Clear();
                _logger.LogInformation("処理中ウィンドウリストをクリア: {Count} 個のウィンドウ", processingCount);
                Console.WriteLine($"🧹 処理中ウィンドウリストをクリア: {processingCount} 個のウィンドウ");
                System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🧹 処理中ウィンドウリストをクリア: {processingCount} 個のウィンドウ{Environment.NewLine}");
            }

            _logger.LogInformation("✅ 翻訳停止処理が完了しました");
            Console.WriteLine("✅ 継続的翻訳停止完了");
            System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ 継続的翻訳停止完了{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳停止処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 翻訳表示切り替え要求イベントの処理
    /// </summary>
    public async Task HandleAsync(ToggleTranslationDisplayRequestEvent eventData)
    {
        try
        {
            _logger.LogDebug("翻訳表示切り替え要求を処理中: IsVisible={IsVisible}", eventData.IsVisible);

            // オーバーレイの表示/非表示を切り替え
            if (eventData.IsVisible)
            {
                await _overlayManager.ShowAsync().ConfigureAwait(false);
            }
            else
            {
                await _overlayManager.HideAsync().ConfigureAwait(false);
            }

            // 表示状態変更イベントを発行
            var visibilityEvent = new TranslationDisplayVisibilityChangedEvent(eventData.IsVisible);
            await _eventAggregator.PublishAsync(visibilityEvent).ConfigureAwait(false);

            _logger.LogDebug("翻訳表示切り替えが完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "翻訳表示切り替え処理中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 設定変更イベントの処理
    /// </summary>
    public async Task HandleAsync(SettingsChangedEvent eventData)
    {
        try
        {
            _logger.LogInformation("設定変更を適用中");

            // オーバーレイ設定を更新
            _overlayManager.SetOpacity(eventData.OverlayOpacity);
            
            // フォントサイズに基づいて最大幅を調整
            var maxWidth = eventData.FontSize * 25; // フォントサイズの25倍を最大幅とする
            _overlayManager.SetMaxWidth(maxWidth);

            // TODO: Application層の設定サービスと統合
            // var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
            // await settingsService.UpdateSettingsAsync(eventData);

            _logger.LogInformation("設定変更が適用されました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "設定変更処理中にエラーが発生しました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 実際の翻訳処理を実行
    /// </summary>
    private async Task ProcessTranslationAsync(WindowInfo targetWindow)
    {
        try
        {
            _logger.LogInformation("Starting continuous translation process for window: {WindowTitle} (Handle={Handle})", 
                targetWindow.Title, targetWindow.Handle);

            // 1. 翻訳中状態に変更
            _logger.LogDebug("Changing translation status to translating");
            var translatingEvent = new TranslationStatusChangedEvent(TranslationStatus.Translating);
            await _eventAggregator.PublishAsync(translatingEvent).ConfigureAwait(false);

            // 2. 翻訳結果のObservableを購読してUIイベントに変換
            _logger.LogDebug("Setting up translation result subscription for continuous translation");
            _continuousTranslationSubscription = _translationService.TranslationResults
                .Subscribe(result => 
                {
                    Console.WriteLine($"📝 継続的翻訳結果受信:");
                    Console.WriteLine($"   📖 オリジナル: '{result.OriginalText}'");
                    Console.WriteLine($"   🌐 翻訳結果: '{result.TranslatedText}'");
                    Console.WriteLine($"   📊 信頼度: {result.Confidence}");
                    Console.WriteLine($"   📍 表示位置: (100, 200)");
                    
                    System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📝 継続的翻訳結果受信:{Environment.NewLine}");
                    System.IO.File.AppendAllText("debug_app_logs.txt", $"   📖 オリジナル: '{result.OriginalText}'{Environment.NewLine}");
                    System.IO.File.AppendAllText("debug_app_logs.txt", $"   🌐 翻訳結果: '{result.TranslatedText}'{Environment.NewLine}");
                    System.IO.File.AppendAllText("debug_app_logs.txt", $"   📊 信頼度: {result.Confidence}{Environment.NewLine}");
                    System.IO.File.AppendAllText("debug_app_logs.txt", $"   📍 表示位置: (100, 200){Environment.NewLine}");
                    
                    _logger.LogInformation("Continuous translation result: '{Original}' -> '{Translated}' (confidence: {Confidence})", 
                        result.OriginalText, result.TranslatedText, result.Confidence);
                        
                    var displayEvent = new TranslationResultDisplayEvent
                    {
                        OriginalText = result.OriginalText,
                        TranslatedText = result.TranslatedText,
                        DetectedPosition = new System.Drawing.Point(100, 200) // 固定位置
                    };

                    // 非同期でイベントを発行（Subscribeコールバック内なのでConfigureAwait不要）
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _eventAggregator.PublishAsync(displayEvent);
                            Console.WriteLine("✅ 継続的翻訳結果表示イベント発行完了");
                            System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ 継続的翻訳結果表示イベント発行完了{Environment.NewLine}");
                            _logger.LogDebug("Continuous translation result display event published");
                        }
                        catch (Exception eventEx)
                        {
                            _logger.LogError(eventEx, "Failed to publish continuous translation display event");
                        }
                    });
                });

            // 3. 継続的翻訳を開始
            _logger.LogDebug("Starting continuous automatic translation");
            await _translationService.StartAutomaticTranslationAsync().ConfigureAwait(false);

            _logger.LogInformation("✅ Continuous translation started successfully for window: {WindowTitle}", targetWindow.Title);
            Console.WriteLine($"✅ 継続的翻訳開始: ウィンドウ '{targetWindow.Title}' (Handle={targetWindow.Handle})");
            System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ 継続的翻訳開始: ウィンドウ '{targetWindow.Title}' (Handle={targetWindow.Handle}){Environment.NewLine}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during translation processing: {ErrorMessage}", ex.Message);
            await DisplayErrorMessageAsync(ex).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// フォールバック翻訳結果を表示（キャプチャ失敗時）
    /// </summary>
    private async Task DisplayFallbackTranslationAsync()
    {
        Console.WriteLine("💥 フォールバック翻訳結果を表示:");
        Console.WriteLine("   📖 オリジナル: '(キャプチャ失敗)'");
        Console.WriteLine("   🌐 翻訳結果: 'ウィンドウキャプチャに失敗しました'");
        Console.WriteLine("   📍 表示位置: (100, 200)");
        
        System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 フォールバック翻訳結果を表示:{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   📖 オリジナル: '(キャプチャ失敗)'{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   🌐 翻訳結果: 'ウィンドウキャプチャに失敗しました'{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   📍 表示位置: (100, 200){Environment.NewLine}");
        
        var fallbackEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(キャプチャ失敗)",
            TranslatedText = "ウィンドウキャプチャに失敗しました",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(fallbackEvent).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// テキスト未検出メッセージを表示
    /// </summary>
    private async Task DisplayNoTextFoundMessageAsync()
    {
        Console.WriteLine("🔍 テキスト未検出メッセージを表示:");
        Console.WriteLine("   📖 オリジナル: '(テキスト未検出)'");
        Console.WriteLine("   🌐 翻訳結果: '翻訳対象のテキストが見つかりませんでした'");
        Console.WriteLine("   📍 表示位置: (100, 200)");
        
        System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 テキスト未検出メッセージを表示:{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   📖 オリジナル: '(テキスト未検出)'{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   🌐 翻訳結果: '翻訳対象のテキストが見つかりませんでした'{Environment.NewLine}");
        System.IO.File.AppendAllText("debug_app_logs.txt", $"   📍 表示位置: (100, 200){Environment.NewLine}");
        
        var noTextEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(テキスト未検出)",
            TranslatedText = "翻訳対象のテキストが見つかりませんでした",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(noTextEvent).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    private async Task DisplayErrorMessageAsync(Exception exception)
    {
        var errorEvent = new TranslationResultDisplayEvent
        {
            OriginalText = "(エラー)",
            TranslatedText = $"翻訳処理中にエラーが発生しました: {exception.Message}",
            DetectedPosition = new System.Drawing.Point(100, 200)
        };

        await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);

        var errorStatusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
        await _eventAggregator.PublishAsync(errorStatusEvent).ConfigureAwait(false);
    }
}
