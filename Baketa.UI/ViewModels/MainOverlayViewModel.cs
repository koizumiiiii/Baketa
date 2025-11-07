#pragma warning disable CS0618 // Type or member is obsolete
using Baketa.Application.Services.Diagnostics;
using Baketa.Application.Services.Translation;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.EventTypes;
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
using System.Globalization;
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

    // 🚀 EventHandler初期化完了状態（UI安全性向上）
    private bool _isEventHandlerInitialized;

    // 🔥 [PHASE2_PROBLEM2] 翻訳エンジン初期化状態（StartButton制御）
    private bool _isTranslationEngineInitializing;

    private WindowInfo? _selectedWindow;

    public MainOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<MainOverlayViewModel> logger,
        IWindowManagerAdapter windowManager,
        IInPlaceTranslationOverlayManager inPlaceOverlayManager,
        LoadingOverlayManager loadingManager,
        IDiagnosticReportService diagnosticReportService,
        IWindowManagementService windowManagementService,
        ITranslationControlService translationControlService,
        SimpleSettingsViewModel settingsViewModel)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _inPlaceOverlayManager = inPlaceOverlayManager ?? throw new ArgumentNullException(nameof(inPlaceOverlayManager));
        _loadingManager = loadingManager ?? throw new ArgumentNullException(nameof(loadingManager));
        _diagnosticReportService = diagnosticReportService ?? throw new ArgumentNullException(nameof(diagnosticReportService));
        _windowManagementService = windowManagementService ?? throw new ArgumentNullException(nameof(windowManagementService));
        _translationControlService = translationControlService ?? throw new ArgumentNullException(nameof(translationControlService));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

        // 初期状態設定 - OCR初期化状態を動的に管理
        _isOcrInitialized = false; // OCR初期化を正常に監視（MonitorOcrInitializationAsyncで設定）
        _currentStatus = TranslationStatus.Idle; // アイドル状態から開始

        // 🔥 [FIX] 翻訳エンジンは既に起動済み（ServerManagerHostedServiceで起動）
        // MainOverlayViewModel初期化時点でサーバーは準備完了しているため、falseで開始
        _isTranslationEngineInitializing = false;
        
        Logger?.LogDebug("🎯 NEW UI FLOW VERSION - MainOverlayViewModel初期化完了");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🎯 NEW UI FLOW VERSION - MainOverlayViewModel初期化完了");
        
        // 直接ファイル書き込みでも記録
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
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
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IWindowManagementService _windowManagementService;
    private readonly ITranslationControlService _translationControlService;
    private readonly SimpleSettingsViewModel _settingsViewModel;

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
                    // 🔥 [PHASE6.1_GEMINI_FIX] 自分自身の変更通知を追加（WhenAnyValue検知のため必須）
                    this.RaisePropertyChanged(nameof(IsTranslationActive));

                    // 依存プロパティの通知
                    this.RaisePropertyChanged(nameof(StartStopText));
                    this.RaisePropertyChanged(nameof(SettingsEnabled));
                    this.RaisePropertyChanged(nameof(ShowHideEnabled));
                    this.RaisePropertyChanged(nameof(IsStartStopEnabled)); // 🔧 CRITICAL FIX: StartStopCommandのCanExecute更新
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // 🔥 [PHASE6.1_GEMINI_FIX] 自分自身の変更通知を追加（WhenAnyValue検知のため必須）
                        this.RaisePropertyChanged(nameof(IsTranslationActive));

                        // 依存プロパティの通知
                        this.RaisePropertyChanged(nameof(StartStopText));
                        this.RaisePropertyChanged(nameof(SettingsEnabled));
                        this.RaisePropertyChanged(nameof(ShowHideEnabled));
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled)); // 🔧 CRITICAL FIX: StartStopCommandのCanExecute更新
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
                    // 🔧 [ULTRATHINK_ROOT_CAUSE_FIX] Start/Stopボタン状態更新通知追加
                    this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                        // 🔧 [ULTRATHINK_ROOT_CAUSE_FIX] Start/Stopボタン状態更新通知追加
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                    });
                }
            }
        }
    }

    /// <summary>
    /// EventHandler初期化完了状態 - Start button UI safety
    /// </summary>
    public bool IsEventHandlerInitialized
    {
        get => _isEventHandlerInitialized;
        set
        {
            var changed = SetPropertySafe(ref _isEventHandlerInitialized, value);
            if (changed)
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                    });
                }

                Logger?.LogDebug($"🚀 EventHandler初期化状態変更: IsEventHandlerInitialized={value}");
            }
        }
    }

    /// <summary>
    /// 🔥 [PHASE2_PROBLEM2] 翻訳エンジン初期化状態 - Start button制御
    /// TranslationInitializationServiceがPythonサーバー起動完了後にfalseに設定
    /// </summary>
    public bool IsTranslationEngineInitializing
    {
        get => _isTranslationEngineInitializing;
        set
        {
            var changed = SetPropertySafe(ref _isTranslationEngineInitializing, value);
            if (changed)
            {
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                    });
                }

                Logger?.LogDebug($"🔥 [PHASE2_PROBLEM2] 翻訳エンジン初期化状態変更: IsTranslationEngineInitializing={value}");
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
            // 🔥 [PHASE6.1_ROOT_CAUSE_FIX] Start/Stop両方の条件を正しく実装
            // Start可能条件: ウィンドウ選択済み、OCR初期化完了、ローディング中でない、翻訳中でない
            var canStart = !IsLoading && IsWindowSelected && IsOcrInitialized && IsEventHandlerInitialized && !IsTranslationEngineInitializing && !IsTranslationActive;

            // Stop可能条件: 翻訳実行中、ローディング中でない
            var canStop = IsTranslationActive && !IsLoading;

            var enabled = canStart || canStop;

            Logger?.LogDebug($"🔍 IsStartStopEnabled計算: canStart={canStart}, canStop={canStop}, IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}, IsEventHandlerInitialized={IsEventHandlerInitialized}, IsTranslationEngineInitializing={IsTranslationEngineInitializing}, 結果={enabled}");

            // デバッグ用に実際の状態をファイルログにも出力
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                    $"🔍 [START_BUTTON_STATE] IsStartStopEnabled={enabled}, canStart={canStart}, canStop={canStop}, IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}, IsEventHandlerInitialized={IsEventHandlerInitialized}, IsTranslationEngineInitializing={IsTranslationEngineInitializing}");
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
            Logger?.LogDebug($"🔍 StartStopText計算: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, 結果='{result}'");
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
            // 🔥 [PHASE6.1_FINAL_FIX_V2] WhenAnyValueによる依存プロパティ監視 + 初期値発行
            // 根本原因: Cold ObservableはSubscribeされるまで値を発行しない
            // 解決策1: 依存する6つのプロパティを個別に監視
            // 解決策2: StartWith()で初期値を強制的に発行してReactiveCommandに確実に通知

            Console.WriteLine("🔧🔧🔧 [INIT] canExecuteObservable作成開始");

            var canExecuteObservable = this.WhenAnyValue(
                x => x.IsLoading,
                x => x.IsWindowSelected,
                x => x.IsOcrInitialized,
                x => x.IsEventHandlerInitialized,
                x => x.IsTranslationEngineInitializing,
                x => x.IsTranslationActive,
                (isLoading, isWindowSelected, isOcrInitialized, isEventHandlerInitialized, isTranslationEngineInitializing, isTranslationActive) =>
                {
                    // Start可能条件: ウィンドウ選択済み、OCR初期化完了、ローディング中でない、翻訳中でない
                    var canStart = !isLoading && isWindowSelected && isOcrInitialized && isEventHandlerInitialized && !isTranslationEngineInitializing && !isTranslationActive;

                    // Stop可能条件: 翻訳実行中、ローディング中でない
                    var canStop = isTranslationActive && !isLoading;

                    var enabled = canStart || canStop;

                    Console.WriteLine($"🔍🔍🔍 [OBSERVABLE_CHANGE] canExecute計算: canStart={canStart}, canStop={canStop}, IsTranslationActive={isTranslationActive}, enabled={enabled}, Thread:{System.Threading.Thread.CurrentThread.ManagedThreadId}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [OBSERVABLE_CHANGE] canStart={canStart}, canStop={canStop}, IsTranslationActive={isTranslationActive}, enabled={enabled}");

                    return enabled;
                })
                .Do(canExecute =>
                {
                    Console.WriteLine($"🔍🔍🔍 [DO_OPERATOR] canExecute値: {canExecute}, Thread:{System.Threading.Thread.CurrentThread.ManagedThreadId}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [DO_OPERATOR] canExecute値: {canExecute}");
                })
                .StartWith(false) // 🔥 [PHASE6.1_FINAL_FIX_V3] Cold Observable問題の完全解決 - 初期値を強制発行
                .ObserveOn(RxApp.MainThreadScheduler);

            Console.WriteLine("🔧🔧🔧 [INIT] canExecuteObservable作成完了");
                
            Logger?.LogDebug("🏗️ ReactiveCommand.CreateFromTask開始");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🏗️ ReactiveCommand.CreateFromTask開始");
                
            var startStopCmd = ReactiveCommand.CreateFromTask(ExecuteStartStopAsync,
                canExecuteObservable, // ローディング中は無効
                outputScheduler: RxApp.MainThreadScheduler);
                
            Logger?.LogDebug("✅ ReactiveCommand.CreateFromTask完了");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ ReactiveCommand.CreateFromTask完了");
            
            // StartStopCommandの実行をトラッキング（開始と完了を分けて記録）
            startStopCmd.IsExecuting.Subscribe(isExecuting =>
            {
                if (isExecuting)
                {
                    Logger?.LogDebug("🚀 StartStopCommand実行開始");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 StartStopCommand実行開始");
                }
                else
                {
                    Logger?.LogDebug("✅ StartStopCommand実行完了");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ StartStopCommand実行完了");
                }
            });
            
            // 🔥 [PHASE6.1_DIAGNOSTIC_DEEP] コマンド結果の監視
            startStopCmd.Subscribe(result =>
            {
                Console.WriteLine($"🎬🎬🎬 [COMMAND_SUBSCRIBE] StartStopCommand.Subscribe()実行！IsTranslationActive={IsTranslationActive}, Thread:{System.Threading.Thread.CurrentThread.ManagedThreadId}");
                Logger?.LogDebug($"🎬 StartStopCommandの結果を受信: {result.GetType().Name}");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🎬 [COMMAND_SUBSCRIBE] StartStopCommand.Subscribe()実行！IsTranslationActive={IsTranslationActive}");
            });
            
            // StartStopCommandのエラーをトラッキング
            startStopCmd.ThrownExceptions.Subscribe(ex =>
            {
                Logger?.LogDebug($"❌ StartStopCommandでエラー発生: {ex.Message}");
                Logger?.LogDebug($"❌ スタックトレース: {ex.StackTrace}");
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
                Logger?.LogDebug("🔧 SettingsCommandが実行されました");
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

        // 🔥 [PHASE2_PROBLEM2] Pythonサーバー状態変更イベントの購読（翻訳エンジン初期化完了検知）
        SubscribeToEvent<Baketa.Core.Events.EventTypes.PythonServerStatusChangedEvent>(OnPythonServerStatusChanged);
    }

    private void InitializePropertyChangeHandlers()
    {
        // 初期状態をログ出力 - 直接ファイル書き込みで確実に出力
        var initMessage1 = $"🎯 [INIT_STATE] IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}";
        var initMessage2 = $"🎯 [INIT_STATE] IsStartStopEnabled={IsStartStopEnabled}, StartStopText='{StartStopText}'";
        
        Logger?.LogDebug(initMessage1);
        Logger?.LogDebug(initMessage2);
        
        try
        {
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"InitializePropertyChangeHandlers ファイル書き込みエラー: {ex.Message}");
        }
        
        // IsLoadingプロパティの変更を監視して依存プロパティの変更通知を発行
        this.WhenAnyValue(x => x.IsLoading)
            .Subscribe(isLoading =>
            {
                Logger?.LogDebug($"🔄 IsLoading状態変更: {isLoading}");
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
                Logger?.LogDebug($"🔄 OCR初期化状態変更: {isInitialized}");
                this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
            });
            
        // IsWindowSelectedプロパティの変更を監視
        this.WhenAnyValue(x => x.IsWindowSelected)
            .Subscribe(isSelected =>
            {
                Logger?.LogDebug($"🔄 ウィンドウ選択状態変更: {isSelected}");
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
            Logger?.LogDebug("🔄 OCR初期化監視開始");
            
            var timeout = TimeSpan.FromSeconds(30); // 30秒でタイムアウト
            var startTime = DateTime.UtcNow;
            
            while (DateTime.UtcNow - startTime < timeout)
            {
                try
                {
                    // ServiceProviderからOCRサービスを取得して初期化状態をチェック
                    var serviceProvider = Program.ServiceProvider;
                    Logger?.LogDebug($"🔍 ServiceProvider: {serviceProvider != null}");
                    if (serviceProvider != null)
                    {
                        var ocrService = serviceProvider.GetService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                        Logger?.LogDebug($"🔍 IOcrEngine取得: {ocrService != null}");
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
                                    Logger?.LogDebug("✅ OCR初期化完了 - UI状態更新");
                                });
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug($"⚠️ OCR初期化チェックエラー: {ex.Message}");
                    Logger?.LogDebug($"⚠️ スタックトレース: {ex.StackTrace}");
                }
                
                await Task.Delay(500).ConfigureAwait(false); // 500ms間隔でチェック
            }
            
            // タイムアウト時の処理
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsOcrInitialized = true; // タイムアウト時は強制的に初期化済みとする
                CurrentStatus = TranslationStatus.Idle;
                Logger?.LogDebug("⏰ OCR初期化監視タイムアウト - 強制的に初期化済み状態に移行");
            });
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "OCR初期化監視でエラーが発生");
            Logger?.LogDebug($"❌ OCR初期化監視エラー: {ex.Message}");
            
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
            // 🔥 [PHASE13.2.21] 型情報診断ログ追加
            Logger?.LogDebug($"🔍 [PHASE13.2.21] IOcrEngine実際の型: {ocrService.GetType().FullName}");
            Logger?.LogDebug($"🔍 [PHASE13.2.21] IOcrEngine.GetType().Name: {ocrService.GetType().Name}");

            // 🔥 [PHASE13.2.30] WarmupAsync重複実行防止: PooledOcrServiceが自動的にWarmupAsyncを実行
            // 根本原因: MainOverlayViewModelとPooledOcrService両方がWarmupAsyncを呼び出し、
            //           2回目のWarmupAsyncでPaddlePredictor(Detector) run failedエラーが発生
            // 修正内容: MainOverlayViewModelでのWarmupAsync強制実行を削除し、
            //           PooledOcrServiceの自動WarmupAsyncに任せる
            if (ocrService.GetType().GetProperty("IsInitialized") is var prop && prop != null)
            {
                var isInitialized = (bool)(prop.GetValue(ocrService) ?? false);
                Logger?.LogDebug($"🔍 [PHASE13.2.30] OCR IsInitialized: {isInitialized}");

                if (isInitialized)
                {
                    // ✅ [PHASE13.2.30] PooledOcrServiceが既にWarmupAsync実行済み - そのまま成功を返す
                    Logger?.LogDebug("✅ [PHASE13.2.30] PooledOcrService初期化済み - WarmupAsync不要");
                    return true;
                }

                // 未初期化の場合はInitializeAsync()を呼び出す（後続のフォールバック処理へ）
                Logger?.LogDebug("🔍 [PHASE13.2.30] IsInitialized=false - InitializeAsync実行へ");
            }

            // フォールバック: InitializeAsyncを呼んでみて、初期化結果を返す
            Logger?.LogDebug("🔥 [PHASE13.2.20] OCR InitializeAsync呼び出し開始");
            var result = await ocrService.InitializeAsync().ConfigureAwait(false);
            Logger?.LogDebug($"🔍 [PHASE13.2.20] OCR InitializeAsync結果: {result}");
            return result;
        }
        catch (Exception ex)
        {
            Logger?.LogDebug($"❌ OCR初期化チェックエラー: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteSelectWindowAsync()
    {
        Logger?.LogDebug("🖥️ ExecuteSelectWindowAsync開始");
        Console.WriteLine("🖥️ MainOverlayViewModel.ExecuteSelectWindowAsync開始");
        Logger?.LogInformation("ウィンドウ選択処理開始");
        
        try
        {
            Console.WriteLine($"🔧 _windowManagementService null check: {_windowManagementService == null}");
            
            // 🔒 安全化: ウィンドウ選択開始前にネイティブキャプチャを一時停止
            Console.WriteLine("🔒 [SAFETY] ネイティブキャプチャを一時停止します");
            Logger?.LogDebug("🔒 [SAFETY] ネイティブキャプチャを一時停止します");
            Baketa.Infrastructure.Platform.Windows.Capture.NativeWindowsCaptureWrapper.PauseForWindowSelection();
            
            Console.WriteLine("🔧 _windowManagementService.ShowWindowSelectionAsync()呼び出し開始");
            
            // WindowManagementServiceを通じてウィンドウ選択ダイアログを表示
            var selectedWindow = await _windowManagementService.ShowWindowSelectionAsync().ConfigureAwait(false);
            
            Console.WriteLine($"🔧 _windowManagementService.ShowWindowSelectionAsync()呼び出し完了: result={selectedWindow != null}");
            
            if (selectedWindow == null)
            {
                // 🔒 安全化: キャンセル時もネイティブキャプチャを再開
                Console.WriteLine("🚀 [SAFETY] ウィンドウ選択キャンセル - ネイティブキャプチャを再開します");
                Logger?.LogDebug("🚀 [SAFETY] ウィンドウ選択キャンセル - ネイティブキャプチャを再開します");
                Baketa.Infrastructure.Platform.Windows.Capture.NativeWindowsCaptureWrapper.ResumeAfterWindowSelection();
                
                Logger?.LogDebug("❌ ウィンドウ選択がキャンセルされました");
                Console.WriteLine("❌ ウィンドウ選択がキャンセルされました");
                Logger?.LogDebug("ウィンドウ選択がキャンセルされました");
                return;
            }
            
            Logger?.LogDebug($"✅ ウィンドウが選択されました: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Console.WriteLine($"✅ ウィンドウが選択されました: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Logger?.LogInformation("ウィンドウが選択されました: '{Title}' (Handle={Handle})", selectedWindow.Title, selectedWindow.Handle);
            
            // 🔒 安全化: ウィンドウ選択完了後にネイティブキャプチャを再開
            Console.WriteLine("🚀 [SAFETY] ウィンドウ選択完了 - ネイティブキャプチャを再開します");
            Logger?.LogDebug("🚀 [SAFETY] ウィンドウ選択完了 - ネイティブキャプチャを再開します");
            Baketa.Infrastructure.Platform.Windows.Capture.NativeWindowsCaptureWrapper.ResumeAfterWindowSelection();
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                SelectedWindow = selectedWindow;
                IsWindowSelected = true;
                CurrentStatus = TranslationStatus.Ready; // 準備完了状態
            });
            
            Logger?.LogDebug($"✅ ウィンドウ選択処理完了 - IsWindowSelected: {IsWindowSelected}, IsStartStopEnabled: {IsStartStopEnabled}");
            Console.WriteLine($"✅ ウィンドウ選択処理完了 - IsWindowSelected: {IsWindowSelected}, IsStartStopEnabled: {IsStartStopEnabled}");
            Logger?.LogInformation("ウィンドウ選択処理完了");
        }
        catch (Exception ex)
        {
            // 🔒 安全化: エラー時もネイティブキャプチャを再開
            Console.WriteLine("🚀 [SAFETY] ウィンドウ選択エラー - ネイティブキャプチャを再開します");
            Logger?.LogDebug("🚀 [SAFETY] ウィンドウ選択エラー - ネイティブキャプチャを再開します");
            Baketa.Infrastructure.Platform.Windows.Capture.NativeWindowsCaptureWrapper.ResumeAfterWindowSelection();
            
            Logger?.LogError(ex, "ウィンドウ選択処理中にエラーが発生");
            Console.WriteLine($"💥 MainOverlayViewModel.ExecuteSelectWindowAsyncエラー: {ex.Message}");
            Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
            Logger?.LogDebug($"❌ ウィンドウ選択処理エラー: {ex.Message}");
            
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
        Logger?.LogDebug("🔥🔥🔥 ExecuteStartStopAsync メソッドが呼び出されました！🔥🔥🔥");
        Logger?.LogDebug($"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔥🔥🔥 ExecuteStartStopAsync メソッドが呼び出されました！🔥🔥🔥");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteStartStopAsync開始 - IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");

        // 🔧 [PHASE6.1_TEMPORARY_DISABLED] 診断レポート生成を一時的に無効化
        // 理由: Stop処理のブロッキング問題を切り分けるため
        // TODO: 根本原因解決後に再有効化
        /*
        {
            var operation = IsTranslationActive ? "Stop" : "Start";
            var trigger = $"execute_{operation.ToLower(CultureInfo.InvariantCulture)}_button_pressed";
            var context = $"ExecuteStartStopAsync {operation} operation";

            Logger?.LogDebug($"📊 診断レポート生成開始（バックグラウンド実行 - {operation}操作時）");
            Console.WriteLine($"📊 診断レポート生成開始（バックグラウンド実行 - {operation}操作時）");

            _ = Task.Run(() => _diagnosticReportService.GenerateReportAsync(trigger, context));
        }
        */
        
        try
        {
            Logger?.LogDebug($"🔍 IsTranslationActive = {IsTranslationActive}");
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 IsTranslationActive = {IsTranslationActive}");
            
            if (IsTranslationActive)
            {
                Logger?.LogDebug("🔴 StopTranslationAsync呼び出し");
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴 StopTranslationAsync呼び出し");
                await StopTranslationAsync().ConfigureAwait(false);
            }
            else
            {
                Logger?.LogDebug("🟢 StartTranslationAsync呼び出し");
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
        Logger?.LogDebug("🚀 StartTranslationAsync開始");
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🚀 StartTranslationAsync開始");
        Logger?.LogInformation("🚀 翻訳ワークフローを開始");

        // 🔧 診断レポート生成（統一サービス使用）
        Logger?.LogDebug("📊 診断レポート生成開始（統一サービス使用 - Start押下時）");
        await _diagnosticReportService.GenerateReportAsync("start_button_pressed", "StartTranslationAsync operation").ConfigureAwait(false);

        try
        {
            // 既に選択されたウィンドウを使用
            var selectedWindow = SelectedWindow;
            if (selectedWindow == null)
            {
                Logger?.LogDebug("❌ ウィンドウが選択されていません");
                Logger?.LogError("ウィンドウが選択されていない状態で翻訳開始が要求されました");
                return;
            }

            Logger?.LogDebug($"✅ 選択済みウィンドウを使用: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Logger?.LogInformation("選択済みウィンドウを使用: '{Title}' (Handle={Handle})", selectedWindow.Title, selectedWindow.Handle);

            // ローディング状態開始（ウィンドウ選択後）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                Logger?.LogDebug($"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
            });
            
            // 画面中央ローディングオーバーレイ表示
            Logger?.LogDebug("🔄 LoadingOverlayManager.ShowAsync呼び出し開始");
            try
            {
                await _loadingManager.ShowAsync().ConfigureAwait(false);
                Logger?.LogDebug("✅ LoadingOverlayManager.ShowAsync呼び出し完了");
            }
            catch (Exception loadingEx)
            {
                Logger?.LogDebug($"❌ LoadingOverlayManager.ShowAsync例外: {loadingEx.Message}");
                Logger?.LogError(loadingEx, "ローディングオーバーレイ表示に失敗");
            }

            // 2. 翻訳開始
            var uiTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger?.LogDebug("📊 翻訳状態をアクティブに設定");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📊 翻訳状態をアクティブに設定");
            Logger?.LogDebug("📊 翻訳状態をキャプチャ中に設定");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Capturing; // TranslationStatus.Activeがないため適切な値を使用
                IsTranslationActive = true;
                IsTranslationResultVisible = true; // 翻訳開始時は表示状態に設定
                IsLoading = false; // ローディング状態終了
                Logger?.LogDebug($"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
            });
            
            // 画面中央ローディングオーバーレイ非表示
            Logger?.LogDebug("🔄 LoadingOverlayManager.HideAsync呼び出し開始");
            try
            {
                await _loadingManager.HideAsync().ConfigureAwait(false);
                Logger?.LogDebug("✅ LoadingOverlayManager.HideAsync呼び出し完了");
            }
            catch (Exception loadingEx)
            {
                Logger?.LogDebug($"❌ LoadingOverlayManager.HideAsync例外: {loadingEx.Message}");
                Logger?.LogError(loadingEx, "ローディングオーバーレイ非表示に失敗");
            }
            uiTimer.Stop();
            Logger?.LogDebug($"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");

            // オーバーレイマネージャーを初期化
            var overlayInitTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger?.LogDebug("🖼️ オーバーレイマネージャー初期化開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー初期化開始");
            Logger?.LogDebug("🖼️ オーバーレイマネージャーを初期化");
            
            await _inPlaceOverlayManager.InitializeAsync().ConfigureAwait(false);
            overlayInitTimer.Stop();
            Logger?.LogDebug($"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⏱️ オーバーレイマネージャー初期化時間: {overlayInitTimer.ElapsedMilliseconds}ms");
            
            // オーバーレイマネージャーを表示状態に設定
            Logger?.LogDebug("🖼️ オーバーレイマネージャー表示状態設定開始");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖼️ オーバーレイマネージャー表示状態設定開始");
            // ARオーバーレイは自動で表示管理（表示はTextChunk個別処理）
            Logger?.LogDebug("✅ オーバーレイマネージャー表示状態設定完了");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ オーバーレイマネージャー表示状態設定完了");

            var eventTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📢 StartTranslationRequestEventを発行");
            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            var startTranslationEvent = new StartTranslationRequestEvent(selectedWindow);
            Logger?.LogDebug($"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"📨 EventID: {startTranslationEvent.Id}, TargetWindow: {selectedWindow.Title}");
            
            // 直接ファイル書き込みでイベント発行を記録
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"MainOverlayViewModel ファイル書き込みエラー: {fileEx.Message}");
            }
            
            await PublishEventAsync(startTranslationEvent).ConfigureAwait(false);
            
            try
            {
                // System.IO.File.AppendAllText( // 診断システム実装により debug_app_logs.txt への出力を無効化;
            }
            catch (Exception fileEx)
            {
                System.Diagnostics.Debug.WriteLine($"MainOverlayViewModel ファイル書き込みエラー: {fileEx.Message}");
            }
            
            eventTimer.Stop();
            Logger?.LogDebug($"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ StartTranslationRequestEvent発行完了 - イベント処理時間: {eventTimer.ElapsedMilliseconds}ms");

            overallTimer.Stop();
            Logger?.LogDebug($"⏱️ 【総合時間】翻訳開始処理全体: {overallTimer.ElapsedMilliseconds}ms (UI更新: {uiTimer.ElapsedMilliseconds}ms, オーバーレイ初期化: {overlayInitTimer.ElapsedMilliseconds}ms, イベント処理: {eventTimer.ElapsedMilliseconds}ms)");
            
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
                Logger?.LogDebug($"⚠️ ローディング非表示エラー: {loadingEx.Message}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ ローディング非表示エラー: {loadingEx.Message}");
            }
            
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
                IsTranslationActive = false;
                IsLoading = false; // ローディング状態終了
                Logger?.LogDebug($"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
            });
        }
    }


    private async Task StopTranslationAsync()
    {
        // 🔥 [PHASE6.1_STOP_PROOF] Stop処理開始の確実な証拠 - SafeFileLoggerで確実にファイル出力
        Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔴🔴🔴 [STOP_PROOF] StopTranslationAsync開始 - Stopボタンが押されました！");
        Console.WriteLine("🔴🔴🔴 [STOP_PROOF] StopTranslationAsync開始 - Stopボタンが押されました！");

        var stopEventPublished = false;

        try
        {
            Logger?.LogDebug("🔴 翻訳停止処理開始");
            Logger?.LogInformation("Stopping translation");

            // 🔥 [STOP_CLEANUP] セマフォ強制リセット - タイムアウト中でも即座にクリーンアップ
            // 問題: gRPCタイムアウト中（0-10秒）にStopしても、セマフォが保持されたまま
            // 解決策: AggregatedChunksReadyEventHandlerのセマフォを強制解放
            Console.WriteLine("🚀 [STOP_CLEANUP_DEBUG] MainOverlayViewModel - ResetSemaphoreForStop()呼び出し直前");
            Logger?.LogDebug("🚀 [STOP_CLEANUP_DEBUG] MainOverlayViewModel - ResetSemaphoreForStop()呼び出し直前");
            Baketa.Application.EventHandlers.Translation.AggregatedChunksReadyEventHandler.ResetSemaphoreForStop();
            Console.WriteLine("✅ [STOP_CLEANUP_DEBUG] MainOverlayViewModel - ResetSemaphoreForStop()呼び出し完了");
            Logger?.LogDebug("✅ [STOP_CLEANUP_DEBUG] MainOverlayViewModel - ResetSemaphoreForStop()呼び出し完了");

            // 翻訳停止（ウィンドウ選択状態は維持）
            Logger?.LogDebug("🔴 翻訳状態をアイドルに設定");

            // 🔥 [PHASE6.1_STOP_PROOF] UI状態変更前のログ - この直後にボタン表示が"Stop"→"Start"に変わる
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔄 [STOP_PROOF] IsTranslationActive=falseに設定する直前（ボタン表示が変わる瞬間）");
            Console.WriteLine("🔄 [STOP_PROOF] IsTranslationActive=falseに設定する直前（ボタン表示が変わる瞬間）");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = IsWindowSelected ? TranslationStatus.Ready : TranslationStatus.Idle; // ウィンドウ選択状態に応じて遷移
                IsTranslationActive = false;
                IsTranslationResultVisible = false; // 翻訳停止時は非表示にリセット
                // IsWindowSelectedとSelectedWindowは維持（再選択不要）
                Logger?.LogDebug($"✅ 翻訳停止状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsTranslationResultVisible={IsTranslationResultVisible}, IsWindowSelected={IsWindowSelected}");

                // 🔥 [PHASE6.1_STOP_PROOF] UI状態変更完了のログ - ボタン表示が"Start"に変わった
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [STOP_PROOF] IsTranslationActive=false設定完了、StartStopText='{StartStopText}' (ボタンが「Start」に変わった)");
                Console.WriteLine($"✅ [STOP_PROOF] IsTranslationActive=false設定完了、StartStopText='{StartStopText}' (ボタンが「Start」に変わった)");
            });

            // 🚀 RACE CONDITION FIX: StopTranslationRequestEventを最優先で発行（Task.Run終了の影響を回避）
            Logger?.LogDebug("🚀 [RACE_CONDITION_FIX] StopTranslationRequestEvent最優先発行開始");

            // 🔥 [PHASE6.1_STOP_PROOF] イベント発行前のログ
            Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📤 [STOP_PROOF] StopTranslationRequestEvent発行開始");
            Console.WriteLine("📤 [STOP_PROOF] StopTranslationRequestEvent発行開始");

            try
            {
                var stopTranslationEvent = new StopTranslationRequestEvent();
                await PublishEventAsync(stopTranslationEvent).ConfigureAwait(false);
                stopEventPublished = true;
                Logger?.LogDebug("✅ [RACE_CONDITION_FIX] StopTranslationRequestEvent最優先発行成功");

                // 🔥 [PHASE6.1_STOP_PROOF] イベント発行成功のログ
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ [STOP_PROOF] StopTranslationRequestEvent発行成功 (ID: {stopTranslationEvent.Id})");
                Console.WriteLine($"✅ [STOP_PROOF] StopTranslationRequestEvent発行成功 (ID: {stopTranslationEvent.Id})");
            }
            catch (Exception eventEx)
            {
                Logger?.LogDebug($"❌ [RACE_CONDITION_FIX] StopTranslationRequestEvent最優先発行失敗: {eventEx.Message}");

                // 🔥 [PHASE6.1_STOP_PROOF] イベント発行失敗のログ
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"❌ [STOP_PROOF] StopTranslationRequestEvent発行失敗: {eventEx.Message}");
                Console.WriteLine($"❌ [STOP_PROOF] StopTranslationRequestEvent発行失敗: {eventEx.Message}");

                // イベント発行失敗でも継続（後でリトライ）
            }

            // オーバーレイを非表示にしてリセット（OCRリセットとは独立処理）
            Logger?.LogDebug("🔄 オーバーレイ非表示・リセット開始");
            try
            {
                await _inPlaceOverlayManager.HideAllInPlaceOverlaysAsync().ConfigureAwait(false);
                Logger?.LogDebug("✅ オーバーレイ非表示完了");
                await _inPlaceOverlayManager.ResetAsync().ConfigureAwait(false);
                Logger?.LogDebug("✅ オーバーレイリセット完了");
            }
            catch (Exception overlayEx)
            {
                Logger?.LogDebug($"⚠️ オーバーレイ処理エラー（OCRリセットには影響なし）: {overlayEx.Message}");
                // オーバーレイエラーはOCRリセットに影響しないため継続
            }

            Logger?.LogDebug("✅ 翻訳停止処理完了");
            Logger?.LogInformation("Translation stopped successfully");
        }
        catch (Exception ex)
        {
            Logger?.LogDebug($"❌ StopTranslationAsync例外発生: {ex.Message}");
            Logger?.LogDebug($"❌ StackTrace: {ex.StackTrace}");
            Logger?.LogError(ex, "StopTranslationAsync中に例外発生");
        }
        finally
        {
            // StopTranslationRequestEventが未発行の場合、最終フォールバック実行
            if (!stopEventPublished)
            {
                try
                {
                    Logger?.LogDebug("🔄 [FINAL_FALLBACK] StopTranslationRequestEvent最終フォールバック発行");
                    var fallbackStopEvent = new StopTranslationRequestEvent();
                    await PublishEventAsync(fallbackStopEvent).ConfigureAwait(false);
                    Logger?.LogDebug("✅ [FINAL_FALLBACK] StopTranslationRequestEvent最終フォールバック発行成功");
                }
                catch (Exception eventEx)
                {
                    Logger?.LogDebug($"❌ [FINAL_FALLBACK] 最終フォールバックイベント発行も失敗: {eventEx.Message}");
                    Logger?.LogError(eventEx, "最終フォールバックStopTranslationRequestEvent発行失敗");
                }
            }
        }
    }

    private async void ExecuteShowHide()
    {
        Logger?.LogDebug($"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔘 ExecuteShowHide開始 - IsTranslationActive: {IsTranslationActive}, IsTranslationResultVisible: {IsTranslationResultVisible}");
        
        // 翻訳が非アクティブの場合は何もしない（安全措置）
        if (!IsTranslationActive)
        {
            Logger?.LogDebug("⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "⚠️ 翻訳が非アクティブのため、非表示ボタンの操作をスキップ");
            Logger?.LogWarning("非表示ボタンが翻訳非アクティブ時に押されました");
            return;
        }
        
        Logger?.LogDebug("Show/Hide toggle requested - Current: {Current} -> New: {New}", IsTranslationResultVisible, !IsTranslationResultVisible);
        
        var newVisibility = !IsTranslationResultVisible;
        Logger?.LogDebug($"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔄 表示状態変更: {IsTranslationResultVisible} -> {newVisibility}");
        
        IsTranslationResultVisible = newVisibility;
        
        // 重複処理除去: オーバーレイの制御はTranslationFlowEventProcessorで一元管理
        Logger?.LogDebug($"👁️ オーバーレイ表示状態変更: {IsTranslationResultVisible} (処理はイベント経由で実行)");
        
        var toggleEvent = new ToggleTranslationDisplayRequestEvent(IsTranslationResultVisible);
        await PublishEventAsync(toggleEvent).ConfigureAwait(false);
        
        Logger?.LogDebug($"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ 非表示ボタン処理完了 - IsTranslationResultVisible: {IsTranslationResultVisible}, Text: '{ShowHideText}', Icon: '{ShowHideIcon}'");
        Logger?.LogDebug("Translation display visibility toggled: {IsVisible}", IsTranslationResultVisible);
    }

    private static SimpleSettingsView? _currentSettingsDialog;

    private async void ExecuteSettings()
    {
        // 即座にアラートを表示してコマンドが呼ばれたことを確認
        Logger?.LogDebug("🚨🚨🚨 ExecuteSettings が呼ばれました！🚨🚨🚨");
        
        try
        {
            var currentDialogHash = _currentSettingsDialog?.GetHashCode();
            Logger?.LogDebug($"🔧 [MainOverlayViewModel] ExecuteSettings開始 - 現在のダイアログ: {currentDialogHash}");
            Logger?.LogDebug($"🔧 [MainOverlayViewModel] IsLoading: {IsLoading}, SettingsEnabled: {SettingsEnabled}");
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
            
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsViewModel使用開始");

            // DI注入されたSimpleSettingsViewModelを使用
            var settingsViewModel = _settingsViewModel;
            var vmHash = settingsViewModel.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SimpleSettingsViewModel取得: {vmHash}");

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
                Logger?.LogDebug($"🔧 [MainOverlayViewModel] Settings dialog Closedイベント - ダイアログ: {dialogHash}");
                Logger?.LogDebug("Settings dialog closed event received");
                var previousDialog = _currentSettingsDialog;
                _currentSettingsDialog = null;
                Logger?.LogDebug($"🔧 [MainOverlayViewModel] _currentSettingsDialogをnullに設定 - 前の値: {previousDialog?.GetHashCode()}");
            };

            // ViewModelのCloseRequestedイベントハンドル - 直接Close()を呼び出し
            if (settingsViewModel != null)
            {
                settingsViewModel.CloseRequested += () =>
                {
                    Logger?.LogDebug($"🔧 [MainOverlayViewModel] Settings dialog close requested by ViewModel - VM: {vmHash}");
                    Logger?.LogDebug("Settings dialog close requested by ViewModel");
                    var dialog = _currentSettingsDialog;
                    var currentDialogHash2 = dialog?.GetHashCode();
                    Logger?.LogDebug($"🔧 [MainOverlayViewModel] 現在のダイアログ状態: {currentDialogHash2}, 作成時: {dialogHash}");
                    if (dialog != null)
                    {
                        Logger?.LogDebug($"🔧 [MainOverlayViewModel] 直接Close()を呼び出し - 対象: {currentDialogHash2}");
                        dialog.Close();
                        Logger?.LogDebug($"🔧 [MainOverlayViewModel] Close()完了 - 対象: {currentDialogHash2}");
                    }
                    else
                    {
                        Logger?.LogDebug($"⚠️ [MainOverlayViewModel] _currentSettingsDialogがnull");
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
            Logger?.LogDebug($"💥 [MainOverlayViewModel] ExecuteSettingsエラー: {ex.Message}");
            Logger?.LogDebug($"💥 [MainOverlayViewModel] スタックトレース: {ex.StackTrace}");
            DebugHelper.Log($"💥 [MainOverlayViewModel] ExecuteSettingsエラー: {ex.Message}");
            DebugHelper.Log($"💥 [MainOverlayViewModel] スタックトレース: {ex.StackTrace}");
            if (ex.InnerException != null)
            {
                Logger?.LogDebug($"💥 [MainOverlayViewModel] InnerException: {ex.InnerException.Message}");
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

    /// <summary>
    /// 🔥 [PHASE2_PROBLEM2] Pythonサーバー状態変更イベントハンドラー
    /// TranslationInitializationServiceがサーバー起動完了時にこのイベントを発行
    /// StartButton制御の核心部分
    /// </summary>
    private async Task OnPythonServerStatusChanged(Baketa.Core.Events.EventTypes.PythonServerStatusChangedEvent eventData)
    {
        try
        {
            Logger?.LogInformation("🔥 [PHASE2_PROBLEM2] Pythonサーバー状態変更: Ready={IsReady}, Port={Port}, Message={Message}",
                eventData.IsServerReady, eventData.ServerPort, eventData.StatusMessage);

            // UI更新をメインスレッドで実行
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // StartCaptureCommandの有効/無効を制御
                IsTranslationEngineInitializing = !eventData.IsServerReady;

                // サーバー準備完了時の追加処理
                if (eventData.IsServerReady)
                {
                    Logger?.LogInformation("✅ [PHASE2_PROBLEM2] 翻訳サーバー準備完了 - StartButton有効化");
                    Logger?.LogDebug("✅ [PHASE2_PROBLEM2] 翻訳サーバー準備完了 - StartButton有効化");
                }
                else
                {
                    // 初期化中または失敗時
                    if (eventData.StatusMessage.Contains("エラー"))
                    {
                        Logger?.LogWarning("❌ [PHASE2_PROBLEM2] 翻訳サーバーエラー - StartButton無効化");
                        Logger?.LogDebug($"❌ [PHASE2_PROBLEM2] 翻訳サーバーエラー: {eventData.StatusMessage}");
                    }
                    else
                    {
                        Logger?.LogInformation("🔄 [PHASE2_PROBLEM2] 翻訳サーバー初期化中 - StartButton無効化");
                        Logger?.LogDebug("🔄 [PHASE2_PROBLEM2] 翻訳サーバー初期化中 - StartButton無効化");
                    }
                }
            });

            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ [PHASE2_PROBLEM2] Pythonサーバー状態変更イベント処理エラー");
            Logger?.LogDebug($"❌ [PHASE2_PROBLEM2] イベント処理エラー: {ex.Message}");
        }
    }

    #endregion
}