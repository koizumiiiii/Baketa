using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.License.Events;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Settings;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// ライセンス情報設定画面のViewModel
/// プラン情報、トークン使用量、プラン詳細、Patreon連携ステータスを表示
/// </summary>
public sealed class LicenseInfoViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
    private readonly IPatreonOAuthService? _patreonService;
    private readonly PatreonSettings? _patreonSettings;
    private readonly ILogger<LicenseInfoViewModel>? _logger;

    private PlanType _currentPlan;
    private string _planDisplayName = string.Empty;
    private string _planDescription = string.Empty;
    private long _tokensUsed;
    private long _tokenLimit;
    private double _usagePercentage;
    private bool _hasCloudAccess;
    private bool _isQuotaExceeded;
    private DateTime? _expirationDate;
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    // Patreon関連
    private bool _isPatreonConnected;
    private PatreonSyncStatus _patreonSyncStatus = PatreonSyncStatus.NotConnected;
    private DateTime? _lastSyncTime;
    private string _patreonStatusDisplay = string.Empty;
    private bool _isPatreonEnabled;

    /// <summary>
    /// LicenseInfoViewModelを初期化します
    /// </summary>
    public LicenseInfoViewModel(
        ILicenseManager licenseManager,
        IEventAggregator eventAggregator,
        IPatreonOAuthService? patreonService = null,
        IOptions<PatreonSettings>? patreonSettings = null,
        ILogger<LicenseInfoViewModel>? logger = null) : base(eventAggregator)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _patreonService = patreonService;
        _patreonSettings = patreonSettings?.Value;
        _logger = logger;

        // Patreonが有効かどうか
        _isPatreonEnabled = !string.IsNullOrWhiteSpace(_patreonSettings?.ClientId);

        // ライセンス状態変更イベントの購読
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // Patreonステータス変更イベントの購読
        if (_patreonService != null)
        {
            _patreonService.StatusChanged += OnPatreonStatusChanged;
        }

        // コマンドの初期化
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshLicenseStateAsync);
        ChangePlanCommand = ReactiveCommand.Create(OpenPlanChangePage);
        ConnectPatreonCommand = ReactiveCommand.Create(OpenPatreonAuthPage, this.WhenAnyValue(x => x.IsPatreonEnabled));
        DisconnectPatreonCommand = ReactiveCommand.CreateFromTask(DisconnectPatreonAsync, this.WhenAnyValue(x => x.IsPatreonConnected));
        SyncPatreonCommand = ReactiveCommand.CreateFromTask(SyncPatreonAsync, this.WhenAnyValue(x => x.IsPatreonConnected));

        // 初期状態の読み込み
        LoadCurrentState();
        LoadPatreonState();

        _logger?.LogDebug("LicenseInfoViewModel初期化完了 - PatreonEnabled={Enabled}", _isPatreonEnabled);
    }

    #region プロパティ

    /// <summary>
    /// 現在のプランタイプ
    /// </summary>
    public PlanType CurrentPlan
    {
        get => _currentPlan;
        private set => this.RaiseAndSetIfChanged(ref _currentPlan, value);
    }

    /// <summary>
    /// プラン表示名
    /// </summary>
    public string PlanDisplayName
    {
        get => _planDisplayName;
        private set => this.RaiseAndSetIfChanged(ref _planDisplayName, value);
    }

    /// <summary>
    /// プラン説明
    /// </summary>
    public string PlanDescription
    {
        get => _planDescription;
        private set => this.RaiseAndSetIfChanged(ref _planDescription, value);
    }

    /// <summary>
    /// 使用済みトークン数
    /// </summary>
    public long TokensUsed
    {
        get => _tokensUsed;
        private set => this.RaiseAndSetIfChanged(ref _tokensUsed, value);
    }

    /// <summary>
    /// トークン上限
    /// </summary>
    public long TokenLimit
    {
        get => _tokenLimit;
        private set => this.RaiseAndSetIfChanged(ref _tokenLimit, value);
    }

    /// <summary>
    /// 使用率（0-100）
    /// </summary>
    public double UsagePercentage
    {
        get => _usagePercentage;
        private set => this.RaiseAndSetIfChanged(ref _usagePercentage, value);
    }

    /// <summary>
    /// クラウドAIアクセス権があるか
    /// </summary>
    public bool HasCloudAccess
    {
        get => _hasCloudAccess;
        private set => this.RaiseAndSetIfChanged(ref _hasCloudAccess, value);
    }

    /// <summary>
    /// クォータを超過しているか
    /// </summary>
    public bool IsQuotaExceeded
    {
        get => _isQuotaExceeded;
        private set => this.RaiseAndSetIfChanged(ref _isQuotaExceeded, value);
    }

    /// <summary>
    /// プラン有効期限
    /// </summary>
    public DateTime? ExpirationDate
    {
        get => _expirationDate;
        private set => this.RaiseAndSetIfChanged(ref _expirationDate, value);
    }

    /// <summary>
    /// 有効期限の表示文字列
    /// </summary>
    public string ExpirationDateDisplay => ExpirationDate.HasValue
        ? ExpirationDate.Value.ToString("yyyy/MM/dd")
        : Strings.License_NoExpiration;

    /// <summary>
    /// トークン使用量の表示文字列
    /// </summary>
    public string TokenUsageDisplay => HasCloudAccess
        ? $"{TokensUsed:N0} / {TokenLimit:N0}"
        : Strings.License_LocalOnly;

    /// <summary>
    /// クラウドアクセス状態の表示文字列
    /// </summary>
    public string CloudAccessDisplay => HasCloudAccess
        ? Strings.Common_Yes
        : Strings.Common_No;

    /// <summary>
    /// ステータスメッセージ
    /// </summary>
    public string StatusMessage
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

    // --- Patreon関連プロパティ ---

    /// <summary>
    /// Patreon連携が有効（設定されている）かどうか
    /// </summary>
    public bool IsPatreonEnabled
    {
        get => _isPatreonEnabled;
        private set => this.RaiseAndSetIfChanged(ref _isPatreonEnabled, value);
    }

    /// <summary>
    /// Patreon連携済みかどうか
    /// </summary>
    public bool IsPatreonConnected
    {
        get => _isPatreonConnected;
        private set => this.RaiseAndSetIfChanged(ref _isPatreonConnected, value);
    }

    /// <summary>
    /// Patreon同期ステータス
    /// </summary>
    public PatreonSyncStatus PatreonSyncStatus
    {
        get => _patreonSyncStatus;
        private set => this.RaiseAndSetIfChanged(ref _patreonSyncStatus, value);
    }

    /// <summary>
    /// 最終同期日時
    /// </summary>
    public DateTime? LastSyncTime
    {
        get => _lastSyncTime;
        private set => this.RaiseAndSetIfChanged(ref _lastSyncTime, value);
    }

    /// <summary>
    /// 最終同期日時の表示文字列
    /// </summary>
    public string LastSyncTimeDisplay => LastSyncTime.HasValue
        ? LastSyncTime.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm")
        : "未同期";

    /// <summary>
    /// Patreonステータスの表示文字列
    /// </summary>
    public string PatreonStatusDisplay
    {
        get => _patreonStatusDisplay;
        private set => this.RaiseAndSetIfChanged(ref _patreonStatusDisplay, value);
    }

    /// <summary>
    /// Patreon同期ステータスの表示文字列
    /// </summary>
    public string PatreonSyncStatusDisplay => PatreonSyncStatus switch
    {
        PatreonSyncStatus.NotConnected => "未接続",
        PatreonSyncStatus.Synced => "同期済み",
        PatreonSyncStatus.Offline => "オフライン（キャッシュ使用中）",
        PatreonSyncStatus.TokenExpired => "再認証が必要",
        PatreonSyncStatus.Error => "エラー",
        _ => "不明"
    };

    /// <summary>
    /// Patreon同期ステータスがエラーかどうか
    /// </summary>
    public bool IsPatreonStatusError => PatreonSyncStatus is PatreonSyncStatus.TokenExpired or PatreonSyncStatus.Error;

    /// <summary>
    /// Patreonエラーメッセージ
    /// </summary>
    public string PatreonErrorMessage => PatreonSyncStatus switch
    {
        PatreonSyncStatus.TokenExpired => "Patreonの認証有効期限が切れました。再接続してください。",
        PatreonSyncStatus.Error => "Patreon連携でエラーが発生しています。再同期をお試しください。",
        _ => string.Empty
    };

    #endregion

    #region コマンド

    /// <summary>
    /// ライセンス状態更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    /// <summary>
    /// プラン変更コマンド（外部Webページを開く）
    /// </summary>
    public ReactiveCommand<Unit, Unit> ChangePlanCommand { get; }

    /// <summary>
    /// Patreon連携コマンド（認証ページを開く）
    /// </summary>
    public ReactiveCommand<Unit, Unit> ConnectPatreonCommand { get; }

    /// <summary>
    /// Patreon連携解除コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> DisconnectPatreonCommand { get; }

    /// <summary>
    /// Patreon手動同期コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> SyncPatreonCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 現在のライセンス状態を読み込みます
    /// </summary>
    private void LoadCurrentState()
    {
        var state = _licenseManager.CurrentState;
        UpdateFromState(state);
    }

    /// <summary>
    /// ライセンス状態からUIを更新します
    /// </summary>
    private void UpdateFromState(LicenseState state)
    {
        CurrentPlan = state.CurrentPlan;
        PlanDisplayName = GetPlanDisplayName(state.CurrentPlan);
        PlanDescription = GetPlanDescription(state.CurrentPlan);
        TokensUsed = state.CloudAiTokensUsed;
        TokenLimit = state.MonthlyTokenLimit;
        UsagePercentage = TokenLimit > 0 ? (double)TokensUsed / TokenLimit * 100 : 0;
        HasCloudAccess = state.CurrentPlan.HasCloudAiAccess();
        IsQuotaExceeded = state.IsQuotaExceeded;
        ExpirationDate = state.ExpirationDate;

        // 派生プロパティの更新通知
        this.RaisePropertyChanged(nameof(ExpirationDateDisplay));
        this.RaisePropertyChanged(nameof(TokenUsageDisplay));
        this.RaisePropertyChanged(nameof(CloudAccessDisplay));
    }

    /// <summary>
    /// ライセンス状態を更新します
    /// </summary>
    private async System.Threading.Tasks.Task RefreshLicenseStateAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;
            IsStatusError = false;

            var state = await _licenseManager.RefreshStateAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                UpdateFromState(state);
                StatusMessage = Strings.License_RefreshSuccess;
            });

            _logger?.LogInformation("ライセンス状態を更新しました: Plan={Plan}", state.CurrentPlan);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ライセンス状態の更新に失敗しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = Strings.License_RefreshFailed;
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// プラン変更ページを外部ブラウザで開きます
    /// </summary>
    private void OpenPlanChangePage()
    {
        const string planChangeUrl = "https://koizumiiiii.github.io/Baketa/pages/pricing.html";

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = planChangeUrl,
                UseShellExecute = true
            });
            StatusMessage = Strings.License_OpeningPlanPage;
            IsStatusError = false;
            _logger?.LogInformation("プラン変更ページを開きました: {Url}", planChangeUrl);
        }
        catch (Exception ex)
        {
            StatusMessage = Strings.License_PlanPageOpenFailed;
            IsStatusError = true;
            _logger?.LogError(ex, "プラン変更ページを開けませんでした: {Url}", planChangeUrl);
        }
    }

    /// <summary>
    /// ライセンス状態変更イベントハンドラ
    /// </summary>
    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            UpdateFromState(e.NewState);
            _logger?.LogDebug("ライセンス状態が変更されました: {OldPlan} -> {NewPlan}",
                e.OldState.CurrentPlan, e.NewState.CurrentPlan);
        });
    }

    /// <summary>
    /// プラン表示名を取得
    /// </summary>
    private static string GetPlanDisplayName(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => Strings.License_Plan_Free,
            PlanType.Standard => Strings.License_Plan_Standard,
            PlanType.Pro => Strings.License_Plan_Pro,
            PlanType.Premia => Strings.License_Plan_Premia,
            _ => Strings.License_Plan_Unknown
        };
    }

    /// <summary>
    /// プラン説明を取得
    /// </summary>
    private static string GetPlanDescription(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => Strings.License_Desc_Free,
            PlanType.Standard => Strings.License_Desc_Standard,
            PlanType.Pro => Strings.License_Desc_Pro,
            PlanType.Premia => Strings.License_Desc_Premia,
            _ => string.Empty
        };
    }

    // --- Patreon関連メソッド ---

    /// <summary>
    /// Patreon状態を読み込みます
    /// </summary>
    private void LoadPatreonState()
    {
        if (_patreonService == null)
        {
            return;
        }

        IsPatreonConnected = _patreonService.IsAuthenticated;
        PatreonSyncStatus = _patreonService.SyncStatus;
        LastSyncTime = _patreonService.LastSyncTime;

        UpdatePatreonStatusDisplay();
    }

    /// <summary>
    /// Patreonステータス表示を更新します
    /// </summary>
    private void UpdatePatreonStatusDisplay()
    {
        if (!IsPatreonConnected)
        {
            PatreonStatusDisplay = "Patreon未連携";
            return;
        }

        PatreonStatusDisplay = PatreonSyncStatus switch
        {
            PatreonSyncStatus.Synced => $"連携済み（{LastSyncTimeDisplay}）",
            PatreonSyncStatus.Offline => $"オフライン（最終同期: {LastSyncTimeDisplay}）",
            PatreonSyncStatus.TokenExpired => "再認証が必要です",
            PatreonSyncStatus.Error => "同期エラー",
            _ => "連携済み"
        };

        // 派生プロパティの更新通知
        this.RaisePropertyChanged(nameof(PatreonSyncStatusDisplay));
        this.RaisePropertyChanged(nameof(LastSyncTimeDisplay));
        this.RaisePropertyChanged(nameof(IsPatreonStatusError));
        this.RaisePropertyChanged(nameof(PatreonErrorMessage));
    }

    /// <summary>
    /// Patreon認証ページを開きます
    /// </summary>
    private void OpenPatreonAuthPage()
    {
        if (_patreonService == null)
        {
            StatusMessage = "Patreon連携が設定されていません";
            IsStatusError = true;
            return;
        }

        try
        {
            // CSRF対策用のstate値を生成
            var state = Guid.NewGuid().ToString("N");

            // TODO: state値をローカルに保存してコールバック時に検証

            var authUrl = _patreonService.GenerateAuthorizationUrl(state);

            Process.Start(new ProcessStartInfo
            {
                FileName = authUrl,
                UseShellExecute = true
            });

            StatusMessage = "ブラウザでPatreon認証ページを開きました";
            IsStatusError = false;
            _logger?.LogInformation("Patreon認証ページを開きました");
        }
        catch (Exception ex)
        {
            StatusMessage = "認証ページを開けませんでした";
            IsStatusError = true;
            _logger?.LogError(ex, "Patreon認証ページを開けませんでした");
        }
    }

    /// <summary>
    /// Patreon連携を解除します
    /// </summary>
    private async System.Threading.Tasks.Task DisconnectPatreonAsync()
    {
        if (_patreonService == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;

            await _patreonService.DisconnectAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsPatreonConnected = false;
                PatreonSyncStatus = PatreonSyncStatus.NotConnected;
                LastSyncTime = null;
                UpdatePatreonStatusDisplay();
                StatusMessage = "Patreon連携を解除しました";
                IsStatusError = false;
            });

            _logger?.LogInformation("Patreon連携を解除しました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon連携の解除に失敗しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "連携解除に失敗しました";
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
        }
    }

    /// <summary>
    /// Patreonライセンス状態を同期します
    /// </summary>
    private async System.Threading.Tasks.Task SyncPatreonAsync()
    {
        if (_patreonService == null)
        {
            return;
        }

        try
        {
            IsLoading = true;
            StatusMessage = string.Empty;

            var result = await _patreonService.SyncLicenseAsync(forceRefresh: true).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PatreonSyncStatus = result.Status;
                LastSyncTime = _patreonService.LastSyncTime;
                UpdatePatreonStatusDisplay();

                if (result.Success)
                {
                    StatusMessage = result.FromCache
                        ? "キャッシュから読み込みました"
                        : "Patreonと同期しました";
                    IsStatusError = false;
                }
                else
                {
                    StatusMessage = result.ErrorMessage ?? "同期に失敗しました";
                    IsStatusError = true;
                }
            });

            _logger?.LogInformation("Patreon同期完了: Status={Status}, Plan={Plan}", result.Status, result.Plan);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Patreon同期に失敗しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "同期に失敗しました";
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
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
            LastSyncTime = e.LastSyncTime;
            UpdatePatreonStatusDisplay();

            _logger?.LogDebug("Patreonステータスが変更されました: {OldStatus} -> {NewStatus}",
                e.PreviousStatus, e.NewStatus);
        });
    }

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _licenseManager.StateChanged -= OnLicenseStateChanged;

            if (_patreonService != null)
            {
                _patreonService.StatusChanged -= OnPatreonStatusChanged;
            }
        }
        base.Dispose(disposing);
    }

    #endregion
}
