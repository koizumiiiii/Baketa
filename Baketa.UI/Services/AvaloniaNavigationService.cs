using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Baketa.UI.ViewModels.Auth;
using Baketa.UI.Views.Auth;
using Baketa.UI.Views;
using Baketa.UI.ViewModels;

namespace Baketa.UI.Services;

/// <summary>
/// Avalonia用ナビゲーションサービス実装
/// </summary>
/// <remarks>
/// AvaloniaNavigationServiceを初期化します
/// </remarks>
/// <param name="serviceProvider">サービスプロバイダー</param>
/// <param name="logger">ロガー</param>
internal sealed class AvaloniaNavigationService(
    IServiceProvider serviceProvider,
    ILogger<AvaloniaNavigationService> logger) : INavigationService, IDisposable
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<AvaloniaNavigationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private bool _disposed;

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, Exception?> _logNavigating =
        LoggerMessage.Define<string>(
            LogLevel.Debug,
            new EventId(1, "Navigating"),
            "画面遷移: {Screen}");

    private static readonly Action<ILogger, string, Exception> _logNavigationError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, "NavigationError"),
            "画面遷移エラー: {Screen}");

    /// <summary>
    /// ログイン画面を表示します
    /// </summary>
    /// <returns>ログインが成功した場合true</returns>
    public async Task<bool> ShowLoginAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "Login", null);

            var loginViewModel = _serviceProvider.GetRequiredService<LoginViewModel>();
            var loginWindow = new LoginView(loginViewModel);

            var result = await ShowDialogAsync(loginWindow).ConfigureAwait(false);
            return result == true;
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "Login", ex);
            return false;
        }
    }

    /// <summary>
    /// サインアップ画面を表示します
    /// </summary>
    /// <returns>サインアップが成功した場合true</returns>
    public async Task<bool> ShowSignupAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "Signup", null);

            var signupViewModel = _serviceProvider.GetRequiredService<SignupViewModel>();
            var signupWindow = new SignupView(signupViewModel);

            var result = await ShowDialogAsync(signupWindow).ConfigureAwait(false);
            return result == true;
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "Signup", ex);
            return false;
        }
    }

    /// <summary>
    /// メイン画面を表示します
    /// </summary>
    public async Task ShowMainWindowAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "MainOverlayView", null);

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                var mainOverlayViewModel = _serviceProvider.GetRequiredService<ViewModels.MainOverlayViewModel>();
                var mainOverlayView = new MainOverlayView
                {
                    DataContext = mainOverlayViewModel
                };

                desktop.MainWindow = mainOverlayView;
                mainOverlayView.Show();
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "MainOverlayView", ex);
        }
    }

    /// <summary>
    /// 設定画面を表示します
    /// </summary>
    public async Task ShowSettingsAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "Settings", null);

            // αテスト向けSimpleSettings画面を表示
            var settingsViewModel = _serviceProvider.GetRequiredService<SimpleSettingsViewModel>();
            var settingsWindow = new SimpleSettingsView
            {
                DataContext = settingsViewModel
            };
            
            Console.WriteLine("🔧 設定画面を表示");
            await ShowDialogAsync(settingsWindow).ConfigureAwait(false);
            Console.WriteLine("🔧 設定画面表示完了");
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "Settings", ex);
        }
    }

    /// <summary>
    /// 現在のウィンドウを閉じます
    /// </summary>
    public async Task CloseCurrentWindowAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "CloseCurrentWindow", null);

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow?.Close();
            }

            await Task.CompletedTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "CloseCurrentWindow", ex);
        }
    }

    /// <summary>
    /// ログアウトして認証画面に戻ります
    /// </summary>
    public async Task LogoutAndShowLoginAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "LogoutAndShowLogin", null);

            // 現在のセッションをクリア
            var authService = _serviceProvider.GetRequiredService<Baketa.Core.Abstractions.Auth.IAuthService>();
            await authService.SignOutAsync().ConfigureAwait(false);

            await ShowLoginAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "LogoutAndShowLogin", ex);
        }
    }

    /// <summary>
    /// ダイアログとしてウィンドウを表示します
    /// </summary>
    /// <param name="window">表示するウィンドウ</param>
    /// <returns>ダイアログの結果</returns>
    private static async Task<bool?> ShowDialogAsync(Window window)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow != null)
        {
            return await window.ShowDialog<bool?>(desktop.MainWindow).ConfigureAwait(false);
        }
        else
        {
            window.Show();
            return null;
        }
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            // マネージドリソースの解放
            // 現在は特に解放すべきリソースがないが、将来的な拡張に備える
            _disposed = true;
        }
    }

    /// <summary>
    /// オブジェクトが破棄されているかチェックします
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }
}
