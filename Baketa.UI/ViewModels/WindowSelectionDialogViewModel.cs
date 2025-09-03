using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Platform.Windows.Adapters;
using Baketa.Core.Utilities;
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
        
        // コマンドの初期化（UI スレッドで安全に初期化）
        try
        {
            SelectWindowCommand = ReactiveCommand.CreateFromTask<WindowInfo>(ExecuteSelectWindowAsync,
                outputScheduler: RxApp.MainThreadScheduler);
            CancelCommand = ReactiveCommand.Create(ExecuteCancel,
                outputScheduler: RxApp.MainThreadScheduler);
            RefreshCommand = ReactiveCommand.CreateFromTask(ExecuteRefreshAsync,
                outputScheduler: RxApp.MainThreadScheduler);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "WindowSelectionDialogViewModelのReactiveCommand初期化エラー");
            throw;
        }
        
        // 🚀 GEMINI FIX: レースコンディション回避 - コンストラクタ内非同期初期化を削除
        // Task.Runによる初期ロードは削除し、明示的なRefreshCommand呼び出しに変更
        
        Logger?.LogInformation("WindowSelectionDialogViewModel初期化完了 - 非同期ロードはRefreshCommand経由で実行");
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
            try
            {
                this.RaiseAndSetIfChanged(ref _selectedWindow, value);
                // CanSelectプロパティの変更通知
                this.RaisePropertyChanged(nameof(CanSelect));
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でSelectedWindow設定失敗 - 直接設定で続行");
                _selectedWindow = value;
                // 直接設定でもCanSelectの通知を試行
                try
                {
                    this.RaisePropertyChanged(nameof(CanSelect));
                }
                catch { /* セカンダリ例外は無視 */ }
            }
        }
    }

    /// <summary>
    /// ロード中状態
    /// </summary>
    public new bool IsLoading
    {
        get => _isLoading;
        private set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isLoading, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsLoading設定失敗 - 直接設定で続行");
                _isLoading = value;
            }
        }
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
    public WindowInfo? DialogResult { get; internal set; }

    /// <summary>
    /// ダイアログが閉じられたかどうか
    /// </summary>
    private bool _isClosed;
    public bool IsClosed
    {
        get => _isClosed;
        private set
        {
            try
            {
                this.RaiseAndSetIfChanged(ref _isClosed, value);
            }
            catch (InvalidOperationException ex)
            {
                Logger?.LogWarning(ex, "UIスレッド違反でIsClosed設定失敗 - 直接設定で続行");
                _isClosed = value;
            }
        }
    }

    /// <summary>
    /// ウィンドウが翻訳対象として有効かを判定
    /// </summary>
    /// <param name="window">ウィンドウ情報</param>
    /// <returns>有効な場合はtrue</returns>
    private static bool IsValidWindow(WindowInfo window)
    {
        // 基本的な条件チェック
        if (string.IsNullOrWhiteSpace(window.Title))
            return false;
        
        // 表示状態のチェック
        if (!window.IsVisible || window.IsMinimized)
            return false;
        
        // ウィンドウサイズの有効性チェック
        if (window.Bounds.Width <= 0 || window.Bounds.Height <= 0)
            return false;
        
        // 極小ウィンドウの除外（翻訳には適さない）
        if (window.Bounds.Width < 100 || window.Bounds.Height < 50)
            return false;
        
        // システムウィンドウ系の除外
        if (IsSystemWindow(window.Title))
            return false;
        
        return true;
    }

    /// <summary>
    /// システムウィンドウかどうかを判定
    /// </summary>
    /// <param name="title">ウィンドウタイトル</param>
    /// <returns>システムウィンドウの場合はtrue</returns>
    private static bool IsSystemWindow(string title)
    {
        var systemWindowPatterns = new[]
        {
            "Program Manager",
            "Desktop Window Manager",
            "Windows Shell Experience Host",
            "Microsoft Text Input Application",
            "Task Manager",
            "Settings",
            "Control Panel",
            "Registry Editor",
            "Event Viewer",
            "Device Manager"
        };

        return systemWindowPatterns.Any(pattern => 
            title.Contains(pattern, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 利用可能なウィンドウを読み込み
    /// </summary>
    private async Task LoadAvailableWindowsAsync()
    {
        try
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = true;
            });
            
            Logger?.LogDebug("🔍 LoadAvailableWindowsAsync開始");
            Console.WriteLine("🔍 LoadAvailableWindowsAsync開始");

            // バックグラウンドでウィンドウリストを取得
            var windows = await Task.Run(() =>
            {
                Logger?.LogDebug("🔍 _windowManager.GetRunningApplicationWindows()呼び出し開始");
                Console.WriteLine("🔍 _windowManager.GetRunningApplicationWindows()呼び出し開始");
                
                var rawWindows = _windowManager.GetRunningApplicationWindows();
                
                Logger?.LogDebug("🔍 _windowManager.GetRunningApplicationWindows()呼び出し完了: {Count}個のウィンドウを取得", rawWindows.Count);
                Console.WriteLine($"🔍 _windowManager.GetRunningApplicationWindows()呼び出し完了: {rawWindows.Count}個のウィンドウを取得");
                
                var filteredWindows = rawWindows
                    .Where(IsValidWindow)
                    .Where(w => w.Title != "WindowSelectionDialog") // 自分自身を除外
                    .ToList();
                    
                Logger?.LogDebug("🔍 フィルタリング完了: {Count}個の有効ウィンドウ", filteredWindows.Count);
                Console.WriteLine($"🔍 フィルタリング完了: {filteredWindows.Count}個の有効ウィンドウ");
                
                return filteredWindows;
            }).ConfigureAwait(false);

            // UIスレッドで更新
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableWindows.Clear();
                foreach (var window in windows)
                {
                    AvailableWindows.Add(window);
                }
            });

            Logger?.LogDebug("✅ LoadAvailableWindowsAsync完了: {Count}個のウィンドウを表示", AvailableWindows.Count);
            Console.WriteLine($"✅ LoadAvailableWindowsAsync完了: {AvailableWindows.Count}個のウィンドウを表示");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "❌ LoadAvailableWindowsAsyncでエラー発生");
            Console.WriteLine($"❌ LoadAvailableWindowsAsyncでエラー発生: {ex.Message}");
            
            // フォールバック：空の一覧を表示
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                AvailableWindows.Clear();
            });
        }
        finally
        {
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsLoading = false;
            });
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
                Logger?.LogWarning("No window selected");
                return;
            }

            Logger?.LogInformation("Window selection executed: '{Title}' (Handle: {Handle})", 
                selectedWindow.Title, selectedWindow.Handle);
            
            DebugLogUtility.WriteLog($"📢 ウィンドウ選択実行: '{selectedWindow.Title}' (Handle={selectedWindow.Handle})");

            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                DialogResult = selectedWindow;
                IsClosed = true;
            });

            DebugLogUtility.WriteLog($"✅ ウィンドウ選択ダイアログ完了: DialogResult設定済み");
            Logger?.LogDebug("Window selection processing completed");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error during window selection processing: {ErrorMessage}", ex.Message);
            DebugLogUtility.WriteLog($"❌ ウィンドウ選択処理エラー: {ex.Message}");
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
    internal async Task ExecuteRefreshAsync()
    {
        Logger?.LogDebug("Refreshing window list");
        await LoadAvailableWindowsAsync().ConfigureAwait(false);
    }
}