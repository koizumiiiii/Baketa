using Baketa.Application.Services.Translation;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Baketa.UI.Services;
using Baketa.UI.Views;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using System;
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
    private bool _isOverlayVisible;

    public MainOverlayViewModel(
        IEventAggregator eventAggregator,
        ILogger<MainOverlayViewModel> logger,
        IWindowManagerAdapter windowManager,
        TranslationResultOverlayManager overlayManager)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        _overlayManager = overlayManager ?? throw new ArgumentNullException(nameof(overlayManager));
        InitializeCommands();
        InitializeEventHandlers();
    }

    private readonly IWindowManagerAdapter _windowManager;
    private readonly TranslationResultOverlayManager _overlayManager;

    #region Properties

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set => this.RaiseAndSetIfChanged(ref _isCollapsed, value);
    }

    public bool IsTranslationActive
    {
        get => _isTranslationActive;
        set => this.RaiseAndSetIfChanged(ref _isTranslationActive, value);
    }

    public TranslationStatus CurrentStatus
    {
        get => _currentStatus;
        set => this.RaiseAndSetIfChanged(ref _currentStatus, value);
    }

    public bool IsOverlayVisible
    {
        get => _isOverlayVisible;
        set => this.RaiseAndSetIfChanged(ref _isOverlayVisible, value);
    }

    // UI状態の計算プロパティ
    public bool ShowHideEnabled => true;
    public bool SettingsEnabled => !IsTranslationActive;
    public string StartStopText => IsTranslationActive ? "Stop" : "Start";
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
        StartStopCommand = ReactiveCommand.CreateFromTask(ExecuteStartStopAsync);
        ShowHideCommand = ReactiveCommand.Create(ExecuteShowHide, 
            this.WhenAnyValue(x => x.ShowHideEnabled));
        SettingsCommand = ReactiveCommand.Create(ExecuteSettings,
            this.WhenAnyValue(x => x.SettingsEnabled));
        FoldCommand = ReactiveCommand.Create(ExecuteFold);
        ExitCommand = ReactiveCommand.CreateFromTask(ExecuteExitAsync);
    }

    private void InitializeEventHandlers()
    {
        // 翻訳状態変更イベントの購読
        SubscribeToEvent<TranslationStatusChangedEvent>(OnTranslationStatusChanged);

        // 翻訳結果表示状態変更イベントの購読
        SubscribeToEvent<TranslationDisplayVisibilityChangedEvent>(OnTranslationDisplayVisibilityChanged);
    }

    #endregion

    #region Command Handlers

    private async Task ExecuteStartStopAsync()
    {
        try
        {
            if (IsTranslationActive)
            {
                await StopTranslationAsync().ConfigureAwait(false);
            }
            else
            {
                await StartTranslationAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error during start/stop translation");
            CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
        }
    }

    private async Task StartTranslationAsync()
    {
        Logger?.LogInformation("🚀 翻訳ワークフローを開始");

        try
        {
            // 1. ウィンドウ選択ダイアログを表示
            Logger?.LogDebug("🔍 ウィンドウ選択ダイアログを表示");
            var selectedWindow = await ShowWindowSelectionDialogAsync().ConfigureAwait(false);
            if (selectedWindow == null)
            {
                Logger?.LogDebug("❌ ウィンドウ選択がキャンセルされました");
                return; // キャンセル
            }

            Logger?.LogInformation("✅ ウィンドウが選択されました: '{Title}' (Handle={Handle})", 
                selectedWindow.Title, selectedWindow.Handle);

            // 2. 翻訳開始
            Logger?.LogDebug("📊 翻訳状態をキャプチャ中に設定");
            CurrentStatus = TranslationStatus.Capturing; // TranslationStatus.Activeがないため適切な値を使用
            IsTranslationActive = true;

            // オーバーレイマネージャーを初期化
            Logger?.LogDebug("🖼️ オーバーレイマネージャーを初期化");
            await _overlayManager.InitializeAsync().ConfigureAwait(false);

            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            var startTranslationEvent = new StartTranslationRequestEvent(selectedWindow);
            await PublishEventAsync(startTranslationEvent).ConfigureAwait(false);

            Logger?.LogInformation("🎉 翻訳が正常に開始されました: '{Title}'", selectedWindow.Title);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "💥 翻訳開始に失敗: {ErrorMessage}", ex.Message);
            CurrentStatus = TranslationStatus.Idle; // エラー時は待機状態に戻す
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

            var owner = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;
            
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
        Logger?.LogInformation("Stopping translation");

        // 翻訳停止 + ウィンドウ選択解除
        CurrentStatus = TranslationStatus.Idle;
        IsTranslationActive = false;
        IsOverlayVisible = false;

        // オーバーレイを非表示にして初期化
        await _overlayManager.HideAsync().ConfigureAwait(false);

        var stopTranslationEvent = new StopTranslationRequestEvent();
        await PublishEventAsync(stopTranslationEvent).ConfigureAwait(false);

        Logger?.LogInformation("Translation stopped successfully");
    }

    private async void ExecuteShowHide()
    {
        Logger?.LogDebug("Show/Hide toggle requested");
        
        IsOverlayVisible = !IsOverlayVisible;
        
        // オーバーレイマネージャーを使用して表示/非表示を切り替え
        if (IsOverlayVisible)
        {
            await _overlayManager.ShowAsync().ConfigureAwait(false);
        }
        else
        {
            await _overlayManager.HideAsync().ConfigureAwait(false);
        }
        
        var toggleEvent = new ToggleTranslationDisplayRequestEvent(IsOverlayVisible);
        await PublishEventAsync(toggleEvent).ConfigureAwait(false);
        
        Logger?.LogDebug("Translation display visibility toggled: {IsVisible}", IsOverlayVisible);
    }

    private async void ExecuteSettings()
    {
        try
        {
            Logger?.LogDebug("Opening simple settings dialog");

            // SimpleSettingsViewModelを作成
            var settingsViewModel = new SimpleSettingsViewModel(EventAggregator, 
                Microsoft.Extensions.Logging.LoggerFactoryExtensions.CreateLogger<SimpleSettingsViewModel>(
                    Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory.Instance));

            // 設定ダイアログを表示
            var settingsDialog = new SimpleSettingsView
            {
                DataContext = settingsViewModel
            };

            var owner = Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow : null;

            if (owner != null)
            {
                await settingsDialog.ShowDialog(owner).ConfigureAwait(false);
            }
            else
            {
                settingsDialog.Show();
            }

            Logger?.LogDebug("Settings dialog opened");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to open settings dialog");
        }
    }

    private void ExecuteFold()
    {
        IsCollapsed = !IsCollapsed;
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

    private Task OnTranslationStatusChanged(TranslationStatusChangedEvent statusEvent)
    {
        var previousStatus = CurrentStatus;
        CurrentStatus = statusEvent.Status;
        IsTranslationActive = statusEvent.Status == TranslationStatus.Capturing || statusEvent.Status == TranslationStatus.Translating;
        
        Logger?.LogInformation("📊 翻訳状態変更: {PreviousStatus} -> {CurrentStatus}", 
            previousStatus, statusEvent.Status);
            
        // 状態に応じてUIの状態を詳細にログ出力
        Logger?.LogDebug("🔄 UI状態更新: IsTranslationActive={IsActive}, StartStopText='{Text}', StatusClass='{Class}'", 
            IsTranslationActive, StartStopText, StatusIndicatorClass);
            
        return Task.CompletedTask;
    }

    private Task OnTranslationDisplayVisibilityChanged(TranslationDisplayVisibilityChangedEvent visibilityEvent)
    {
        IsOverlayVisible = visibilityEvent.IsVisible;
        Logger?.LogDebug("Translation display visibility changed: {IsVisible}", visibilityEvent.IsVisible);
        return Task.CompletedTask;
    }

    #endregion
}