using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Baketa.Infrastructure.License.Services;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
    private readonly IPatreonOAuthService? _patreonService;
    private readonly PatreonSettings? _patreonSettings;
    private readonly ILicenseManager? _licenseManager;
    private readonly LicenseSettings? _licenseSettings;
    private readonly ILoggerFactory? _loggerFactory;
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

    // Patreon連携関連
    private bool _isPatreonConnected;
    private string? _patreonUserName;
    private PlanType _currentPlan = PlanType.Free;
    private DateTime? _patreonLastSyncTime;
    private PatreonSyncStatus _patreonSyncStatus = PatreonSyncStatus.NotConnected;
    private bool _isPatreonSyncing;

    /// <summary>
    /// AccountSettingsViewModelを初期化します
    /// </summary>
    public AccountSettingsViewModel(
        IAuthService authService,
        INavigationService navigationService,
        IEventAggregator eventAggregator,
        IPatreonOAuthService? patreonService = null,
        IOptions<PatreonSettings>? patreonSettings = null,
        ILicenseManager? licenseManager = null,
        IOptions<LicenseSettings>? licenseSettings = null,
        ILoggerFactory? loggerFactory = null,
        ILogger<AccountSettingsViewModel>? logger = null) : base(eventAggregator)
    {
        _authService = authService ?? throw new ArgumentNullException(nameof(authService));
        _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
        _patreonService = patreonService;
        _patreonSettings = patreonSettings?.Value;
        _licenseManager = licenseManager;
        _licenseSettings = licenseSettings?.Value;
        _loggerFactory = loggerFactory;
        _logger = logger;

        // 認証状態変更イベントの購読
        _authService.AuthStatusChanged += OnAuthStatusChanged;

        // Patreonステータス変更イベントの購読
        if (_patreonService != null)
        {
            _patreonService.StatusChanged += OnPatreonStatusChanged;
        }

        // ライセンス状態変更イベントの購読（プロモーションコード適用時のUI更新用）
        if (_licenseManager != null)
        {
            _licenseManager.StateChanged += OnLicenseStateChanged;
        }

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

        // Patreonコマンドの初期化
        ConnectPatreonCommand = ReactiveCommand.CreateFromTask(
            ExecuteConnectPatreonAsync,
            this.WhenAnyValue(x => x.IsPatreonConnected, x => x.IsPatreonSyncing,
                (connected, syncing) => !connected && !syncing));

        SyncPatreonCommand = ReactiveCommand.CreateFromTask(
            ExecuteSyncPatreonAsync,
            this.WhenAnyValue(x => x.IsPatreonConnected, x => x.IsPatreonSyncing,
                (connected, syncing) => connected && !syncing));

        DisconnectPatreonCommand = ReactiveCommand.CreateFromTask(
            ExecuteDisconnectPatreonAsync,
            this.WhenAnyValue(x => x.IsPatreonConnected, x => x.IsPatreonSyncing,
                (connected, syncing) => connected && !syncing));

        // 初期状態の読み込み（例外を適切に処理）
        _ = LoadCurrentSessionAsync().ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger?.LogError(t.Exception, "セッション読み込みの初期化に失敗しました");
            }
        }, TaskScheduler.Default);

        // Patreon状態の読み込み
        _ = LoadPatreonStatusAsync();
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
    /// <remarks>
    /// CA1863: ローカライズされたリソース文字列は言語変更時に内容が変わるため、
    /// CompositeFormatキャッシュは不適切。プロパティアクセスは低頻度。
    /// </remarks>
#pragma warning disable CA1863
    public string AuthProviderDisplay => string.IsNullOrEmpty(AuthProvider)
        ? string.Empty
        : string.Format(Strings.Settings_Account_LoggedInWith, AuthProvider);
