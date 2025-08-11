#pragma warning disable CS0618 // Type or member is obsolete
using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Utilities;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.Views;
using Microsoft.Extensions.DependencyInjection;
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
    private bool _isWindowSelected;
    private bool _isOcrInitialized;
    private WindowInfo? _selectedWindow;

    public MainOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<MainOverlayViewModel> logger,
        IWindowManagerAdapter windowManager,
        IInPlaceTranslationOverlayManager inPlaceOverlayManager,
        LoadingOverlayManager loadingManager,
        IServiceProvider serviceProvider)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _inPlaceOverlayManager = inPlaceOverlayManager ?? throw new ArgumentNullException(nameof(inPlaceOverlayManager));
        _loadingManager = loadingManager ?? throw new ArgumentNullException(nameof(loadingManager));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        
        // 初期状態設定 - OCR初期化状態を動的に管理
        _isOcrInitialized = true; // Phase 3デバッグのため一時的に初期化済みとする
        _currentStatus = TranslationStatus.Idle; // アイドル状態から開始
        
        DebugLogUtility.WriteLog("🎯 NEW UI FLOW VERSION - MainOverlayViewModel初期化完了");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎯 NEW UI FLOW VERSION - MainOverlayViewModel初期化完了");
        
        // 直接ファイル書き込みでも記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DIRECT] MainOverlayViewModel初期化完了{Environment.NewLine}");
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"MainOverlayViewModel初期化 ファイル書き込みエラー: {fileEx.Message}");
        }
        
        // OCR初期化状態を監視するタスクを開始
        _ = Task.Run(MonitorOcrInitializationAsync);
        
        InitializeCommands();
        InitializeEventHandlers();
        InitializePropertyChangeHandlers();
    }

    private readonly IWindowManagerAdapter _windowManager;
    private readonly IInPlaceTranslationOverlayManager _inPlaceOverlayManager;
    private readonly LoadingOverlayManager _loadingManager;
    private readonly IServiceProvider _serviceProvider;

    #region Properties

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => SetPropertySafe(ref _isCollapsed, value);
    }

    public bool IsTranslationActive
    {
        get => _isTranslationActive;
        set
        {
            var changed = SetPropertySafe(ref _isTranslationActive, value);
            if (changed)
            {
                // 依存プロパティの変更通知を安全に送信
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(StartStopText));
                    this.RaisePropertyChanged(nameof(SettingsEnabled));
                    this.RaisePropertyChanged(nameof(ShowHideEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(StartStopText));
                        this.RaisePropertyChanged(nameof(SettingsEnabled));
                        this.RaisePropertyChanged(nameof(ShowHideEnabled));
                    });
                }
            }
        }
    }

    public TranslationStatus CurrentStatus
    {
        get => _currentStatus;
        set
        {
            var changed = SetPropertySafe(ref _currentStatus, value);
            if (changed)
            {
                // 依存プロパティの変更通知を安全に送信
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(StatusIndicatorClass));
                    this.RaisePropertyChanged(nameof(InitializationText));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(StatusIndicatorClass));
                        this.RaisePropertyChanged(nameof(InitializationText));
                    });
                }
            }
        }
    }

    public bool IsTranslationResultVisible
    {
        get => _isTranslationResultVisible;
        set
        {
            var changed = SetPropertySafe(ref _isTranslationResultVisible, value);
            if (changed)
            {
                // 依存プロパティの変更通知を安全に送信
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(ShowHideText));
                    this.RaisePropertyChanged(nameof(ShowHideIcon));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(ShowHideText));
                        this.RaisePropertyChanged(nameof(ShowHideIcon));
                    });
                }
            }
        }
    }

    public bool IsWindowSelected
    {
        get => _isWindowSelected;
        set
        {
            var changed = SetPropertySafe(ref _isWindowSelected, value);
            if (changed)
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                    this.RaisePropertyChanged(nameof(StartStopText));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                        this.RaisePropertyChanged(nameof(StartStopText));
                    });
                }
            }
        }
    }

    public bool IsOcrInitialized
    {
        get => _isOcrInitialized;
        set
        {
            var changed = SetPropertySafe(ref _isOcrInitialized, value);
            if (changed)
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                    });
                }
            }
        }
    }

    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set => SetPropertySafe(ref _selectedWindow, value);
    }


    // UI状態の計算プロパティ
    public bool ShowHideEnabled => IsTranslationActive; // 翻訳中のみ有効
    public bool SettingsEnabled => !IsLoading && !IsTranslationActive; // ローディング中または翻訳実行中は無効
    public bool IsSelectWindowEnabled => IsOcrInitialized && !IsLoading; // OCR初期化完了かつローディング中以外
    public bool IsStartStopEnabled 
    { 
        get 
        {
            var enabled = !IsLoading && IsWindowSelected; // ローディング中以外かつウィンドウ選択済み
            DebugLogUtility.WriteLog($"🔍 IsStartStopEnabled計算: IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, 結果={enabled}");
            
            // デバッグ用に実際の状態をファイルログにも出力
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", 
                    $"🔍 [START_BUTTON_STATE] IsStartStopEnabled={enabled}, IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}");
            }
            catch { }
            
            return enabled;
        }
    }
    public string StartStopText 
    { 
        get 
        {
            var result = IsTranslationActive ? "Stop" : "Start";
            DebugLogUtility.WriteLog($"🔍 StartStopText計算: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, 結果='{result}'");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 StartStopText計算: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, 結果='{result}'");
            return result;
        }
    }
    public string LoadingText => IsLoading ? "🔄 翻訳準備中..." : "";
    public string ShowHideText => IsTranslationResultVisible ? "Hide" : "Show"; // 非表示ボタンのテキスト
    public string ShowHideIcon => IsTranslationResultVisible ? "👁️" : "🙈"; // 非表示ボタンのアイコン（例）
    public string InitializationText => CurrentStatus switch
    {
        TranslationStatus.Initializing => "初期化中...",
        TranslationStatus.Idle => "未選択",
        TranslationStatus.Ready => "準備完了",
        TranslationStatus.Capturing or TranslationStatus.ProcessingOCR or TranslationStatus.Translating => "翻訳中",
        _ => "待機中"
    };
    public string StatusIndicatorClass => CurrentStatus switch
    {
        TranslationStatus.Initializing => "initializing",
        TranslationStatus.Idle => "idle",
        TranslationStatus.Ready => "ready",
        TranslationStatus.Capturing or TranslationStatus.ProcessingOCR or TranslationStatus.Translating => "active",
        TranslationStatus.Completed => "completed",
        TranslationStatus.Error => "error",
        TranslationStatus.Cancelled => "cancelled",
        _ => "idle"
    };

    #endregion

    #region Commands

    public ICommand SelectWindowCommand { get; private set; } = null!;
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
            // canExecute Observableをデバッグ
            var canExecuteObservable = this.WhenAnyValue(x => x.IsStartStopEnabled)
                .Do(canExecute => 
                {
                    DebugLogUtility.WriteLog($"🔍 StartStopCommand canExecute変更: {canExecute}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 StartStopCommand canExecute変更: {canExecute}");
                })
                .ObserveOn(RxApp.MainThreadScheduler);
                
            DebugLogUtility.WriteLog("🏗️ ReactiveCommand.CreateFromTask開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ ReactiveCommand.CreateFromTask開始");
                
            var startStopCmd = ReactiveCommand.CreateFromTask(ExecuteStartStopAsync,
                canExecuteObservable, // ローディング中は無効
                outputScheduler: RxApp.MainThreadScheduler);
                
            DebugLogUtility.WriteLog("✅ ReactiveCommand.CreateFromTask完了");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ ReactiveCommand.CreateFromTask完了");
            
            // StartStopCommandの実行をトラッキング（開始と完了を分けて記録）
            startStopCmd.IsExecuting.Subscribe(isExecuting =>
            {
                if (isExecuting)
                {
                    DebugLogUtility.WriteLog("🚀 StartStopCommand実行開始");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 StartStopCommand実行開始");
                }
                else
                {
                    DebugLogUtility.WriteLog("✅ StartStopCommand実行完了");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StartStopCommand実行完了");
                }
            });
            
            // コマンド結果の監視
            startStopCmd.Subscribe(result => 
            {
                DebugLogUtility.WriteLog($"🎬 StartStopCommandの結果を受信: {result.GetType().Name}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🎬 StartStopCommandの結果を受信: {result.GetType().Name}");
            });
            
            // StartStopCommandのエラーをトラッキング
            startStopCmd.ThrownExceptions.Subscribe(ex =>
            {
                DebugLogUtility.WriteLog($"❌ StartStopCommandでエラー発生: {ex.Message}");
                DebugLogUtility.WriteLog($"❌ スタックトレース: {ex.StackTrace}");
                Logger?.LogError(ex, "StartStopCommandでエラーが発生しました");
            });
            
            StartStopCommand = startStopCmd;
            
            SelectWindowCommand = ReactiveCommand.CreateFromTask(ExecuteSelectWindowAsync,
                this.WhenAnyValue(x => x.IsSelectWindowEnabled).ObserveOn(RxApp.MainThreadScheduler),
                outputScheduler: RxApp.MainThreadScheduler);
                
            ShowHideCommand = ReactiveCommand.Create(ExecuteShowHide,
                this.WhenAnyValue(x => x.IsTranslationActive).ObserveOn(RxApp.MainThreadScheduler), // 翻訳中のみ有効
                outputScheduler: RxApp.MainThreadScheduler);
            var settingsCmd = ReactiveCommand.Create(ExecuteSettings,
                this.WhenAnyValue(x => x.IsLoading, x => x.IsTranslationActive, (isLoading, isTranslationActive) => !isLoading && !isTranslationActive).ObserveOn(RxApp.MainThreadScheduler),
                outputScheduler: RxApp.MainThreadScheduler);
            
            // SettingsCommandの実行をトラッキング
            settingsCmd.Subscribe(_ => 
            {
                DebugLogUtility.WriteLog("🔧 SettingsCommandが実行されました");
            });
            
            SettingsCommand = settingsCmd;
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
        // 初期状態をログ出力 - 直接ファイル書き込みで確実に出力
        var initMessage1 = $"🎯 [INIT_STATE] IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}";
        var initMessage2 = $"🎯 [INIT_STATE] IsStartStopEnabled={IsStartStopEnabled}, StartStopText='{StartStopText}'";
        
        DebugLogUtility.WriteLog(initMessage1);
        DebugLogUtility.WriteLog(initMessage2);
        
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {initMessage1}{Environment.NewLine}");
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {initMessage2}{Environment.NewLine}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InitializePropertyChangeHandlers ファイル書き込みエラー: {ex.Message}");
        }
        
        // IsLoadingプロパティの変更を監視して依存プロパティの変更通知を発行
        this.WhenAnyValue(x => x.IsLoading)
            .Subscribe(isLoading =>
            {
                DebugLogUtility.WriteLog($"🔄 IsLoading状態変更: {isLoading}");
                this.RaisePropertyChanged(nameof(LoadingText));
                this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                this.RaisePropertyChanged(nameof(SettingsEnabled));
                this.RaisePropertyChanged(nameof(StartStopText));
            });
            
        // IsOcrInitializedプロパティの変更を監視
        this.WhenAnyValue(x => x.IsOcrInitialized)
            .Subscribe(isInitialized =>
            {
                DebugLogUtility.WriteLog($"🔄 OCR初期化状態変更: {isInitialized}");
                this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
            });
            
        // IsWindowSelectedプロパティの変更を監視
        this.WhenAnyValue(x => x.IsWindowSelected)
            .Subscribe(isSelected =>
            {
                DebugLogUtility.WriteLog($"🔄 ウィンドウ選択状態変更: {isSelected}");
                this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                this.RaisePropertyChanged(nameof(StartStopText));
            });
    }

    #endregion

    #region OCR Initialization Monitoring

    /// <summary>
    /// OCR初期化状態を監視し、完了時にUI状態を更新
    /// </summary>
    private async Task MonitorOcrInitializationAsync()
    {
        try
        {
            DebugLogUtility.WriteLog("🔄 OCR初期化監視開始");
            
            var timeout = TimeSpan.FromSeconds(30); // 30秒でタイムアウト
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    // ServiceProviderからOCRサービスを取得して初期化状態をチェック
                    var serviceProvider = Program.ServiceProvider;
                    DebugLogUtility.WriteLog($"🔍 ServiceProvider: {serviceProvider != null}");
                    if (serviceProvider != null)
                    {
                        var ocrService = serviceProvider.GetService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                        DebugLogUtility.WriteLog($"🔍 IOcrEngine取得: {ocrService != null}");
                        if (ocrService != null)
                        {
                            // OCRサービスが初期化済みかチェック
                            var isInitialized = await CheckOcrServiceInitialized(ocrService).ConfigureAwait(false);
                            if (isInitialized)
                            {
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                {
                                    IsOcrInitialized = true;
                                    CurrentStatus = TranslationStatus.Idle;
                                    DebugLogUtility.WriteLog("✅ OCR初期化完了 - UI状態更新");
                                });
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogUtility.WriteLog($"⚠️ OCR初期化チェックエラー: {ex.Message}");
                    DebugLogUtility.WriteLog($"⚠️ スタックトレース: {ex.StackTrace}");
                }
                
                await Task.Delay(500).ConfigureAwait(false); // 500ms間隔でチェック
            }
            
            // タイムアウト時の処理
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsOcrInitialized = true; // タイムアウト時は強制的に初期化済みとする
                CurrentStatus = TranslationStatus.Idle;
                DebugLogUtility.WriteLog("⏰ OCR初期化監視タイムアウト - 強制的に初期化済み状態に移行");
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "OCR初期化監視でエラーが発生");
            DebugLogUtility.WriteLog($"❌ OCR初期化監視エラー: {ex.Message}");
            
            // エラー時も強制的に初期化済み状態にする
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsOcrInitialized = true;
                CurrentStatus = TranslationStatus.Idle;
            });
        }
    }

    /// <summary>
    /// OCRサービスの初期化状態をチェック
    /// </summary>
    private async Task<bool> CheckOcrServiceInitialized(Baketa.Core.Abstractions.OCR.IOcrEngine ocrService)
    {
        try
        {
            // OCRサービスに初期化済みかどうかの確認メソッドがあるかチェック
            if (ocrService.GetType().GetProperty("IsInitialized") is var prop && prop != null)
            {
                var isInitialized = (bool)(prop.GetValue(ocrService) ?? false);
                return isInitialized;
            }
            
            // フォールバック: InitializeAsyncを呼んでみて、すでに初期化済みの場合は即座に完了する
            await ocrService.InitializeAsync().ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteSelectWindowAsync()
    {
        DebugLogUtility.WriteLog("🖥️ ExecuteSelectWindowAsync開始");
        Logger?.LogInformation("ウィンドウ選択処理開始");
        
        try
        {
            var selectedWindow = await ShowWindowSelectionDialogAsync().ConfigureAwait(false);
            if (selectedWindow == null)
            {
                DebugLogUtility.WriteLog("❌ ウィンドウ選択がキャンセルされました");
                Logger?.LogDebug("ウィンドウ選択がキャンセルされました");
                return;
            }
            
            DebugLogUtility.WriteLog($"✅ ウィンドウが選択されました: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Logger?.LogInformation("ウィンドウが選択されました: '{Title}' (Handle={Handle})", selectedWindow.Title, selectedWindow.Handle);
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedWindow = selectedWindow;
                IsWindowSelected = true;
                CurrentStatus = TranslationStatus.Ready; // 準備完了状態
            });
            
            DebugLogUtility.WriteLog($"✅ ウィンドウ選択処理完了 - IsWindowSelected: {IsWindowSelected}, IsStartStopEnabled: {IsStartStopEnabled}");
            Logger?.LogInformation("ウィンドウ選択処理完了");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "ウィンドウ選択処理中にエラーが発生");
            DebugLogUtility.WriteLog($"❌ ウィンドウ選択処理エラー: {ex.Message}");
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsWindowSelected = false;
                SelectedWindow = null;
            });
        }
    }

    private async Task ExecuteStartStopAsync()
    {
        Console.WriteLine("🔥🔥🔥 ExecuteStartStopAsync メソッドが呼び出されました！🔥🔥🔥");
        Console.WriteLine($"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        DebugLogUtility.WriteLog("🔥🔥🔥 ExecuteStartStopAsync メソッドが呼び出されました！🔥🔥🔥");
        DebugLogUtility.WriteLog($"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔥🔥🔥 ExecuteStartStopAsync メソッドが呼び出されました！🔥🔥🔥");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        
        try
        {
            DebugLogUtility.WriteLog($"🔍 IsTranslationActive = {IsTranslationActive}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 IsTranslationActive = {IsTranslationActive}");
            
            if (IsTranslationActive)
            {
                DebugLogUtility.WriteLog("🔴 StopTranslationAsync呼び出し");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 StopTranslationAsync呼び出し");
                await StopTranslationAsync().ConfigureAwait(false);
            }
            else
            {
                DebugLogUtility.WriteLog("🟢 StartTranslationAsync呼び出し");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🟢 StartTranslationAsync呼び出し");
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
        DebugLogUtility.WriteLog("🚀 StartTranslationAsync開始");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 StartTranslationAsync開始");
        Logger?.LogInformation("🚀 翻訳ワークフローを開始");

        try
        {
            // 既に選択されたウィンドウを使用
            var selectedWindow = SelectedWindow;
            if (selectedWindow == null)
            {
                DebugLogUtility.WriteLog("❌ ウィンドウが選択されていません");
                Logger?.LogError("ウィンドウが選択されていない状態で翻訳開始が要求されました");
                return;
            }

            DebugLogUtility.WriteLog($"✅ 選択済みウィンドウを使用: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Logger?.LogInformation("選択済みウィンドウを使用: '{Title}' (Handle={Handle})", selectedWindow.Title, selectedWindow.Handle);

            // ローディング状態開始（ウィンドウ選択後）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                DebugLogUtility.WriteLog($"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
            });
            
            // 画面中央ローディングオーバーレイ表示
            DebugLogUtility.WriteLog("🔄 LoadingOverlayManager.ShowAsync呼び出し開始");
            try
            {
                await _loadingManager.ShowAsync().ConfigureAwait(false);
                DebugLogUtility.WriteLog("✅ LoadingOverlayManager.ShowAsync呼び出し完了");
            }
            catch (Exception loadingEx)
            {
                DebugLogUtility.WriteLog($"❌ LoadingOverlayManager.ShowAsync例外: {loadingEx.Message}");
                Logger?.LogError(loadingEx, "ローディングオーバーレイ表示に失敗");
            }

            // 2. 翻訳開始
            var uiTimer = System.Diagnostics.Stopwatch.StartNew();
            DebugLogUtility.WriteLog("📊 翻訳状態をアクティブに設定");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📊 翻訳状態をアクティブに設定");
            Logger?.LogDebug("📊 翻訳状態をキャプチャ中に設定");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Capturing; // TranslationStatus.Activeがないため適切な値を使用
                IsTranslationActive = true;
                IsTranslationResultVisible = true; // 翻訳開始時は表示状態に設定
                IsLoading = false; // ローディング状態終了
                DebugLogUtility.WriteLog($"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
            });
            
            // 画面中央ローディングオーバーレイ非表示
            DebugLogUtility.WriteLog("🔄 LoadingOverlayManager.HideAsync呼び出し開始");
            try
            {
                await _loadingManager.HideAsync().ConfigureAwait(false);
                DebugLogUtility.WriteLog("✅ LoadingOverlayManager.HideAsync呼び出し完了");
            }
            catch (Exception loadingEx)
            {
                DebugLogUtility.WriteLog($"❌ LoadingOverlayManager.HideAsync例外: {loadingEx.Message}");
                Logger?.LogError(loadingEx, "ローディングオーバーレイ非表示に失敗");
            }
            uiTimer.Stop();
            DebugLogUtility.WriteLog($"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");

            // オーバーレイマネージャーを初期化
            var overlayInitTimer = System.Diagnostics.Stopwatch.StartNew();
            DebugLogUtility.WriteLog("🖼️ オーバーレイマネージャー初期化開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー初期化開始");
            Logger?.LogDebug("🖼️ オーバーレイマネージャーを初期化");
            
            await _inPlaceOverlayManager.InitializeAsync().ConfigureAwait(false);
            overlayInitTimer.Stop();
            DebugLogUtility.WriteLog($"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            
            // オーバーレイマネージャーを表示状態に設定
            DebugLogUtility.WriteLog("🖼️ オーバーレイマネージャー表示状態設定開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー表示状態設定開始");
            // ARオーバーレイは自動で表示管理（表示はTextChunk個別処理）
            DebugLogUtility.WriteLog("✅ オーバーレイマネージャー表示状態設定完了");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ オーバーレイマネージャー表示状態設定完了");

            var eventTimer = System.Diagnostics.Stopwatch.StartNew();
            DebugLogUtility.WriteLog("📢 StartTranslationRequestEventを発行");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📢 StartTranslationRequestEventを発行");
            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            var startTranslationEvent = new StartTranslationRequestEvent(selectedWindow);
            DebugLogUtility.WriteLog($"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            
            // 直接ファイル書き込みでイベント発行を記録
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DIRECT] MainOverlayViewModel - StartTranslationRequestEvent発行開始: EventID={startTranslationEvent.Id}, Window={selectedWindow.Title}{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"MainOverlayViewModel ファイル書き込みエラー: {fileEx.Message}");
            }
            
            await PublishEventAsync(startTranslationEvent).ConfigureAwait(false);
            
            try
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎯 [DIRECT] MainOverlayViewModel - StartTranslationRequestEvent発行完了: EventID={startTranslationEvent.Id}{Environment.NewLine}");
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"MainOverlayViewModel ファイル書き込みエラー: {fileEx.Message}");
            }
            
            eventTimer.Stop();
            DebugLogUtility.WriteLog($"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");

            overallTimer.Stop();
            DebugLogUtility.WriteLog($"⏱️ 【総合時間】翻訳開始処理全体: {overallTimer.ElapsedMilliseconds}ms (UI更新: {uiTimer.ElapsedMilliseconds}ms, オーバーレイ初期化: {overlayInitTimer.ElapsedMilliseconds}ms, イベント処理: {eventTimer.ElapsedMilliseconds}ms)");
            
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
                DebugLogUtility.WriteLog($"⚠️ ローディング非表示エラー: {loadingEx.Message}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ ローディング非表示エラー: {loadingEx.Message}");
            }
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
                IsTranslationActive = false;
                IsLoading = false; // ローディング状態終了
                DebugLogUtility.WriteLog($"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
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
            DebugLogUtility.WriteLog("🏁 ShowWindowSelectionDialogAsync開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏁 ShowWindowSelectionDialogAsync開始");
            
            DebugLogUtility.WriteLog("🏁 WindowManagerAdapter確認開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🏁 WindowManagerAdapter状態: {(_windowManager != null ? "利用可能" : "null")}");
            
            var dialogViewModel = new WindowSelectionDialogViewModel(EventAggregator, 
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<WindowSelectionDialogViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance), _windowManager!);
            var dialog = new WindowSelectionDialogView
            {
                DataContext = dialogViewModel
            };

            DebugLogUtility.WriteLog("🏁 ダイアログViewModel・View作成完了");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏁 ダイアログViewModel・View作成完了");

            // UIスレッドで安全にApplication.Currentにアクセス
            var owner = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                return Avalonia.Application.Current?.ApplicationLifetime
                    is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                    ? desktop.MainWindow : null;
            });
            
            DebugLogUtility.WriteLog($"🏁 オーナーウィンドウ取得: {(owner != null ? "成功" : "null")}");
            
            WindowInfo? result = null;
            if (owner != null)
            {
                DebugLogUtility.WriteLog("🏁 ShowDialogでダイアログ表示開始");
                result = await dialog.ShowDialog<WindowInfo?>(owner).ConfigureAwait(false);
                DebugLogUtility.WriteLog($"🏁 ShowDialog完了: {(result != null ? $"結果='{result.Title}'" : "null")}");
            }
            else
            {
                DebugLogUtility.WriteLog("🏁 Showでダイアログ表示開始（フォールバック）");
                dialog.Show();
                // ShowDialogではなくShowで表示し、IsClosedで制御
                while (!dialogViewModel.IsClosed)
                {
                    await Task.Delay(100).ConfigureAwait(false);
                    DebugLogUtility.WriteLog($"🏁 ダイアログ待機中: IsClosed={dialogViewModel.IsClosed}");
                }
                result = dialogViewModel.DialogResult;
                DebugLogUtility.WriteLog($"🏁 ダイアログ結果取得: {(result != null ? $"結果='{result.Title}'" : "null")}");
                dialog.Close();
            }

            DebugLogUtility.WriteLog($"🏁 ShowWindowSelectionDialogAsync完了: {(result != null ? $"成功='{result.Title}'" : "キャンセル")}");
            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to show window selection dialog");
            DebugLogUtility.WriteLog($"🏁 ShowWindowSelectionDialogAsyncエラー: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🏁 ShowWindowSelectionDialogAsyncエラー: {ex.Message}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🏁 エラースタックトレース: {ex.StackTrace}");
            return null;
        }
    }

    private async Task StopTranslationAsync()
    {
        DebugLogUtility.WriteLog("🔴 翻訳停止処理開始");
        Logger?.LogInformation("Stopping translation");

        // 翻訳停止（ウィンドウ選択状態は維持）
        DebugLogUtility.WriteLog("🔴 翻訳状態をアイドルに設定");
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = IsWindowSelected ? TranslationStatus.Ready : TranslationStatus.Idle; // ウィンドウ選択状態に応じて遷移
            IsTranslationActive = false;
            IsTranslationResultVisible = false; // 翻訳停止時は非表示にリセット
            // IsWindowSelectedとSelectedWindowは維持（再選択不要）
            DebugLogUtility.WriteLog($"✅ 翻訳停止状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsTranslationResultVisible={IsTranslationResultVisible}, IsWindowSelected={IsWindowSelected}");
        });

        // オーバーレイを非表示にしてリセット
        await _inPlaceOverlayManager.HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
        await _inPlaceOverlayManager.ResetAsync().ConfigureAwait(false);

        var stopTranslationEvent = new StopTranslationRequestEvent();
        await PublishEventAsync(stopTranslationEvent).ConfigureAwait(false);

        DebugLogUtility.WriteLog("✅ 翻訳停止処理完了");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ 翻訳停止処理完了");
        Logger?.LogInformation("Translation stopped successfully");
    }

    private async void ExecuteShowHide()
    {
        DebugLogUtility.WriteLog($"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        
        // 翻訳が非アクティブの場合は何もしない（安全措置）
        if (!IsTranslationActive)
        {
            DebugLogUtility.WriteLog("⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            Logger?.LogWarning("非表示ボタンが翻訳非アクティブ時に押されました");
            return;
        }
        
        Logger?.LogDebug("Show/Hide toggle requested - Current: {Current} -> New: {New}", IsTranslationResultVisible, !IsTranslationResultVisible);
        
        var newVisibility = !IsTranslationResultVisible;
        DebugLogUtility.WriteLog($"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        
        IsTranslationResultVisible = newVisibility;
        
        // 重複処理除去: オーバーレイの制御はTranslationFlowEventProcessorで一元管理
        DebugLogUtility.WriteLog($"👁️ オーバーレイ表示状態変更: {IsTranslationResultVisible} (処理はイベント経由で実行)");
        
        var toggleEvent = new ToggleTranslationDisplayRequestEvent(IsTranslationResultVisible);
        await PublishEventAsync(toggleEvent).ConfigureAwait(false);
        
        DebugLogUtility.WriteLog($"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        Logger?.LogDebug("Translation display visibility toggled: {IsVisible}", IsTranslationResultVisible);
    }

    private static SimpleSettingsView? _currentSettingsDialog;

    private async void ExecuteSettings()
    {
        // 即座にアラートを表示してコマンドが呼ばれたことを確認
        DebugLogUtility.WriteLog("🚨🚨🚨 ExecuteSettings が呼ばれました！🚨🚨🚨");
        
        try
        {
            var currentDialogHash = _currentSettingsDialog?.GetHashCode();
            DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] ExecuteSettings開始 - 現在のダイアログ: {currentDialogHash}");
            DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] IsLoading: {IsLoading}, SettingsEnabled: {SettingsEnabled}");
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

            // DIコンテナ経由でSimpleSettingsViewModelを取得
            var settingsViewModel = _serviceProvider.GetRequiredService<SimpleSettingsViewModel>();
            var vmHash = settingsViewModel.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsViewModel作成: {vmHash}");

            // ViewModelの設定を読み込み
            DebugHelper.Log($"🔧 [MainOverlayViewModel] LoadSettingsAsync呼び出し前");
            try
            {
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    await settingsViewModel.LoadSettingsAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
                DebugHelper.Log($"🔧 [MainOverlayViewModel] LoadSettingsAsync呼び出し完了");
            }
            catch (Exception loadEx)
            {
                DebugHelper.Log($"💥 [MainOverlayViewModel] LoadSettingsAsync例外: {loadEx.Message}");
            }

            // 設定ダイアログをUIスレッドで作成
            var dialogHash = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _currentSettingsDialog = new SimpleSettingsView
                {
                    DataContext = settingsViewModel
                };
                var hash = _currentSettingsDialog.GetHashCode();
                DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsView作成: {hash}");
                return hash;
            });

            // ダイアログが閉じられたときの処理
            _currentSettingsDialog!.Closed += (_, _) =>
            {
                DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] Settings dialog Closedイベント - ダイアログ: {dialogHash}");
                Logger?.LogDebug("Settings dialog closed event received");
                var previousDialog = _currentSettingsDialog;
                _currentSettingsDialog = null;
                DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] _currentSettingsDialogをnullに設定 - 前の値: {previousDialog?.GetHashCode()}");
            };

            // ViewModelのCloseRequestedイベントハンドル - 直接Close()を呼び出し
            if (settingsViewModel != null)
            {
                settingsViewModel.CloseRequested += () =>
                {
                    DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] Settings dialog close requested by ViewModel - VM: {vmHash}");
                    Logger?.LogDebug("Settings dialog close requested by ViewModel");
                    var dialog = _currentSettingsDialog;
                    var currentDialogHash2 = dialog?.GetHashCode();
                    DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] 現在のダイアログ状態: {currentDialogHash2}, 作成時: {dialogHash}");
                    if (dialog != null)
                    {
                        DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] 直接Close()を呼び出し - 対象: {currentDialogHash2}");
                        dialog.Close();
                        DebugLogUtility.WriteLog($"🔧 [MainOverlayViewModel] Close()完了 - 対象: {currentDialogHash2}");
                    }
                    else
                    {
                        DebugLogUtility.WriteLog($"⚠️ [MainOverlayViewModel] _currentSettingsDialogがnull");
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
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _currentSettingsDialog.Show();
            });
            DebugHelper.Log($"🔧 [MainOverlayViewModel] Show()完了 - ダイアログ: {dialogHash}");
            
            Logger?.LogDebug("Settings dialog opened");
        }
        catch (Exception ex)
        {
            DebugLogUtility.WriteLog($"💥 [MainOverlayViewModel] ExecuteSettingsエラー: {ex.Message}");
            DebugLogUtility.WriteLog($"💥 [MainOverlayViewModel] スタックトレース: {ex.StackTrace}");
            DebugHelper.Log($"💥 [MainOverlayViewModel] ExecuteSettingsエラー: {ex.Message}");
            DebugHelper.Log($"💥 [MainOverlayViewModel] スタックトレース: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                DebugLogUtility.WriteLog($"💥 [MainOverlayViewModel] InnerException: {ex.InnerException.Message}");
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
            IsTranslationActive = statusEvent.Status == TranslationStatus.Capturing 
                                  || statusEvent.Status == TranslationStatus.ProcessingOCR 
                                  || statusEvent.Status == TranslationStatus.Translating;
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