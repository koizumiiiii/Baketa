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
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// ライセンス情報設定画面のViewModel
/// プラン情報、トークン使用量、プラン詳細を表示
/// </summary>
public sealed class LicenseInfoViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
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

    /// <summary>
    /// LicenseInfoViewModelを初期化します
    /// </summary>
    public LicenseInfoViewModel(
        ILicenseManager licenseManager,
        IEventAggregator eventAggregator,
        ILogger<LicenseInfoViewModel>? logger = null) : base(eventAggregator)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _logger = logger;

        // ライセンス状態変更イベントの購読
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // コマンドの初期化
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshLicenseStateAsync);
        ChangePlanCommand = ReactiveCommand.Create(OpenPlanChangePage);

        // 初期状態の読み込み
        LoadCurrentState();

        _logger?.LogDebug("LicenseInfoViewModel初期化完了");
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
        const string planChangeUrl = "https://baketa.app/pricing";

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

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _licenseManager.StateChanged -= OnLicenseStateChanged;
        }
        base.Dispose(disposing);
    }

    #endregion
}
