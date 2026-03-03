using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;
using Baketa.UI.Views;
using Baketa.UI.Views.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Avalonia用ナビゲーションサービス実装
/// </summary>
/// <remarks>
/// AvaloniaNavigationServiceを初期化します
/// </remarks>
/// <param name="serviceProvider">サービスプロバイダー</param>
/// <param name="logger">ロガー</param>
/// <param name="dispatcher">[Issue #485] UIスレッドディスパッチャー</param>
internal sealed class AvaloniaNavigationService(
    IServiceProvider serviceProvider,
    ILogger<AvaloniaNavigationService> logger,
    IUIDispatcher dispatcher) : INavigationService, IDisposable
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<AvaloniaNavigationService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly IUIDispatcher _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
    private bool _disposed;

    // 🔥 [ISSUE#167] 二重ナビゲーション防止フラグ
    private volatile bool _isNavigatingToMainWindow;

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
        MainOverlayViewModel? mainOverlayViewModel = null;
        bool isCalledFromSettingsDialog = false;
        try
        {
            _logNavigating(_logger, "Login", null);

            // 設定画面から呼ばれているかチェック（設定画面が開いている場合はMainOverlayViewを操作しない）
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                isCalledFromSettingsDialog = desktop.Windows.Any(w => w is Views.SettingsWindow && w.IsVisible);
            }

            if (!isCalledFromSettingsDialog)
            {
                // 🔥 [ISSUE#167] メインウィンドウを表示し、認証モードを有効化
                // UIスレッドで実行する必要がある
                await _dispatcher.InvokeAsync(async () =>
                {
                    mainOverlayViewModel = _serviceProvider.GetService<MainOverlayViewModel>();
                    if (mainOverlayViewModel != null)
                    {
                        mainOverlayViewModel.SetAuthenticationMode(true);
                    }
                    await ShowMainWindowInternalAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("設定画面からのログイン呼び出し - MainOverlayView操作をスキップ");
            }

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
        finally
        {
            // 🔥 [ISSUE#167] 認証モードを解除（設定画面から呼ばれた場合はスキップ）
            if (!isCalledFromSettingsDialog)
            {
                mainOverlayViewModel?.SetAuthenticationMode(false);
            }
        }
    }

    /// <summary>
    /// サインアップ画面を表示します
    /// </summary>
    /// <returns>サインアップが成功した場合true</returns>
    public async Task<bool> ShowSignupAsync()
    {
        ThrowIfDisposed();
        MainOverlayViewModel? mainOverlayViewModel = null;
        bool isCalledFromSettingsDialog = false;
        try
        {
            _logNavigating(_logger, "Signup", null);

            // 設定画面から呼ばれているかチェック（設定画面が開いている場合はMainOverlayViewを操作しない）
            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                isCalledFromSettingsDialog = desktop.Windows.Any(w => w is Views.SettingsWindow && w.IsVisible);
            }

            if (!isCalledFromSettingsDialog)
            {
                // 🔥 [ISSUE#167] メインウィンドウを表示し、認証モードを有効化
                // UIスレッドで実行する必要がある
                await _dispatcher.InvokeAsync(async () =>
                {
                    mainOverlayViewModel = _serviceProvider.GetService<MainOverlayViewModel>();
                    if (mainOverlayViewModel != null)
                    {
                        mainOverlayViewModel.SetAuthenticationMode(true);
                    }
                    await ShowMainWindowInternalAsync().ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            else
            {
                _logger.LogDebug("設定画面からのサインアップ呼び出し - MainOverlayView操作をスキップ");
            }

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
        finally
        {
            // 🔥 [ISSUE#167] 認証モードを解除（設定画面から呼ばれた場合はスキップ）
            if (!isCalledFromSettingsDialog)
            {
                mainOverlayViewModel?.SetAuthenticationMode(false);
            }
        }
    }

    /// <summary>
    /// メイン画面を表示します
    /// </summary>
    public async Task ShowMainWindowAsync()
    {
        ThrowIfDisposed();

        // 🔥 [ISSUE#167] 二重ナビゲーション防止（LoginViewModelとSignupViewModelの両方が呼び出す可能性がある）
        if (_isNavigatingToMainWindow)
        {
            _logger.LogDebug("[NAVIGATION] ShowMainWindowAsync: 既にナビゲーション中のためスキップ");
            return;
        }

        _isNavigatingToMainWindow = true;
        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                await ShowMainWindowInternalAsync().ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        finally
        {
            _isNavigatingToMainWindow = false;
        }
    }

    /// <summary>
    /// メイン画面を表示します（内部実装、UIスレッドで呼び出すこと）
    /// </summary>
    private Task ShowMainWindowInternalAsync()
    {
        try
        {
            _logNavigating(_logger, "MainOverlayView", null);

            if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 🔥 [ISSUE#167] 既にMainOverlayViewが存在する場合は表示のみ
                // LoginView/SignupViewが設定されている場合は新しいMainOverlayViewを作成
                if (desktop.MainWindow is MainOverlayView existingOverlay)
                {
                    existingOverlay.Show();
                    _logger.LogDebug("Showing existing MainOverlayView");
                    return Task.CompletedTask;
                }

                // 🔥 [ISSUE#167] 古いウィンドウを記録
                var oldWindow = desktop.MainWindow;
                _logger.LogDebug("[NAVIGATION_DEBUG] 古いウィンドウ: {WindowType}", oldWindow?.GetType().Name ?? "null");

                // MainOverlayViewを作成
                _logger.LogDebug("[NAVIGATION_DEBUG] MainOverlayViewModel取得開始");
                var mainOverlayViewModel = _serviceProvider.GetRequiredService<ViewModels.MainOverlayViewModel>();
                _logger.LogDebug("[NAVIGATION_DEBUG] MainOverlayViewModel取得完了");

                _logger.LogDebug("[NAVIGATION_DEBUG] MainOverlayView作成開始");
                var mainOverlayView = new MainOverlayView
                {
                    DataContext = mainOverlayViewModel
                };
                _logger.LogDebug("[NAVIGATION_DEBUG] MainOverlayView作成完了");

                // 🔥 [ISSUE#167] MainOverlayViewを先にMainWindowに設定してから表示
                // これによりアプリのShutdownを防ぐ
                _logger.LogDebug("[NAVIGATION_DEBUG] MainWindow設定開始");
                desktop.MainWindow = mainOverlayView;
                _logger.LogDebug("[NAVIGATION_DEBUG] MainWindow設定完了、Show()呼び出し");
                mainOverlayView.Show();
                _logger.LogDebug("[NAVIGATION_DEBUG] Show()完了");

                // 🔥 [ISSUE#167] 古いウィンドウ（LoginView/SignupView）を閉じる
                if (oldWindow != null && oldWindow != mainOverlayView)
                {
                    _logger.LogDebug("[NAVIGATION_DEBUG] 古いウィンドウClose開始: {WindowType}", oldWindow.GetType().Name);
                    oldWindow.Close();
                    _logger.LogDebug("[NAVIGATION_DEBUG] 古いウィンドウClose完了");
                }

                _logger.LogDebug("[NAVIGATION_DEBUG] ShowMainWindowInternalAsync完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[NAVIGATION_ERROR] ShowMainWindowInternalAsync例外: {Message}", ex.Message);
            _logNavigationError(_logger, "MainOverlayView", ex);
        }

        return Task.CompletedTask;
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

            Console.WriteLine($"🔍 [NAVIGATION_DEBUG] SettingsWindowViewModel取得開始");

            var settingsViewModel = _serviceProvider.GetRequiredService<SettingsWindowViewModel>();

            Console.WriteLine($"🔍 [NAVIGATION_DEBUG] SettingsWindowViewModel取得完了: {settingsViewModel?.GetType().Name ?? "null"}");

            var settingsWindow = new SettingsWindow
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
    /// ログイン画面に切り替えます（ダイアログとして表示）
    /// </summary>
    public async Task SwitchToLoginAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "SwitchToLogin", null);

            await _dispatcher.InvokeAsync(async () =>
            {
                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is MainOverlayView mainOverlay)
                {
                    // 🔥 [ISSUE#167] MainOverlayView上にダイアログとして表示
                    var loginViewModel = _serviceProvider.GetRequiredService<LoginViewModel>();
                    var loginWindow = new LoginView(loginViewModel);

                    await loginWindow.ShowDialog<bool?>(mainOverlay).ConfigureAwait(false);
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logNavigationError(_logger, "SwitchToLogin", ex);
        }
    }

    /// <summary>
    /// サインアップ画面に切り替えます（ダイアログとして表示）
    /// </summary>
    public async Task SwitchToSignupAsync()
    {
        ThrowIfDisposed();
        try
        {
            _logNavigating(_logger, "SwitchToSignup", null);
            _logger.LogDebug("[AUTH_DEBUG] SwitchToSignupAsync開始");

            await _dispatcher.InvokeAsync(async () =>
            {
                _logger.LogDebug("[AUTH_DEBUG] UIThread.InvokeAsync内部開始");

                if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is MainOverlayView mainOverlay)
                {
                    _logger.LogDebug("[AUTH_DEBUG] MainOverlayView検出、SignupView作成開始");

                    // 🔥 [ISSUE#167] MainOverlayView上にダイアログとして表示
                    var signupViewModel = _serviceProvider.GetRequiredService<SignupViewModel>();
                    _logger.LogDebug("[AUTH_DEBUG] SignupViewModel取得完了");

                    var signupWindow = new SignupView(signupViewModel);
                    _logger.LogDebug("[AUTH_DEBUG] SignupView作成完了、ShowDialog呼び出し");

                    await signupWindow.ShowDialog<bool?>(mainOverlay).ConfigureAwait(false);
                    _logger.LogDebug("[AUTH_DEBUG] ShowDialog完了");
                }
                else
                {
                    var currentDesktop = Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
                    _logger.LogWarning("[AUTH_DEBUG] MainOverlayViewが見つからない: MainWindow={WindowType}",
                        currentDesktop?.MainWindow?.GetType().Name ?? "null");
                }
            }).ConfigureAwait(false);

            _logger.LogDebug("[AUTH_DEBUG] SwitchToSignupAsync完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AUTH_DEBUG] SwitchToSignupAsync例外: {Message}", ex.Message);
            _logNavigationError(_logger, "SwitchToSignup", ex);
        }
    }

    /// <summary>
    /// ダイアログとしてウィンドウを表示します
    /// </summary>
    /// <param name="window">表示するウィンドウ</param>
    /// <returns>ダイアログの結果</returns>
    private static async Task<bool?> ShowDialogAsync(Window window)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 優先順位:
            // 1. アクティブなウィンドウ（IsActive=true）
            // 2. フォーカスされているウィンドウ（IsFocused=true）
            // 3. 最後に追加されたウィンドウ（設定画面などのモーダルダイアログ）
            // 4. MainWindow
            var ownerWindow = desktop.Windows.FirstOrDefault(w => w.IsActive)
                           ?? desktop.Windows.FirstOrDefault(w => w.IsFocused)
                           ?? desktop.Windows.LastOrDefault(w => w != window && w.IsVisible)
                           ?? desktop.MainWindow;

            if (ownerWindow != null)
            {
                Console.WriteLine($"📌 [ShowDialogAsync] owner={ownerWindow.GetType().Name}, dialog={window.GetType().Name}");
                return await window.ShowDialog<bool?>(ownerWindow).ConfigureAwait(false);
            }
        }

        Console.WriteLine($"⚠️ [ShowDialogAsync] ownerWindow not found, showing as regular window");
        window.Show();
        return null;
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
