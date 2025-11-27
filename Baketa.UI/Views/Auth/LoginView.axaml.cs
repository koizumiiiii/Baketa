using System;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;

namespace Baketa.UI.Views.Auth;

/// <summary>
/// ログイン画面のView
/// </summary>
public partial class LoginView : Window
{
    private readonly LoginViewModel? _viewModel;
    private bool _isAuthenticationSuccess;

    /// <summary>
    /// LoginViewを初期化します（デザイナー用）
    /// </summary>
    public LoginView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// LoginViewを初期化します
    /// </summary>
    /// <param name="viewModel">LoginViewModel</param>
    public LoginView(LoginViewModel viewModel) : this()
    {
        DataContext = viewModel;
        _viewModel = viewModel;

        // 🔥 [ISSUE#167] ダイアログを閉じる要求イベント
        // 認証成功時と画面切り替え時の両方でこのイベントが発火される
        viewModel.CloseDialogRequested += OnCloseDialogRequested;
    }

    private void OnCloseDialogRequested()
    {
        _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: CloseDialogRequestedイベント受信");
        // 認証成功フラグを設定（OnClosedで使用）
        _isAuthenticationSuccess = true;
        // Post()を使用して確実にキューイングし、現在の処理が完了してからCloseを実行
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: Close(true)実行");
            Close(true);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    /// <summary>
    /// ウィンドウが閉じられた時のリソース解放
    /// </summary>
    /// <param name="e">イベント引数</param>
    protected override void OnClosed(EventArgs e)
    {
        _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: OnClosed開始");

        if (_viewModel != null)
        {
            _viewModel.CloseDialogRequested -= OnCloseDialogRequested;
            // 🔥 [ISSUE#167] ViewModelをDisposeしてAuthStatusChangedイベントの購読を解除
            _viewModel.Dispose();
        }

        base.OnClosed(e);

        _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: base.OnClosed完了");

        // 🔥 [FIX] Phase 2: ウィンドウが完全に閉じた後に認証モードを解除
        // これにより、ReactiveUI通知が破棄済みのウィンドウにアクセスするのを防ぐ
        if (_isAuthenticationSuccess)
        {
            _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: 認証成功のため状態リセットをスケジュール");
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow?.DataContext is MainOverlayViewModel mainOverlayViewModel)
                {
                    mainOverlayViewModel.SetAuthenticationMode(false);
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        _viewModel?.LogDebug("[AUTH_DEBUG] LoginView: OnClosed完了");
    }
}
