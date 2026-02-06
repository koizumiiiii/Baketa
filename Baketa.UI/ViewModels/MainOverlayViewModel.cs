#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Baketa.Application.Services.Diagnostics;
using Baketa.Application.Services.Translation;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Auth; // 🔥 [ISSUE#176] 認証状態監視用
using Baketa.Core.Abstractions.Settings; // [Issue #261] 同意同期用
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.GPU; // 🔥 [PHASE5.2E] IWarmupService用
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION] IOverlayManager統一インターフェース用
using Baketa.Core.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Monitoring;
using Baketa.Core.Utilities;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Helpers;
using Baketa.UI.Resources;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using WindowInfo = Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo;

namespace Baketa.UI.ViewModels;

/// <summary>
/// メインオーバーレイのViewModel
/// αテスト向け基本実装 - 翻訳開始/停止、状態表示、設定アクセス
/// </summary>
public class MainOverlayViewModel : ViewModelBase
{
    private bool _isCollapsed;
    private volatile bool _isTranslationActive;  // [Issue #291] スレッドセーフ化
    private TranslationStatus _currentStatus;
    private bool _isTranslationResultVisible; // 初期状態は非表示
    private bool _isWindowSelected;
    private bool _isOcrInitialized;

    // 🚀 EventHandler初期化完了状態（UI安全性向上）
    private bool _isEventHandlerInitialized;

    // 🔥 [PHASE2_PROBLEM2] 翻訳エンジン初期化状態（StartButton制御）
    private bool _isTranslationEngineInitializing;

    // 🔥 [PHASE5.2E] Startボタンツールチップ（ウォームアップ進捗表示用）
    private string _startButtonTooltip = null!; // コンストラクタで初期化

    // 🔥 [ISSUE#163_TOGGLE] シングルショットオーバーレイ表示状態（トグル動作用）
    private bool _isSingleshotOverlayVisible;

    // 🔥 [ISSUE#167] 認証モード（ログイン/サインアップ画面表示中はExitボタン以外無効化）
    private bool _isAuthenticationMode;

    // 🔥 [ISSUE#176] ログイン状態（ログアウト時はTargetを非活性にする）
    private bool _isLoggedIn;

    // 🔥 [Issue #300] VRAM警告状態（メモリ不足警告表示用）
    private bool _hasMemoryWarning;
    private bool _memoryWarningNotificationShown; // 一度だけ通知するためのフラグ

    private WindowInfo? _selectedWindow;

    public MainOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<MainOverlayViewModel> logger,
        IWindowManagerAdapter windowManager,
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        IOverlayManager overlayManager,
        LoadingOverlayManager loadingManager,
        IDiagnosticReportService diagnosticReportService,
        IWindowManagementService windowManagementService,
        ITranslationControlService translationControlService,
        SettingsWindowViewModel settingsViewModel,
        IWarmupService warmupService, // 🔥 [PHASE5.2E] ウォームアップサービス依存追加
        Baketa.Infrastructure.Services.IFirstRunService firstRunService, // 初回起動判定サービス
        ITranslationModeService translationModeService, // 🔥 [ISSUE#163_PHASE4] 翻訳モードサービス依存追加
        IErrorNotificationService errorNotificationService, // 🔥 [ISSUE#171_PHASE2] エラー通知サービス依存追加
        IAuthService authService, // 🔥 [ISSUE#176] 認証状態監視用
        Services.INotificationService notificationService, // 🔥 [Issue #300] トースト通知サービス
        IUnifiedSettingsService unifiedSettingsService, // 🔥 [Issue #318] EXモード表示用
        ILocalizationService? localizationService = null, // 言語変更時のボタンテキスト更新用
        IConsentService? consentService = null) // [Issue #261] 同意同期用（オプショナル）
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        _loadingManager = loadingManager ?? throw new ArgumentNullException(nameof(loadingManager));
        _diagnosticReportService = diagnosticReportService ?? throw new ArgumentNullException(nameof(diagnosticReportService));
        _windowManagementService = windowManagementService ?? throw new ArgumentNullException(nameof(windowManagementService));
        _translationControlService = translationControlService ?? throw new ArgumentNullException(nameof(translationControlService));
        _settingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

        // 🔥 [PHASE5.2E] ウォームアップサービス依存設定とイベント購読
        _warmupService = warmupService ?? throw new ArgumentNullException(nameof(warmupService));
        _warmupService.WarmupProgressChanged += OnWarmupProgressChanged;

        // 初回起動判定サービス設定
        _firstRunService = firstRunService ?? throw new ArgumentNullException(nameof(firstRunService));

        // 🔥 [ISSUE#163_PHASE4] 翻訳モードサービス設定
        _translationModeService = translationModeService ?? throw new ArgumentNullException(nameof(translationModeService));

        // 🔥 [ISSUE#171_PHASE2] エラー通知サービス設定
        _errorNotificationService = errorNotificationService ?? throw new ArgumentNullException(nameof(errorNotificationService));

        // 🔥 [ISSUE#176] 認証サービス設定とイベント購読（ログアウト時のUI状態リセット用）
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _authService.AuthStatusChanged += OnAuthStatusChanged;

        // 🔥 [Issue #300] トースト通知サービス設定
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));

        // 🔥 [Issue #318] EXモード表示用設定サービス
        _unifiedSettingsService = unifiedSettingsService ?? throw new ArgumentNullException(nameof(unifiedSettingsService));
        _unifiedSettingsService.SettingsChanged += OnUnifiedSettingsChanged;

        // [Issue #261] 同意サービス（オプショナル - 同期に使用）
        _consentService = consentService;

        // 言語変更時にStrings依存の計算プロパティを再通知
        if (localizationService != null)
        {
            localizationService.LanguageChanged += OnLanguageChanged;
        }

        // 初期状態設定 - OCR初期化状態を動的に管理
        _isOcrInitialized = false; // OCR初期化を正常に監視（MonitorOcrInitializationAsyncで設定）
        _currentStatus = TranslationStatus.Idle; // アイドル状態から開始
        _startButtonTooltip = Strings.MainOverlay_StartButton_Tooltip; // ローカライズ対応

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

        // 🔥 [ISSUE#176] 認証状態を初期化（起動時のログイン状態を取得）
        _ = Task.Run(InitializeAuthStateAsync);

        InitializeCommands();
        InitializeEventHandlers();
        InitializePropertyChangeHandlers();

        // 初回起動チェックと設定画面自動表示
        _ = Task.Run(CheckAndHandleFirstRunAsync);
    }

    private readonly IWindowManagerAdapter _windowManager;
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly IOverlayManager _overlayManager;
    private readonly LoadingOverlayManager _loadingManager;
    private readonly IDiagnosticReportService _diagnosticReportService;
    private readonly IWindowManagementService _windowManagementService;
    private readonly ITranslationControlService _translationControlService;
    private readonly SettingsWindowViewModel _settingsViewModel;
    private readonly IWarmupService _warmupService;
    private readonly Baketa.Infrastructure.Services.IFirstRunService _firstRunService;
    private readonly ITranslationModeService _translationModeService; // 🔥 [ISSUE#163_PHASE4] 翻訳モードサービス
    private readonly IErrorNotificationService _errorNotificationService; // 🔥 [ISSUE#171_PHASE2] エラー通知サービス
    private readonly IAuthService _authService; // 🔥 [ISSUE#176] 認証状態監視用
    private readonly Services.INotificationService _notificationService; // 🔥 [Issue #300] トースト通知サービス
    private readonly IConsentService? _consentService; // [Issue #261] 同意同期用
    private readonly IUnifiedSettingsService _unifiedSettingsService; // 🔥 [Issue #318] EXモード表示用

    #region Properties

    /// <summary>
    /// 🔥 [WARMUP_FIX] ウォームアップ完了状態を監視可能なプロパティとして公開
    /// ReactiveCommandのWhenAnyValueでウォームアップ完了状態を監視するため必須
    /// </summary>
    public bool IsWarmupCompleted => _warmupService.IsWarmupCompleted;

    /// <summary>
    /// 🔥 [Issue #318] EXモード（Cloud AI翻訳）が有効かどうか
    /// UseLocalEngine=falseの場合にEXモードが有効
    /// </summary>
    public bool IsEXModeEnabled => !_unifiedSettingsService.GetTranslationSettings().UseLocalEngine;

    /// <summary>
    /// 🔥 [ISSUE#163_PHASE4] 現在の翻訳モード（None/Live/Singleshot）
    /// TranslationModeServiceから取得
    /// </summary>
    public Baketa.Core.Abstractions.Services.TranslationMode CurrentTranslationMode => _translationModeService.CurrentMode;

    /// <summary>
    /// 🔥 [Issue #300] VRAM/メモリ警告状態
    /// true: VRAM使用率がCritical(75-90%)またはEmergency(>90%)の場合
    /// UI表示: 警告アイコンや枠線の色変更に使用
    /// </summary>
    public bool HasMemoryWarning
    {
        get => _hasMemoryWarning;
        private set
        {
            if (SetPropertySafe(ref _hasMemoryWarning, value))
            {
                Logger?.LogDebug("[Issue #300] HasMemoryWarning changed to {Value}", value);
            }
        }
    }

    /// <summary>
    /// 🔥 [ISSUE#163_TOGGLE] シングルショットオーバーレイ表示状態
    /// true: オーバーレイ表示中（次回のShot押下でオーバーレイ削除）
    /// false: オーバーレイ非表示（次回のShot押下で翻訳実行）
    /// </summary>
    public bool IsSingleshotOverlayVisible
    {
        get => _isSingleshotOverlayVisible;
        set
        {
            var changed = SetPropertySafe(ref _isSingleshotOverlayVisible, value);
            if (changed)
            {
                // 🔥 [ISSUE#164] 依存プロパティの変更通知
                if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                {
                    this.RaisePropertyChanged(nameof(IsSingleshotActive));
                    this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                    // 🔥 [ISSUE#164_FIX] SingleshotIconSourceは計算プロパティなので手動通知が必要
                    this.RaisePropertyChanged(nameof(SingleshotIconSource));
                    // 🔥 [ISSUE#164] SingleshotButtonTooltipは計算プロパティなので手動通知が必要
                    this.RaisePropertyChanged(nameof(SingleshotButtonTooltip));
                    // 🔥 [Issue #357] SingleshotButtonTextは計算プロパティなので手動通知が必要
                    this.RaisePropertyChanged(nameof(SingleshotButtonText));
                    // 🔥 [ISSUE#164_FIX] IsLiveEnabledは!IsSingleshotOverlayVisibleに依存するため通知が必要
                    this.RaisePropertyChanged(nameof(IsLiveEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsSingleshotActive));
                        this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                        // 🔥 [ISSUE#164_FIX] SingleshotIconSourceは計算プロパティなので手動通知が必要
                        this.RaisePropertyChanged(nameof(SingleshotIconSource));
                        // 🔥 [ISSUE#164] SingleshotButtonTooltipは計算プロパティなので手動通知が必要
                        this.RaisePropertyChanged(nameof(SingleshotButtonTooltip));
                        // 🔥 [Issue #357] SingleshotButtonTextは計算プロパティなので手動通知が必要
                        this.RaisePropertyChanged(nameof(SingleshotButtonText));
                        // 🔥 [ISSUE#164_FIX] IsLiveEnabledは!IsSingleshotOverlayVisibleに依存するため通知が必要
                        this.RaisePropertyChanged(nameof(IsLiveEnabled));
                    });
                }
            }
        }
    }

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
#pragma warning disable CS0420 // volatile ref: SetPropertySafeはUIスレッド制御付きで安全に値を設定
            var changed = SetPropertySafe(ref _isTranslationActive, value);
