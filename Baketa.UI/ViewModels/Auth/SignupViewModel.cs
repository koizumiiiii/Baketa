using System;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using ReactiveUI;
// ReactiveUI.Fody.Helpersは不要（FodyのReactiveUIウィービングが無効化されているため）
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
    private readonly IOAuthCallbackHandler _oauthHandler;
    private readonly INavigationService _navigationService;
    private readonly IPasswordStrengthValidator _passwordValidator;
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

    // 🔥 [FIX] FodyのReactiveUIウィービングが無効化されているため、手動でPropertyChangedを実装
    // ReactiveUIの標準的なRaiseAndSetIfChangedを直接使用（SetPropertySafeはStackOverflowの原因になる可能性）
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

    private string _confirmPassword = string.Empty;
    public string ConfirmPassword
    {
        get => _confirmPassword;
        set => this.RaiseAndSetIfChanged(ref _confirmPassword, value);
    }

    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }

    private bool _acceptTerms;
    public bool AcceptTerms
    {
        get => _acceptTerms;
        set => this.RaiseAndSetIfChanged(ref _acceptTerms, value);
    }

    private bool _acceptPrivacyPolicy;
    public bool AcceptPrivacyPolicy
    {
        get => _acceptPrivacyPolicy;
        set => this.RaiseAndSetIfChanged(ref _acceptPrivacyPolicy, value);
    }

    // ErrorMessageとIsLoadingはViewModelBaseに既に定義済み

    // 成功メッセージ（緑色で表示）
    private string? _successMessage;
    public string? SuccessMessage
    {
        get => _successMessage;
        set => this.RaiseAndSetIfChanged(ref _successMessage, value);
    }

    // パスワード強度表示
    private PasswordStrength _passwordStrength = PasswordStrength.Weak;
    public PasswordStrength PasswordStrength
    {
        get => _passwordStrength;
        set => this.RaiseAndSetIfChanged(ref _passwordStrength, value);
    }

    private string _passwordStrengthMessage = string.Empty;
    public string PasswordStrengthMessage
    {
        get => _passwordStrengthMessage;
        set => this.RaiseAndSetIfChanged(ref _passwordStrengthMessage, value);
    }

    private static readonly SolidColorBrush GrayBrush = new(Color.Parse("#808080"));
    private static readonly SolidColorBrush RedBrush = new(Color.Parse("#FF4444"));
    private static readonly SolidColorBrush OrangeBrush = new(Color.Parse("#FFA500"));
    private static readonly SolidColorBrush GreenBrush = new(Color.Parse("#44BB44"));

    private IBrush _passwordStrengthBrush = GrayBrush;
    public IBrush PasswordStrengthBrush
    {
        get => _passwordStrengthBrush;
        set => this.RaiseAndSetIfChanged(ref _passwordStrengthBrush, value);
    }

    // IValidatableViewModel implementation
    public IValidationContext ValidationContext { get; } = new ValidationContext();

    // Legal page URLs (GitHub Pages)
    // TODO: Move to configuration/settings when custom domain is available
    private const string TermsOfServiceUrl = "https://koizumiiiii.github.io/Baketa/pages/terms-of-service.html";
    private const string PrivacyPolicyUrl = "https://koizumiiiii.github.io/Baketa/pages/privacy-policy.html";

    // Commands (initialized in SetupCommands method)
    public ReactiveCommand<Unit, Unit> SignupWithEmailCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithGoogleCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithDiscordCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> SignupWithTwitchCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> NavigateToLoginCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> ExitCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> OpenTermsCommand { get; private set; } = null!;
    public ReactiveCommand<Unit, Unit> OpenPrivacyPolicyCommand { get; private set; } = null!;

    /// <summary>
    /// SignupViewModelを初期化します
    /// </summary>
    /// <param name="authService">認証サービス</param>
    /// <param name="oauthHandler">OAuthコールバックハンドラー</param>
    /// <param name="navigationService">ナビゲーションサービス</param>
    /// <param name="passwordValidator">パスワード強度バリデーター</param>
    /// <param name="eventAggregator">イベント集約器</param>
    /// <param name="logger">ロガー</param>
    public SignupViewModel(
        IAuthService authService,
        IOAuthCallbackHandler oauthHandler,
        INavigationService navigationService,
        IPasswordStrengthValidator passwordValidator,
        IEventAggregator eventAggregator,
        ILogger<SignupViewModel>? logger = null) : base(eventAggregator, logger)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _oauthHandler = oauthHandler ?? throw new ArgumentNullException(nameof(oauthHandler));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _passwordValidator = passwordValidator ?? throw new ArgumentNullException(nameof(passwordValidator));
        _logger = logger;

        // バリデーションルールの設定
        SetupValidationRules();

        // コマンドの初期化
        SetupCommands();

        // 認証状態変更イベントの購読
        SubscribeToAuthEvents();

        // パスワード強度のリアクティブ更新を設定
        SetupPasswordStrengthIndicator();
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

        // Passwordバリデーション（パスワード強度バリデーターを使用）
        var passwordRule = this.ValidationRule(
            vm => vm.Password,
            password => IsValidPassword(password),
            "パスワードは8文字以上で、大文字・小文字・数字・記号のうち3種類以上を含む必要があります");
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
        // 注意: このセレクタ内でログ出力やプロパティ変更を行わないこと（StackOverflowの原因になる）
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

        // OAuthサインアップコマンド（OAuth認証では利用規約同意は不要 - 一般的なUXパターン）
        var canExecuteOAuth = this.WhenAnyValue(x => x.IsLoading, isLoading => !isLoading);

        SignupWithGoogleCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Google),
            canExecuteOAuth);
        Disposables.Add(SignupWithGoogleCommand);

        SignupWithDiscordCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Discord),
            canExecuteOAuth);
        Disposables.Add(SignupWithDiscordCommand);

        SignupWithTwitchCommand = ReactiveCommand.CreateFromTask(
            () => ExecuteOAuthSignupAsync(AuthProvider.Twitch),
            canExecuteOAuth);
        Disposables.Add(SignupWithTwitchCommand);

        // ログイン画面への遷移コマンド（ダイアログを閉じてから切り替え）
        NavigateToLoginCommand = ReactiveCommand.Create(() =>
        {
            _logger?.LogInformation("[AUTH_DEBUG] NavigateToLoginCommand実行開始");

            // 🔥 [ISSUE#167] ダイアログを閉じて、その後LoginViewを表示
            _logger?.LogInformation("[AUTH_DEBUG] CloseDialogRequestedイベント発火 (画面切り替え)");
            CloseDialogRequested?.Invoke(false); // false = 画面切り替え（認証成功ではない）

            // UIスレッドで非同期にLoginViewを表示（ダイアログが閉じた後に実行される）
            _ = Task.Run(async () =>
            {
                await Task.Delay(150).ConfigureAwait(false); // ダイアログが閉じるのを待つ
                _logger?.LogInformation("[AUTH_DEBUG] SwitchToLoginAsync呼び出し");
                await _navigationService.SwitchToLoginAsync().ConfigureAwait(false);
            });
        });
        Disposables.Add(NavigateToLoginCommand);

        // アプリケーション終了コマンド
        ExitCommand = ReactiveCommand.Create(() =>
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        });
        Disposables.Add(ExitCommand);

        // 利用規約を開くコマンド
        OpenTermsCommand = ReactiveCommand.Create(() => OpenUrlInBrowser(TermsOfServiceUrl));
        Disposables.Add(OpenTermsCommand);

        // プライバシーポリシーを開くコマンド
        OpenPrivacyPolicyCommand = ReactiveCommand.Create(() => OpenUrlInBrowser(PrivacyPolicyUrl));
        Disposables.Add(OpenPrivacyPolicyCommand);

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
        var oauthCommands = new[] { SignupWithGoogleCommand, SignupWithDiscordCommand, SignupWithTwitchCommand };
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
        _logger?.LogDebug("[AUTH_DEBUG] SignupViewModel.OnAuthStatusChanged呼び出し開始 - IsLoggedIn={IsLoggedIn}, Thread={ThreadId}",
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
                _logger?.LogDebug("[AUTH_DEBUG] CloseDialogRequested発火前");
                CloseDialogRequested?.Invoke(true); // true = 認証成功
                _logger?.LogDebug("[AUTH_DEBUG] CloseDialogRequested発火後");

                // 注意: ErrorMessageとSetAuthenticationModeはViewのOnClosedで処理される
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[AUTH_DEBUG] UIThread内処理で例外: {Message}", ex.Message);
            }
        });

        _logger?.LogDebug("[AUTH_DEBUG] SignupViewModel.OnAuthStatusChanged InvokeAsync発行完了");
    }

    /// <summary>
    /// 認証成功フラグ（ダイアログを閉じるために使用）
    /// </summary>
    private bool _authenticationSucceeded;
    public bool AuthenticationSucceeded
    {
        get => _authenticationSucceeded;
        set => this.RaiseAndSetIfChanged(ref _authenticationSucceeded, value);
    }

    /// <summary>
    /// ダイアログを閉じる要求イベント
    /// パラメータ: 認証成功の場合はtrue、画面切り替えの場合はfalse
    /// </summary>
    public event Action<bool>? CloseDialogRequested;

    /// <summary>
    /// デバッグログを出力します（Viewからの呼び出し用）
    /// </summary>
    /// <param name="message">ログメッセージ</param>
    public void LogDebug(string message) => _logger?.LogDebug("{Message}", message);

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

            // 🔥 [FIX] ConfigureAwait(true)に変更してUIスレッドで継続処理を実行
            // ConfigureAwait(false)だとバックグラウンドスレッドになり、プロパティ変更でAccessViolationが発生する
            var result = await _authService.SignUpWithEmailPasswordAsync(Email, Password);

            if (result is AuthSuccess success)
            {
                if (_logger != null)
                    _logSignupSuccess(_logger, Email, null);

                // 🔥 [UX改善] 成功メッセージを緑色で表示し、数秒後にログイン画面へ自動遷移
                SuccessMessage = "確認メールを送信しました。メール内のリンクをクリックしてから、ログインしてください。3秒後にログイン画面に移動します...";
                ErrorMessage = null; // エラーメッセージをクリア

                _logger?.LogInformation("サインアップ成功: 確認メールを送信しました（Email: {Email}）", Email);

                // 3秒待ってからログイン画面へ遷移
                await Task.Delay(3000);

                // ダイアログを閉じてLoginViewを表示
                CloseDialogRequested?.Invoke(false); // false = 画面切り替え（認証成功ではない）
                await Task.Delay(150); // ダイアログが閉じるのを待つ
                await _navigationService.SwitchToLoginAsync();
            }
            else if (result is AuthFailure failure)
            {
                // 🔥 [FIX] EmailNotConfirmedは成功として扱う（確認メール送信成功）
                // SupabaseAuthServiceは確認メール送信時にAuthFailure(EmailNotConfirmed)を返す
                if (failure.ErrorCode == AuthErrorCodes.EmailNotConfirmed)
                {
                    if (_logger != null)
                        _logSignupSuccess(_logger, Email, null);

                    // 緑色の成功メッセージを表示
                    SuccessMessage = "確認メールを送信しました。メール内のリンクをクリックしてから、ログインしてください。3秒後にログイン画面に移動します...";
                    ErrorMessage = null;

                    _logger?.LogInformation("サインアップ成功（メール確認待ち）: 確認メールを送信しました（Email: {Email}）", Email);

                    // 3秒待ってからログイン画面へ遷移
                    await Task.Delay(3000);

                    // ダイアログを閉じてLoginViewを表示
                    CloseDialogRequested?.Invoke(false);
                    await Task.Delay(150);
                    await _navigationService.SwitchToLoginAsync();
                }
                else
                {
                    // 通常のエラー
                    ErrorMessage = GetAuthFailureMessage(failure.ErrorCode, failure.Message);
                }
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
    private bool IsValidPassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return false;

        var result = _passwordValidator.ValidatePassword(password);
        return result.IsValid;
    }

    /// <summary>
    /// パスワード強度インジケーターのリアクティブ更新を設定します
    /// </summary>
    private void SetupPasswordStrengthIndicator()
    {
        // パスワード変更時に強度を更新
        var passwordStrengthSubscription = this.WhenAnyValue(x => x.Password)
            .Subscribe(password =>
            {
                if (string.IsNullOrWhiteSpace(password))
                {
                    PasswordStrength = PasswordStrength.Weak;
                    PasswordStrengthMessage = string.Empty;
                    PasswordStrengthBrush = GrayBrush;
                    return;
                }

                var strength = _passwordValidator.GetPasswordStrength(password);
                PasswordStrength = strength;
                PasswordStrengthMessage = _passwordValidator.GetStrengthMessage(strength);
                PasswordStrengthBrush = strength switch
                {
                    PasswordStrength.Weak => RedBrush,
                    PasswordStrength.Medium => OrangeBrush,
                    PasswordStrength.Strong => GreenBrush,
                    _ => GrayBrush
                };
            });
        Disposables.Add(passwordStrengthSubscription);
    }

    /// <summary>
    /// 認証失敗メッセージを取得します
    /// </summary>
    /// <param name="errorCode">エラーコード</param>
    /// <param name="message">エラーメッセージ</param>
    /// <returns>ユーザーフレンドリーなエラーメッセージ</returns>
    private static string GetAuthFailureMessage(string errorCode, string message)
    {
        // 🔥 [FIX] AuthErrorCodes定数を使用（大文字小文字の不一致を修正）
        return errorCode switch
        {
            AuthErrorCodes.UserAlreadyExists => "このメールアドレスは既に使用されています",
            AuthErrorCodes.WeakPassword => "パスワードが弱すぎます。より強固なパスワードを設定してください",
            AuthErrorCodes.InvalidCredentials => "無効なメールアドレス形式です",
            AuthErrorCodes.EmailNotConfirmed => "確認メールを送信しました。メール内のリンクをクリックしてから、ログイン画面でログインしてください。",
            AuthErrorCodes.RateLimitExceeded => "リクエストが多すぎます。しばらく時間をおいてから再試行してください",
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

    /// <summary>
    /// 指定したURLをデフォルトブラウザで開きます
    /// </summary>
    /// <param name="url">開くURL</param>
    private void OpenUrlInBrowser(string url)
    {
        try
        {
            _logger?.LogDebug("外部URLを開く: {Url}", url);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "URLを開けませんでした: {Url}", url);
        }
    }
}
