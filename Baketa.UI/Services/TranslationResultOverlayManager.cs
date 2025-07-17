using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Services;
using Baketa.UI.Framework.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Baketa.UI.Utils;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// 翻訳結果オーバーレイの管理サービス
/// </summary>
public class TranslationResultOverlayManager(
    IEventAggregator eventAggregator,
    ISettingsService settingsService,
    ILogger<TranslationResultOverlayManager> logger) : IDisposable
{
    private readonly IEventAggregator _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
    private readonly ISettingsService _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
    private readonly ILogger<TranslationResultOverlayManager> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private TranslationResultOverlayView? _overlayWindow;
    private TranslationResultOverlayViewModel? _viewModel;
    private bool _isInitialized;
    private bool _disposed;
    private readonly object _initializeLock = new();

    /// <summary>
    /// オーバーレイを初期化
    /// </summary>
    public async Task InitializeAsync()
    {
        Console.WriteLine($"🔧 TranslationResultOverlayManager.InitializeAsync開始 - _isInitialized: {_isInitialized}, _disposed: {_disposed}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔧 TranslationResultOverlayManager.InitializeAsync開始 - _isInitialized: {_isInitialized}, _disposed: {_disposed}");
        
        lock (_initializeLock)
        {
            if (_isInitialized || _disposed)
            {
                Console.WriteLine($"⚠️ オーバーレイマネージャー初期化スキップ (initialized: {_isInitialized}, disposed: {_disposed})");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ オーバーレイマネージャー初期化スキップ (initialized: {_isInitialized}, disposed: {_disposed})");
                _logger.LogDebug("Overlay manager initialization skipped (initialized: {IsInitialized}, disposed: {IsDisposed})", 
                    _isInitialized, _disposed);
                return;
            }
            
            Console.WriteLine("🔒 オーバーレイマネージャー初期化ロック取得、実際の初期化を開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔒 オーバーレイマネージャー初期化ロック取得、実際の初期化を開始");
        }

        try
        {
            _logger.LogDebug("Starting actual overlay manager initialization");

            // ViewModelを作成（デバッグ用ログを有効化）
            Console.WriteLine("🏗️ TranslationResultOverlayViewModel作成開始 (OverlayManager内)");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ TranslationResultOverlayViewModel作成開始 (OverlayManager内)");
            
            var loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => 
                builder.AddConsole().SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Debug));
            var viewModelLogger = loggerFactory.CreateLogger<TranslationResultOverlayViewModel>();
            
            _viewModel = new TranslationResultOverlayViewModel(_eventAggregator, viewModelLogger);
            
            // 初期フォントサイズを設定から取得して適用
            var fontSize = _settingsService.GetValue("UI:FontSize", 14);
            _viewModel.FontSize = fontSize;
            Console.WriteLine($"🔤 初期フォントサイズ適用: {fontSize}");
            
            Console.WriteLine("✅ TranslationResultOverlayViewModel作成完了 (OverlayManager内)");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ TranslationResultOverlayViewModel作成完了 (OverlayManager内)");

            // UIスレッドでウィンドウを作成
            Console.WriteLine("🧵 UIスレッドでTranslationResultOverlayView作成開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🧵 UIスレッドでTranslationResultOverlayView作成開始");
            
            Console.WriteLine("🏁 UIスレッド処理開始前");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏁 UIスレッド処理開始前");
            
            // UIスレッド処理をタイムアウト付きで実行
            var uiTask = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    Console.WriteLine("🏗️ TranslationResultOverlayView作成開始");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ TranslationResultOverlayView作成開始");
                    _logger.LogDebug("🏗️ TranslationResultOverlayView作成開始");
                    
                    Console.WriteLine("🔧 new TranslationResultOverlayView()呼び出し直前");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 new TranslationResultOverlayView()呼び出し直前");
                    
                    _overlayWindow = new TranslationResultOverlayView();
                    
                    Console.WriteLine("✅ new TranslationResultOverlayView()呼び出し完了");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ new TranslationResultOverlayView()呼び出し完了");
                    
                    Console.WriteLine("🔗 DataContext設定開始");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔗 DataContext設定開始");
                    _logger.LogDebug("🔗 DataContext設定開始");
                    _overlayWindow.DataContext = _viewModel;
                    
                    Console.WriteLine("✅ TranslationResultOverlayView作成・DataContext設定完了");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ TranslationResultOverlayView作成・DataContext設定完了");
                    _logger.LogDebug("✅ TranslationResultOverlayView作成・DataContext設定完了");
                    
                    // 作成されたオブジェクトの検証
                    Console.WriteLine($"🔍 作成されたウィンドウ: {(_overlayWindow != null ? "not null" : "null")}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 作成されたウィンドウ: {(_overlayWindow != null ? "not null" : "null")}");
                    Console.WriteLine($"🔍 DataContext設定: {(_overlayWindow?.DataContext != null ? "not null" : "null")}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 DataContext設定: {(_overlayWindow?.DataContext != null ? "not null" : "null")}");
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"💥 UIスレッド違反でオーバーレイウィンドウ作成失敗: {ex.Message}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 UIスレッド違反でオーバーレイウィンドウ作成失敗: {ex.Message}");
                    Console.WriteLine($"💥 UIスレッド違反スタックトレース: {ex.StackTrace}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 UIスレッド違反スタックトレース: {ex.StackTrace}");
                    _logger?.LogWarning(ex, "UIスレッド違反でオーバーレイウィンドウ作成失敗 - 強制続行");
                    throw; // オーバーレイ作成失敗は致命的なので再スロー
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"💥 オーバーレイウィンドウ作成で予期しない例外: {ex.GetType().Name}: {ex.Message}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 オーバーレイウィンドウ作成で予期しない例外: {ex.GetType().Name}: {ex.Message}");
                    Console.WriteLine($"💥 予期しない例外スタックトレース: {ex.StackTrace}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 予期しない例外スタックトレース: {ex.StackTrace}");
                    if (ex.InnerException != null)
                    {
                        Console.WriteLine($"💥 内部例外: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 内部例外: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    }
                    _logger?.LogError(ex, "オーバーレイウィンドウ作成で予期しない例外");
                    throw;
                }
            });
            
            Console.WriteLine("⏰ UIスレッド処理のタイムアウト監視開始（30秒）");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⏰ UIスレッド処理のタイムアウト監視開始（30秒）");
            
            // タイムアウト付きでUIスレッド処理を待機（時間を延長）
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
            var completedTask = await Task.WhenAny(uiTask.GetTask(), timeoutTask).ConfigureAwait(false);
            
            if (completedTask == timeoutTask)
            {
                Console.WriteLine("⚠️ UIスレッド処理がタイムアウトしました（30秒）- オーバーレイ機能を無効化して続行");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ UIスレッド処理がタイムアウトしました（30秒）");
                _logger?.LogWarning("TranslationResultOverlayViewの作成がタイムアウトしました。オーバーレイ機能を無効化して続行します。");
                
                // エラーではなく無効化して続行
                lock (_initializeLock)
                {
                    _isInitialized = false;
                    _disposed = true; // オーバーレイ機能を無効化
                }
                return;
            }
            
            // UIスレッド処理の完了を待機
            await uiTask;
            
            Console.WriteLine("🏁 UIスレッド処理完了後");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏁 UIスレッド処理完了後");

            // UIスレッド処理が正常完了した場合のみ初期化フラグを設定
            if (_overlayWindow != null && _viewModel != null)
            {
                lock (_initializeLock)
                {
                    _isInitialized = true;
                    Console.WriteLine("🔓 オーバーレイマネージャー初期化完了フラグ設定");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔓 オーバーレイマネージャー初期化完了フラグ設定");
                }
            }
            else
            {
                Console.WriteLine("⚠️ オーバーレイWindow/ViewModelのnullチェック失敗 - 初期化未完了");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ オーバーレイWindow/ViewModelのnullチェック失敗 - 初期化未完了");
                throw new InvalidOperationException("TranslationResultOverlayView または ViewModel の作成に失敗しました");
            }
            
            Console.WriteLine("🎉 TranslationResultOverlayManager.InitializeAsync正常完了");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎉 TranslationResultOverlayManager.InitializeAsync正常完了");
            _logger.LogInformation("Translation result overlay initialized successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 TranslationResultOverlayManager.InitializeAsync例外: {ex.GetType().Name}: {ex.Message}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 TranslationResultOverlayManager.InitializeAsync例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 スタックトレース: {ex.StackTrace}");
            _logger.LogError(ex, "Failed to initialize translation result overlay");
            throw;
        }
    }

    /// <summary>
    /// オーバーレイを表示
    /// </summary>
    public async Task ShowAsync()
    {
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (_overlayWindow != null && _viewModel != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _viewModel.IsOverlayVisible = true;
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogWarning(ex, "UIスレッド違反でオーバーレイ表示失敗 - 続行");
                    // 表示失敗は致命的ではないので続行
                }
            });
        }
    }

    /// <summary>
    /// オーバーレイを非表示
    /// </summary>
    public async Task HideAsync()
    {
        if (_overlayWindow != null && _viewModel != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _viewModel.IsOverlayVisible = false;
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogWarning(ex, "UIスレッド違反でオーバーレイ非表示失敗 - 続行");
                    // 非表示失敗は致命的ではないので続行
                }
            });
        }
    }

    /// <summary>
    /// 翻訳結果を表示
    /// </summary>
    public async Task DisplayTranslationResultAsync(string originalText, string translatedText, System.Drawing.Point? position = null)
    {
        if (!_isInitialized || _disposed)
        {
            await InitializeAsync().ConfigureAwait(false);
        }

        if (_viewModel != null)
        {
            var displayEvent = new TranslationResultDisplayEvent
            {
                OriginalText = originalText,
                TranslatedText = translatedText,
                DetectedPosition = position
            };

            await _eventAggregator.PublishAsync(displayEvent).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// オーバーレイの透明度を設定
    /// </summary>
    public void SetOpacity(double opacity)
    {
        if (_viewModel != null)
        {
            _viewModel.OverlayOpacity = Math.Max(0.1, Math.Min(1.0, opacity));
        }
    }

    /// <summary>
    /// オーバーレイの最大幅を設定
    /// </summary>
    public void SetMaxWidth(double maxWidth)
    {
        if (_viewModel != null)
        {
            _viewModel.MaxWidth = Math.Max(200, Math.Min(800, maxWidth));
        }
    }

    /// <summary>
    /// オーバーレイのフォントサイズを設定
    /// </summary>
    public void SetFontSize(int fontSize)
    {
        if (_viewModel != null)
        {
            _viewModel.FontSize = Math.Max(8, Math.Min(72, fontSize));
            Console.WriteLine($"🔤 フォントサイズ設定: {fontSize}");
        }
    }

    /// <summary>
    /// オーバーレイをリセット（Stop時に呼び出し）
    /// </summary>
    public async Task ResetAsync()
    {
        Console.WriteLine("🔄 TranslationResultOverlayManager - リセット開始");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 TranslationResultOverlayManager - リセット開始");
        
        if (_overlayWindow != null)
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    _overlayWindow.Hide();
                    _overlayWindow.Close();
                    Console.WriteLine("✅ オーバーレイウィンドウを閉じました");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ オーバーレイウィンドウを閉じました");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ オーバーレイウィンドウクローズエラー: {ex.Message}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ オーバーレイウィンドウクローズエラー: {ex.Message}");
                }
            });
        }
        
        _overlayWindow = null;
        _viewModel = null;
        _isInitialized = false;
        
        Console.WriteLine("✅ TranslationResultOverlayManager - リセット完了");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ TranslationResultOverlayManager - リセット完了");
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            _overlayWindow?.Close();
            _overlayWindow = null;
            _viewModel = null;
            _isInitialized = false;
            _disposed = true;
            
            _logger.LogDebug("Translation result overlay disposed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disposing translation result overlay");
        }
        
        GC.SuppressFinalize(this);
    }
}
