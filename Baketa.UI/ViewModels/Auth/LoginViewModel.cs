using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Security;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
// ReactiveUI.Fody.Helpersは不要（FodyのReactiveUIウィービングが無効化されているため）
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Extensions;

namespace Baketa.UI.ViewModels.Auth;

/// <summary>
/// ログイン画面のViewModel
/// </summary>
public sealed class LoginViewModel : ViewModelBase, ReactiveUI.Validation.Abstractions.IValidatableViewModel
{
    private readonly IAuthService _authService;
    private readonly IOAuthCallbackHandler _oauthHandler;
    private readonly INavigationService _navigationService;
    private readonly ITokenStorage _tokenStorage;
    private readonly SecureSessionManager _sessionManager;
    private readonly LoginAttemptTracker _attemptTracker;
    private readonly SecurityAuditLogger _auditLogger;
    private readonly ILogger<LoginViewModel>? _logger;

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, Exception?> _logLoginAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "LoginAttempt"),
            "ログイン試行: {Email}");

    private static readonly Action<ILogger, string, Exception?> _logLoginSuccess =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "LoginSuccess"),
            "ログイン成功: {Email}");

    private static readonly Action<ILogger, string, Exception> _logLoginError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, "LoginError"),
            "ログイン失敗: {Email}");

    private static readonly Action<ILogger, string, Exception?> _logOAuthAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, "OAuthAttempt"),
            "OAuth認証試行: {Provider}");

    // 🔥 [FIX] FodyのReactiveUIウィービングが無効化されているため、手動でPropertyChangedを実装
    // ReactiveUIの標準的なRaiseAndSetIfChangedを直接使用
    private string _email = string.Empty;
    public string Email
    {
        get => _email;
        set => this.RaiseAndSetIfChanged(ref _email, value);
    }

    private string _password = string.Empty;
    public string Password
    {
        get => _password;
        set => this.RaiseAndSetIfChanged(ref _password, value);
    }

    private bool _rememberMe = true;
    public bool RememberMe
    {
        get => _rememberMe;
        set => this.RaiseAndSetIfChanged(ref _rememberMe, value);
    }

    // ErrorMessageとIsLoadingはViewModelBaseに既に定義済み

    // IValidatableViewModel implementation
    public IValidationContext ValidationContext { get; } = new ValidationContext();

    // Commands (initialized in SetupCommands method)
    public ReactiveCommand<Unit, Unit> LoginWithEmailCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithGoogleCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithDiscordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithTwitchCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ForgotPasswordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> NavigateToSignupCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ExitCommand { get; private set; } = null!;

    /// <summary>
    /// LoginViewModelを初期化します
    /// </summary>
    /// <param name="authService">認証サービス</param>
    /// <param name="oauthHandler">OAuthコールバックハンドラー</param>
    /// <param name="navigationService">ナビゲーションサービス</param>
    /// <param name="tokenStorage">トークン永続化ストレージ</param>
    /// <param name="sessionManager">セッション管理</param>
    /// <param name="attemptTracker">ログイン試行追跡器</param>
    /// <param name="auditLogger">セキュリティ監査ログ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public LoginViewModel(
        IAuthService authService,
        IOAuthCallbackHandler oauthHandler,
        INavigationService navigationService,
        ITokenStorage tokenStorage,
        SecureSessionManager sessionManager,
        LoginAttemptTracker attemptTracker,
        SecurityAuditLogger auditLogger,
        IEventAggregator eventAggregator,
        ILogger<LoginViewModel>? logger = null) : base(eventAggregator, logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _oauthHandler = oauthHandler ?? throw new ArgumentNullException(nameof(oauthHandler));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _tokenStorage = tokenStorage ?? throw new ArgumentNullException(nameof(tokenStorage));
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        _attemptTracker = attemptTracker ?? throw new ArgumentNullException(nameof(attemptTracker));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger;

        // バリデーションルールの設定
        SetupValidationRules();

        // コマンドの初期化
        SetupCommands();

        // 認証状態変更イベントの購読
        SubscribeToAuthEvents();
    }

    /// <summary>
    /// バリデーションルールを設定します
    /// </summary>
    private void SetupValidationRules()
    {
        // Emailバリデーション（強化版）
        var emailRule = this.ValidationRule(
            vm => vm.Email,
            email => InputValidator.IsValidEmail(email),
            "有効なメールアドレスを入力してください");
        Disposables.Add(emailRule);

        // Passwordバリデーション（基本チェック）
        var passwordRule = this.ValidationRule(
            vm => vm.Password,
            password => !string.IsNullOrWhiteSpace(password) && password.Length >= 6,
            "パスワードは6文字以上で入力してください");
        Disposables.Add(passwordRule);

        // ブロック状態チェック
        var blockRule = this.ValidationRule(
            vm => vm.Email,
            email => !_attemptTracker.IsBlocked(email ?? string.Empty),
            "アカウントがロックされています");
        Disposables.Add(blockRule);
    }

    /// <summary>
    /// コマンドを設定します
    /// </summary>
    private void SetupCommands()
    {
        // メール/パスワードログインコマンド
        var canExecuteEmailLogin = this.WhenAnyValue(
            x => x.Email,
            x => x.Password,
            x => x.IsLoading,
            (email, password, isLoading) =>
                !string.IsNullOrWhiteSpace(email) &&
                !string.IsNullOrWhiteSpace(password) &&
                !isLoading);

        LoginWithEmailCommand = ReactiveCommand.CreateFromTask(
            ExecuteLoginWithEmailAsync,
            canExecuteEmailLogin);
        Disposables.Add(LoginWithEmailCommand);

        // OAuthログインコマンド
        var canExecuteOAuth = this.WhenAnyValue(x => x.IsLoading, isLoading => !isLoading);

        LoginWithGoogleCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthLoginAsync(AuthProvider.Google),
            canExecuteOAuth);
        Disposables.Add(LoginWithGoogleCommand);

        LoginWithDiscordCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthLoginAsync(AuthProvider.Discord),
            canExecuteOAuth);
        Disposables.Add(LoginWithDiscordCommand);

        LoginWithTwitchCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthLoginAsync(AuthProvider.Twitch),
            canExecuteOAuth);
        Disposables.Add(LoginWithTwitchCommand);

        // パスワードリセットコマンド
        var canExecuteForgotPassword = this.WhenAnyValue(
            x => x.Email,
            x => x.IsLoading,
            (email, isLoading) => !string.IsNullOrWhiteSpace(email) && InputValidator.IsValidEmail(email) && !isLoading);

        ForgotPasswordCommand = ReactiveCommand.CreateFromTask(
            ExecuteForgotPasswordAsync,
            canExecuteForgotPassword);
        Disposables.Add(ForgotPasswordCommand);

        // サインアップ画面への遷移コマンド（ダイアログを閉じてから切り替え）
        NavigateToSignupCommand = ReactiveCommand.Create(() =>
        {
            _logger?.LogInformation("[AUTH_DEBUG] NavigateToSignupCommand実行開始");

            // 🔥 [ISSUE#167] ダイアログを閉じて、その後SignupViewを表示
            _logger?.LogInformation("[AUTH_DEBUG] CloseDialogRequestedイベント発火 (画面切り替え)");
            CloseDialogRequested?.Invoke(false); // false = 画面切り替え（認証成功ではない）

            // UIスレッドで非同期にSignupViewを表示（ダイアログが閉じた後に実行される）
            _ = Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false); // ダイアログが閉じるのを待つ
                _logger?.LogInformation("[AUTH_DEBUG] SwitchToSignupAsync呼び出し");
                await _navigationService.SwitchToSignupAsync().ConfigureAwait(false);
            });
        });
        Disposables.Add(NavigateToSignupCommand);

        // アプリケーション終了コマンド
        ExitCommand = ReactiveCommand.Create(() =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
        Disposables.Add(ExitCommand);

        // エラーハンドリング
        SetupCommandErrorHandling();
    }

    /// <summary>
    /// コマンドのエラーハンドリングを設定します
    /// </summary>
    private void SetupCommandErrorHandling()
    {
        // メールログインエラーハンドリング
        LoginWithEmailCommand.ThrownExceptions.Subscribe(ex =>
        {
            if (_logger != null)
                _logLoginError(_logger, Email, ex);
            ErrorMessage = GetUserFriendlyErrorMessage(ex);
        });

        // OAuthエラーハンドリング
        var oauthCommands = new[] { LoginWithGoogleCommand, LoginWithDiscordCommand, LoginWithTwitchCommand };
        foreach (var command in oauthCommands)
        {
            command.ThrownExceptions.Subscribe(ex =>
            {
                ErrorMessage = GetUserFriendlyErrorMessage(ex);
            });
        }

        // パスワードリセットエラーハンドリング
        ForgotPasswordCommand.ThrownExceptions.Subscribe(ex =>
        {
            ErrorMessage = $"パスワードリセットに失敗しました: {ex.Message}";
        });
    }

    /// <summary>
    /// 認証イベントを購読します
    /// </summary>
    private void SubscribeToAuthEvents()
    {
        _authService.AuthStatusChanged += OnAuthStatusChanged;
    }

    /// <summary>
    /// 認証状態変更イベントハンドラ
    /// </summary>
    /// <param name="sender">送信者</param>
    /// <param name="e">イベント引数</param>
    private void OnAuthStatusChanged(object? sender, AuthStatusChangedEventArgs e)
    {
        _logger?.LogDebug("[AUTH_DEBUG] LoginViewModel.OnAuthStatusChanged呼び出し開始 - IsLoggedIn={IsLoggedIn}, Thread={ThreadId}",
            e.IsLoggedIn, Environment.CurrentManagedThreadId);

        if (!e.IsLoggedIn)
        {
            _logger?.LogDebug("[AUTH_DEBUG] IsLoggedIn=falseのためスキップ");
            return;
        }

        // 🔥 [FIX] UIスレッド違反を回避するため、全ての[Reactive]プロパティ操作をUIスレッドで実行
        // AuthStatusChangedイベントは非UIスレッドから発火される可能性がある
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
        {
            try
            {
                _logger?.LogDebug("[AUTH_DEBUG] UIThread内処理開始 - Thread={ThreadId}", Environment.CurrentManagedThreadId);
                _logger?.LogInformation("認証成功: ダイアログを閉じます");

                // 🔥 [FIX] Phase 2: ダイアログを閉じるだけ
                // 状態変更（SetAuthenticationMode）はViewのOnClosedイベントで行う
                // これにより、ウィンドウが完全に破棄された後に確実に状態変更される
                _logger?.LogDebug("[AUTH_DEBUG] CloseDialogRequested発火前 (認証成功)");
                CloseDialogRequested?.Invoke(true); // true = 認証成功
                _logger?.LogDebug("[AUTH_DEBUG] CloseDialogRequested発火後");

                // 注意: ErrorMessageとSetAuthenticationModeはViewのOnClosedで処理される
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[AUTH_DEBUG] UIThread内処理で例外: {Message}", ex.Message);
            }
        });

        _logger?.LogDebug("[AUTH_DEBUG] LoginViewModel.OnAuthStatusChanged InvokeAsync発行完了");
    }

    /// <summary>
    /// ダイアログを閉じる要求イベント
    /// 認証成功時と画面切り替え時の両方でこのイベントが発火される
    /// パラメータ: 認証成功の場合はtrue、画面切り替えの場合はfalse
    /// </summary>
    public event Action<bool>? CloseDialogRequested;

    /// <summary>
    /// デバッグログを出力します（Viewからの呼び出し用）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public void LogDebug(string message) => _logger?.LogDebug("{Message}", message);

    /// <summary>
    /// メール/パスワードログインを実行します
    /// </summary>
    private async Task ExecuteLoginWithEmailAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // セキュリティチェック
            var sanitizedEmail = InputValidator.SanitizeInput(Email);

            // ブロック状態の確認
            if (_attemptTracker.IsBlocked(sanitizedEmail))
            {
                var remainingTime = _attemptTracker.GetRemainingLockoutTime(sanitizedEmail);
                ErrorMessage = remainingTime.HasValue
                    ? $"アカウントがロックされています。残り時間: {remainingTime.Value.TotalMinutes:F0}分"
                    : "アカウントがロックされています";

                _auditLogger.LogSecurityEvent(
                    SecurityAuditLogger.SecurityEventType.LoginBlocked,
                    $"ブロック中のアカウントでのログイン試行: {sanitizedEmail}",
                    sanitizedEmail);
                return;
            }

            // ログイン試行の記録
            _auditLogger.LogLoginAttempt(sanitizedEmail, false, "試行開始");

            if (_logger != null)
                _logLoginAttempt(_logger, sanitizedEmail, null);

            // 🔥 [FIX] ConfigureAwait(true)に変更してUIスレッドで継続処理を実行
            // ConfigureAwait(false)だとバックグラウンドスレッドになり、プロパティ変更でAccessViolationが発生する
            var result = await _authService.SignInWithEmailPasswordAsync(sanitizedEmail, Password);

            if (result is AuthSuccess success)
            {
                // 成功時の処理
                _attemptTracker.RecordSuccessfulLogin(sanitizedEmail);
                _auditLogger.LogLoginAttempt(sanitizedEmail, true);

                if (_logger != null)
                    _logLoginSuccess(_logger, sanitizedEmail, null);

                // セッション管理の開始
                _sessionManager.StartSession(success.Session, RememberMe);

                // Remember Me: トークンを永続化
                if (RememberMe)
                {
                    await _tokenStorage.StoreTokensAsync(
                        success.Session.AccessToken,
                        success.Session.RefreshToken);

                    _logger?.LogInformation("Remember Me: トークンを永続化しました");
                }

                // 認証成功イベントによりOnAuthStatusChangedがナビゲーションを処理
            }
            else if (result is AuthFailure failure)
            {
                // 失敗時の処理
                _attemptTracker.RecordFailedAttempt(sanitizedEmail);
                _auditLogger.LogLoginAttempt(sanitizedEmail, false, $"{failure.ErrorCode}: {failure.Message}");

                // ロックアウト状態の確認
                if (_attemptTracker.IsBlocked(sanitizedEmail))
                {
                    var stats = _attemptTracker.GetStats();
                    var lockoutTime = _attemptTracker.GetRemainingLockoutTime(sanitizedEmail);

                    _auditLogger.LogAccountLockout(sanitizedEmail,
                        5, // MaxAttempts based on LoginAttemptTracker
                        lockoutTime ?? TimeSpan.FromMinutes(15));
                }

                ErrorMessage = GetAuthFailureMessage(failure.ErrorCode, failure.Message);
            }
        }
        catch (Exception ex)
        {
            // 例外発生時の処理
            var sanitizedEmail = InputValidator.SanitizeInput(Email);
            _attemptTracker.RecordFailedAttempt(sanitizedEmail);
            _auditLogger.LogLoginAttempt(sanitizedEmail, false, ex.Message);

            if (_logger != null)
                _logLoginError(_logger, sanitizedEmail, ex);
            ErrorMessage = GetUserFriendlyErrorMessage(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// OAuthログインを実行します
    /// </summary>
    /// <param name="provider">認証プロバイダー</param>
    private async Task ExecuteOAuthLoginAsync(AuthProvider provider)
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (_logger != null)
                _logOAuthAttempt(_logger, provider.ToString(), null);

            // 🔥 [FIX] ConfigureAwait(true)でUIスレッドを維持
            // OAuthCallbackHandlerを使用してブラウザベースのOAuth認証を開始
            var result = await _oauthHandler.StartOAuthFlowAsync(provider);

            // 🔥 [FIX] ViewModelがDisposeされている場合は何もしない
            // OAuth成功時、AuthStatusChangedイベントがダイアログを閉じてViewModelをDisposeする
            // その後にこのコードが実行されるとAccessViolationが発生する
            if (IsDisposed)
            {
                _logger?.LogDebug("OAuth完了後、ViewModelが既にDisposeされているためスキップ");
                return;
            }

            if (result is AuthSuccess)
            {
                // 認証成功時はOnAuthStatusChangedイベントで処理されるため、ここでは何もしない
                _logger?.LogInformation("OAuth認証成功: {Provider}", provider);
            }
            else if (result is AuthFailure failure)
            {
                if (!IsDisposed)
                {
                    ErrorMessage = GetAuthFailureMessage(failure.ErrorCode, failure.Message);
                }
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                ErrorMessage = GetUserFriendlyErrorMessage(ex);
            }
        }
        finally
        {
            // 🔥 [FIX] Disposeされていない場合のみIsLoadingを変更
            if (!IsDisposed)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>
    /// パスワードリセットを実行します
    /// </summary>
    private async Task ExecuteForgotPasswordAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            // セキュリティチェック
            var sanitizedEmail = InputValidator.SanitizeInput(Email);

            // パスワードリセットメール送信
            var success = await _authService.SendPasswordResetEmailAsync(sanitizedEmail);

            if (success)
            {
                // 成功メッセージを表示（ErrorMessageフィールドを情報表示にも使用）
                ErrorMessage = Resources.Strings.Auth_Success_PasswordResetEmailSent;

                // セキュリティ監査ログ
                _auditLogger.LogSecurityEvent(
                    SecurityAuditLogger.SecurityEventType.PasswordChange,
                    $"パスワードリセットメール送信成功: {sanitizedEmail}",
                    sanitizedEmail);
            }
            else
            {
                ErrorMessage = Resources.Strings.Auth_Error_PasswordResetFailed;

                // 失敗ログ
                _auditLogger.LogSecurityEvent(
                    SecurityAuditLogger.SecurityEventType.PasswordChange,
                    $"パスワードリセットメール送信失敗: {sanitizedEmail}",
                    sanitizedEmail);
            }
        }
        catch (Exception ex)
        {
            var sanitizedEmail = InputValidator.SanitizeInput(Email);
            _logger?.LogError(ex, "パスワードリセット実行エラー: {Email}", sanitizedEmail);
            ErrorMessage = Resources.Strings.Auth_Error_PasswordResetError;

            // エラーログ
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.PasswordChange,
                $"パスワードリセット例外: {sanitizedEmail} - {ex.Message}",
                sanitizedEmail);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// リソース解放処理
    /// </summary>
    /// <param name="disposing">マネージドリソースを解放するかどうか</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authService.AuthStatusChanged -= OnAuthStatusChanged;
        }
        base.Dispose(disposing);
    }


    /// <summary>
    /// 認証失敗メッセージを取得します
    /// </summary>
    /// <param name="errorCode">エラーコード</param>
    /// <param name="message">エラーメッセージ</param>
    /// <returns>ユーザーフレンドリーなエラーメッセージ</returns>
    private static string GetAuthFailureMessage(string errorCode, string message)
    {
        // [Issue #179] ローカライズされたエラーメッセージを使用
        return errorCode switch
        {
            "invalid_credentials" or AuthErrorCodes.InvalidCredentials => Resources.Strings.Auth_Error_InvalidEmail,
            "email_not_confirmed" or AuthErrorCodes.EmailNotConfirmed => Resources.Strings.Auth_Error_EmailNotConfirmed,
            "too_many_requests" or AuthErrorCodes.RateLimitExceeded => Resources.Strings.Auth_Error_RateLimitExceeded,
            "user_not_found" => Resources.Strings.Auth_Error_InvalidEmail,
            "weak_password" or AuthErrorCodes.WeakPassword => Resources.Strings.Auth_Error_WeakPassword,
            AuthErrorCodes.InvalidToken => Resources.Strings.Auth_Error_InvalidToken,
            AuthErrorCodes.TokenExpired => Resources.Strings.Auth_Error_TokenExpired,
            _ => Resources.Strings.Auth_Error_LoginFailed
        };
    }

    /// <summary>
    /// ユーザーフレンドリーなエラーメッセージを取得します
    /// </summary>
    /// <param name="ex">例外</param>
    /// <returns>エラーメッセージ</returns>
    private static string GetUserFriendlyErrorMessage(Exception ex)
    {
        // [Issue #179] ローカライズされたエラーメッセージを使用
        return ex switch
        {
            TimeoutException => Resources.Strings.Auth_Error_Timeout,
            System.Net.Http.HttpRequestException => Resources.Strings.Auth_Error_NetworkError,
            TaskCanceledException => Resources.Strings.Auth_Error_Timeout,
            UnauthorizedAccessException => Resources.Strings.Auth_Error_InvalidToken,
            _ => Resources.Strings.Auth_Error_LoginFailed
        };
    }
}
