#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Services;
using Baketa.Core.Utilities;
using Baketa.UI.ViewModels;
using Baketa.UI.Utils;
using ReactiveUI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Translation;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳フロー統合のイベントプロセッサー
/// UI層とApplication層の翻訳フローを統合
/// </summary>
public class TranslationFlowEventProcessor : 
    IEventProcessor<StartTranslationRequestEvent>,
    IEventProcessor<StopTranslationRequestEvent>,
    IEventProcessor<ToggleTranslationDisplayRequestEvent>,
    IEventProcessor<SettingsChangedEvent>,
    IEventProcessor<LanguageSettingsChangedEvent>
{
    private readonly ILogger<TranslationFlowEventProcessor> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly TranslationResultOverlayManager _overlayManager;
    private readonly IMultiWindowOverlayManager _multiWindowOverlayManager;
    private readonly ICaptureService _captureService;
    private readonly ITranslationOrchestrationService _translationService;
    private readonly ISettingsService _settingsService;
    private readonly IOcrEngine _ocrEngine;
    
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
        IMultiWindowOverlayManager multiWindowOverlayManager,
        ICaptureService captureService,
        ITranslationOrchestrationService translationService,
        ISettingsService settingsService,
        IOcrEngine ocrEngine)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _multiWindowOverlayManager = multiWindowOverlayManager ?? throw new ArgumentNullException(nameof(multiWindowOverlayManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        
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
        Console.WriteLine($"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
        Console.WriteLine($"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
        
        DebugLogUtility.WriteLog($"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
        DebugLogUtility.WriteLog($"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
        DebugLogUtility.WriteLog($"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
        
        // ファイルログで確実に記録
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
        
        _logger.LogInformation("🚀 HandleAsync(StartTranslationRequestEvent) 呼び出し開始: {EventId}", eventData.Id);
        _logger.LogInformation("🎯 ターゲットウィンドウ: {WindowTitle} (Handle={Handle})", 
            eventData.TargetWindow?.Title ?? "null", eventData.TargetWindow?.Handle ?? IntPtr.Zero);
        
        // イベントデータの妥当性チェック
        if (eventData.TargetWindow == null)
        {
            var errorMessage = "ターゲットウィンドウがnullです";
            Console.WriteLine($"❌ {errorMessage}");
            DebugLogUtility.WriteLog($"❌ {errorMessage}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ {errorMessage}");
            _logger.LogError("{ErrorMessage}", errorMessage);
            return;
        }
        
        // 重複処理防止チェック（ウィンドウハンドルベース）
        lock (_processedEventLock)
        {
            _logger.LogInformation("🔍 重複チェック: 現在処理中のウィンドウ数={Count}", _processingWindows.Count);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 重複チェック: 現在処理中のウィンドウ数={_processingWindows.Count}");
            
            if (_processingWindows.Contains(eventData.TargetWindow.Handle))
            {
                _logger.LogWarning("⚠️ 重複処理をスキップ: {WindowTitle} (Handle={Handle})", 
                    eventData.TargetWindow.Title, eventData.TargetWindow.Handle);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ 重複処理をスキップ: {eventData.TargetWindow.Title} (Handle={eventData.TargetWindow.Handle})");
                return;
            }
            _processingWindows.Add(eventData.TargetWindow.Handle);
            _logger.LogInformation("✅ ウィンドウを処理中リストに追加: {Handle}", eventData.TargetWindow.Handle);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ ウィンドウを処理中リストに追加: {eventData.TargetWindow.Handle}");
        }
        
        try
        {
            _logger.LogInformation("Processing translation start request for window: {WindowTitle} (Handle={Handle})", 
                eventData.TargetWindow.Title, eventData.TargetWindow.Handle);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 翻訳開始要求処理開始: {eventData.TargetWindow.Title} (Handle={eventData.TargetWindow.Handle})");

            // 1. 翻訳状態を「キャプチャ中」に変更
            _logger.LogDebug("Changing translation status to capturing");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📊 翻訳状態をキャプチャ中に変更");
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ1a - 翻訳状態変更イベント作成");
                var statusEvent = new TranslationStatusChangedEvent(TranslationStatus.Capturing);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ1b - 翻訳状態変更イベント発行開始");
                await _eventAggregator.PublishAsync(statusEvent).ConfigureAwait(false);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ1c - 翻訳状態変更イベント発行完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 翻訳状態変更イベント発行完了");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [HandleAsync] ステップ1エラー - 翻訳状態変更イベント発行エラー: {ex.Message}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ 翻訳状態変更イベント発行エラー: {ex.Message}");
                _logger.LogError(ex, "翻訳状態変更イベント発行エラー");
            }

            // 2. 古いオーバーレイマネージャーは使用しない（マルチウィンドウシステムに移行）
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ2開始 - 古いオーバーレイスキップ");
            _logger.LogDebug("Skipping old overlay manager - using multi-window system");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⏭️ 古いオーバーレイシステムをスキップ - マルチウィンドウシステムを使用");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ2完了");

            // 3. 実際の翻訳処理を開始
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ3開始 - 翻訳処理準備");
            _logger.LogDebug("Starting translation process");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 実際の翻訳処理開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 [HandleAsync] ProcessTranslationAsync呼び出し直前 - TargetWindow: {eventData.TargetWindow?.Title}");
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ProcessTranslationAsync呼び出し開始");
                await ProcessTranslationAsync(eventData.TargetWindow!).ConfigureAwait(false);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ProcessTranslationAsync呼び出し完了");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 翻訳処理完了");
            }
            catch (Exception ex)
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ 翻訳処理エラー: {ex.Message}");
                _logger.LogError(ex, "翻訳処理エラー");
                throw; // 外側のcatchで処理させる
            }

            _logger.LogInformation("✅ 翻訳開始処理が完了しました");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 翻訳開始処理が完了しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred during translation start processing: {ErrorMessage}", ex.Message);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ 翻訳開始処理で例外発生: {ex.GetType().Name}: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ スタックトレース: {ex.StackTrace}");
            
            // エラー時のみ処理中リストから削除
            lock (_processedEventLock)
            {
                _processingWindows.Remove(eventData.TargetWindow.Handle);
                _logger.LogDebug("Translation processing error cleanup for window handle: {Handle}", eventData.TargetWindow.Handle);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🧹 エラー時のウィンドウハンドル削除: {eventData.TargetWindow.Handle}");
            }
            
            // エラー状態に変更
            try
            {
                var errorEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
                await _eventAggregator.PublishAsync(errorEvent).ConfigureAwait(false);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ エラー状態イベント発行完了");
            }
            catch (Exception eventEx)
            {
                _logger.LogError(eventEx, "エラー状態イベント発行失敗");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ エラー状態イベント発行失敗: {eventEx.Message}");
            }
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

            // 2. マルチウィンドウオーバーレイを非表示（古いオーバーレイは使用しない）
            await _multiWindowOverlayManager.HideAllOverlaysAsync().ConfigureAwait(false);

            // 3. 実際の翻訳停止処理
            await _translationService.StopAutomaticTranslationAsync().ConfigureAwait(false);

            // 4. 継続的翻訳結果購読を停止
            if (_continuousTranslationSubscription != null)
            {
                Console.WriteLine("🛑 継続的翻訳結果購読を停止中...");
                _continuousTranslationSubscription.Dispose();
                _continuousTranslationSubscription = null;
                _logger.LogInformation("継続的翻訳結果購読を停止");
                Console.WriteLine("🛑 継続的翻訳結果購読を停止完了");
            }
            else
            {
                Console.WriteLine("⚠️ 継続的翻訳結果購読がnull - 停止処理スキップ");
            }

            // 5. 処理中ウィンドウリストをクリア - 継続翻訳の再開を許可するため
            lock (_processedEventLock)
            {
                var processingCount = _processingWindows.Count;
                Console.WriteLine($"🧹 処理中ウィンドウリストをクリア中: {processingCount} 個のウィンドウ");
                _processingWindows.Clear();
                _logger.LogInformation("処理中ウィンドウリストをクリア: {Count} 個のウィンドウ", processingCount);
                Console.WriteLine($"🧹 処理中ウィンドウリストクリア完了: {processingCount} 個のウィンドウを解放");
            }

            _logger.LogInformation("✅ 翻訳停止処理が完了しました");
            Console.WriteLine("✅ 継続的翻訳停止完了");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ 継続的翻訳停止完了{Environment.NewLine}");
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
            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] SettingsChangedEvent処理開始");
            _logger.LogInformation("設定変更を適用中");

            // オーバーレイ設定を更新
            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] オーバーレイ透明度設定: {eventData.OverlayOpacity}");
            _overlayManager.SetOpacity(eventData.OverlayOpacity);
            
            // フォントサイズを設定から取得して設定
            var fontSize = _settingsService.GetValue("UI:FontSize", eventData.FontSize);
            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] フォントサイズ設定: {fontSize}");
            _overlayManager.SetFontSize(fontSize);
            
            // フォントサイズに基づいて最大幅を調整
            var maxWidth = fontSize * 25; // フォントサイズの25倍を最大幅とする
            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] オーバーレイ最大幅設定: {maxWidth}");
            _overlayManager.SetMaxWidth(maxWidth);

            // 言語設定が変更された場合は翻訳エンジンを再設定
            if (eventData.SourceLanguage != null && eventData.TargetLanguage != null)
            {
                _logger.LogInformation("言語設定変更: {Source} → {Target}", eventData.SourceLanguage, eventData.TargetLanguage);
                // TODO: 翻訳エンジンの言語ペア更新
                // await _translationService.UpdateLanguagePairAsync(eventData.SourceLanguage, eventData.TargetLanguage);
            }

            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] SettingsChangedEvent処理完了");
            _logger.LogInformation("設定変更が適用されました");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 [TranslationFlowEventProcessor] SettingsChangedEvent処理エラー: {ex.Message}");
            _logger.LogError(ex, "設定変更処理中にエラーが発生しました");
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 実際の翻訳処理を実行
    /// </summary>
    private async Task ProcessTranslationAsync(WindowInfo targetWindow)
    {
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 [ProcessTranslationAsync] 開始 - ウィンドウ: {targetWindow?.Title ?? "null"} (Handle={targetWindow?.Handle ?? IntPtr.Zero})");
        
        if (targetWindow == null)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ [ProcessTranslationAsync] targetWindowがnullです");
            return;
        }
        
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] targetWindow null チェック通過");
        
        try
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] try ブロック開始");
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] _logger.LogInformation呼び出し前");
            _logger.LogInformation("Starting continuous translation process for window: {WindowTitle} (Handle={Handle})", 
                targetWindow.Title, targetWindow.Handle);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] _logger.LogInformation呼び出し後");

            // 1. 翻訳中状態に変更
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ1 - 翻訳中状態変更開始");
            _logger.LogDebug("Changing translation status to translating");
            var translatingEvent = new TranslationStatusChangedEvent(TranslationStatus.Translating);
            await _eventAggregator.PublishAsync(translatingEvent).ConfigureAwait(false);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ1完了 - 翻訳中状態変更完了");

            // 2. 翻訳結果のObservableを購読してUIイベントに変換
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ2 - Observable購読設定開始");
            _logger.LogDebug("Setting up translation result subscription for continuous translation");
            DebugLogUtility.WriteLog("🔗 継続翻訳結果のObservable購読を設定中");
            DebugLogUtility.WriteLog($"🔍 現在の購読状態(設定前): {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] Observable購読オブジェクト作成開始");
            _continuousTranslationSubscription = _translationService.TranslationResults
                .ObserveOn(RxApp.MainThreadScheduler) // UIスレッドスケジューラで実行
                .Subscribe(result => 
                {
                    DebugLogUtility.WriteLog($"📝 継続的翻訳結果受信:");
                    DebugLogUtility.WriteLog($"   📖 オリジナル: '{result.OriginalText}'");
                    DebugLogUtility.WriteLog($"   🌐 翻訳結果: '{result.TranslatedText}'");
                    DebugLogUtility.WriteLog($"   📊 信頼度: {result.Confidence}");
                    DebugLogUtility.WriteLog($"   🎯 座標ベースモード: {result.IsCoordinateBasedMode}");
                    
                    Console.WriteLine($"📝 [TranslationFlowEventProcessor] 継続的翻訳結果受信:");
                    Console.WriteLine($"   📖 オリジナル: '{result.OriginalText}'");
                    Console.WriteLine($"   🌐 翻訳結果: '{result.TranslatedText}'");
                    Console.WriteLine($"   🎯 座標ベースモード: {result.IsCoordinateBasedMode}");
                    
                    _logger.LogInformation("Continuous translation result: '{Original}' -> '{Translated}' (confidence: {Confidence}, coordinateMode: {CoordinateMode})", 
                        result.OriginalText, result.TranslatedText, result.Confidence, result.IsCoordinateBasedMode);
                        
                    // 座標ベース翻訳の場合は既にオーバーレイで表示されているためスキップ
                    if (result.IsCoordinateBasedMode)
                    {
                        DebugLogUtility.WriteLog($"🎯 座標ベース翻訳モードのため、既にAR表示済み - フォールバック表示をスキップ");
                        _logger.LogDebug("座標ベース翻訳結果は既に表示済み - フォールバック表示をスキップ");
                        return;
                    }
                    
                    // 従来モードの場合のみフォールバック表示を実行
                    DebugLogUtility.WriteLog($"📄 従来翻訳モード - フォールバック表示を実行");
                    
                    // フォールバック: 簡易TextChunkを作成（従来システム用）
                    var textChunk = new Baketa.Core.Abstractions.Translation.TextChunk
                    {
                        ChunkId = result.GetHashCode(),
                        TextResults = [],
                        CombinedBounds = new System.Drawing.Rectangle(100, 200, 300, 50), // 仮の座標（従来システム用）
                        CombinedText = result.OriginalText,
                        TranslatedText = result.TranslatedText,
                        SourceWindowHandle = targetWindow.Handle,
                        DetectedLanguage = result.DetectedLanguage ?? "ja"
                    };
                    
                    var textChunks = new List<Baketa.Core.Abstractions.Translation.TextChunk> { textChunk };
                    DebugLogUtility.WriteLog($"🔍 フォールバックTextChunk作成: '{result.OriginalText}' -> '{result.TranslatedText}'");

                    // 非同期でマルチウィンドウオーバーレイに表示
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _multiWindowOverlayManager.DisplayTranslationResultsAsync(textChunks).ConfigureAwait(false);
                            DebugLogUtility.WriteLog("✅ 継続的翻訳結果マルチウィンドウ表示完了");
                            
                            // 翻訳結果が表示されたため、進行中のOCRタイムアウト処理をキャンセル
                            try
                            {
                                _ocrEngine.CancelCurrentOcrTimeout();
                            }
                            catch (Exception cancelEx)
                            {
                                DebugLogUtility.WriteLog($"⚠️ OCRタイムアウトキャンセル試行中にエラー: {cancelEx.Message}");
                            }
                            
                            _logger.LogDebug("Continuous translation result displayed via multi-window overlay");
                        }
                        catch (Exception eventEx)
                        {
                            DebugLogUtility.WriteLog($"❌ マルチウィンドウオーバーレイ表示エラー: {eventEx.Message}");
                            _logger.LogError(eventEx, "Failed to display translation result via multi-window overlay");
                        }
                    });
                });

            // 3. 継続的翻訳を開始
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ3 - TranslationService呼び出し開始");
            _logger.LogDebug("Starting continuous automatic translation");
            DebugLogUtility.WriteLog("🏁 TranslationService.StartAutomaticTranslationAsync呼び出し中...");
            DebugLogUtility.WriteLog($"   🔍 サービス状態: {(_translationService != null ? "利用可能" : "null")}");
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ProcessTranslationAsync] _translationService の実際の型: {_translationService?.GetType()?.FullName ?? "null"}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync呼び出し直前");
            await _translationService!.StartAutomaticTranslationAsync(targetWindow.Handle).ConfigureAwait(false);
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync呼び出し完了");
            DebugLogUtility.WriteLog("🏁 TranslationService.StartAutomaticTranslationAsync完了");
            DebugLogUtility.WriteLog($"   🔍 自動翻訳アクティブ: {_translationService.IsAutomaticTranslationActive}");

            _logger.LogInformation("✅ Continuous translation started successfully for window: {WindowTitle}", targetWindow.Title);
            DebugLogUtility.WriteLog($"✅ 継続的翻訳開始完了: ウィンドウ '{targetWindow.Title}' (Handle={targetWindow.Handle})");
            DebugLogUtility.WriteLog($"🔍 購読状態(終了時): {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
            // System.IO.File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ 継続的翻訳開始: ウィンドウ '{targetWindow.Title}' (Handle={targetWindow.Handle}){Environment.NewLine}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] try ブロック正常終了");
        }
        catch (Exception ex)
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [ProcessTranslationAsync] 例外発生: {ex.GetType().Name}: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [ProcessTranslationAsync] スタックトレース: {ex.StackTrace}");
            
            _logger.LogError(ex, "Error occurred during translation processing: {ErrorMessage}", ex.Message);
            await DisplayErrorMessageAsync(ex).ConfigureAwait(false);
        }
        
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] メソッド終了");
    }

    /// <summary>
    /// フォールバック翻訳結果を表示（キャプチャ失敗時）
    /// </summary>
    private async Task DisplayFallbackTranslationAsync()
    {
        Console.WriteLine("💥 フォールバック翻訳結果を表示（マルチウィンドウオーバーレイ使用）");
        
        // マルチウィンドウオーバーレイでエラーメッセージを表示
        var fallbackChunk = new Baketa.Core.Abstractions.Translation.TextChunk
        {
            ChunkId = "fallback".GetHashCode(),
            TextResults = [],
            CombinedBounds = new System.Drawing.Rectangle(100, 200, 300, 50),
            CombinedText = "(キャプチャ失敗)",
            TranslatedText = "ウィンドウキャプチャに失敗しました",
            SourceWindowHandle = IntPtr.Zero,
            DetectedLanguage = "ja"
        };

        await _multiWindowOverlayManager.DisplayTranslationResultsAsync([fallbackChunk]).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// テキスト未検出メッセージを表示
    /// </summary>
    private async Task DisplayNoTextFoundMessageAsync()
    {
        Console.WriteLine("🔍 テキスト未検出メッセージを表示（マルチウィンドウオーバーレイ使用）");
        
        // マルチウィンドウオーバーレイでテキスト未検出メッセージを表示
        var noTextChunk = new Baketa.Core.Abstractions.Translation.TextChunk
        {
            ChunkId = "no-text".GetHashCode(),
            TextResults = [],
            CombinedBounds = new System.Drawing.Rectangle(100, 200, 400, 50),
            CombinedText = "(テキスト未検出)",
            TranslatedText = "翻訳対象のテキストが見つかりませんでした",
            SourceWindowHandle = IntPtr.Zero,
            DetectedLanguage = "ja"
        };

        await _multiWindowOverlayManager.DisplayTranslationResultsAsync([noTextChunk]).ConfigureAwait(false);

        var completedEvent = new TranslationStatusChangedEvent(TranslationStatus.Completed);
        await _eventAggregator.PublishAsync(completedEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// エラーメッセージを表示
    /// </summary>
    private async Task DisplayErrorMessageAsync(Exception exception)
    {
        // マルチウィンドウオーバーレイでエラーメッセージを表示
        var errorChunk = new Baketa.Core.Abstractions.Translation.TextChunk
        {
            ChunkId = "error".GetHashCode(),
            TextResults = [],
            CombinedBounds = new System.Drawing.Rectangle(100, 200, 500, 50),
            CombinedText = "(エラー)",
            TranslatedText = $"翻訳処理中にエラーが発生しました: {exception.Message}",
            SourceWindowHandle = IntPtr.Zero,
            DetectedLanguage = "ja"
        };

        await _multiWindowOverlayManager.DisplayTranslationResultsAsync([errorChunk]).ConfigureAwait(false);

        var errorStatusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
        await _eventAggregator.PublishAsync(errorStatusEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// 言語設定変更イベントの処理
    /// </summary>
    public async Task HandleAsync(LanguageSettingsChangedEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        try
        {
            _logger.LogInformation("言語設定変更イベントを処理します: {TranslationLanguage}", eventData.TranslationLanguage);

            // 翻訳言語ペアを設定サービスに保存
            if (!string.IsNullOrEmpty(eventData.TranslationLanguage))
            {
                var parts = eventData.TranslationLanguage.Split('→');
                if (parts.Length == 2)
                {
                    var sourceLanguage = parts[0].Trim();
                    var targetLanguage = parts[1].Trim();
                    
                    // 言語コードに変換（表示名から言語コードへ）
                    var sourceCode = ConvertDisplayNameToLanguageCode(sourceLanguage);
                    var targetCode = ConvertDisplayNameToLanguageCode(targetLanguage);
                    
                    _logger.LogInformation("言語設定を保存: {Source} → {Target}", sourceCode, targetCode);
                    
                    // 設定サービスに保存
                    _settingsService.SetValue("Translation:Languages:DefaultSourceLanguage", sourceCode);
                    _settingsService.SetValue("Translation:Languages:DefaultTargetLanguage", targetCode);
                    
                    _logger.LogInformation("言語設定の保存が完了しました");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "言語設定変更イベントの処理中にエラーが発生しました");
        }
        
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <summary>
    /// 表示名から言語コードに変換
    /// </summary>
    private static string ConvertDisplayNameToLanguageCode(string displayName)
    {
        return displayName switch
        {
            "日本語" or "Japanese" => "ja",
            "英語" or "English" => "en",
            "中国語" or "Chinese" => "zh",
            _ => "en" // デフォルト
        };
    }
}
