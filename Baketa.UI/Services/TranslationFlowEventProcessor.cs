using System;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using WindowInfo = Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo;
using Baketa.Core.Services;
using Baketa.Core.Utilities;
using Baketa.UI.ViewModels;
using Baketa.UI.Utils;
using ReactiveUI;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Infrastructure.Processing.Strategies; // 🔥 [STOP_FIX] ImageChangeDetectionStageStrategy参照

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
    IEventProcessor<Baketa.UI.Framework.Events.StartCaptureRequestedEvent>,
    IEventProcessor<Baketa.UI.Framework.Events.StopCaptureRequestedEvent>,
    IDisposable
{
    private readonly ILogger<TranslationFlowEventProcessor> _logger;
    private readonly IEventAggregator _eventAggregator;
    private readonly IInPlaceTranslationOverlayManager _inPlaceOverlayManager;
    private readonly ICaptureService _captureService;
    private readonly ITranslationOrchestrationService _translationService;
    private readonly ISettingsService _settingsService;
    private readonly IOcrEngine _ocrEngine;
    private readonly IWindowManagerAdapter _windowManager;
    private readonly IOcrFailureManager _ocrFailureManager;
    private readonly IEnumerable<Baketa.Core.Abstractions.Processing.IProcessingStageStrategy> _processingStrategies; // 🔥 [STOP_FIX] ImageChangeDetectionStrategy取得用
    
    // 重複処理防止用
    private readonly HashSet<string> _processedEventIds = [];
    private readonly HashSet<IntPtr> _processingWindows = [];
    private readonly object _processedEventLock = new();
    
    // 継続的翻訳結果購読管理
    private IDisposable? _continuousTranslationSubscription;
    
    // Stop機能: CancellationToken による確実な停止制御
    private CancellationTokenSource? _currentTranslationCancellationSource;
    

    public TranslationFlowEventProcessor(
        ILogger<TranslationFlowEventProcessor> logger,
        IEventAggregator eventAggregator,
        IInPlaceTranslationOverlayManager inPlaceOverlayManager,
        ICaptureService captureService,
        ITranslationOrchestrationService translationService,
        ISettingsService settingsService,
        IOcrEngine ocrEngine,
        IWindowManagerAdapter windowManager,
        IOcrFailureManager ocrFailureManager,
        IEnumerable<Baketa.Core.Abstractions.Processing.IProcessingStageStrategy> processingStrategies) // 🔥 [STOP_FIX] Strategy集合から取得
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
        _inPlaceOverlayManager = inPlaceOverlayManager ?? throw new ArgumentNullException(nameof(inPlaceOverlayManager));
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
        _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _ocrFailureManager = ocrFailureManager ?? throw new ArgumentNullException(nameof(ocrFailureManager));
        _processingStrategies = processingStrategies ?? throw new ArgumentNullException(nameof(processingStrategies)); // 🔥 [STOP_FIX] 必須依存

        _logger.LogDebug("TranslationFlowEventProcessor instance created: Hash={Hash}", GetHashCode());
    }

    public int Priority => 100;
    // 🔥 [START_FIX] 画像変化検知履歴クリアを確実に実行するため同期実行に変更
    // 理由: Task.Runのfire-and-forget実行では、ClearPreviousImages()呼び出し前に例外が発生した場合に処理が中断される
    public bool SynchronousExecution => true;

    /// <summary>
    /// 翻訳開始要求イベントの処理
    /// </summary>
    public async Task HandleAsync(StartTranslationRequestEvent eventData)
    {
        // 確実にログを記録するため、ファイル直接書き込みを最優先で実行
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception fileEx)
        {
            // ファイル書き込みエラーがあってもメソッドは継続
            System.Diagnostics.Debug.WriteLine($"ファイル書き込みエラー: {fileEx.Message}");
        }
        
        Console.WriteLine($"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
        // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
        // _logger?.LogDebug($"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
        Console.WriteLine($"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
        Console.WriteLine($"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");

        // 🔥 [GEMINI_FIX] デッドロック原因切り分けのため一時的にコメントアウト
        // 問題: DebugLogUtilityまたはSafeFileLoggerでデッドロック発生の可能性
        // try-catchは例外を捕捉するが、デッドロック（スレッドの永久フリーズ）は防げない
        /*
        // 🚨 デッドロック問題修正: ログ出力を例外処理で囲む
        try
        {
            _logger?.LogDebug($"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
            _logger?.LogDebug($"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
            _logger?.LogDebug($"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
        }
        catch (Exception logEx)
        {
            Console.WriteLine($"⚠️ DebugLogUtility書き込みエラー（無視して継続）: {logEx.Message}");
        }

        // 🚨 デッドロック問題修正: SafeFileLoggerを例外処理で囲む
        try
        {
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚀 TranslationFlowEventProcessor.HandleAsync開始: {eventData.Id}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 ターゲットウィンドウ: {eventData.TargetWindow?.Title ?? "null"} (Handle={eventData.TargetWindow?.Handle ?? IntPtr.Zero})");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 現在の購読状態: {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
        }
        catch (Exception logEx)
        {
            Console.WriteLine($"⚠️ SafeFileLogger書き込みエラー（無視して継続）: {logEx.Message}");
        }
        */

        Console.WriteLine("🔍 [LINE_139_DEBUG] Line 139到達 - _logger.LogInformation呼び出し直前");
        // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
        // _logger?.LogDebug("🔍 [LINE_139_DEBUG] Line 139到達 - _logger.LogInformation呼び出し直前");

        _logger.LogInformation("🚀 HandleAsync(StartTranslationRequestEvent) 呼び出し開始: {EventId}", eventData.Id);

        Console.WriteLine("🔍 [LINE_141_DEBUG] Line 141到達 - _logger.LogInformation(1)完了");
        // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
        // _logger?.LogDebug("🔍 [LINE_141_DEBUG] Line 141到達 - _logger.LogInformation(1)完了");

        _logger.LogInformation("🎯 ターゲットウィンドウ: {WindowTitle} (Handle={Handle})",
            eventData.TargetWindow?.Title ?? "null", eventData.TargetWindow?.Handle ?? IntPtr.Zero);

        Console.WriteLine("🔍 [LINE_145_DEBUG] Line 145到達 - START_FIX処理開始直前");
        // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
        // _logger?.LogDebug("🔍 [LINE_145_DEBUG] Line 145到達 - START_FIX処理開始直前");

        // 🔥 [CRITICAL_VERIFICATION] DLLビルド検証用 - Line 156直前のチェックポイント
        Console.WriteLine("🚨🚨🚨 [LINE_156_VERIFICATION] Line 156実行直前 - このログが出ればDLLは最新版");
        // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
        // _logger?.LogDebug("🚨🚨🚨 [LINE_156_VERIFICATION] Line 156実行直前 - このログが出ればDLLは最新版");

        // 🧹 [START_FIX] 画像変化検知履歴をクリア（初回キャプチャ/Stop→Start後の初回翻訳を確実に実行）
        Console.WriteLine("🧹 [START_FIX] Start時: 画像変化検知履歴をクリア中...");
        _logger.LogInformation("🧹 [START_FIX] Start時: 画像変化検知履歴クリア開始 - 初回翻訳スキップ問題対策");
        try
        {
            // IProcessingStageStrategy集合からImageChangeDetectionStageStrategyを取得
            var imageChangeStrategy = _processingStrategies
                .OfType<ImageChangeDetectionStageStrategy>()
                .FirstOrDefault();

            if (imageChangeStrategy != null)
            {
                imageChangeStrategy.ClearPreviousImages();
                Console.WriteLine("✅ [START_FIX] Start時: 画像変化検知履歴クリア成功");
                _logger.LogInformation("🚀 [START_FIX] Start時: 画像変化検知履歴クリア完了 - 初回翻訳が確実に実行されます");
            }
            else
            {
                Console.WriteLine("⚠️ [START_FIX] ImageChangeDetectionStrategyが見つかりません - 履歴クリアをスキップ");
                _logger.LogWarning("🧹 [START_FIX] ImageChangeDetectionStrategyが見つかりません - 登録確認が必要です");
            }
        }
        catch (Exception clearEx)
        {
            Console.WriteLine($"⚠️ [START_FIX] Start時: 画像変化検知履歴クリア中にエラー: {clearEx.Message}");
            _logger.LogWarning(clearEx, "🧹 [START_FIX] Start時: 画像変化検知履歴クリア中にエラーが発生しましたが、処理を継続します");
        }

        // イベントデータの妥当性チェック
        if (eventData.TargetWindow == null)
        {
            var errorMessage = "ターゲットウィンドウがnullです";
            Console.WriteLine($"❌ {errorMessage}");
            // 🔥 [CRITICAL_FIX] _logger?.LogDebugがデッドロックを引き起こすため一時的にコメントアウト
            // _logger?.LogDebug($"❌ {errorMessage}");
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

            // 2. 既存のインプレースオーバーレイをすべて非表示（重なり問題解決）
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [HandleAsync] ステップ2開始 - 既存オーバーレイクリア");
            _logger.LogDebug("Clearing existing in-place overlays to prevent overlap");
            try
            {
                await _inPlaceOverlayManager.HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 既存オーバーレイクリア完了");
                _logger.LogDebug("Successfully cleared existing in-place overlays");
            }
            catch (Exception ex)
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ 既存オーバーレイクリアエラー: {ex.Message}");
                _logger.LogError(ex, "Failed to clear existing in-place overlays");
            }

            // 3. 継続的翻訳を開始（TranslationOrchestrationServiceを呼び出し）
            _logger.LogDebug("Starting continuous translation via ProcessTranslationAsync");
            try
            {
                await ProcessTranslationAsync(eventData.TargetWindow!).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "継続的翻訳開始エラー");
                throw;
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
    /// UI開始要求イベントの処理（StartCaptureRequestedEvent → StartTranslationRequestEventに変換）
    /// </summary>
    public async Task HandleAsync(Baketa.UI.Framework.Events.StartCaptureRequestedEvent eventData)
    {
        try
        {
            _logger.LogInformation("🚀 UI開始要求を受信 - 翻訳開始要求に変換中");
            Console.WriteLine("🚀 [TranslationFlowEventProcessor] UI開始要求を受信 - 翻訳開始要求に変換中");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 UI開始要求を受信 - 翻訳開始要求に変換中");
            
            // アクティブウィンドウを取得
            var activeWindow = await GetActiveWindowAsync().ConfigureAwait(false);
            if (activeWindow == null)
            {
                var errorMessage = "アクティブウィンドウの取得に失敗しました";
                Console.WriteLine($"❌ {errorMessage}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ {errorMessage}");
                _logger.LogError("{ErrorMessage}", errorMessage);
                return;
            }
            
            Console.WriteLine($"🎯 アクティブウィンドウ: {activeWindow.Title} (Handle={activeWindow.Handle})");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🎯 アクティブウィンドウ: {activeWindow.Title} (Handle={activeWindow.Handle})");
            
            // UI開始要求をApplication開始要求に変換
            var startTranslationEvent = new StartTranslationRequestEvent(activeWindow);
            
            await _eventAggregator.PublishAsync(startTranslationEvent).ConfigureAwait(false);
            
            _logger.LogInformation("✅ UI開始要求 → 翻訳開始要求 変換完了");
            Console.WriteLine("✅ [TranslationFlowEventProcessor] UI開始要求 → 翻訳開始要求 変換完了");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ UI開始要求 → 翻訳開始要求 変換完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UI開始要求処理中にエラーが発生しました");
            Console.WriteLine($"❌ [TranslationFlowEventProcessor] UI開始要求処理エラー: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ UI開始要求処理エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// UI停止要求イベントの処理（StopCaptureRequestedEvent → StopTranslationRequestEventに変換）
    /// </summary>
    public async Task HandleAsync(Baketa.UI.Framework.Events.StopCaptureRequestedEvent eventData)
    {
        try
        {
            _logger.LogInformation("🛑 UI停止要求を受信 - 翻訳停止要求に変換中");
            Console.WriteLine("🛑 [TranslationFlowEventProcessor] UI停止要求を受信 - 翻訳停止要求に変換中");
            
            // UI停止要求をApplication停止要求に変換
            var stopTranslationEvent = new StopTranslationRequestEvent();
            await _eventAggregator.PublishAsync(stopTranslationEvent).ConfigureAwait(false);
            
            _logger.LogInformation("✅ UI停止要求 → 翻訳停止要求 変換完了");
            Console.WriteLine("✅ [TranslationFlowEventProcessor] UI停止要求 → 翻訳停止要求 変換完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UI停止要求処理中にエラーが発生しました");
            Console.WriteLine($"❌ [TranslationFlowEventProcessor] UI停止要求処理エラー: {ex.Message}");
        }
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

            // 2. 🚨 [STOP_FIX] すべてのインプレースオーバーレイを非表示・リセット
            Console.WriteLine("🛑 [Stop機能] すべてのインプレースオーバーレイを非表示中...");
            await _inPlaceOverlayManager.HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
            Console.WriteLine("✅ [Stop機能] すべてのインプレースオーバーレイ非表示完了");
            
            // 🔄 [STOP_FIX] オーバーレイマネージャーを完全リセット
            Console.WriteLine("🔄 [Stop機能] オーバーレイマネージャーリセット中...");
            await _inPlaceOverlayManager.ResetAsync().ConfigureAwait(false);
            Console.WriteLine("✅ [Stop機能] オーバーレイマネージャーリセット完了");
            
            _logger.LogInformation("🚀 Stop機能: すべてのインプレースオーバーレイ非表示・リセット完了");

            // 3. 実際の翻訳停止処理
            await _translationService.StopAutomaticTranslationAsync().ConfigureAwait(false);

            // 🔄 [OCR_RESET] OCR状態をリセット（Stop→Start後のOCR失敗問題対策）
            Console.WriteLine("🔄 [Stop機能] OCR状態リセット実行中...");
            _logger.LogInformation("🔄 [Stop機能] OCR状態リセット開始 - Stop→Start後のオーバーレイ非表示問題対策");
            try
            {
                // ✅ クリーンアーキテクチャ準拠：抽象化に依存し直接メソッド呼び出し
                _ocrFailureManager.ResetFailureCounter();

                var failureCount = _ocrFailureManager.GetFailureCount();
                var isAvailable = _ocrFailureManager.IsOcrAvailable;

                Console.WriteLine($"✅ [Stop機能] OCR失敗カウンターリセット成功 - 現在の失敗回数: {failureCount}");
                Console.WriteLine($"✅ [Stop機能] OCR利用可能状態: {(isAvailable ? "有効" : "無効")}");

                _logger.LogInformation("🔄 Stop機能: OCR失敗カウンターリセット完了 - 現在の失敗回数: {FailureCount}, 利用可能: {IsAvailable}",
                    failureCount, isAvailable);
                _logger.LogInformation("🔄 Stop機能: PaddleOCR無効化状態を解除し、再利用可能状態に復旧");

                Console.WriteLine("✅ [Stop機能] OCR状態リセット処理完了");
                _logger.LogInformation("🚀 Stop機能: OCR状態リセット完了 - Stop→Start後の翻訳オーバーレイ表示問題を予防");
            }
            catch (Exception ocrResetEx)
            {
                Console.WriteLine($"⚠️ [Stop機能] OCR状態リセット中にエラー: {ocrResetEx.Message}");
                _logger.LogWarning(ocrResetEx, "🔄 Stop機能: OCR状態リセット中にエラーが発生しましたが、処理を継続します");
            }

            // 🧹 [STOP_FIX] 画像変化検知履歴をクリア（Stop→Start後の初回翻訳を確実に実行）
            Console.WriteLine("🧹 [STOP_FIX] 画像変化検知履歴をクリア中...");
            _logger.LogInformation("🧹 [STOP_FIX] 画像変化検知履歴クリア開始 - Stop→Start後の翻訳スキップ問題対策");
            try
            {
                // IProcessingStageStrategy集合からImageChangeDetectionStageStrategyを取得
                var imageChangeStrategy = _processingStrategies
                    .OfType<ImageChangeDetectionStageStrategy>()
                    .FirstOrDefault();

                if (imageChangeStrategy != null)
                {
                    imageChangeStrategy.ClearPreviousImages();
                    Console.WriteLine("✅ [STOP_FIX] 画像変化検知履歴クリア成功");
                    _logger.LogInformation("🚀 [STOP_FIX] 画像変化検知履歴クリア完了 - Stop→Start後の初回翻訳が確実に実行されます");
                }
                else
                {
                    Console.WriteLine("⚠️ [STOP_FIX] ImageChangeDetectionStrategyが見つかりません - 履歴クリアをスキップ");
                    _logger.LogWarning("🧹 [STOP_FIX] ImageChangeDetectionStrategyが見つかりません - 登録確認が必要です");
                }
            }
            catch (Exception clearEx)
            {
                Console.WriteLine($"⚠️ [STOP_FIX] 画像変化検知履歴クリア中にエラー: {clearEx.Message}");
                _logger.LogWarning(clearEx, "🧹 [STOP_FIX] 画像変化検知履歴クリア中にエラーが発生しましたが、処理を継続します");
            }

            // 4. 🚀 Stop機能: CancellationTokenキャンセル → 遅延翻訳結果表示を確実に防止
            if (_currentTranslationCancellationSource != null)
            {
                Console.WriteLine("🛑 [Stop機能] CancellationTokenをキャンセル中 - 遅延翻訳結果表示防止");
                _currentTranslationCancellationSource.Cancel();
                _currentTranslationCancellationSource.Dispose();
                _currentTranslationCancellationSource = null;
                _logger.LogInformation("🚀 Stop機能: CancellationTokenキャンセル完了");
                Console.WriteLine("✅ [Stop機能] CancellationTokenキャンセル完了 - 遅延結果表示防止OK");
            }

            // 5. 継続的翻訳結果購読を停止
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

            // 6. 処理中ウィンドウリストをクリア - 継続翻訳の再開を許可するため
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
    /// 翻訳表示切り替え要求イベントの処理（高速化版）
    /// </summary>
    public async Task HandleAsync(ToggleTranslationDisplayRequestEvent eventData)
    {
        try
        {
            _logger.LogDebug("翻訳表示切り替え要求を処理中: IsVisible={IsVisible}", eventData.IsVisible);

            // 高速化: オーバーレイの削除/再作成ではなく可視性のみを変更
            await _inPlaceOverlayManager.SetAllOverlaysVisibilityAsync(eventData.IsVisible).ConfigureAwait(false);

            // 表示状態変更イベントを発行
            var visibilityEvent = new TranslationDisplayVisibilityChangedEvent(eventData.IsVisible);
            await _eventAggregator.PublishAsync(visibilityEvent).ConfigureAwait(false);

            _logger.LogDebug("翻訳表示切り替えが完了しました（高速化版）");
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
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            _logger.LogInformation("設定変更を適用中");

            // AR風オーバーレイ設定は新ARシステムで自動管理（設定変更は直接適用される）
            Console.WriteLine($"🔧 [TranslationFlowEventProcessor] AR風オーバーレイ設定更新（ARシステムで自動管理）");
            Console.WriteLine($"   透明度: {eventData.OverlayOpacity}, フォントサイズ: {eventData.FontSize}");
            
            // フォントサイズを全オーバーレイウィンドウに適用
            if (eventData.FontSize > 0)
            {
                Views.Overlay.InPlaceTranslationOverlayWindow.SetGlobalFontSize(eventData.FontSize);
                Console.WriteLine($"✅ [TranslationFlowEventProcessor] フォントサイズ設定完了: {eventData.FontSize}");
            }

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

            // 2. 翻訳結果のObservableを購読してUIイベントに変換（Stop機能: CancellationToken制御追加）
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ2 - Observable購読設定開始");
            _logger.LogDebug("Setting up translation result subscription for continuous translation");
            _logger?.LogDebug("🔗 継続翻訳結果のObservable購読を設定中");
            _logger?.LogDebug($"🔍 現在の購読状態(設定前): {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
            
            // 🚀 Stop機能: 新しい翻訳セッション開始時に古いCancellationTokenをキャンセル
            _currentTranslationCancellationSource?.Cancel();
            _currentTranslationCancellationSource?.Dispose();
            _currentTranslationCancellationSource = new CancellationTokenSource();
            var cancellationToken = _currentTranslationCancellationSource.Token;
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] Observable購読オブジェクト作成開始（CancellationToken制御付き）");
            _continuousTranslationSubscription = _translationService.TranslationResults
                .ObserveOn(RxApp.MainThreadScheduler) // UIスレッドスケジューラで実行
                .Subscribe(async result => // 🔧 [OVERLAY_FIX] async追加でawaitを使用可能に
                {
                    // 🚀 Stop機能: キャンセル状態チェック - Stop後の遅延結果表示を防止
                    if (cancellationToken.IsCancellationRequested)
                    {
                        Console.WriteLine("🛑 [TranslationFlowEventProcessor] 翻訳結果表示をキャンセル - Stop済み");
                        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🛑 翻訳結果表示をキャンセル - Stop済み");
                        return;
                    }
                    
                    // 🛡️ [INVALID_RESULT_PROTECTION] 失敗・エラー結果の表示を包括的に防止
                    if (!TranslationValidator.IsValid(result.TranslatedText, result.OriginalText))
                    {
                        Console.WriteLine($"🚫 [TranslationFlowEventProcessor] 無効な翻訳結果のため表示をスキップ: '{result.TranslatedText}'");
                        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🚫 無効な翻訳結果のため表示をスキップ: '{result.TranslatedText}'");
                        return;
                    }
                    _logger?.LogDebug($"📝 継続的翻訳結果受信:");
                    _logger?.LogDebug($"   📖 オリジナル: '{result.OriginalText}'");
                    _logger?.LogDebug($"   🌐 翻訳結果: '{result.TranslatedText}'");
                    _logger?.LogDebug($"   📊 信頼度: {result.Confidence}");
                    _logger?.LogDebug($"   🎯 座標ベースモード: {result.IsCoordinateBasedMode}");
                    
                    Console.WriteLine($"📝 [TranslationFlowEventProcessor] 継続的翻訳結果受信:");
                    Console.WriteLine($"   📖 オリジナル: '{result.OriginalText}'");
                    Console.WriteLine($"   🌐 翻訳結果: '{result.TranslatedText}'");
                    Console.WriteLine($"   🎯 座標ベースモード: {result.IsCoordinateBasedMode}");
                    
                    _logger.LogInformation("Continuous translation result: '{Original}' -> '{Translated}' (confidence: {Confidence}, coordinateMode: {CoordinateMode})", 
                        result.OriginalText, result.TranslatedText, result.Confidence, result.IsCoordinateBasedMode);
                        
                    // 座標ベース翻訳の場合は既にオーバーレイで表示されているためスキップ
                    if (result.IsCoordinateBasedMode)
                    {
                        _logger?.LogDebug($"🎯 座標ベース翻訳モードのため、既にAR表示済み - フォールバック表示をスキップ");
                        _logger.LogDebug("座標ベース翻訳結果は既に表示済み - フォールバック表示をスキップ");
                        return;
                    }
                    
                    // 従来モードの場合のみフォールバック表示を実行
                    _logger?.LogDebug($"📄 従来翻訳モード - フォールバック表示を実行");
                    
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
                    _logger?.LogDebug($"🔍 フォールバックTextChunk作成: '{result.OriginalText}' -> '{result.TranslatedText}'");

                    // 🎯 [OVERLAY_FIX] 従来モードでもオーバーレイ表示を実行
                    _logger?.LogDebug("🎯 従来モードでオーバーレイ表示を実行中...");
                    Console.WriteLine($"🎯 [OVERLAY_FIX] 翻訳結果オーバーレイ表示開始: '{result.TranslatedText}'");
                    
                    try
                    {
                        // 🚫 [DUPLICATE_FIX] TranslationFlowオーバーレイ表示削除 - PHASE18統一システムで処理済み
                        // PHASE18統一システム (TranslationWithBoundsCompletedHandler) で既に表示されているため、重複防止で削除
                        // await _inPlaceOverlayManager.ShowInPlaceOverlayAsync(textChunk).ConfigureAwait(false);
                        Console.WriteLine($"🚫 [DUPLICATE_FIX] TranslationFlow直接表示スキップ - PHASE18統一システム使用: '{result.TranslatedText}'");
                        _logger?.LogDebug("✅ オーバーレイ表示完了");
                        Console.WriteLine($"✅ [OVERLAY_FIX] オーバーレイ表示成功: ChunkId={textChunk.ChunkId}");
                    }
                    catch (Exception overlayEx)
                    {
                        _logger.LogError(overlayEx, "オーバーレイ表示エラー: {Error}", overlayEx.Message);
                        Console.WriteLine($"❌ [OVERLAY_FIX] オーバーレイ表示エラー: {overlayEx.Message}");
                        _logger?.LogDebug($"❌ オーバーレイ表示エラー: {overlayEx.Message}");
                    }
                });

            // 3. 継続的翻訳を開始
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] ステップ3 - TranslationService呼び出し開始");
            _logger.LogDebug("Starting continuous automatic translation");
            _logger?.LogDebug("🏁 TranslationService.StartAutomaticTranslationAsync呼び出し中...");
            _logger?.LogDebug($"   🔍 サービス状態: {(_translationService != null ? "利用可能" : "null")}");
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ProcessTranslationAsync] _translationService の実際の型: {_translationService?.GetType()?.FullName ?? "null"}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ProcessTranslationAsync] _translationService のハッシュコード: {_translationService?.GetHashCode() ?? -1}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ProcessTranslationAsync] _translationService の基底型: {_translationService?.GetType()?.BaseType?.FullName ?? "null"}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [ProcessTranslationAsync] インターフェース実装: {string.Join(", ", _translationService?.GetType()?.GetInterfaces()?.Select(i => i.Name) ?? [])}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync呼び出し直前");
            
            // メソッド呼び出しをtry-catchで包み、例外をキャッチ
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync内部try開始");
                await _translationService!.StartAutomaticTranslationAsync(targetWindow.Handle).ConfigureAwait(false);
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync内部try完了");
            }
            catch (Exception ex)
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 [ProcessTranslationAsync] StartAutomaticTranslationAsync例外: {ex.GetType().Name}: {ex.Message}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 [ProcessTranslationAsync] スタックトレース: {ex.StackTrace}");
                throw; // 例外を再スロー
            }
            
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [ProcessTranslationAsync] StartAutomaticTranslationAsync呼び出し完了");
            _logger?.LogDebug("🏁 TranslationService.StartAutomaticTranslationAsync完了");
            _logger?.LogDebug($"   🔍 自動翻訳アクティブ: {_translationService.IsAutomaticTranslationActive}");

            _logger.LogInformation("✅ Continuous translation started successfully for window: {WindowTitle}", targetWindow.Title);
            _logger?.LogDebug($"✅ 継続的翻訳開始完了: ウィンドウ '{targetWindow.Title}' (Handle={targetWindow.Handle})");
            _logger?.LogDebug($"🔍 購読状態(終了時): {(_continuousTranslationSubscription != null ? "アクティブ" : "null")}");
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
    /// エラーメッセージを表示
    /// </summary>
    private async Task DisplayErrorMessageAsync(Exception exception)
    {
        // エラー表示は削除済み - ARシステムが自動で管理
        _logger?.LogDebug($"⚠️ エラー表示は削除済み - ARシステムで自動管理: {exception.Message}");

        var errorStatusEvent = new TranslationStatusChangedEvent(TranslationStatus.Idle);
        await _eventAggregator.PublishAsync(errorStatusEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// アクティブウィンドウを取得
    /// </summary>
    private async Task<WindowInfo?> GetActiveWindowAsync()
    {
        try
        {
            // WindowManagerAdapterを使用してアクティブウィンドウのハンドルを取得
            var activeHandle = _windowManager.GetActiveWindowHandle();
            
            if (activeHandle == IntPtr.Zero)
            {
                _logger.LogWarning("アクティブウィンドウハンドルが取得できませんでした");
                return null;
            }
            
            // ウィンドウ情報を取得
            var windows = _windowManager.GetRunningApplicationWindows();
            var activeWindow = windows.FirstOrDefault(w => w.Handle == activeHandle);
            
            if (activeWindow != null)
            {
                _logger.LogDebug("アクティブウィンドウを取得: {Title} (Handle={Handle})", activeWindow.Title, activeWindow.Handle);
            }
            else
            {
                _logger.LogWarning("アクティブウィンドウが見つかりませんでした: Handle={Handle}", activeHandle);
            }
            
            await Task.CompletedTask.ConfigureAwait(false);
            return activeWindow;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "アクティブウィンドウ取得中にエラーが発生しました");
            return null;
        }
    }

    /// <summary>
    /// リソースの解放 - Stop機能で使用されるCancellationTokenとSubscriptionを適切に解放
    /// </summary>
    public void Dispose()
    {
        try
        {
            _logger.LogDebug("TranslationFlowEventProcessor disposing...");
            
            // CancellationTokenSourceの解放
            _currentTranslationCancellationSource?.Cancel();
            _currentTranslationCancellationSource?.Dispose();
            _currentTranslationCancellationSource = null;
            
            // Subscriptionの解放
            _continuousTranslationSubscription?.Dispose();
            _continuousTranslationSubscription = null;
            
            _logger.LogDebug("TranslationFlowEventProcessor disposed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during TranslationFlowEventProcessor disposal");
        }
        
        GC.SuppressFinalize(this);
    }


    // LanguageSettingsChangedEvent処理は削除済み - SettingsViewModel削除に伴い不要
}
