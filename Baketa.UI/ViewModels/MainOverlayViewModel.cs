using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using WindowInfo = Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo;

namespace Baketa.UI.ViewModels;

/// <summary>
/// メインオーバーレイのViewModel
/// αテスト向け基本実装 - 翻訳開始/停止、状態表示、設定アクセス
/// </summary>
public class MainOverlayViewModel : ViewModelBase
{
    private bool _isCollapsed;
    private bool _isTranslationActive;
    private TranslationStatus _currentStatus;
    private bool _isTranslationResultVisible; // 初期状態は非表示

    public MainOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<MainOverlayViewModel> logger,
        IWindowManagerAdapter windowManager,
        TranslationResultOverlayManager overlayManager,
        LoadingOverlayManager loadingManager)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _loadingManager = loadingManager ?? throw new ArgumentNullException(nameof(loadingManager));
        InitializeCommands();
        InitializeEventHandlers();
        InitializePropertyChangeHandlers();
    }

    private readonly IWindowManagerAdapter _windowManager;
    private readonly TranslationResultOverlayManager _overlayManager;
    private readonly LoadingOverlayManager _loadingManager;

    #region Properties

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isCollapsed, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsCollapsed設定失敗 - 直接設定で続行");
                _isCollapsed = value;
            }
        }
    }

    public bool IsTranslationActive
    {
        get => _isTranslationActive;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isTranslationActive, value);
                // 依存プロパティの変更通知
                this.RaisePropertyChanged(nameof(StartStopText));
                this.RaisePropertyChanged(nameof(SettingsEnabled));
                this.RaisePropertyChanged(nameof(ShowHideEnabled)); // 非表示ボタンの有効状態
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsTranslationActive設定失敗 - 直接設定で続行");
                _isTranslationActive = value;
                // 例外時も依存プロパティの変更通知を試行
                try
                {
                    this.RaisePropertyChanged(nameof(StartStopText));
                    this.RaisePropertyChanged(nameof(SettingsEnabled));
                    this.RaisePropertyChanged(nameof(ShowHideEnabled));
                }
                catch { /* 二次例外は無視 */ }
            }
        }
    }

    public TranslationStatus CurrentStatus
    {
        get => _currentStatus;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _currentStatus, value);
                // 依存プロパティの変更通知
                this.RaisePropertyChanged(nameof(StatusIndicatorClass));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でCurrentStatus設定失敗 - 直接設定で続行");
                _currentStatus = value;
                // 例外時も依存プロパティの変更通知を試行
                try
                {
                    this.RaisePropertyChanged(nameof(StatusIndicatorClass));
                }
                catch { /* 二次例外は無視 */ }
            }
        }
    }

    public bool IsTranslationResultVisible
    {
        get => _isTranslationResultVisible;
        set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isTranslationResultVisible, value);
                // 依存プロパティの変更通知
                this.RaisePropertyChanged(nameof(ShowHideText));
                this.RaisePropertyChanged(nameof(ShowHideIcon));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsTranslationResultVisible設定失敗 - 直接設定で続行");
                _isTranslationResultVisible = value;
                // 例外時も依存プロパティの変更通知を試行
                try
                {
                    this.RaisePropertyChanged(nameof(ShowHideText));
                    this.RaisePropertyChanged(nameof(ShowHideIcon));
                }
                catch { /* 二次例外は無視 */ }
            }
        }
    }


    // UI状態の計算プロパティ
    public bool ShowHideEnabled => IsTranslationActive; // 翻訳中のみ有効
    public bool SettingsEnabled => !IsLoading; // ローディング中のみ無効（翻訳中でも設定可能）
    public bool IsStartStopEnabled => !IsLoading; // ローディング中は無効
    public string StartStopText 
    { 
        get 
        {
            var result = IsTranslationActive ? "Stop" : "Start";
            Console.WriteLine($"🔍 StartStopText計算: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, 結果='{result}'");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 StartStopText計算: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, 結果='{result}'");
            return result;
        }
    }
    public string LoadingText => IsLoading ? "🔄 翻訳準備中..." : "";
    public string ShowHideText => IsTranslationResultVisible ? "Hide" : "Show"; // 非表示ボタンのテキスト
    public string ShowHideIcon => IsTranslationResultVisible ? "👁️" : "🙈"; // 非表示ボタンのアイコン（例）
    public string StatusIndicatorClass => CurrentStatus switch
    {
        TranslationStatus.Idle => "inactive",
        TranslationStatus.Capturing or TranslationStatus.Translating => "active",
        TranslationStatus.Completed => "inactive",
        _ => "inactive"
    };

    #endregion

    #region Commands

    public ICommand StartStopCommand { get; private set; } = null!;
    public ICommand ShowHideCommand { get; private set; } = null!;
    public ICommand SettingsCommand { get; private set; } = null!;
    public ICommand FoldCommand { get; private set; } = null!;
    public ICommand ExitCommand { get; private set; } = null!;

    #endregion

    #region Initialization

    private void InitializeCommands()
    {
        // 各コマンドをUIスレッドで安全に初期化
        try
        {
            var startStopCmd = ReactiveCommand.CreateFromTask(ExecuteStartStopAsync,
                this.WhenAnyValue(x => x.IsStartStopEnabled).ObserveOn(RxApp.MainThreadScheduler), // ローディング中は無効
                outputScheduler: RxApp.MainThreadScheduler);
            
            // StartStopCommandの実行をトラッキング
            startStopCmd.Subscribe(_ => 
            {
                Console.WriteLine("🎬 StartStopCommandが実行されました");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎬 StartStopCommandが実行されました");
            });
            
            StartStopCommand = startStopCmd;
            ShowHideCommand = ReactiveCommand.Create(ExecuteShowHide,
                this.WhenAnyValue(x => x.IsTranslationActive).ObserveOn(RxApp.MainThreadScheduler), // 翻訳中のみ有効
                outputScheduler: RxApp.MainThreadScheduler);
            SettingsCommand = ReactiveCommand.Create(ExecuteSettings,
                this.WhenAnyValue(x => x.IsLoading).Select(x => !x).ObserveOn(RxApp.MainThreadScheduler),
                outputScheduler: RxApp.MainThreadScheduler);
            FoldCommand = ReactiveCommand.Create(ExecuteFold,
                outputScheduler: RxApp.MainThreadScheduler);
            ExitCommand = ReactiveCommand.CreateFromTask(ExecuteExitAsync,
                outputScheduler: RxApp.MainThreadScheduler);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ReactiveCommand初期化エラー");
            throw;
        }
    }

    private void InitializeEventHandlers()
    {
        // 翻訳状態変更イベントの購読
        SubscribeToEvent<TranslationStatusChangedEvent>(OnTranslationStatusChanged);

        // 翻訳結果表示状態変更イベントの購読
        SubscribeToEvent<TranslationDisplayVisibilityChangedEvent>(OnTranslationDisplayVisibilityChanged);
    }

    private void InitializePropertyChangeHandlers()
    {
        // IsLoadingプロパティの変更を監視して依存プロパティの変更通知を発行
        this.WhenAnyValue(x => x.IsLoading)
            .Subscribe(isLoading =>
            {
                Console.WriteLine($"🔄 IsLoading状態変更: {isLoading}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 IsLoading状態変更: {isLoading}");
                this.RaisePropertyChanged(nameof(LoadingText));
                this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                this.RaisePropertyChanged(nameof(SettingsEnabled));
                this.RaisePropertyChanged(nameof(StartStopText)); // StartStopTextも更新
            });
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteStartStopAsync()
    {
        Console.WriteLine($"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        
        try
        {
            Console.WriteLine($"🔍 IsTranslationActive = {IsTranslationActive}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 IsTranslationActive = {IsTranslationActive}");
            
            if (IsTranslationActive)
            {
                Console.WriteLine("🔴 StopTranslationAsync呼び出し");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 StopTranslationAsync呼び出し");
                await StopTranslationAsync().ConfigureAwait(false);
            }
            else
            {
                Console.WriteLine("🟢 StartTranslationAsync呼び出し");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🟢 StartTranslationAsync呼び出し");
                await StartTranslationAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error during start/stop translation");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
                IsTranslationActive = false;
                IsLoading = false; // エラー時はローディング状態も終了
            });
        }
    }

    private async Task StartTranslationAsync()
    {
        var overallTimer = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine("🚀 StartTranslationAsync開始");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 StartTranslationAsync開始");
        Logger?.LogInformation("🚀 翻訳ワークフローを開始");

        try
        {
            // 1. ウィンドウ選択ダイアログを表示
            var dialogTimer = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("🔍 ウィンドウ選択ダイアログを表示開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 ウィンドウ選択ダイアログを表示開始");
            Logger?.LogDebug("🔍 ウィンドウ選択ダイアログを表示");
            var selectedWindow = await ShowWindowSelectionDialogAsync().ConfigureAwait(false);
            dialogTimer.Stop();
            if (selectedWindow == null)
            {
                Console.WriteLine("❌ ウィンドウ選択がキャンセルされました");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "❌ ウィンドウ選択がキャンセルされました");
                Logger?.LogDebug("❌ ウィンドウ選択がキャンセルされました");
                return; // キャンセル
            }

            Console.WriteLine($"✅ ウィンドウが選択されました: '{selectedWindow.Title}' (Handle={selectedWindow.Handle}) - 選択時間: {dialogTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ ウィンドウが選択されました: '{selectedWindow.Title}' (Handle={selectedWindow.Handle}) - 選択時間: {dialogTimer.ElapsedMilliseconds}ms");
            Logger?.LogInformation("✅ ウィンドウが選択されました: '{Title}' (Handle={Handle}) - 選択時間: {ElapsedMs}ms", 
                selectedWindow.Title, selectedWindow.Handle, dialogTimer.ElapsedMilliseconds);

            // ローディング状態開始（ウィンドウ選択後）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                Console.WriteLine($"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
            });
            
            // 画面中央ローディングオーバーレイ表示
            await _loadingManager.ShowAsync().ConfigureAwait(false);

            // 2. 翻訳開始
            var uiTimer = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("📊 翻訳状態をアクティブに設定");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📊 翻訳状態をアクティブに設定");
            Logger?.LogDebug("📊 翻訳状態をキャプチャ中に設定");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Capturing; // TranslationStatus.Activeがないため適切な値を使用
                IsTranslationActive = true;
                IsTranslationResultVisible = true; // 翻訳開始時は表示状態に設定
                IsLoading = false; // ローディング状態終了
                Console.WriteLine($"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
            });
            
            // 画面中央ローディングオーバーレイ非表示
            await _loadingManager.HideAsync().ConfigureAwait(false);
            uiTimer.Stop();
            Console.WriteLine($"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");

            // オーバーレイマネージャーを初期化
            var overlayInitTimer = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("🖼️ オーバーレイマネージャー初期化開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー初期化開始");
            Logger?.LogDebug("🖼️ オーバーレイマネージャーを初期化");
            
            await _overlayManager.InitializeAsync().ConfigureAwait(false);
            overlayInitTimer.Stop();
            Console.WriteLine($"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            
            // オーバーレイマネージャーを表示状態に設定
            Console.WriteLine("🖼️ オーバーレイマネージャー表示状態設定開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー表示状態設定開始");
            await _overlayManager.ShowAsync().ConfigureAwait(false);
            Console.WriteLine("✅ オーバーレイマネージャー表示状態設定完了");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ オーバーレイマネージャー表示状態設定完了");

            var eventTimer = System.Diagnostics.Stopwatch.StartNew();
            Console.WriteLine("📢 StartTranslationRequestEventを発行");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📢 StartTranslationRequestEventを発行");
            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            var startTranslationEvent = new StartTranslationRequestEvent(selectedWindow);
            Console.WriteLine($"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            await PublishEventAsync(startTranslationEvent).ConfigureAwait(false);
            eventTimer.Stop();
            Console.WriteLine($"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");

            overallTimer.Stop();
            Console.WriteLine($"⏱️ 【総合時間】翻訳開始処理全体: {overallTimer.ElapsedMilliseconds}ms (ダイアログ: {dialogTimer.ElapsedMilliseconds}ms, UI更新: {uiTimer.ElapsedMilliseconds}ms, オーバーレイ初期化: {overlayInitTimer.ElapsedMilliseconds}ms, イベント処理: {eventTimer.ElapsedMilliseconds}ms)");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ 【総合時間】翻訳開始処理全体: {overallTimer.ElapsedMilliseconds}ms (ダイアログ: {dialogTimer.ElapsedMilliseconds}ms, UI更新: {uiTimer.ElapsedMilliseconds}ms, オーバーレイ初期化: {overlayInitTimer.ElapsedMilliseconds}ms, イベント処理: {eventTimer.ElapsedMilliseconds}ms)");

            Logger?.LogInformation("🎉 翻訳が正常に開始されました: '{Title}' - 総処理時間: {TotalMs}ms", selectedWindow.Title, overallTimer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "💥 翻訳開始に失敗: {ErrorMessage}", ex.Message);
            
            // エラー時はローディングオーバーレイを非表示
            try
            {
                await _loadingManager.HideAsync().ConfigureAwait(false);
            }
            catch (Exception loadingEx)
            {
                Console.WriteLine($"⚠️ ローディング非表示エラー: {loadingEx.Message}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ ローディング非表示エラー: {loadingEx.Message}");
            }
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
                IsTranslationActive = false;
                IsLoading = false; // ローディング状態終了
                Console.WriteLine($"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
            });
        }
    }

    /// <summary>
    /// ウィンドウ選択ダイアログを表示
    /// </summary>
    private async Task<WindowInfo?> ShowWindowSelectionDialogAsync()
    {
        try
        {
            var dialogViewModel = new WindowSelectionDialogViewModel(EventAggregator, 
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<WindowSelectionDialogViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance), _windowManager);
            var dialog = new WindowSelectionDialogView
            {
                DataContext = dialogViewModel
            };

            // UIスレッドで安全にApplication.Currentにアクセス
            var owner = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                return Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;
            });
            
            WindowInfo? result = null;
            if (owner != null)
            {
                result = await dialog.ShowDialog<WindowInfo?>(owner).ConfigureAwait(false);
            }
            else
            {
                dialog.Show();
                // ShowDialogではなくShowで表示し、IsClosedで制御
                while (!dialogViewModel.IsClosed)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                }
                result = dialogViewModel.DialogResult;
                dialog.Close();
            }

            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to show window selection dialog");
            return null;
        }
    }

    private async Task StopTranslationAsync()
    {
        Console.WriteLine("🔴 翻訳停止処理開始");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 翻訳停止処理開始");
        Logger?.LogInformation("Stopping translation");

        // 翻訳停止 + ウィンドウ選択解除
        Console.WriteLine("🔴 翻訳状態をアイドルに設定");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 翻訳状態をアイドルに設定");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = TranslationStatus.Idle;
            IsTranslationActive = false;
            IsTranslationResultVisible = false; // 翻訳停止時は非表示にリセット
            Console.WriteLine($"✅ 翻訳停止状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsTranslationResultVisible={IsTranslationResultVisible}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 翻訳停止状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsTranslationResultVisible={IsTranslationResultVisible}");
        });

        // オーバーレイを非表示にしてリセット
        await _overlayManager.HideAsync().ConfigureAwait(false);
        await _overlayManager.ResetAsync().ConfigureAwait(false);

        var stopTranslationEvent = new StopTranslationRequestEvent();
        await PublishEventAsync(stopTranslationEvent).ConfigureAwait(false);

        Console.WriteLine("✅ 翻訳停止処理完了");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 翻訳停止処理完了");
        Logger?.LogInformation("Translation stopped successfully");
    }

    private async void ExecuteShowHide()
    {
        Console.WriteLine($"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        
        // 翻訳が非アクティブの場合は何もしない（安全措置）
        if (!IsTranslationActive)
        {
            Console.WriteLine("⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            Logger?.LogWarning("非表示ボタンが翻訳非アクティブ時に押されました");
            return;
        }
        
        Logger?.LogDebug("Show/Hide toggle requested - Current: {Current} -> New: {New}", IsTranslationResultVisible, !IsTranslationResultVisible);
        
        var newVisibility = !IsTranslationResultVisible;
        Console.WriteLine($"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        
        IsTranslationResultVisible = newVisibility;
        
        // オーバーレイマネージャーを使用して表示/非表示を切り替え
        if (IsTranslationResultVisible)
        {
            Console.WriteLine("👁️ オーバーレイ表示");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "👁️ オーバーレイ表示");
            await _overlayManager.ShowAsync().ConfigureAwait(false);
        }
        else
        {
            Console.WriteLine("🙈 オーバーレイ非表示");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🙈 オーバーレイ非表示");
            await _overlayManager.HideAsync().ConfigureAwait(false);
        }
        
        var toggleEvent = new ToggleTranslationDisplayRequestEvent(IsTranslationResultVisible);
        await PublishEventAsync(toggleEvent).ConfigureAwait(false);
        
        Console.WriteLine($"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        Logger?.LogDebug("Translation display visibility toggled: {IsVisible}", IsTranslationResultVisible);
    }

    private static SimpleSettingsView? _currentSettingsDialog;

    private async void ExecuteSettings()
    {
        try
        {
            var currentDialogHash = _currentSettingsDialog?.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] ExecuteSettings開始 - 現在のダイアログ: {currentDialogHash}");
            
            // 既に設定画面が開いている場合は何もしない
            if (_currentSettingsDialog != null)
            {
                DebugHelper.Log($"🔧 [MainOverlayViewModel] 設定ダイアログが既に存在 - アクティベート: {currentDialogHash}");
                Logger?.LogDebug("Settings dialog is already open, activating");
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _currentSettingsDialog.Activate();
                });
                return;
            }

            DebugHelper.Log($"🔧 [MainOverlayViewModel] 新しい設定ダイアログを作成開始");
            Logger?.LogDebug("Opening simple settings dialog");
            
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsViewModel作成開始");

            // SimpleSettingsViewModelを作成
            var settingsViewModel = new SimpleSettingsViewModel(EventAggregator, 
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<SimpleSettingsViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));
            var vmHash = settingsViewModel.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsViewModel作成: {vmHash}");

            // ViewModelの設定を読み込み
            DebugHelper.Log($"🔧 [MainOverlayViewModel] LoadSettingsAsync呼び出し前");
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await settingsViewModel.LoadSettingsAsync().ConfigureAwait(false);
                });
                DebugHelper.Log($"🔧 [MainOverlayViewModel] LoadSettingsAsync呼び出し完了");
            }
            catch (Exception loadEx)
            {
                DebugHelper.Log($"💥 [MainOverlayViewModel] LoadSettingsAsync例外: {loadEx.Message}");
            }

            // 設定ダイアログを作成
            _currentSettingsDialog = new SimpleSettingsView
            {
                DataContext = settingsViewModel
            };
            var dialogHash = _currentSettingsDialog.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsView作成: {dialogHash}");

            // ダイアログが閉じられたときの処理
            _currentSettingsDialog.Closed += (_, _) =>
            {
                Console.WriteLine($"🔧 [MainOverlayViewModel] Settings dialog Closedイベント - ダイアログ: {dialogHash}");
                Logger?.LogDebug("Settings dialog closed event received");
                var previousDialog = _currentSettingsDialog;
                _currentSettingsDialog = null;
                Console.WriteLine($"🔧 [MainOverlayViewModel] _currentSettingsDialogをnullに設定 - 前の値: {previousDialog?.GetHashCode()}");
            };

            // ViewModelのCloseRequestedイベントハンドル - 直接Close()を呼び出し
            if (settingsViewModel != null)
            {
                settingsViewModel.CloseRequested += () =>
                {
                    Console.WriteLine($"🔧 [MainOverlayViewModel] Settings dialog close requested by ViewModel - VM: {vmHash}");
                    Logger?.LogDebug("Settings dialog close requested by ViewModel");
                    var dialog = _currentSettingsDialog;
                    var currentDialogHash2 = dialog?.GetHashCode();
                    Console.WriteLine($"🔧 [MainOverlayViewModel] 現在のダイアログ状態: {currentDialogHash2}, 作成時: {dialogHash}");
                    if (dialog != null)
                    {
                        Console.WriteLine($"🔧 [MainOverlayViewModel] 直接Close()を呼び出し - 対象: {currentDialogHash2}");
                        dialog.Close();
                        Console.WriteLine($"🔧 [MainOverlayViewModel] Close()完了 - 対象: {currentDialogHash2}");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ [MainOverlayViewModel] _currentSettingsDialogがnull");
                    }
                };
            }

            // UIスレッドで安全にApplication.Currentにアクセス
            var owner = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                return Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;
            });

            // ShowDialog()ではなくShow()を使用（モーダルダイアログの問題を回避）
            DebugHelper.Log($"🔧 [MainOverlayViewModel] Show()呼び出し - ダイアログ: {dialogHash}");
            _currentSettingsDialog.Show();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] Show()完了 - ダイアログ: {dialogHash}");
            
            Logger?.LogDebug("Settings dialog opened");
        }
        catch (Exception ex)
        {
            DebugHelper.Log($"💥 [MainOverlayViewModel] ExecuteSettingsエラー: {ex.Message}");
            DebugHelper.Log($"💥 [MainOverlayViewModel] スタックトレース: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                DebugHelper.Log($"💥 [MainOverlayViewModel] InnerException: {ex.InnerException.Message}");
            }
            Logger?.LogError(ex, "Failed to open settings dialog");
            _currentSettingsDialog = null;
        }
    }

    private async void ExecuteFold()
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsCollapsed = !IsCollapsed;
        });
        Logger?.LogDebug("Overlay fold state changed: {IsCollapsed}", IsCollapsed);
    }

    private async Task ExecuteExitAsync()
    {
        if (IsTranslationActive)
        {
            // 翻訳中の場合は確認
            var confirmationRequest = new ConfirmationRequestEvent(
                "翻訳を停止してアプリを終了しますか？",
                "終了確認");
            await PublishEventAsync(confirmationRequest).ConfigureAwait(false);
            
            var confirmed = await confirmationRequest.GetResultAsync().ConfigureAwait(false);
            if (!confirmed)
            {
                Logger?.LogDebug("Exit cancelled by user");
                return;
            }
        }

        var exitEvent = new ExitApplicationRequestEvent();
        await PublishEventAsync(exitEvent).ConfigureAwait(false);
        
        Logger?.LogInformation("Application exit requested");
    }

    #endregion

    #region Event Handlers

    private async Task OnTranslationStatusChanged(TranslationStatusChangedEvent statusEvent)
    {
        var previousStatus = CurrentStatus;
        
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = statusEvent.Status;
            IsTranslationActive = statusEvent.Status == TranslationStatus.Capturing || statusEvent.Status == TranslationStatus.Translating;
        });
        
        Logger?.LogInformation("📊 翻訳状態変更: {PreviousStatus} -> {CurrentStatus}", 
            previousStatus, statusEvent.Status);
            
        // 状態に応じてUIの状態を詳細にログ出力
        Logger?.LogDebug("🔄 UI状態更新: IsTranslationActive={IsActive}, StartStopText='{Text}', StatusClass='{Class}'", 
            IsTranslationActive, StartStopText, StatusIndicatorClass);
    }

    private async Task OnTranslationDisplayVisibilityChanged(TranslationDisplayVisibilityChangedEvent visibilityEvent)
    {
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsTranslationResultVisible = visibilityEvent.IsVisible;
        });
        
        Logger?.LogDebug("Translation display visibility changed: {IsVisible}", visibilityEvent.IsVisible);
    }

    #endregion
}