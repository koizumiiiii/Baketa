using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.UI.Framework.Events;
using Baketa.UI.Framework;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using WindowInfo = Baketa.Core.Abstractions.Platform.Windows.Adapters.WindowInfo;

namespace Baketa.UI.ViewModels;

/// <summary>
/// ウィンドウ選択ダイアログのViewModel
/// </summary>
public class WindowSelectionDialogViewModel : ViewModelBase
{
    private readonly IWindowManagerAdapter _windowManager;
    private WindowInfo? _selectedWindow;
    private bool _isLoading;

    public WindowSelectionDialogViewModel(
        IEventAggregator eventAggregator,
        ILogger<WindowSelectionDialogViewModel> logger,
        IWindowManagerAdapter windowManager)
        : base(eventAggregator, logger)
    {
        _windowManager = windowManager ?? throw new ArgumentNullException(nameof(windowManager));
        
        AvailableWindows = [];
        
        // コマンドの初期化
        SelectWindowCommand = ReactiveCommand.CreateFromTask<WindowInfo>(ExecuteSelectWindowAsync);
        CancelCommand = ReactiveCommand.Create(ExecuteCancel);
        RefreshCommand = ReactiveCommand.CreateFromTask(ExecuteRefreshAsync);
        
        // 初期ロード
        _ = Task.Run(LoadAvailableWindowsAsync);
    }

    /// <summary>
    /// 利用可能なウィンドウリスト
    /// </summary>
    public ObservableCollection<WindowInfo> AvailableWindows { get; }

    /// <summary>
    /// 選択中のウィンドウ
    /// </summary>
    public WindowInfo? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedWindow, value);
            // CanSelectプロパティの変更通知
            this.RaisePropertyChanged(nameof(CanSelect));
        }
    }

    /// <summary>
    /// ロード中状態
    /// </summary>
    public new bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    /// <summary>
    /// 選択可能状態（ウィンドウが選択されている）
    /// </summary>
    public bool CanSelect => SelectedWindow != null;

    /// <summary>
    /// ウィンドウ選択コマンド
    /// </summary>
    public ReactiveCommand<WindowInfo, Unit> SelectWindowCommand { get; }

    /// <summary>
    /// キャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelCommand { get; }

    /// <summary>
    /// 更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>
    /// ダイアログ結果
    /// </summary>
    public WindowInfo? DialogResult { get; private set; }

    /// <summary>
    /// ダイアログが閉じられたかどうか
    /// </summary>
    private bool _isClosed;
    public bool IsClosed
    {
        get => _isClosed;
        private set => this.RaiseAndSetIfChanged(ref _isClosed, value);
    }

    /// <summary>
    /// 利用可能なウィンドウを読み込み
    /// </summary>
    private async Task LoadAvailableWindowsAsync()
    {
        try
        {
            IsLoading = true;
            Logger?.LogDebug("Loading available windows...");

            await Task.Run(async () =>
            {
                var windows = _windowManager.GetRunningApplicationWindows()
                    .Where(w => w.IsVisible && !w.IsMinimized && !string.IsNullOrWhiteSpace(w.Title))
                    .Where(w => w.Title != "WindowSelectionDialog") // 自分自身を除外
                    .OrderBy(w => w.Title)
                    .ToList();

                // UIスレッドで更新
                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    AvailableWindows.Clear();
                    foreach (var window in windows)
                    {
                        AvailableWindows.Add(window);
                    }
                });
            }).ConfigureAwait(false);

            Logger?.LogDebug("Loaded {Count} available windows", AvailableWindows.Count);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to load available windows");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// ウィンドウ選択実行
    /// </summary>
    private async Task ExecuteSelectWindowAsync(WindowInfo selectedWindow)
    {
        try
        {
            if (selectedWindow == null)
            {
                Logger?.LogWarning("❌ ウィンドウが選択されていません");
                return;
            }

            Logger?.LogInformation("🎯 ウィンドウ選択実行: '{Title}' (Handle: {Handle})", 
                selectedWindow.Title, selectedWindow.Handle);

            DialogResult = selectedWindow;
            IsClosed = true;

            // ウィンドウ選択イベントを発行
            Logger?.LogDebug("📢 StartTranslationRequestEventを発行");
            await EventAggregator.PublishAsync(new StartTranslationRequestEvent(selectedWindow)).ConfigureAwait(false);
            
            Logger?.LogDebug("✅ ウィンドウ選択処理完了");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "💥 ウィンドウ選択処理中にエラー: {ErrorMessage}", ex.Message);
        }
    }

    /// <summary>
    /// キャンセル実行
    /// </summary>
    private void ExecuteCancel()
    {
        Logger?.LogDebug("Window selection cancelled");
        
        // UIスレッドで確実に実行
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            DialogResult = null;
            IsClosed = true;
        });
    }

    /// <summary>
    /// 更新実行
    /// </summary>
    private async Task ExecuteRefreshAsync()
    {
        Logger?.LogDebug("Refreshing window list");
        await LoadAvailableWindowsAsync().ConfigureAwait(false);
    }
}