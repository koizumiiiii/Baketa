using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// アカウント設定画面のViewModel
/// 認証状態の表示、ログイン/ログアウト、パスワードリセット機能を提供
/// </summary>
public sealed class AccountSettingsViewModel : ViewModelBase
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<AccountSettingsViewModel>? _logger;

    private bool _isLoggedIn;
    private string? _userEmail;
    private string? _userDisplayName;
    private string? _authProvider;
    private string? _statusMessage;
    private bool _isStatusError;
    private string _resetEmail = string.Empty;
    private bool _showPasswordResetForm;
    private bool _passwordResetSent;

    /// <summary>
    /// AccountSettingsViewModelを初期化します
    /// </summary>
    public AccountSettingsViewModel(
        IAuthService authService,
        INavigationService navigationService,
        IEventAggregator eventAggregator,
        ILogger<AccountSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _logger = logger;

        // 認証状態変更イベントの購読
        _authService.AuthStatusChanged += OnAuthStatusChanged;

        // コマンドの初期化
        var canExecuteWhenNotLoading = this.WhenAnyValue(x => x.IsLoading).Select(loading => !loading);

        LoginCommand = ReactiveCommand.CreateFromTask(ExecuteLoginAsync, canExecuteWhenNotLoading);
        SignupCommand = ReactiveCommand.CreateFromTask(ExecuteSignupAsync, canExecuteWhenNotLoading);
        LogoutCommand = ReactiveCommand.CreateFromTask(ExecuteLogoutAsync,
            this.WhenAnyValue(x => x.IsLoggedIn, x => x.IsLoading, (loggedIn, loading) => loggedIn && !loading));

        ShowPasswordResetFormCommand = ReactiveCommand.Create(ExecuteShowPasswordResetForm);
        HidePasswordResetFormCommand = ReactiveCommand.Create(ExecuteHidePasswordResetForm);
        SendPasswordResetCommand = ReactiveCommand.CreateFromTask(
            ExecuteSendPasswordResetAsync,
            this.WhenAnyValue(x => x.ResetEmail, x => x.IsLoading,
                (email, loading) => !string.IsNullOrWhiteSpace(email) && !loading));

        // 初期状態の読み込み（例外を適切に処理）
        _ = LoadCurrentSessionAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger?.LogError(t.Exception, "セッション読み込みの初期化に失敗しました");
            }
        }, TaskScheduler.Default);
    }

    #region プロパティ

    /// <summary>
    /// ログイン中かどうか
    /// </summary>
    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set => this.RaiseAndSetIfChanged(ref _isLoggedIn, value);
    }

    /// <summary>
    /// ユーザーのメールアドレス
    /// </summary>
    public string? UserEmail
    {
        get => _userEmail;
        private set => this.RaiseAndSetIfChanged(ref _userEmail, value);
    }

    /// <summary>
    /// ユーザーの表示名
    /// </summary>
    public string? UserDisplayName
    {
        get => _userDisplayName;
        private set => this.RaiseAndSetIfChanged(ref _userDisplayName, value);
    }

    /// <summary>
    /// 認証プロバイダー名（OAuth使用時）
    /// </summary>
    public string? AuthProvider
    {
        get => _authProvider;
        private set
        {
            this.RaiseAndSetIfChanged(ref _authProvider, value);
            this.RaisePropertyChanged(nameof(AuthProviderDisplay));
        }
    }

    /// <summary>
    /// ローカライズされた認証プロバイダー表示文字列
    /// </summary>
    public string AuthProviderDisplay => string.IsNullOrEmpty(AuthProvider)
        ? string.Empty
        : string.Format(Strings.Settings_Account_LoggedInWith, AuthProvider);

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string? StatusMessage
    {
        get => _statusMessage;
        private set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    /// <summary>
    /// ステータスがエラーかどうか
    /// </summary>
    public bool IsStatusError
    {
        get => _isStatusError;
        private set => this.RaiseAndSetIfChanged(ref _isStatusError, value);
    }

    /// <summary>
    /// パスワードリセット用メールアドレス
    /// </summary>
    public string ResetEmail
    {
        get => _resetEmail;
        set => this.RaiseAndSetIfChanged(ref _resetEmail, value);
    }

    /// <summary>
    /// パスワードリセットフォームを表示するか
    /// </summary>
    public bool ShowPasswordResetForm
    {
        get => _showPasswordResetForm;
        private set => this.RaiseAndSetIfChanged(ref _showPasswordResetForm, value);
    }

    /// <summary>
    /// パスワードリセットメールが送信されたか
    /// </summary>
    public bool PasswordResetSent
    {
        get => _passwordResetSent;
        private set => this.RaiseAndSetIfChanged(ref _passwordResetSent, value);
    }

    #endregion

    #region コマンド

    /// <summary>
    /// ログインコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> LoginCommand { get; }

    /// <summary>
    /// サインアップコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SignupCommand { get; }

    /// <summary>
    /// ログアウトコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> LogoutCommand { get; }

    /// <summary>
    /// パスワードリセットフォーム表示コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ShowPasswordResetFormCommand { get; }

    /// <summary>
    /// パスワードリセットフォーム非表示コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> HidePasswordResetFormCommand { get; }

    /// <summary>
    /// パスワードリセットメール送信コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SendPasswordResetCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 現在のセッションを読み込みます
    /// </summary>
    private async Task LoadCurrentSessionAsync()
    {
        try
        {
            IsLoading = true;
            var session = await _authService.GetCurrentSessionAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (session != null)
                {
                    IsLoggedIn = true;
                    UserEmail = session.User.Email;
                    UserDisplayName = session.User.DisplayName ?? session.User.Email;
                    AuthProvider = session.User.Provider?.ToString();
                    _logger?.LogDebug("現在のセッションを読み込みました: {Email}", MaskEmail(UserEmail));
                }
                else
                {
                    IsLoggedIn = false;
                    UserEmail = null;
                    UserDisplayName = null;
                    AuthProvider = null;
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "セッションの読み込みに失敗しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("セッションの読み込みに失敗しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// 認証状態変更イベントハンドラ
    /// </summary>
    private void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            IsLoggedIn = e.IsLoggedIn;

            if (e.IsLoggedIn && e.User != null)
            {
                UserEmail = e.User.Email;
                UserDisplayName = e.User.DisplayName ?? e.User.Email;
                AuthProvider = e.User.Provider?.ToString();
                SetStatusMessage("ログインしました", false);
                _logger?.LogInformation("認証状態が変更されました: ログイン");
            }
            else
            {
                UserEmail = null;
                UserDisplayName = null;
                AuthProvider = null;
                SetStatusMessage("ログアウトしました", false);
                _logger?.LogInformation("認証状態が変更されました: ログアウト");
            }
        });
    }

    /// <summary>
    /// ログイン処理を実行します
    /// </summary>
    private async Task ExecuteLoginAsync()
    {
        try
        {
            IsLoading = true;
            ClearStatusMessage();

            var result = await _navigationService.ShowLoginAsync().ConfigureAwait(false);

            if (result)
            {
                _logger?.LogInformation("ログインダイアログから正常にログインしました");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ログイン処理中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("ログイン処理中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// サインアップ処理を実行します
    /// </summary>
    private async Task ExecuteSignupAsync()
    {
        try
        {
            IsLoading = true;
            ClearStatusMessage();

            var result = await _navigationService.ShowSignupAsync().ConfigureAwait(false);

            if (result)
            {
                _logger?.LogInformation("サインアップダイアログから正常にサインアップしました");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "サインアップ処理中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("サインアップ処理中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// ログアウト処理を実行します
    /// </summary>
    private async Task ExecuteLogoutAsync()
    {
        try
        {
            IsLoading = true;
            ClearStatusMessage();

            await _authService.SignOutAsync().ConfigureAwait(false);

            _logger?.LogInformation("ログアウトしました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ログアウト処理中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("ログアウト処理中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// パスワードリセットフォームを表示します
    /// </summary>
    private void ExecuteShowPasswordResetForm()
    {
        ShowPasswordResetForm = true;
        PasswordResetSent = false;
        ResetEmail = string.Empty;
        ClearStatusMessage();
    }

    /// <summary>
    /// パスワードリセットフォームを非表示にします
    /// </summary>
    private void ExecuteHidePasswordResetForm()
    {
        ShowPasswordResetForm = false;
        PasswordResetSent = false;
        ResetEmail = string.Empty;
        ClearStatusMessage();
    }

    /// <summary>
    /// パスワードリセットメールを送信します
    /// </summary>
    private async Task ExecuteSendPasswordResetAsync()
    {
        if (string.IsNullOrWhiteSpace(ResetEmail))
        {
            SetStatusMessage("メールアドレスを入力してください", true);
            return;
        }

        try
        {
            IsLoading = true;
            ClearStatusMessage();

            var result = await _authService.SendPasswordResetEmailAsync(ResetEmail).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result)
                {
                    PasswordResetSent = true;
                    SetStatusMessage("パスワードリセットメールを送信しました。メールを確認してください。", false);
                    _logger?.LogInformation("パスワードリセットメールを送信しました: {Email}", MaskEmail(ResetEmail));
                }
                else
                {
                    SetStatusMessage("パスワードリセットメールの送信に失敗しました", true);
                    _logger?.LogWarning("パスワードリセットメールの送信に失敗しました: {Email}", MaskEmail(ResetEmail));
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "パスワードリセット処理中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("パスワードリセット処理中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// ステータスメッセージを設定します
    /// </summary>
    private void SetStatusMessage(string message, bool isError)
    {
        StatusMessage = message;
        IsStatusError = isError;
    }

    /// <summary>
    /// ステータスメッセージをクリアします
    /// </summary>
    private void ClearStatusMessage()
    {
        StatusMessage = null;
        IsStatusError = false;
    }

    /// <summary>
    /// メールアドレスをマスクします（ログ用）
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[unknown]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "[invalid]";

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        var maskedLocal = localPart.Length <= 2
            ? "***"
            : localPart[..2] + "***";

        return maskedLocal + domain;
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authService.AuthStatusChanged -= OnAuthStatusChanged;
        }
        base.Dispose(disposing);
    }

    #endregion
}