#pragma warning restore CA1863

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

    /// <summary>
    /// Patreonと連携済みかどうか
    /// </summary>
    public bool IsPatreonConnected
    {
        get => _isPatreonConnected;
        private set => this.RaiseAndSetIfChanged(ref _isPatreonConnected, value);
    }

    /// <summary>
    /// Patreonユーザー名
    /// </summary>
    public string? PatreonUserName
    {
        get => _patreonUserName;
        private set => this.RaiseAndSetIfChanged(ref _patreonUserName, value);
    }

    /// <summary>
    /// 現在のプラン（内部使用）
    /// </summary>
    private PlanType CurrentPlan
    {
        get => _currentPlan;
        set => _currentPlan = value;
    }

    /// <summary>
    /// プラン名を取得（ステータスメッセージ用）
    /// </summary>
    // Issue #125: Standardプラン廃止
    // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
    private string GetPlanName(PlanType plan) => plan switch
    {
        PlanType.Ultimate => "Ultimate",
        PlanType.Premium => "Premium",
        PlanType.Pro => "Pro",
        _ => "Free"
    };

    /// <summary>
    /// Patreon最終同期日時
    /// </summary>
    public DateTime? PatreonLastSyncTime
    {
        get => _patreonLastSyncTime;
        private set
        {
            this.RaiseAndSetIfChanged(ref _patreonLastSyncTime, value);
            this.RaisePropertyChanged(nameof(PatreonLastSyncTimeDisplay));
        }
    }

    /// <summary>
    /// 最終同期日時の表示文字列
    /// </summary>
    public string PatreonLastSyncTimeDisplay => PatreonLastSyncTime.HasValue
        ? PatreonLastSyncTime.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
        : "未同期";

    /// <summary>
    /// Patreon同期ステータス
    /// </summary>
    public PatreonSyncStatus PatreonSyncStatus
    {
        get => _patreonSyncStatus;
        private set
        {
            this.RaiseAndSetIfChanged(ref _patreonSyncStatus, value);
            this.RaisePropertyChanged(nameof(PatreonSyncStatusDisplay));
        }
    }

    /// <summary>
    /// 同期ステータス表示文字列
    /// </summary>
    public string PatreonSyncStatusDisplay => IsPatreonSyncing
        ? Strings.Settings_Account_PatreonSyncing
        : PatreonSyncStatus switch
        {
            PatreonSyncStatus.Synced => Strings.Settings_Account_PatreonStatusSynced,
            PatreonSyncStatus.Offline => Strings.Settings_Account_PatreonStatusOffline,
            PatreonSyncStatus.TokenExpired => Strings.Settings_Account_PatreonStatusTokenExpired,
            PatreonSyncStatus.Error => Strings.Settings_Account_PatreonStatusError,
            _ => Strings.Settings_Account_PatreonStatusNotConnected
        };

    /// <summary>
    /// Patreon同期中かどうか
    /// </summary>
    public bool IsPatreonSyncing
    {
        get => _isPatreonSyncing;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPatreonSyncing, value);
            this.RaisePropertyChanged(nameof(PatreonSyncStatusDisplay));
        }
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

    /// <summary>
    /// Patreon連携開始コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConnectPatreonCommand { get; }

    /// <summary>
    /// Patreon同期コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SyncPatreonCommand { get; }

    /// <summary>
    /// Patreon連携解除コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DisconnectPatreonCommand { get; }

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

    #region Patreonメソッド

    /// <summary>
    /// Patreon状態を読み込みます
    /// </summary>
    private async Task LoadPatreonStatusAsync()
    {
        // モックモード時はライセンスマネージャーから直接プランを取得
        if (_licenseSettings?.EnableMockMode == true && _licenseManager != null)
        {
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var licenseState = _licenseManager.CurrentState;
                IsPatreonConnected = true; // モックモードでは常に連携済みとして扱う
                PatreonUserName = Strings.Settings_Account_MockModeTestUser;
                CurrentPlan = licenseState.CurrentPlan;
                PatreonLastSyncTime = DateTime.Now;
                PatreonSyncStatus = PatreonSyncStatus.Synced;
                _logger?.LogDebug("モックモード: ライセンス状態からプランを取得 Plan={Plan}", CurrentPlan);
            });
            return;
        }

        if (_patreonService == null)
        {
            return;
        }

        try
        {
            var credentials = await _patreonService.LoadCredentialsAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (credentials != null)
                {
                    IsPatreonConnected = _patreonService.IsAuthenticated;
                    PatreonUserName = credentials.FullName;
                    CurrentPlan = credentials.LastKnownPlan;
                    PatreonLastSyncTime = credentials.LastSyncTime;
                    PatreonSyncStatus = _patreonService.SyncStatus;
                    _logger?.LogDebug("Patreon状態を読み込みました: Connected={Connected}, Plan={Plan}",
                        IsPatreonConnected, CurrentPlan);
                }
                else
                {
                    IsPatreonConnected = false;
                    PatreonUserName = null;
                    CurrentPlan = PlanType.Free;
                    PatreonLastSyncTime = null;
                    PatreonSyncStatus = PatreonSyncStatus.NotConnected;
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon状態の読み込みに失敗しました");
        }
    }

    /// <summary>
    /// Patreonステータス変更イベントハンドラ
    /// </summary>
    private void OnPatreonStatusChanged(object? sender, PatreonStatusChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            PatreonSyncStatus = e.NewStatus;
            CurrentPlan = e.Plan;
            PatreonLastSyncTime = e.LastSyncTime;

            if (e.NewStatus == PatreonSyncStatus.Synced)
            {
                IsPatreonConnected = true;
                SetStatusMessage($"Patreon同期完了: {GetPlanName(CurrentPlan)}プラン", false);
            }
            else if (e.NewStatus == PatreonSyncStatus.Error)
            {
                SetStatusMessage(e.ErrorMessage ?? "Patreon同期エラー", true);
            }
            else if (e.NewStatus == PatreonSyncStatus.NotConnected)
            {
                IsPatreonConnected = false;
                PatreonUserName = null;
            }

            _logger?.LogDebug("Patreonステータス変更: {Status}, Plan={Plan}", e.NewStatus, e.Plan);
        });
    }

    /// <summary>
    /// ライセンス状態変更イベントハンドラ
    /// </summary>
    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentPlan = e.NewState.CurrentPlan;
            _logger?.LogDebug("ライセンス状態変更: {OldPlan} -> {NewPlan}, Reason={Reason}",
                e.OldState.CurrentPlan, e.NewState.CurrentPlan, e.Reason);
        });
    }

    /// <summary>
    /// Patreon連携を開始します（非同期版・ローカルHTTPコールバック対応）
    /// </summary>
    private async Task ExecuteConnectPatreonAsync()
    {
        if (_patreonService == null || _patreonSettings == null)
        {
            SetStatusMessage("Patreonサービスが利用できません", true);
            return;
        }

        try
        {
            IsPatreonSyncing = true;
            ClearStatusMessage();

            // ローカルHTTPコールバックサーバーを使用（DIからロガーを取得）
            var callbackLogger = _loggerFactory?.CreateLogger<PatreonCallbackServer>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PatreonCallbackServer>.Instance;
            await using var callbackServer = new PatreonCallbackServer(
                _patreonService,
                Options.Create(_patreonSettings),
                callbackLogger);

            // CSRF対策用のstate生成
            var state = _patreonService.GenerateSecureState();

            // 認証URLを生成
            var authUrl = _patreonService.GenerateAuthorizationUrl(state);

            // コールバックサーバーを開始（バックグラウンドで待機）
            var callbackTask = callbackServer.StartAndWaitForCallbackAsync();

            // デフォルトブラウザで開く
            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatusMessage("ブラウザでPatreon認証を完了してください...", false));

            _logger?.LogInformation("Patreon認証URLをブラウザで開きました、コールバック待機中");

            // コールバックを待機
            var result = await callbackTask.ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Success)
                {
                    IsPatreonConnected = true;
                    PatreonUserName = result.UserName;
                    CurrentPlan = result.Plan;
                    PatreonLastSyncTime = DateTime.UtcNow;
                    PatreonSyncStatus = PatreonSyncStatus.Synced;
                    SetStatusMessage($"✅ Patreon連携完了！{result.Plan}プラン", false);
                    _logger?.LogInformation("Patreon連携成功: Plan={Plan}", result.Plan);
                }
                else
                {
                    SetStatusMessage(result.ErrorMessage ?? "Patreon連携に失敗しました", true);
                    _logger?.LogWarning("Patreon連携失敗: {Error}", result.ErrorMessage);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon連携開始中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
                SetStatusMessage("Patreon連携の開始に失敗しました", true));
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPatreonSyncing = false);
        }
    }

    /// <summary>
    /// Patreon状態を同期します
    /// </summary>
    private async Task ExecuteSyncPatreonAsync()
    {
        if (_patreonService == null)
        {
            return;
        }

        try
        {
            IsPatreonSyncing = true;
            ClearStatusMessage();

            var result = await _patreonService.SyncLicenseAsync(forceRefresh: true).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (result.Success)
                {
                    CurrentPlan = result.Plan;
                    PatreonLastSyncTime = DateTime.UtcNow;
                    PatreonSyncStatus = PatreonSyncStatus.Synced;
                    SetStatusMessage($"同期完了: {GetPlanName(CurrentPlan)}プラン", false);
                    _logger?.LogInformation("Patreon同期成功: Plan={Plan}", result.Plan);
                }
                else
                {
                    PatreonSyncStatus = result.Status;
                    SetStatusMessage(result.ErrorMessage ?? "同期に失敗しました", true);
                    _logger?.LogWarning("Patreon同期失敗: {Error}", result.ErrorMessage);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon同期中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("同期中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPatreonSyncing = false);
        }
    }

    /// <summary>
    /// Patreon連携を解除します
    /// </summary>
    private async Task ExecuteDisconnectPatreonAsync()
    {
        if (_patreonService == null)
        {
            return;
        }

        try
        {
            IsPatreonSyncing = true;
            ClearStatusMessage();

            await _patreonService.DisconnectAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsPatreonConnected = false;
                PatreonUserName = null;
                CurrentPlan = PlanType.Free;
                PatreonLastSyncTime = null;
                PatreonSyncStatus = PatreonSyncStatus.NotConnected;
                SetStatusMessage("Patreon連携を解除しました", false);
                _logger?.LogInformation("Patreon連携を解除しました");
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon連携解除中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SetStatusMessage("連携解除中にエラーが発生しました", true);
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsPatreonSyncing = false);
        }
    }

    #endregion

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _authService.AuthStatusChanged -= OnAuthStatusChanged;

            if (_patreonService != null)
            {
                _patreonService.StatusChanged -= OnPatreonStatusChanged;
            }

            if (_licenseManager != null)
            {
                _licenseManager.StateChanged -= OnLicenseStateChanged;
            }
        }
        base.Dispose(disposing);
    }

    #endregion
}
