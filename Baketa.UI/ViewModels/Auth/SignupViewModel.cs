using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.Validation.Abstractions;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Extensions;

namespace Baketa.UI.ViewModels.Auth;

/// <summary>
/// サインアップ画面のViewModel
/// </summary>
public sealed class SignupViewModel : ViewModelBase, ReactiveUI.Validation.Abstractions.IValidatableViewModel
{
    private readonly IAuthService _authService;
    private readonly INavigationService _navigationService;
    private readonly ILogger<SignupViewModel>? _logger;

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, Exception?> _logSignupAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(1, "SignupAttempt"),
            "サインアップ試行: {Email}");

    private static readonly Action<ILogger, string, Exception?> _logSignupSuccess =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "SignupSuccess"),
            "サインアップ成功: {Email}");

    private static readonly Action<ILogger, string, Exception> _logSignupError =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(3, "SignupError"),
            "サインアップ失敗: {Email}");

    private static readonly Action<ILogger, string, Exception?> _logOAuthAttempt =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(4, "OAuthAttempt"),
            "OAuth認証試行: {Provider}");

    // Reactive properties with Fody
    [Reactive] public string Email { get; set; } = string.Empty;
    [Reactive] public string Password { get; set; } = string.Empty;
    [Reactive] public string ConfirmPassword { get; set; } = string.Empty;
    [Reactive] public string DisplayName { get; set; } = string.Empty;
    [Reactive] public bool AcceptTerms { get; set; }
    [Reactive] public bool AcceptPrivacyPolicy { get; set; }
    // ErrorMessageとIsLoadingはViewModelBaseに既に定義済み

    // IValidatableViewModel implementation
    public IValidationContext ValidationContext { get; } = new ValidationContext();

    // Commands (initialized in SetupCommands method)
    public ReactiveCommand<Unit, Unit> SignupWithEmailCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithGoogleCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithDiscordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithSteamCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> NavigateToLoginCommand { get; private set; } = null!;

    /// <summary>
    /// SignupViewModelを初期化します
    /// </summary>
    /// <param name="authService">認証サービス</param>
    /// <param name="navigationService">ナビゲーションサービス</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public SignupViewModel(
        IAuthService authService,
        INavigationService navigationService,
        IEventAggregator eventAggregator,
        ILogger<SignupViewModel>? logger = null) : base(eventAggregator, logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
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
        // Emailバリデーション
        var emailRule = this.ValidationRule(
            vm => vm.Email,
            email => !string.IsNullOrWhiteSpace(email) && IsValidEmail(email),
            "有効なメールアドレスを入力してください");
        Disposables.Add(emailRule);

        // Passwordバリデーション
        var passwordRule = this.ValidationRule(
            vm => vm.Password,
            password => !string.IsNullOrWhiteSpace(password) && IsValidPassword(password),
            "パスワードは8文字以上で、大文字・小文字・数字を含む必要があります");
        Disposables.Add(passwordRule);

        // ConfirmPasswordバリデーション  
        var confirmPasswordRule = this.ValidationRule(
            vm => vm.ConfirmPassword,
            confirmPassword => confirmPassword == Password,
            "パスワードが一致しません");
        Disposables.Add(confirmPasswordRule);

        // DisplayNameバリデーション
        var displayNameRule = this.ValidationRule(
            vm => vm.DisplayName,
            name => !string.IsNullOrWhiteSpace(name) && name.Length >= 2 && name.Length <= 50,
            "表示名は2文字以上50文字以下で入力してください");
        Disposables.Add(displayNameRule);

        // 利用規約同意バリデーション
        var termsRule = this.ValidationRule(
            vm => vm.AcceptTerms,
            accepted => accepted,
            "利用規約に同意する必要があります");
        Disposables.Add(termsRule);

        // プライバシーポリシー同意バリデーション
        var privacyRule = this.ValidationRule(
            vm => vm.AcceptPrivacyPolicy,
            accepted => accepted,
            "プライバシーポリシーに同意する必要があります");
        Disposables.Add(privacyRule);
    }

    /// <summary>
    /// コマンドを設定します
    /// </summary>
    private void SetupCommands()
    {
        // メール/パスワードサインアップコマンド
        var canExecuteEmailSignup = this.WhenAnyValue(
            x => x.Email,
            x => x.Password,
            x => x.ConfirmPassword,
            x => x.DisplayName,
            x => x.AcceptTerms,
            x => x.AcceptPrivacyPolicy,
            x => x.IsLoading,
            (email, password, confirmPassword, displayName, acceptTerms, acceptPrivacy, isLoading) =>
                !string.IsNullOrWhiteSpace(email) &&
                !string.IsNullOrWhiteSpace(password) &&
                !string.IsNullOrWhiteSpace(confirmPassword) &&
                !string.IsNullOrWhiteSpace(displayName) &&
                password == confirmPassword &&
                acceptTerms &&
                acceptPrivacy &&
                !isLoading);

        SignupWithEmailCommand = ReactiveCommand.CreateFromTask(
            ExecuteSignupWithEmailAsync,
            canExecuteEmailSignup);
        Disposables.Add(SignupWithEmailCommand);

        // OAuthサインアップコマンド
        var canExecuteOAuth = this.WhenAnyValue(
            x => x.AcceptTerms,
            x => x.AcceptPrivacyPolicy,
            x => x.IsLoading,
            (acceptTerms, acceptPrivacy, isLoading) => acceptTerms && acceptPrivacy && !isLoading);

        SignupWithGoogleCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Google),
            canExecuteOAuth);
        Disposables.Add(SignupWithGoogleCommand);

        SignupWithDiscordCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Discord),
            canExecuteOAuth);
        Disposables.Add(SignupWithDiscordCommand);

        SignupWithSteamCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Steam),
            canExecuteOAuth);
        Disposables.Add(SignupWithSteamCommand);

        // ログイン画面への遷移コマンド
        NavigateToLoginCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            await _navigationService.ShowLoginAsync().ConfigureAwait(false);
        });
        Disposables.Add(NavigateToLoginCommand);

        // エラーハンドリング
        SetupCommandErrorHandling();
    }

    /// <summary>
    /// コマンドのエラーハンドリングを設定します
    /// </summary>
    private void SetupCommandErrorHandling()
    {
        // メールサインアップエラーハンドリング
        SignupWithEmailCommand.ThrownExceptions.Subscribe(ex =>
        {
            if (_logger != null)
                _logSignupError(_logger, Email, ex);
            ErrorMessage = GetUserFriendlyErrorMessage(ex);
        });

        // OAuthエラーハンドリング
        var oauthCommands = new[] { SignupWithGoogleCommand, SignupWithDiscordCommand, SignupWithSteamCommand };
        foreach (var command in oauthCommands)
        {
            command.ThrownExceptions.Subscribe(ex =>
            {
                ErrorMessage = GetUserFriendlyErrorMessage(ex);
            });
        }
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
            // TODO: Navigate to main screen or show welcome message
            ErrorMessage = null;
        }
    }

    /// <summary>
    /// メール/パスワードサインアップを実行します
    /// </summary>
    private async Task ExecuteSignupWithEmailAsync()
    {
        try
        {
            IsLoading = true;
            ErrorMessage = null;

            if (_logger != null)
                _logSignupAttempt(_logger, Email, null);

            var result = await _authService.SignUpWithEmailPasswordAsync(Email, Password).ConfigureAwait(false);

            if (result is AuthSuccess success)
            {
                if (_logger != null)
                    _logSignupSuccess(_logger, Email, null);

                // TODO: Handle successful signup (show email verification message, etc.)
            }
            else if (result is AuthFailure failure)
            {
                ErrorMessage = GetAuthFailureMessage(failure.ErrorCode, failure.Message);
            }
        }
        catch (Exception ex)
        {
            if (_logger != null)
                _logSignupError(_logger, Email, ex);
            ErrorMessage = GetUserFriendlyErrorMessage(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// OAuthサインアップを実行します
    /// </summary>
    /// <param name="provider">認証プロバイダー</param>
    private async Task ExecuteOAuthSignupAsync(AuthProvider provider)
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
    /// メールアドレスの形式をチェックします
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <returns>有効な場合true</returns>
    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// パスワードの強度をチェックします
    /// </summary>
    /// <param name="password">パスワード</param>
    /// <returns>有効な場合true</returns>
    private static bool IsValidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
            return false;

        bool hasUpper = false;
        bool hasLower = false;
        bool hasDigit = false;

        foreach (char c in password)
        {
            if (char.IsUpper(c)) hasUpper = true;
            else if (char.IsLower(c)) hasLower = true;
            else if (char.IsDigit(c)) hasDigit = true;

            if (hasUpper && hasLower && hasDigit)
                return true;
        }

        return hasUpper && hasLower && hasDigit;
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
            "email_already_exists" => "このメールアドレスは既に使用されています",
            "weak_password" => "パスワードが弱すぎます。より強固なパスワードを設定してください",
            "invalid_email" => "無効なメールアドレス形式です",
            "email_not_confirmed" => "メールアドレスの確認が必要です。確認メールをご確認ください",
            "too_many_requests" => "リクエストが多すぎます。しばらく時間をおいてから再試行してください",
            "signup_disabled" => "現在、新規アカウント作成を停止しています",
            _ => $"アカウント作成に失敗しました: {message}"
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
