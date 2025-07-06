using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Extensions;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Auth;
using Baketa.UI.Framework;
using Baketa.UI.Services;
using Baketa.UI.Security;

namespace Baketa.UI.ViewModels.Auth;

/// <summary>
/// ログイン画面のViewModel
/// </summary>
public sealed class LoginViewModel : ViewModelBase, ReactiveUI.Validation.Abstractions.IValidatableViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
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

    // Reactive properties with Fody
    [Reactive] public string Email { get; set; } = string.Empty;
    [Reactive] public string Password { get; set; } = string.Empty;
    [Reactive] public bool RememberMe { get; set; } = true;
    // ErrorMessageとIsLoadingはViewModelBaseに既に定義済み

    // IValidatableViewModel implementation
    public IValidationContext ValidationContext { get; } = new ValidationContext();

    // Commands (initialized in SetupCommands method)
    public ReactiveCommand<Unit, Unit> LoginWithEmailCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithGoogleCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithDiscordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> LoginWithSteamCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ForgotPasswordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> NavigateToSignupCommand { get; private set; } = null!;

    /// <summary>
    /// LoginViewModelを初期化します
    /// </summary>
    /// <param name="authService">認証サービス</param>
    /// <param name="navigationService">ナビゲーションサービス</param>
    /// <param name="attemptTracker">ログイン試行追跡器</param>
    /// <param name="auditLogger">セキュリティ監査ログ</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public LoginViewModel(
        IAuthService authService,
        INavigationService navigationService,
        LoginAttemptTracker attemptTracker,
        SecurityAuditLogger auditLogger,
        IEventAggregator eventAggregator,
        ILogger<LoginViewModel>? logger = null) : base(eventAggregator, logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
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

        LoginWithSteamCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthLoginAsync(AuthProvider.Steam),
            canExecuteOAuth);
        Disposables.Add(LoginWithSteamCommand);

        // パスワードリセットコマンド
        var canExecuteForgotPassword = this.WhenAnyValue(
            x => x.Email,
            x => x.IsLoading,
            (email, isLoading) => !string.IsNullOrWhiteSpace(email) && InputValidator.IsValidEmail(email) && !isLoading);

        ForgotPasswordCommand = ReactiveCommand.CreateFromTask(
            ExecuteForgotPasswordAsync,
            canExecuteForgotPassword);
        Disposables.Add(ForgotPasswordCommand);

        // サインアップ画面への遷移コマンド
        NavigateToSignupCommand = ReactiveCommand.CreateFromTask(async () => 
        {
            await _navigationService.ShowSignupAsync().ConfigureAwait(false);
        });
        Disposables.Add(NavigateToSignupCommand);

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
        var oauthCommands = new[] { LoginWithGoogleCommand, LoginWithDiscordCommand, LoginWithSteamCommand };
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
        if (e.IsLoggedIn)
        {
            // TODO: Navigate to main screen or close login window
            ErrorMessage = null;
        }
    }

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

            var result = await _authService.SignInWithEmailPasswordAsync(sanitizedEmail, Password).ConfigureAwait(false);

            if (result is AuthSuccess success)
            {
                // 成功時の処理
                _attemptTracker.RecordSuccessfulLogin(sanitizedEmail);
                _auditLogger.LogLoginAttempt(sanitizedEmail, true);

                if (_logger != null)
                    _logLoginSuccess(_logger, sanitizedEmail, null);

                // セッション情報を必要に応じて保存
                if (RememberMe)
                {
                    // TODO: Implement remember me functionality with SecureSessionManager
                }
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

            var result = await _authService.SignInWithOAuthAsync(provider).ConfigureAwait(false);

            if (result is AuthFailure failure)
            {
                ErrorMessage = GetAuthFailureMessage(failure.ErrorCode, failure.Message);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = GetUserFriendlyErrorMessage(ex);
        }
        finally
        {
            IsLoading = false;
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

            // TODO: Implement ResetPasswordAsync method in IAuthService
            // await _authService.ResetPasswordAsync(Email).ConfigureAwait(false);
            await Task.Delay(1000).ConfigureAwait(false); // Placeholder
            
            // 成功メッセージを表示
            ErrorMessage = null;
            // TODO: Show success message in UI
        }
        catch (Exception ex)
        {
            ErrorMessage = $"パスワードリセットに失敗しました: {ex.Message}";
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
        return errorCode switch
        {
            "invalid_credentials" => "メールアドレスまたはパスワードが正しくありません",
            "email_not_confirmed" => "メールアドレスが確認されていません。確認メールをご確認ください",
            "too_many_requests" => "ログイン試行回数が上限に達しました。しばらく時間をおいてから再試行してください",
            "user_not_found" => "アカウントが見つかりません",
            "weak_password" => "パスワードが弱すぎます",
            _ => $"ログインに失敗しました: {message}"
        };
    }

    /// <summary>
    /// ユーザーフレンドリーなエラーメッセージを取得します
    /// </summary>
    /// <param name="ex">例外</param>
    /// <returns>エラーメッセージ</returns>
    private static string GetUserFriendlyErrorMessage(Exception ex)
    {
        return ex switch
        {
            TimeoutException => "接続がタイムアウトしました。インターネット接続をご確認ください",
            System.Net.Http.HttpRequestException => "サーバーに接続できませんでした。インターネット接続をご確認ください",
            TaskCanceledException => "処理がキャンセルされました",
            UnauthorizedAccessException => "認証に失敗しました",
            _ => $"予期しないエラーが発生しました: {ex.Message}"
        };
    }
}