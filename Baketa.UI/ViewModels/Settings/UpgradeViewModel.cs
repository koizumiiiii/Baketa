using System.Diagnostics;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Payment;
using Baketa.Core.License.Events;
using Baketa.Core.License.Extensions;
using Baketa.Core.License.Models;
using Baketa.Core.Payment.Models;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// プランアップグレード画面のViewModel
/// プラン選択、決済開始、サブスクリプション管理を提供
/// </summary>
public sealed class UpgradeViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
    private readonly IPaymentService _paymentService;
    private readonly ILogger<UpgradeViewModel>? _logger;

    private PlanType _currentPlan;
    private PlanType _selectedPlan;
    private BillingCycle _selectedCycle = BillingCycle.Monthly;
    private bool _isProcessing;
    private string? _statusMessage;
    private bool _isStatusError;
    private SubscriptionInfo? _subscriptionInfo;

    /// <summary>
    /// UpgradeViewModelを初期化します
    /// </summary>
    public UpgradeViewModel(
        ILicenseManager licenseManager,
        IPaymentService paymentService,
        IEventAggregator eventAggregator,
        ILogger<UpgradeViewModel>? logger = null) : base(eventAggregator, logger)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _paymentService = paymentService ?? throw new ArgumentNullException(nameof(paymentService));
        _logger = logger;

        // 現在のプランを設定
        CurrentPlan = _licenseManager.CurrentState.CurrentPlan;
        SelectedPlan = GetDefaultUpgradePlan(CurrentPlan);

        // ライセンス状態変更イベントの購読
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // コマンドの初期化
        var canPurchase = this.WhenAnyValue(
            x => x.SelectedPlan,
            x => x.CurrentPlan,
            x => x.IsProcessing,
            (selected, current, processing) =>
                selected > current &&
                selected != PlanType.Free &&
                !processing);

        PurchaseCommand = ReactiveCommand.CreateFromTask(PurchaseAsync, canPurchase);
        ManageSubscriptionCommand = ReactiveCommand.CreateFromTask(OpenCustomerPortalAsync);
        RefreshLicenseCommand = ReactiveCommand.CreateFromTask(RefreshLicenseAsync);
        CancelSubscriptionCommand = ReactiveCommand.CreateFromTask(CancelSubscriptionAsync);

        // エラーハンドリング（サブスクリプションをDisposablesに追加してメモリリーク防止）
        PurchaseCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                StatusMessage = $"エラー: {ex.Message}";
                IsStatusError = true;
                _logger?.LogError(ex, "購入処理エラー");
            })
            .DisposeWith(Disposables);

        ManageSubscriptionCommand.ThrownExceptions
            .Subscribe(ex => _logger?.LogError(ex, "サブスクリプション管理エラー"))
            .DisposeWith(Disposables);

        RefreshLicenseCommand.ThrownExceptions
            .Subscribe(ex => _logger?.LogError(ex, "ライセンス更新エラー"))
            .DisposeWith(Disposables);

        CancelSubscriptionCommand.ThrownExceptions
            .Subscribe(ex => _logger?.LogError(ex, "キャンセルエラー"))
            .DisposeWith(Disposables);

        // 初期ロード
        _ = LoadSubscriptionInfoAsync();

        _logger?.LogDebug("UpgradeViewModel初期化完了");
    }

    #region プロパティ

    /// <summary>
    /// 現在のプラン
    /// </summary>
    public PlanType CurrentPlan
    {
        get => _currentPlan;
        private set => this.RaiseAndSetIfChanged(ref _currentPlan, value);
    }

    /// <summary>
    /// 現在のプラン表示名
    /// </summary>
    public string CurrentPlanDisplayName => CurrentPlan.GetDisplayName();

    /// <summary>
    /// 選択中のプラン
    /// </summary>
    public PlanType SelectedPlan
    {
        get => _selectedPlan;
        set => this.RaiseAndSetIfChanged(ref _selectedPlan, value);
    }

    /// <summary>
    /// 選択中の課金サイクル
    /// </summary>
    public BillingCycle SelectedCycle
    {
        get => _selectedCycle;
        set => this.RaiseAndSetIfChanged(ref _selectedCycle, value);
    }

    /// <summary>
    /// 月額課金が選択されているか
    /// </summary>
    public bool IsMonthlySelected
    {
        get => SelectedCycle == BillingCycle.Monthly;
        set
        {
            if (value) SelectedCycle = BillingCycle.Monthly;
        }
    }

    /// <summary>
    /// 年額課金が選択されているか
    /// </summary>
    public bool IsYearlySelected
    {
        get => SelectedCycle == BillingCycle.Yearly;
        set
        {
            if (value) SelectedCycle = BillingCycle.Yearly;
        }
    }

    /// <summary>
    /// 処理中フラグ
    /// </summary>
    public bool IsProcessing
    {
        get => _isProcessing;
        private set => this.RaiseAndSetIfChanged(ref _isProcessing, value);
    }

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
    /// サブスクリプション情報
    /// </summary>
    public SubscriptionInfo? SubscriptionInfo
    {
        get => _subscriptionInfo;
        private set => this.RaiseAndSetIfChanged(ref _subscriptionInfo, value);
    }

    /// <summary>
    /// サブスクリプションがアクティブかどうか
    /// </summary>
    public bool HasActiveSubscription => SubscriptionInfo?.IsActive == true;

    /// <summary>
    /// キャンセル予約されているかどうか
    /// </summary>
    public bool IsCancellationPending => SubscriptionInfo?.IsCancellationPending == true;

    /// <summary>
    /// 選択プランの月額料金（円）
    /// </summary>
    public int SelectedPlanMonthlyPrice => SelectedPlan.GetMonthlyPriceYen();

    /// <summary>
    /// 選択プランの年額料金（円）
    /// </summary>
    public int SelectedPlanYearlyPrice => SelectedPlan.GetMonthlyPriceYen() * 12 * 80 / 100; // 20%OFF

    /// <summary>
    /// 選択された課金サイクルの料金表示
    /// </summary>
    public string SelectedPriceDisplay => SelectedCycle switch
    {
        BillingCycle.Monthly => $"¥{SelectedPlanMonthlyPrice:N0}/月",
        BillingCycle.Yearly => $"¥{SelectedPlanYearlyPrice:N0}/年（20%OFF）",
        _ => string.Empty
    };

    /// <summary>
    /// 利用可能なプラン一覧
    /// </summary>
    public IReadOnlyList<PlanInfo> AvailablePlans { get; } = CreatePlanInfoList();

    #endregion

    #region コマンド

    /// <summary>
    /// 購入コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> PurchaseCommand { get; }

    /// <summary>
    /// サブスクリプション管理コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ManageSubscriptionCommand { get; }

    /// <summary>
    /// ライセンス更新コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> RefreshLicenseCommand { get; }

    /// <summary>
    /// サブスクリプションキャンセルコマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> CancelSubscriptionCommand { get; }

    #endregion

    #region メソッド

    /// <summary>
    /// 購入処理を実行
    /// </summary>
    private async Task PurchaseAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = null;
            IsStatusError = false;

            var userId = _licenseManager.CurrentState.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                StatusMessage = "ログインが必要です";
                IsStatusError = true;
                return;
            }

            _logger?.LogInformation(
                "購入処理開始: Plan={Plan}, Cycle={Cycle}",
                SelectedPlan, SelectedCycle);

            var result = await _paymentService.CreateCheckoutSessionAsync(
                userId, SelectedPlan, SelectedCycle).ConfigureAwait(false);

            if (!result.Success || result.Data == null)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = result.ErrorMessage ?? "決済セッションの作成に失敗しました";
                    IsStatusError = true;
                });
                return;
            }

            // ブラウザでチェックアウトページを開く
            Process.Start(new ProcessStartInfo(result.Data.CheckoutUrl)
            {
                UseShellExecute = true
            });

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "決済ページを開きました。完了後、「ライセンス情報を更新」ボタンで反映できます。";
                IsStatusError = false;
            });

            _logger?.LogInformation("決済ページを開きました: SessionId={SessionId}", result.Data.SessionId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "購入処理エラー");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"エラー: {ex.Message}";
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsProcessing = false);
        }
    }

    /// <summary>
    /// カスタマーポータルを開く
    /// </summary>
    private async Task OpenCustomerPortalAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = null;
            IsStatusError = false;

            var userId = _licenseManager.CurrentState.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                // フォールバック: 通常のポータルURL
                Process.Start(new ProcessStartInfo("https://baketa.onfastspring.com/account")
                {
                    UseShellExecute = true
                });
                return;
            }

            var result = await _paymentService.GetSecurePortalUrlAsync(userId)
                .ConfigureAwait(false);

            var url = result.Success && result.Data != null
                ? result.Data.Url
                : "https://baketa.onfastspring.com/account";

            Process.Start(new ProcessStartInfo(url)
            {
                UseShellExecute = true
            });

            _logger?.LogInformation("カスタマーポータルを開きました");
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "カスタマーポータルエラー、フォールバック使用");
            Process.Start(new ProcessStartInfo("https://baketa.onfastspring.com/account")
            {
                UseShellExecute = true
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsProcessing = false);
        }
    }

    /// <summary>
    /// ライセンス状態を更新
    /// </summary>
    private async Task RefreshLicenseAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = "ライセンス情報を確認中...";
            IsStatusError = false;

            var state = await _licenseManager.ForceRefreshAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                CurrentPlan = state.CurrentPlan;
                this.RaisePropertyChanged(nameof(CurrentPlanDisplayName));
                StatusMessage = $"ライセンス情報を更新しました: {state.CurrentPlan.GetDisplayName()}";
                IsStatusError = false;
            });

            // サブスクリプション情報も更新
            await LoadSubscriptionInfoAsync().ConfigureAwait(false);

            _logger?.LogInformation("ライセンス更新成功: Plan={Plan}", state.CurrentPlan);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "ライセンス更新エラー");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"更新に失敗しました: {ex.Message}";
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsProcessing = false);
        }
    }

    /// <summary>
    /// サブスクリプションをキャンセル
    /// </summary>
    private async Task CancelSubscriptionAsync()
    {
        try
        {
            IsProcessing = true;
            StatusMessage = "キャンセル処理中...";
            IsStatusError = false;

            var userId = _licenseManager.CurrentState.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                StatusMessage = "ログインが必要です";
                IsStatusError = true;
                return;
            }

            var result = await _paymentService.CancelSubscriptionAsync(userId)
                .ConfigureAwait(false);

            if (!result.Success)
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    StatusMessage = result.ErrorMessage ?? "キャンセルに失敗しました";
                    IsStatusError = true;
                });
                return;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = "サブスクリプションのキャンセルを予約しました。現在の期間終了後に無料プランに移行します。";
                IsStatusError = false;
            });

            // 情報を更新
            await LoadSubscriptionInfoAsync().ConfigureAwait(false);

            _logger?.LogInformation("サブスクリプションキャンセル予約完了");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "キャンセル処理エラー");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                StatusMessage = $"エラー: {ex.Message}";
                IsStatusError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsProcessing = false);
        }
    }

    /// <summary>
    /// サブスクリプション情報を読み込み
    /// </summary>
    private async Task LoadSubscriptionInfoAsync()
    {
        try
        {
            var userId = _licenseManager.CurrentState.UserId;
            if (string.IsNullOrEmpty(userId))
            {
                SubscriptionInfo = null;
                return;
            }

            var result = await _paymentService.GetSubscriptionAsync(userId)
                .ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                SubscriptionInfo = result.Data;
                this.RaisePropertyChanged(nameof(HasActiveSubscription));
                this.RaisePropertyChanged(nameof(IsCancellationPending));
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "サブスクリプション情報取得エラー");
        }
    }

    /// <summary>
    /// ライセンス状態変更イベントハンドラ
    /// </summary>
    private void OnLicenseStateChanged(object? sender, LicenseStateChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            CurrentPlan = e.NewState.CurrentPlan;
            this.RaisePropertyChanged(nameof(CurrentPlanDisplayName));
            SelectedPlan = GetDefaultUpgradePlan(CurrentPlan);

            _logger?.LogDebug("ライセンス状態変更: {OldPlan} -> {NewPlan}",
                e.OldState.CurrentPlan, e.NewState.CurrentPlan);
        });
    }

    /// <summary>
    /// デフォルトのアップグレード先プランを取得
    /// </summary>
    private static PlanType GetDefaultUpgradePlan(PlanType currentPlan) => currentPlan switch
    {
        PlanType.Free => PlanType.Standard,
        PlanType.Standard => PlanType.Pro,
        PlanType.Pro => PlanType.Premia,
        PlanType.Premia => PlanType.Premia,
        _ => PlanType.Standard
    };

    /// <summary>
    /// プラン情報リストを作成
    /// </summary>
    private static IReadOnlyList<PlanInfo> CreatePlanInfoList()
    {
        return
        [
            new PlanInfo(PlanType.Standard, "スタンダード", 100, "広告なし、ローカル翻訳"),
            new PlanInfo(PlanType.Pro, "プロ", 300, "クラウドAI翻訳 400万トークン/月"),
            new PlanInfo(PlanType.Premia, "プレミア", 500, "クラウドAI翻訳 800万トークン/月、優先サポート")
        ];
    }

    /// <summary>
    /// リソースを解放
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

/// <summary>
/// プラン情報
/// </summary>
public sealed record PlanInfo(
    PlanType PlanType,
    string DisplayName,
    int MonthlyPriceYen,
    string Description)
{
    /// <summary>
    /// 年額料金（20%OFF）
    /// </summary>
    public int YearlyPriceYen => MonthlyPriceYen * 12 * 80 / 100;

    /// <summary>
    /// 月額料金表示
    /// </summary>
    public string MonthlyPriceDisplay => $"¥{MonthlyPriceYen:N0}/月";

    /// <summary>
    /// 年額料金表示
    /// </summary>
    public string YearlyPriceDisplay => $"¥{YearlyPriceYen:N0}/年";
}