#pragma warning restore CS0420
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
                    // 🔥 [ISSUE#164] UI/UX改善用プロパティの通知
                    this.RaisePropertyChanged(nameof(IsLiveActive));
                    this.RaisePropertyChanged(nameof(IsLiveEnabled));
                    this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                    // 🔥 [ISSUE#164_FIX] LiveIconSourceは計算プロパティなので手動通知が必要
                    this.RaisePropertyChanged(nameof(LiveIconSource));
                    // 🔥 [Issue #357] LiveButtonTextは計算プロパティなので手動通知が必要
                    this.RaisePropertyChanged(nameof(LiveButtonText));
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
                        // 🔥 [ISSUE#164] UI/UX改善用プロパティの通知
                        this.RaisePropertyChanged(nameof(IsLiveActive));
                        this.RaisePropertyChanged(nameof(IsLiveEnabled));
                        this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                        // 🔥 [ISSUE#164_FIX] LiveIconSourceは計算プロパティなので手動通知が必要
                        this.RaisePropertyChanged(nameof(LiveIconSource));
                        // 🔥 [Issue #357] LiveButtonTextは計算プロパティなので手動通知が必要
                        this.RaisePropertyChanged(nameof(LiveButtonText));
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
                    // 🔥 [ISSUE#164] UI/UX改善: IsWindowSelectedに依存するボタン状態プロパティの通知
                    this.RaisePropertyChanged(nameof(IsLiveEnabled));
                    this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                        this.RaisePropertyChanged(nameof(StartStopText));
                        // 🔥 [ISSUE#164] UI/UX改善: IsWindowSelectedに依存するボタン状態プロパティの通知
                        this.RaisePropertyChanged(nameof(IsLiveEnabled));
                        this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
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
                    // 🔥 [ISSUE#164] UI/UX改善: IsOcrInitializedに依存するボタン状態プロパティの通知
                    this.RaisePropertyChanged(nameof(IsLiveEnabled));
                    this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                }
                else
                {
                    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                        // 🔧 [ULTRATHINK_ROOT_CAUSE_FIX] Start/Stopボタン状態更新通知追加
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                        // 🔥 [ISSUE#164] UI/UX改善: IsOcrInitializedに依存するボタン状態プロパティの通知
                        this.RaisePropertyChanged(nameof(IsLiveEnabled));
                        this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
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
    /// 🔥 [PHASE5.2E] Startボタンツールチップ - ウォームアップ進捗表示
    /// </summary>
    public string StartButtonTooltip
    {
        get => _startButtonTooltip;
        set => SetPropertySafe(ref _startButtonTooltip, value);
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


    // 🔥 [ISSUE#167] 認証モードプロパティ（ログイン/サインアップ画面表示中）
    /// <summary>
    /// 認証モードかどうか（ログイン/サインアップ画面表示中）
    /// 認証モード中はExitボタン以外のすべてのボタンが無効化される
    /// </summary>
    public bool IsAuthenticationMode
    {
        get => _isAuthenticationMode;
        private set
        {
            if (_isAuthenticationMode != value)
            {
                _isAuthenticationMode = value;
                try
                {
                    if (Avalonia.Threading.Dispatcher.UIThread.CheckAccess())
                    {
                        Logger?.LogDebug("[AUTH_DEBUG] RaisePropertyChanged開始 (UIThread)");
                        this.RaisePropertyChanged(nameof(IsAuthenticationMode));
                        Logger?.LogDebug("[AUTH_DEBUG] IsAuthenticationMode通知完了");
                        // 全ボタン状態を更新
                        this.RaisePropertyChanged(nameof(ShowHideEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] ShowHideEnabled通知完了");
                        this.RaisePropertyChanged(nameof(SettingsEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] SettingsEnabled通知完了");
                        this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] IsSelectWindowEnabled通知完了");
                        this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] IsStartStopEnabled通知完了");
                        this.RaisePropertyChanged(nameof(IsLiveEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] IsLiveEnabled通知完了");
                        this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                        Logger?.LogDebug("[AUTH_DEBUG] IsSingleshotEnabled通知完了");
                    }
                    else
                    {
                        Logger?.LogDebug("[AUTH_DEBUG] RaisePropertyChanged開始 (InvokeAsync)");
                        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            try
                            {
                                this.RaisePropertyChanged(nameof(IsAuthenticationMode));
                                this.RaisePropertyChanged(nameof(ShowHideEnabled));
                                this.RaisePropertyChanged(nameof(SettingsEnabled));
                                this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
                                this.RaisePropertyChanged(nameof(IsStartStopEnabled));
                                this.RaisePropertyChanged(nameof(IsLiveEnabled));
                                this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
                                Logger?.LogDebug("[AUTH_DEBUG] 全RaisePropertyChanged完了 (InvokeAsync)");
                            }
                            catch (Exception ex)
                            {
                                Logger?.LogError(ex, "[AUTH_DEBUG] InvokeAsync内でRaisePropertyChanged例外: {Message}", ex.Message);
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "[AUTH_DEBUG] IsAuthenticationModeセッターで例外: {Message}", ex.Message);
                }
            }
        }
    }

    /// <summary>
    /// 認証モードを設定
    /// ログイン/サインアップ画面表示時にNavigationServiceから呼び出される
    /// </summary>
    public void SetAuthenticationMode(bool isAuthMode)
    {
        Logger?.LogDebug("認証モード変更: {IsAuthMode}", isAuthMode);
        try
        {
            IsAuthenticationMode = isAuthMode;
            Logger?.LogDebug("[AUTH_DEBUG] IsAuthenticationModeプロパティ設定完了");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[AUTH_DEBUG] SetAuthenticationModeで例外: {Message}", ex.Message);
            throw;
        }
    }

    // UI状態の計算プロパティ
    public bool ShowHideEnabled => !_isAuthenticationMode && IsTranslationActive; // 認証モード中または翻訳中でない場合は無効
    public bool SettingsEnabled => !_isAuthenticationMode && !IsLoading && !IsTranslationActive; // 認証モード中、ローディング中、翻訳実行中は無効
    public bool IsSelectWindowEnabled => !_isAuthenticationMode && IsOcrInitialized && !IsLoading && _isLoggedIn; // 認証モード中またはOCR未初期化またはローディング中またはログアウト時は無効
    public bool IsStartStopEnabled
    {
        get
        {
            // 🔥 [ISSUE#167] 認証モード中は無効
            if (_isAuthenticationMode) return false;

            // 🔥 [PHASE6.1_ROOT_CAUSE_FIX] Start/Stop両方の条件を正しく実装
            // 🔥 [PHASE5.2E] ウォームアップ完了条件追加 - Startボタン押下前に全準備完了を保証
            // Start可能条件: ウィンドウ選択済み、OCR初期化完了、ウォームアップ完了、ローディング中でない、翻訳中でない
            var canStart = !IsLoading && IsWindowSelected && IsOcrInitialized && IsEventHandlerInitialized && !IsTranslationEngineInitializing && _warmupService.IsWarmupCompleted && !IsTranslationActive;

            // Stop可能条件: 翻訳実行中（ローディング中でもStopは可能）
            // 🔥 [ISSUE#164_FIX] ローディング中でも翻訳停止を可能にする
            var canStop = IsTranslationActive;

            var enabled = canStart || canStop;

            Logger?.LogDebug($"🔍 IsStartStopEnabled計算: canStart={canStart}, canStop={canStop}, IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}, IsEventHandlerInitialized={IsEventHandlerInitialized}, IsTranslationEngineInitializing={IsTranslationEngineInitializing}, IsWarmupCompleted={_warmupService.IsWarmupCompleted}, 結果={enabled}");

            // デバッグ用に実際の状態をファイルログにも出力
            try
            {
                Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt",
                    $"🔍 [START_BUTTON_STATE] IsStartStopEnabled={enabled}, canStart={canStart}, canStop={canStop}, IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, IsWindowSelected={IsWindowSelected}, IsOcrInitialized={IsOcrInitialized}, IsEventHandlerInitialized={IsEventHandlerInitialized}, IsTranslationEngineInitializing={IsTranslationEngineInitializing}, IsWarmupCompleted={_warmupService.IsWarmupCompleted}");
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

    // 🔥 [ISSUE#164] UI/UX改善用のボタン状態プロパティ（意味的に明確な命名）
    /// <summary>
    /// Liveモードがアクティブか（UIバインディング用）
    /// IsTranslationActiveのエイリアス
    /// </summary>
    public bool IsLiveActive => IsTranslationActive;

    /// <summary>
    /// Singleshotモードがアクティブか（UIバインディング用）
    /// IsSingleshotOverlayVisibleのエイリアス
    /// </summary>
    public bool IsSingleshotActive => IsSingleshotOverlayVisible;

    /// <summary>
    /// Liveボタンが有効か（UIバインディング用）
    /// IsStartStopEnabledと同じ条件だが、Singleshot実行中は無効
    /// </summary>
    public bool IsLiveEnabled => IsStartStopEnabled && !IsSingleshotOverlayVisible;

    /// <summary>
    /// Singleshotボタンが有効か（UIバインディング用）
    /// ExecuteSingleshotCommandのCanExecute条件と同等
    /// </summary>
    public bool IsSingleshotEnabled
    {
        get
        {
            // 🔥 [ISSUE#167] 認証モード中は無効
            if (_isAuthenticationMode) return false;

            // 条件: ウィンドウ選択済み、OCR初期化完了、イベントハンドラー初期化完了、
            //       翻訳エンジン初期化中でない、ウォームアップ完了、
            //       （Live翻訳中でない OR オーバーレイ表示中）、ローディング中でない
            return !IsLoading && IsWindowSelected && IsOcrInitialized && IsEventHandlerInitialized
                   && !IsTranslationEngineInitializing && _warmupService.IsWarmupCompleted
                   && (!IsTranslationActive || IsSingleshotOverlayVisible);
        }
    }

    /// <summary>
    /// Liveボタンのアイコンソース（アクティブ状態で赤色アイコンに切り替え）
    /// </summary>
    /// <remarks>
    /// 🔥 [ISSUE#164] UI/UX改善: アクティブ状態に応じてアイコンを自動切り替え
    /// IsLiveActive（IsTranslationActiveのエイリアス）の値に基づいてアイコンBitmapを返す
    /// Avalonia型コンバーター問題を回避するため、Bitmap型で直接返す
    /// </remarks>
    public Bitmap? LiveIconSource
    {
        get
        {
            try
            {
                var uri = IsLiveActive
                    ? new Uri("avares://Baketa/Assets/Icons/live_active.png")
                    : new Uri("avares://Baketa/Assets/Icons/live.png");
                return ImageHelper.LoadFromAvaloniaResource(uri);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to load Live icon bitmap");
                return null;
            }
        }
    }

    /// <summary>
    /// Singleshotボタンのアイコンソース（アクティブ状態で赤色アイコンに切り替え）
    /// </summary>
    /// <remarks>
    /// 🔥 [ISSUE#164] UI/UX改善: アクティブ状態に応じてアイコンを自動切り替え
    /// IsSingleshotActive（IsSingleshotOverlayVisibleのエイリアス）の値に基づいてアイコンBitmapを返す
    /// Avalonia型コンバーター問題を回避するため、Bitmap型で直接返す
    /// </remarks>
    public Bitmap? SingleshotIconSource
    {
        get
        {
            try
            {
                var uri = IsSingleshotActive
                    ? new Uri("avares://Baketa/Assets/Icons/singleshot_active.png")
                    : new Uri("avares://Baketa/Assets/Icons/singleshot.png");
                return ImageHelper.LoadFromAvaloniaResource(uri);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "Failed to load Singleshot icon bitmap");
                return null;
            }
        }
    }

    /// <summary>
    /// Singleshotボタンのツールチップ（オーバーレイ表示状態で切り替え）
    /// </summary>
    /// <remarks>
    /// [Issue #357] アクティブ時は「リセット」、非アクティブ時は「シングルショット翻訳を実行」
    /// </remarks>
    public string SingleshotButtonTooltip =>
        IsSingleshotOverlayVisible ? Strings.MainOverlay_Singleshot_Reset : Strings.MainOverlay_Singleshot_Execute;

    /// <summary>
    /// [Issue #357] Singleshotボタンのテキスト（オーバーレイ表示状態で切り替え）
    /// </summary>
    /// <remarks>
    /// アクティブ時は「リセット」、非アクティブ時は「Shot翻訳」を表示
    /// </remarks>
    public string SingleshotButtonText =>
        IsSingleshotOverlayVisible ? Strings.MainOverlay_Singleshot_Reset : Strings.MainOverlay_ShotTranslation;

    /// <summary>
    /// [Issue #357] Liveボタンのテキスト（翻訳状態で切り替え）
    /// </summary>
    /// <remarks>
    /// アクティブ時は「停止」、非アクティブ時は「Live翻訳」を表示
    /// </remarks>
    public string LiveButtonText =>
        IsLiveActive ? Strings.MainOverlay_Live_Stop : Strings.MainOverlay_LiveTranslation;

    /// <summary>
    /// 言語変更時にStrings依存の計算プロパティを再通知
    /// </summary>
    private void OnLanguageChanged(object? sender, LanguageChangedEventArgs e)
    {
        this.RaisePropertyChanged(nameof(SingleshotButtonText));
        this.RaisePropertyChanged(nameof(SingleshotButtonTooltip));
        this.RaisePropertyChanged(nameof(LiveButtonText));
        this.RaisePropertyChanged(nameof(InitializationText));
    }

    public string InitializationText => CurrentStatus switch
    {
        TranslationStatus.Initializing => Strings.MainOverlay_Status_Initializing,
        TranslationStatus.Idle => Strings.MainOverlay_Status_Idle,
        TranslationStatus.Ready => Strings.MainOverlay_Status_Ready,
        TranslationStatus.Capturing or TranslationStatus.ProcessingOCR or TranslationStatus.Translating => Strings.MainOverlay_Status_Translating,
        _ => Strings.MainOverlay_Status_Waiting
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
    // 🔥 [ISSUE#163_PHASE4] シングルショット翻訳実行コマンド
    public ICommand ExecuteSingleshotCommand { get; private set; } = null!;

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
                x => x.IsWarmupCompleted, // 🔥 [WARMUP_FIX] ウォームアップ完了状態の監視追加
                (isLoading, isWindowSelected, isOcrInitialized, isEventHandlerInitialized, isTranslationEngineInitializing, isTranslationActive, isWarmupCompleted) =>
                {
                    // Start可能条件: ウィンドウ選択済み、OCR初期化完了、ウォームアップ完了、ローディング中でない、翻訳中でない
                    // 🔥 [WARMUP_FIX] isWarmupCompletedチェック追加 - ウォームアップ完了前のStartボタン押下を防止
                    var canStart = !isLoading && isWindowSelected && isOcrInitialized && isEventHandlerInitialized && !isTranslationEngineInitializing && isWarmupCompleted && !isTranslationActive;

                    // Stop可能条件: 翻訳実行中（ローディング中でもStopは可能）
                    // 🔥 [ISSUE#164_FIX] ローディング中でも翻訳停止を可能にする
                    var canStop = isTranslationActive;

                    var enabled = canStart || canStop;

                    Console.WriteLine($"🔍🔍🔍 [OBSERVABLE_CHANGE] canExecute計算: canStart={canStart}, canStop={canStop}, IsTranslationActive={isTranslationActive}, enabled={enabled}, Thread:{Environment.CurrentManagedThreadId}");
                    Utils.SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 [OBSERVABLE_CHANGE] canStart={canStart}, canStop={canStop}, IsTranslationActive={isTranslationActive}, enabled={enabled}");

                    return enabled;
                })
                .Do(canExecute =>
                {
                    Console.WriteLine($"🔍🔍🔍 [DO_OPERATOR] canExecute値: {canExecute}, Thread:{Environment.CurrentManagedThreadId}");
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
                Console.WriteLine($"🎬🎬🎬 [COMMAND_SUBSCRIBE] StartStopCommand.Subscribe()実行！IsTranslationActive={IsTranslationActive}, Thread:{Environment.CurrentManagedThreadId}");
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

            // 🔥 [ISSUE#163_PHASE4] シングルショット翻訳コマンド初期化
            // 🔥 [ISSUE#163_FIX] Live翻訳と同様の条件を適用: イベントハンドラー初期化完了、翻訳エンジン初期化完了を追加
            // 🔥 [ISSUE#163_TOGGLE] トグル動作対応: オーバーレイ表示中でもボタンを有効化（削除操作のため）
            // 条件: ウィンドウ選択済み、OCR初期化完了、イベントハンドラー初期化完了、翻訳エンジン初期化中でない、ウォームアップ完了、
            //       （Live翻訳中でない OR オーバーレイ表示中）、ローディング中でない
            ExecuteSingleshotCommand = ReactiveCommand.CreateFromTask(ExecuteSingleshotAsync,
                this.WhenAnyValue(
                    x => x.IsLoading,
                    x => x.IsWindowSelected,
                    x => x.IsOcrInitialized,
                    x => x.IsEventHandlerInitialized,        // 🔥 [FIX] Live翻訳と同じ条件追加
                    x => x.IsTranslationEngineInitializing,  // 🔥 [FIX] Live翻訳と同じ条件追加
                    x => x.IsWarmupCompleted,
                    x => x.IsTranslationActive,
                    x => x.IsSingleshotOverlayVisible,       // 🔥 [ISSUE#163_TOGGLE] オーバーレイ表示状態を監視
                    (isLoading, isWindowSelected, isOcrInitialized, isEventHandlerInitialized,
                     isTranslationEngineInitializing, isWarmupCompleted, isTranslationActive, isSingleshotOverlayVisible) =>
                        !isLoading && isWindowSelected && isOcrInitialized && isEventHandlerInitialized &&
                        !isTranslationEngineInitializing && isWarmupCompleted &&
                        (!isTranslationActive || isSingleshotOverlayVisible)) // 🔥 [ISSUE#163_TOGGLE] Live翻訳中でもオーバーレイ表示中なら有効
                .ObserveOn(RxApp.MainThreadScheduler),
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

        // 最初の翻訳結果受信イベントの購読（ローディング終了用）
        Logger?.LogWarning("🔔 [SUBSCRIBE] FirstTranslationResultReceivedEvent購読開始 - 型: {EventType}", typeof(FirstTranslationResultReceivedEvent).FullName);
        SubscribeToEvent<FirstTranslationResultReceivedEvent>(OnFirstTranslationResultReceived);
        Logger?.LogWarning("🔔 [SUBSCRIBE] FirstTranslationResultReceivedEvent購読完了");

        // 🔥 [Issue #300] VRAM警告イベントの購読（メモリ不足時のUI表示用）
        SubscribeToEvent<VramWarningEvent>(OnVramWarning);

        // 🔥 [Issue #300] リソース監視イベントの購読（システムRAM警告も対応）
        SubscribeToEvent<ResourceMonitoringEvent>(OnResourceMonitoringWarning);
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
                // ボタン有効状態の通知
                this.RaisePropertyChanged(nameof(IsLiveEnabled));
                this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
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
            Logger?.LogDebug("[MONITOR_DEBUG] timeout変数設定前");

            var timeout = TimeSpan.FromSeconds(30); // 30秒でタイムアウト
            Logger?.LogDebug("[MONITOR_DEBUG] timeout変数設定後");

            var startTime = DateTime.UtcNow;
            Logger?.LogDebug("[MONITOR_DEBUG] startTime設定後、whileループ開始");

            while (DateTime.UtcNow - startTime < timeout)
            {
                Logger?.LogDebug("[MONITOR_DEBUG] whileループ内部開始");
                try
                {
                    // ServiceProviderからOCRサービスを取得して初期化状態をチェック
                    Logger?.LogDebug("[MONITOR_DEBUG] ServiceProvider取得前");
                    var serviceProvider = Program.ServiceProvider;
                    Logger?.LogDebug("[MONITOR_DEBUG] ServiceProvider取得後: {HasProvider}", serviceProvider != null);
                    if (serviceProvider != null)
                    {
                        Logger?.LogDebug("[STACK_DEBUG] GetService<IOcrEngine>呼び出し前");
                        var ocrService = serviceProvider.GetService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
                        Logger?.LogDebug("[STACK_DEBUG] GetService<IOcrEngine>呼び出し後: {HasService}", ocrService != null);
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

            // 🔥 [ISSUE#171_PHASE2] ユーザーに具体的なエラーメッセージを通知
            var operation = IsTranslationActive ? "停止" : "開始";
            await _errorNotificationService.ShowErrorAsync(
                $"翻訳の{operation}に失敗しました。\n原因: {ex.Message}\n対処: アプリを再起動してください。").ConfigureAwait(false);
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

                // 🔥 [ISSUE#171_PHASE2] ユーザーに具体的なエラーメッセージを通知
                await _errorNotificationService.ShowErrorAsync(
                    "翻訳を開始できません。\n原因: 翻訳対象のウィンドウが選択されていません。\n対処: 「ウィンドウ選択」ボタンから翻訳対象のウィンドウを選択してください。").ConfigureAwait(false);
                return;
            }

            Logger?.LogDebug($"✅ 選択済みウィンドウを使用: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");
            Logger?.LogInformation("選択済みウィンドウを使用: '{Title}' (Handle={Handle})", selectedWindow.Title, selectedWindow.Handle);

            // ローディング状態開始（ウィンドウ選択後）
            // IsLoadingは翻訳結果が返ってくるまで維持される（OnTranslationStatusChangedで解除）
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
                Logger?.LogDebug($"🔄 翻訳準備ローディング開始 - IsLoading={IsLoading}, LoadingText='{LoadingText}', IsStartStopEnabled={IsStartStopEnabled}");
            });

            // 2. 翻訳開始
            var uiTimer = System.Diagnostics.Stopwatch.StartNew();
            Logger?.LogDebug("📊 翻訳状態をキャプチャ中に設定");
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Capturing;
                IsTranslationActive = true;
                IsTranslationResultVisible = true; // 翻訳開始時は表示状態に設定
                // IsLoadingは維持（翻訳結果返却時にOnTranslationStatusChangedで解除）
                Logger?.LogDebug($"✅ 翻訳状態更新完了: IsTranslationActive={IsTranslationActive}, StartStopText='{StartStopText}', IsLoading={IsLoading}, IsStartStopEnabled={IsStartStopEnabled}, IsTranslationResultVisible={IsTranslationResultVisible}");
            });
            uiTimer.Stop();
            Logger?.LogDebug($"⏱️ UI状態更新時間: {uiTimer.ElapsedMilliseconds}ms");

            // 🔧 [OVERLAY_UNIFICATION] IOverlayManagerには InitializeAsync メソッドがないため、初期化処理を削除
            // Win32OverlayManager は DIコンテナで初期化済み
            Logger?.LogDebug("🖼️ オーバーレイマネージャーはDI初期化済み（Win32OverlayManager）");
            // ARオーバーレイは自動で表示管理（表示はTextChunk個別処理）
            Logger?.LogDebug("✅ オーバーレイマネージャー準備完了");

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
            // 🔧 [OVERLAY_UNIFICATION] オーバーレイ初期化時間を削除（Win32OverlayManagerは初期化不要）
            Logger?.LogDebug($"⏱️ 【総合時間】翻訳開始処理全体: {overallTimer.ElapsedMilliseconds}ms (UI更新: {uiTimer.ElapsedMilliseconds}ms, イベント処理: {eventTimer.ElapsedMilliseconds}ms)");
            
            Logger?.LogInformation("🎉 翻訳が正常に開始されました: '{Title}' - 総処理時間: {TotalMs}ms", selectedWindow.Title, overallTimer.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "💥 翻訳開始に失敗: {ErrorMessage}", ex.Message);

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
                IsTranslationActive = false;
                IsLoading = false; // エラー時はローディング状態終了
                Logger?.LogDebug($"💥 エラー時状態リセット: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}");
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
                IsLoading = false; // 翻訳停止時はローディング状態も終了
                IsTranslationResultVisible = false; // 翻訳停止時は非表示にリセット
                // IsWindowSelectedとSelectedWindowは維持（再選択不要）
                Logger?.LogDebug($"✅ 翻訳停止状態更新完了: IsTranslationActive={IsTranslationActive}, IsLoading={IsLoading}, StartStopText='{StartStopText}', IsTranslationResultVisible={IsTranslationResultVisible}, IsWindowSelected={IsWindowSelected}");

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

            // 🔧 [OVERLAY_UNIFICATION] オーバーレイを非表示（IOverlayManager.HideAllAsync）
            Logger?.LogDebug("🔄 オーバーレイ非表示開始");
            try
            {
                await _overlayManager.HideAllAsync().ConfigureAwait(false);
                Logger?.LogDebug("✅ オーバーレイ非表示完了");
                // 🔧 [OVERLAY_UNIFICATION] IOverlayManagerには ResetAsync メソッドがないため削除
                // Win32OverlayManagerは HideAllAsync で全オーバーレイを破棄するため、リセット不要
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

    private static Views.SettingsWindow? _currentSettingsDialog;

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
            Logger?.LogDebug("Opening settings dialog");

            // DI注入されたSettingsWindowViewModelを使用
            var settingsViewModel = _settingsViewModel;
            var vmHash = settingsViewModel.GetHashCode();
            DebugHelper.Log($"🔧 [MainOverlayViewModel] SettingsWindowViewModel取得: {vmHash}");

            // 設定ダイアログをUIスレッドで作成
            var dialogHash = await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                _currentSettingsDialog = new Views.SettingsWindow
                {
                    DataContext = settingsViewModel
                };
                var hash = _currentSettingsDialog.GetHashCode();
                DebugHelper.Log($"🔧 [MainOverlayViewModel] SettingsWindow作成: {hash}");
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

    /// <summary>
    /// 🔥 [ISSUE#163_PHASE4] シングルショット翻訳実行ハンドラー
    /// 🔥 [ISSUE#163_TOGGLE] トグル動作実装:
    ///   - オーバーレイ非表示時: 翻訳実行→オーバーレイ表示
    ///   - オーバーレイ表示時: オーバーレイ削除
    /// </summary>
    private async Task ExecuteSingleshotAsync()
    {
        Logger?.LogDebug("📸 ExecuteSingleshotAsync開始 (IsSingleshotOverlayVisible={IsVisible})", IsSingleshotOverlayVisible);

        try
        {
            // 🔥 [ISSUE#163_TOGGLE] トグル動作: オーバーレイ表示中なら非表示
            if (IsSingleshotOverlayVisible)
            {
                Logger?.LogInformation("🗑️ シングルショットオーバーレイを非表示にします");

                try
                {
                    // 🔥 [ISSUE#163_CRASH_FIX] HideAllAsync()でクラッシュするため、
                    // 暫定対策として可視性のみ変更（破棄はしない）
                    // オーバーレイは次の翻訳実行時に自然にクリアされる
                    await _overlayManager.SetAllVisibilityAsync(false).ConfigureAwait(false);

                    // 状態をリセット
                    IsSingleshotOverlayVisible = false;

                    Logger?.LogInformation("✅ シングルショットオーバーレイ非表示完了");
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "オーバーレイ非表示中にエラーが発生: {ErrorMessage}", ex.Message);
                    // エラーが発生しても状態はリセット
                    IsSingleshotOverlayVisible = false;

                    // 🔥 [ISSUE#171_PHASE2] ユーザーに具体的なエラーメッセージを通知
                    await _errorNotificationService.ShowErrorAsync(
                        $"翻訳結果の非表示に失敗しました。\n原因: {ex.Message}\n対処: アプリを再起動してください。").ConfigureAwait(false);
                }
                return;
            }

            // 🔥 [ISSUE#163_TOGGLE] オーバーレイ非表示時: 翻訳実行
            Logger?.LogInformation("シングルショット翻訳実行開始");

            // 🔔 [LOADING] ローディング表示開始
            IsLoading = true;
            Logger?.LogDebug("🔄 [LOADING] シングルショット翻訳ローディング開始");

            // ウィンドウが選択されているか確認
            var selectedWindow = SelectedWindow;
            if (selectedWindow == null)
            {
                Logger?.LogWarning("ウィンドウが選択されていない状態でシングルショット翻訳が要求されました");

                // 🔔 [LOADING] エラー時はローディング終了
                IsLoading = false;

                // 🔥 [ISSUE#171_PHASE2] ユーザーに具体的なエラーメッセージを通知
                await _errorNotificationService.ShowErrorAsync(
                    "翻訳を実行できません。\n原因: 翻訳対象のウィンドウが選択されていません。\n対処: 「ウィンドウ選択」ボタンから翻訳対象のウィンドウを選択してください。").ConfigureAwait(false);
                return;
            }

            Logger?.LogDebug("✅ シングルショット翻訳対象ウィンドウ: '{Title}' (Handle={Handle})",
                selectedWindow.Title, selectedWindow.Handle);

            // ExecuteSingleshotRequestEventを発行
            var singleshotEvent = new ExecuteSingleshotRequestEvent(selectedWindow);
            Logger?.LogDebug("📤 ExecuteSingleshotRequestEvent発行: EventID={EventId}, TargetWindow={WindowTitle}",
                singleshotEvent.Id, selectedWindow.Title);

            await PublishEventAsync(singleshotEvent).ConfigureAwait(false);

            // 🔥 [ISSUE#163_TOGGLE] オーバーレイ表示状態に変更
            // （実際の表示は翻訳完了後のイベントハンドラで行われる）
            IsSingleshotOverlayVisible = true;

            Logger?.LogDebug("✅ ExecuteSingleshotRequestEvent発行完了（オーバーレイ表示予定）");
            Logger?.LogInformation("シングルショット翻訳実行イベント発行完了: '{Title}'", selectedWindow.Title);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "シングルショット翻訳実行中にエラーが発生: {ErrorMessage}", ex.Message);

            // 🔔 [LOADING] エラー時はローディング終了
            IsLoading = false;

            // エラー時は状態をリセット
            IsSingleshotOverlayVisible = false;
        }
    }

    #endregion

    #region Event Handlers

    private async Task OnTranslationStatusChanged(TranslationStatusChangedEvent statusEvent)
    {
        var previousStatus = CurrentStatus;

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentStatus = statusEvent.Status;

            // 翻訳処理中のステータス判定
            var isProcessing = statusEvent.Status == TranslationStatus.Capturing
                            || statusEvent.Status == TranslationStatus.ProcessingOCR
                            || statusEvent.Status == TranslationStatus.Translating;

            IsTranslationActive = isProcessing;

            // 翻訳が終了・エラー・キャンセルされたらローディングを終了
            // 通常の終了は FirstTranslationResultReceivedEvent で処理
            if (statusEvent.Status is TranslationStatus.Completed
                or TranslationStatus.Error
                or TranslationStatus.Cancelled
                or TranslationStatus.Ready
                or TranslationStatus.Idle)
            {
                if (IsLoading)
                {
                    IsLoading = false;
                    Logger?.LogDebug($"✅ ローディング終了: Status={statusEvent.Status}");
                }
            }
        });

        Logger?.LogInformation("📊 翻訳状態変更: {PreviousStatus} -> {CurrentStatus}",
            previousStatus, statusEvent.Status);

        // 状態に応じてUIの状態を詳細にログ出力
        Logger?.LogDebug("🔄 UI状態更新: IsTranslationActive={IsActive}, IsLoading={IsLoading}, StartStopText='{Text}', StatusClass='{Class}'",
            IsTranslationActive, IsLoading, StartStopText, StatusIndicatorClass);
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
    /// 最初の翻訳結果受信イベントハンドラー（ローディング終了用）
    /// </summary>
    private async Task OnFirstTranslationResultReceived(FirstTranslationResultReceivedEvent evt)
    {
        Logger?.LogWarning("🔔 [LOADING_END] FirstTranslationResultReceivedEvent受信! ID: {EventId}", evt.Id);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            Logger?.LogWarning("🔔 [LOADING_END] UIスレッドで処理開始 - IsLoading: {IsLoading}", IsLoading);
            if (IsLoading)
            {
                IsLoading = false;
                Logger?.LogWarning("✅ [LOADING_END] 最初の翻訳結果受信によりローディング終了 - IsLoading=false");
            }
            else
            {
                Logger?.LogWarning("⚠️ [LOADING_END] 既にIsLoading=false のため変更なし");
            }
        });
    }

    /// <summary>
    /// 🔥 [Issue #300] VRAM警告イベントハンドラー
    /// VRAM使用率がCritical/Emergency状態の場合にUI警告を表示
    /// </summary>
    private async Task OnVramWarning(VramWarningEvent evt)
    {
        Logger?.LogDebug("[Issue #300] VramWarningEvent received: Level={Level}, Usage={Usage:F1}%",
            evt.Level, evt.VramUsagePercent);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            switch (evt.Level)
            {
                case VramWarningLevel.Emergency:
                case VramWarningLevel.Critical:
                    HasMemoryWarning = true;

                    // 一度だけトースト通知を表示
                    if (!_memoryWarningNotificationShown)
                    {
                        _memoryWarningNotificationShown = true;
                        Logger?.LogWarning("[Issue #300] Showing VRAM warning toast: {Message}", evt.Message);
                        await _notificationService.ShowWarningAsync(Strings.GpuMemoryWarning_Title, evt.Message, 8000).ConfigureAwait(false);
                    }
                    break;

                case VramWarningLevel.Normal:
                    HasMemoryWarning = false;
                    // 回復時は通知フラグをリセット（次回また警告可能に）
                    _memoryWarningNotificationShown = false;
                    Logger?.LogInformation("[Issue #300] VRAM warning cleared");
                    break;
            }
        });
    }

    /// <summary>
    /// 🔥 [Issue #300] リソース監視イベントハンドラー
    /// システムRAM警告（WarningRaised）の場合にUI警告を表示
    /// メモリ使用率が回復した場合は警告を解除
    /// </summary>
    private async Task OnResourceMonitoringWarning(ResourceMonitoringEvent evt)
    {
        // 警告発生またはメトリクス変更イベントのみ処理
        if (evt.EventType != ResourceMonitoringEventType.WarningRaised &&
            evt.EventType != ResourceMonitoringEventType.MetricsChanged)
            return;

        Logger?.LogDebug("[Issue #300] ResourceMonitoringEvent received: Type={Type}, MemoryUsage={Usage:F1}%",
            evt.EventType, evt.CurrentMetrics.MemoryUsagePercent);

        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // メモリ使用率の閾値（90%以上で警告）
            const double MemoryWarningThreshold = 90.0;
            const double MemoryRecoveryThreshold = 85.0; // ヒステリシス：85%以下で回復

            var memoryUsage = evt.CurrentMetrics.MemoryUsagePercent;

            // 警告発生時
            if (evt.EventType == ResourceMonitoringEventType.WarningRaised ||
                memoryUsage >= MemoryWarningThreshold)
            {
                if (evt.Warning != null && evt.Warning.Severity >= ResourceWarningSeverity.Warning)
                {
                    HasMemoryWarning = true;

                    // 一度だけトースト通知を表示
                    if (!_memoryWarningNotificationShown)
                    {
                        _memoryWarningNotificationShown = true;
                        // CA1863: CompositeFormatキャッシュ不要（1回のみ実行）
#pragma warning disable CA1863
                        var message = string.Format(Strings.MemoryWarning_SystemMemory, memoryUsage.ToString("F0"));
#pragma warning restore CA1863
                        Logger?.LogWarning("[Issue #300] Showing memory warning toast: {Message}", message);
                        await _notificationService.ShowWarningAsync(Strings.MemoryWarning_Title, message, 8000).ConfigureAwait(false);
                    }
                }
            }
            // メモリ回復時（ヒステリシス付き）
            else if (HasMemoryWarning && memoryUsage < MemoryRecoveryThreshold)
            {
                HasMemoryWarning = false;
                _memoryWarningNotificationShown = false;
                Logger?.LogInformation("[Issue #300] Memory warning cleared (usage: {Usage:F1}%)", memoryUsage);
            }
        });
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

    /// <summary>
    /// 🔥 [PHASE5.2E] ウォームアップ進捗変更イベントハンドラー
    /// BackgroundWarmupServiceがウォームアップ進行中に進捗を通知
    /// UIスレッドで実行してStartボタンのツールチップと有効状態を更新
    /// </summary>
    private void OnWarmupProgressChanged(object? sender, WarmupProgressEventArgs e)
    {
        // 🔥 [GEMINI_FIX] UIスレッドで実行（スレッドセーフティ確保）
        // BackgroundWarmupServiceはバックグラウンドスレッドからイベントを発行するため、
        // UI更新は必ずDispatcher.UIThread.InvokeAsyncでマーシャリングする必要がある
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            // 🔥 [PHASE5.2E.1] ウォームアップ失敗状態を最優先でチェック
            // 失敗時はエラーメッセージを表示し、Startボタンは永続的に無効化
            if (_warmupService.Status == Baketa.Core.Abstractions.GPU.WarmupStatus.Failed)
            {
                // ウォームアップ失敗: ユーザーにアプリ再起動を促すエラーメッセージ
                StartButtonTooltip = Strings.MainOverlay_Warmup_Failed;
                Logger?.LogError(_warmupService.LastError, "❌ [PHASE5.2E.1] ウォームアップ失敗 - Startボタン永続的に無効化");
                // Startボタンは canStartCapture() で IsWarmupCompleted をチェックするため、
                // 失敗状態では永遠に有効化されない（IsWarmupCompleted = false のまま）
            }
            else if (e.Progress < 1.0)
            {
                // 🔥 [ALPHA_0.1.2_FIX] 100%未満のみ進捗表示（100%時は完了扱い）
                // ウォームアップ進行中: 進捗パーセンテージを表示
                // CA1863: ローカライズされたリソース文字列は言語変更時に内容が変わるため、
                // CompositeFormatキャッシュは不適切。ウォームアップ中の数回のみ呼ばれる低頻度処理。
#pragma warning disable CA1863
                StartButtonTooltip = string.Format(Strings.MainOverlay_Warmup_Loading, e.Progress.ToString("P0"));
#pragma warning restore CA1863
                Logger?.LogDebug($"🔥 [PHASE5.2E] ウォームアップ進捗: {e.Progress:P0} - {e.Status}");
            }
            else
            {
                // 🔥 [ALPHA_0.1.2_FIX] 100%到達時にツールチップを即座に「翻訳を開始」に戻す
                // ウォームアップ完了: デフォルトツールチップに戻す
                StartButtonTooltip = Strings.MainOverlay_StartButton_Tooltip;
                Logger?.LogInformation("✅ [PHASE5.2E] ウォームアップ完了 - Startボタン有効化");
            }

            // 🔥 [PHASE5.2E] Startボタンの CanExecute を再評価
            // IsStartStopEnabled プロパティ変更を通知してReactiveCommandのCanExecuteを更新
            this.RaisePropertyChanged(nameof(IsStartStopEnabled));

            // 🔥 [WARMUP_FIX] IsWarmupCompletedプロパティの変更通知
            // ReactiveCommandのWhenAnyValueがウォームアップ完了状態を検出するために必須
            this.RaisePropertyChanged(nameof(IsWarmupCompleted));

            // 🔥 [ISSUE#164] UI/UX改善: Issue #164で追加されたプロパティの変更通知
            // IsLiveEnabledとIsSingleshotEnabledはIsWarmupCompletedに依存しているため通知必須
            this.RaisePropertyChanged(nameof(IsLiveEnabled));
            this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
        });
    }

    #endregion

    #region FirstRun

    /// <summary>
    /// 初回起動チェックと設定画面自動表示
    /// </summary>
    private async Task CheckAndHandleFirstRunAsync()
    {
        try
        {
            Logger?.LogInformation("🔍 初回起動チェック開始");

            if (_firstRunService.IsFirstRun())
            {
                Logger?.LogInformation("✅ 初回起動を検出 - 設定画面を自動表示します");

                // UIスレッドで設定画面を開く
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    Logger?.LogInformation("🎯 設定画面を開きます");
                    ExecuteSettings();
                });

                // 初回起動フラグをマーク
                _firstRunService.MarkAsRun();
                Logger?.LogInformation("✅ 初回起動フラグをマークしました");
            }
            else
            {
                Logger?.LogInformation("ℹ️ 2回目以降の起動です");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ 初回起動チェック処理でエラーが発生しました: {Message}", ex.Message);
        }
    }

    #endregion

    #region Dispose

    /// <summary>
    /// 🔥 [PHASE5.2E] リソース解放処理 - イベント購読解除
    /// メモリリーク防止のため、WarmupProgressChangedイベントの購読を解除
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // 🔥 [PHASE5.2E] イベント購読解除（メモリリーク防止）
            if (_warmupService != null)
            {
                _warmupService.WarmupProgressChanged -= OnWarmupProgressChanged;
            }

            // 🔥 [ISSUE#176] 認証状態変更イベントの購読解除
            if (_authService != null)
            {
                _authService.AuthStatusChanged -= OnAuthStatusChanged;
            }

            // 🔥 [Issue #318] 設定変更イベントの購読解除
            if (_unifiedSettingsService != null)
            {
                _unifiedSettingsService.SettingsChanged -= OnUnifiedSettingsChanged;
            }

            Logger?.LogDebug("🔥 [PHASE5.2E] MainOverlayViewModel Dispose完了 - イベント購読解除");
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// 🔥 [ISSUE#176] 起動時の認証状態を初期化
    /// </summary>
    private async Task InitializeAuthStateAsync()
    {
        try
        {
            var session = await _authService.GetCurrentSessionAsync().ConfigureAwait(false);
            _isLoggedIn = session != null;

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
            });

            Logger?.LogDebug("初期認証状態: IsLoggedIn={IsLoggedIn}", _isLoggedIn);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "認証状態の初期化に失敗しました");
            _isLoggedIn = false;
        }
    }

    /// <summary>
    /// 🔥 [ISSUE#176] 認証状態変更イベントハンドラ
    /// ログアウト時にUIを起動時状態にリセット
    /// </summary>
    private void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        Logger?.LogDebug("[AUTH_DEBUG] 認証状態変更: IsLoggedIn={IsLoggedIn}", e.IsLoggedIn);

        // ログイン状態を更新
        _isLoggedIn = e.IsLoggedIn;

        // UIスレッドでボタン状態を更新
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
        {
            // ボタン状態のPropertyChanged通知
            this.RaisePropertyChanged(nameof(IsSelectWindowEnabled));
            this.RaisePropertyChanged(nameof(IsStartStopEnabled));
            this.RaisePropertyChanged(nameof(IsLiveEnabled));
            this.RaisePropertyChanged(nameof(IsSingleshotEnabled));
            this.RaisePropertyChanged(nameof(SettingsEnabled));

            if (e.IsLoggedIn && e.User?.Id != null)
            {
                // [Issue #261] ログイン時: ローカル同意をDBに同期
                // [Gemini Review] セキュリティ強化: JWTを渡して認証付きで同期
                Logger?.LogDebug("[Issue #261] ログイン検出 - 同意同期開始");
                try
                {
                    if (_consentService != null)
                    {
                        // 現在のセッションからアクセストークンを取得
                        var session = await _authService.GetCurrentSessionAsync().ConfigureAwait(false);
                        if (session != null && !string.IsNullOrEmpty(session.AccessToken))
                        {
                            await _consentService.SyncLocalConsentToServerAsync(e.User.Id, session.AccessToken).ConfigureAwait(false);
                            Logger?.LogDebug("[Issue #261] 同意同期完了");
                        }
                        else
                        {
                            Logger?.LogWarning("[Issue #261] アクセストークンが取得できないため同意同期をスキップ");
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 同期失敗はアプリ動作をブロックしない
                    Logger?.LogWarning(ex, "[Issue #261] 同意同期失敗（継続）");
                }
            }
            else if (!e.IsLoggedIn)
            {
                // ログアウト時: UIを起動時状態にリセット
                Logger?.LogDebug("[AUTH_DEBUG] ログアウト検出 - UIリセット開始");

                // 1. 翻訳を停止（実行中の場合）
                if (IsTranslationActive)
                {
                    Logger?.LogDebug("[AUTH_DEBUG] 翻訳停止");
                    await StopTranslationAsync();
                }

                // 2. オーバーレイを非表示
                if (_overlayManager != null)
                {
                    Logger?.LogDebug("[AUTH_DEBUG] オーバーレイ非表示");
                    await _overlayManager.SetAllVisibilityAsync(false);
                }

                // 3. ターゲットウィンドウ選択をリセット
                SelectedWindow = null;
                IsWindowSelected = false;

                // 4. 翻訳結果を非表示
                IsTranslationResultVisible = false;

                // 5. シングルショットオーバーレイ状態をリセット
                _isSingleshotOverlayVisible = false;

                // 6. ステータスをアイドルに戻す
                CurrentStatus = TranslationStatus.Idle;

                Logger?.LogDebug("[AUTH_DEBUG] ログアウト時UIリセット完了 - 起動時状態に戻りました");
            }
        });
    }

    /// <summary>
    /// 🔥 [Issue #318] 設定変更イベントハンドラ
    /// EXモード（UseLocalEngine）の変更を検出してUIを更新
    /// </summary>
    private void OnUnifiedSettingsChanged(object? sender, SettingsChangedEventArgs e)
    {
        // 翻訳設定が変更された場合、EXモード表示を更新
        if (e.SettingsType == SettingsType.Translation)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                this.RaisePropertyChanged(nameof(IsEXModeEnabled));
                Logger?.LogDebug("[Issue #318] EXモード表示更新: IsEXModeEnabled={IsEX}", IsEXModeEnabled);
            });
        }
    }

    #endregion
}
