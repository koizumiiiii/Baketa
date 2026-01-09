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
using Baketa.Core.Translation.Abstractions;
using Baketa.UI.Framework;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels.Settings;

/// <summary>
/// ライセンス情報設定画面のViewModel
/// プラン情報、トークン使用量、プラン詳細を表示
/// </summary>
/// <remarks>
/// Patreon連携機能はAccountSettingsViewModelに集約されています。
/// </remarks>
public sealed class LicenseInfoViewModel : ViewModelBase
{
    private readonly ILicenseManager _licenseManager;
    private readonly IPromotionCodeService _promotionCodeService;
    private readonly ITokenConsumptionTracker _tokenTracker;
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

    // Issue #237 Phase 2: プロモーションコード関連
    private string _promotionCode = string.Empty;
    private bool _isApplyingPromotion;
    private string _promotionStatusMessage = string.Empty;
    private bool _isPromotionError;
    private bool _hasActivePromotion;
    private PromotionInfo? _activePromotion;

    /// <summary>
    /// LicenseInfoViewModelを初期化します
    /// </summary>
    public LicenseInfoViewModel(
        ILicenseManager licenseManager,
        IPromotionCodeService promotionCodeService,
        IEventAggregator eventAggregator,
        ITokenConsumptionTracker tokenTracker,
        ILogger<LicenseInfoViewModel>? logger = null) : base(eventAggregator)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _promotionCodeService = promotionCodeService ?? throw new ArgumentNullException(nameof(promotionCodeService));
        _tokenTracker = tokenTracker ?? throw new ArgumentNullException(nameof(tokenTracker));
        _logger = logger;

        // ライセンス状態変更イベントの購読
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // プロモーション状態変更イベントの購読
        _promotionCodeService.PromotionStateChanged += OnPromotionStateChanged;

        // コマンドの初期化
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshLicenseStateAsync);
        ChangePlanCommand = ReactiveCommand.Create(OpenPlanChangePage);

        // Issue #237 Phase 2: プロモーションコード適用コマンド
        var canApplyPromotion = this.WhenAnyValue(
            x => x.PromotionCode,
            x => x.IsApplyingPromotion,
            (code, applying) => !string.IsNullOrWhiteSpace(code) && !applying);
        ApplyPromotionCommand = ReactiveCommand.CreateFromTask(ApplyPromotionCodeAsync, canApplyPromotion);

        // 初期状態の読み込み
        // [Issue #275] プロモーション状態を先に読み込む（PlanDisplayNameのサフィックス表示に必要）
        LoadPromotionState();
        LoadCurrentState();

        // [Issue #275再発防止] 起動時にTokenUsageRepositoryからトークン使用量を読み込む
        _ = LoadTokenUsageFromRepositoryAsync();

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

    #region Issue #237 Phase 2: プロモーションコード関連プロパティ

    /// <summary>
    /// 入力されたプロモーションコード
    /// </summary>
    public string PromotionCode
    {
        get => _promotionCode;
        set => this.RaiseAndSetIfChanged(ref _promotionCode, value);
    }

    /// <summary>
    /// プロモーションコード適用中かどうか
    /// </summary>
    public bool IsApplyingPromotion
    {
        get => _isApplyingPromotion;
        private set => this.RaiseAndSetIfChanged(ref _isApplyingPromotion, value);
    }

    /// <summary>
    /// プロモーションステータスメッセージ
    /// </summary>
    public string PromotionStatusMessage
    {
        get => _promotionStatusMessage;
        private set => this.RaiseAndSetIfChanged(ref _promotionStatusMessage, value);
    }

    /// <summary>
    /// プロモーションステータスがエラーかどうか
    /// </summary>
    public bool IsPromotionError
    {
        get => _isPromotionError;
        private set => this.RaiseAndSetIfChanged(ref _isPromotionError, value);
    }

    /// <summary>
    /// アクティブなプロモーションがあるかどうか
    /// </summary>
    public bool HasActivePromotion
    {
        get => _hasActivePromotion;
        private set => this.RaiseAndSetIfChanged(ref _hasActivePromotion, value);
    }

    /// <summary>
    /// アクティブなプロモーションの有効期限表示
    /// </summary>
    public string PromotionExpiresDisplay
    {
        get
        {
            var promotion = _promotionCodeService.GetCurrentPromotion();
            if (promotion == null || !promotion.IsValid)
                return string.Empty;
            return promotion.ExpiresAt.ToString("yyyy/MM/dd");
        }
    }

    /// <summary>
    /// アクティブなプロモーションのコード表示（マスク済み）
    /// </summary>
    public string PromotionCodeDisplay
    {
        get
        {
            var promotion = _promotionCodeService.GetCurrentPromotion();
            if (promotion == null || !promotion.IsValid)
                return string.Empty;

            var code = promotion.Code;

            // 既にマスクされている場合はそのまま返す
            if (code.Contains('*'))
                return code;

            // 下4桁以外をマスク
            // 例: BAKETA-15D12788 → ************2788
            // 例: BAKETA-VIP1-ABCD → ************ABCD
            if (code.Length > 4)
            {
                var lastFour = code[^4..];
                var maskLength = code.Length - 4;
                return new string('*', maskLength) + lastFour;
            }

            // 4文字以下の場合はそのまま（通常ありえない）
            return code;
        }
    }

    /// <summary>
    /// アクティブなプロモーションのプラン表示名
    /// </summary>
    public string PromotionPlanDisplayName
    {
        get
        {
            if (_activePromotion == null || !_activePromotion.IsValid)
                return string.Empty;
            // Issue #125: Standardプラン廃止
            // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
            return _activePromotion.Plan switch
            {
                PlanType.Ultimate => Strings.License_Plan_Ultimate,
                PlanType.Premium => Strings.License_Plan_Premium,
                PlanType.Pro => Strings.License_Plan_Pro,
                _ => Strings.Settings_License_PromotionPlanPromo
            };
        }
    }

    /// <summary>
    /// プロモーション適用メッセージ（UI表示用）
    /// </summary>
    public string PromotionAppliedMessage
    {
        get
        {
            var planName = PromotionPlanDisplayName;
            if (string.IsNullOrEmpty(planName))
                planName = Strings.Settings_License_PromotionPlanPromo;
#pragma warning disable CA1863 // UI表示用メッセージ生成は低頻度のため最適化不要
            return string.Format(Strings.Settings_License_PromotionAppliedFormat, planName);
#pragma warning restore CA1863
        }
    }

    #endregion

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
    /// Issue #237 Phase 2: プロモーションコード適用コマンド
    /// </summary>
    public ReactiveCommand<Unit, Unit> ApplyPromotionCommand { get; }

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
        // プロモーション適用中の場合はサフィックスを追加
        var basePlanName = GetPlanDisplayName(state.CurrentPlan);
        PlanDisplayName = HasActivePromotion && state.CurrentPlan != PlanType.Free
            ? $"{basePlanName} {Strings.License_Plan_PromotionSuffix}"
            : basePlanName;
        PlanDescription = GetPlanDescription(state.CurrentPlan);

        // [Issue #275再発防止] トークン使用量は2つのデータソースがある:
        // 1. LicenseState.CloudAiTokensUsed - サーバー同期値（起動時は0の可能性）
        // 2. TokenUsageRepository - ローカル永続化値
        // LicenseStateの値が0で、ローカルに保存された値がある場合は上書きしない
        if (state.CloudAiTokensUsed > 0 || TokensUsed == 0)
        {
            TokensUsed = state.CloudAiTokensUsed;
        }

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
    /// プラン変更ページ（Patreon）を外部ブラウザで開きます
    /// </summary>
    private void OpenPlanChangePage()
    {
        const string planChangeUrl = "https://patreon.com/baketa_translation";

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
    // Issue #125: Standardプラン廃止
    // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
    private static string GetPlanDisplayName(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => Strings.License_Plan_Free,
            PlanType.Pro => Strings.License_Plan_Pro,
            PlanType.Premium => Strings.License_Plan_Premium,
            PlanType.Ultimate => Strings.License_Plan_Ultimate,
            _ => Strings.License_Plan_Unknown
        };
    }

    /// <summary>
    /// プラン説明を取得
    /// </summary>
    // Issue #125: Standardプラン廃止
    // Issue #257: Pro/Premium/Ultimate 3段階構成に改定
    private static string GetPlanDescription(PlanType planType)
    {
        return planType switch
        {
            PlanType.Free => Strings.License_Desc_Free,
            PlanType.Pro => Strings.License_Desc_Pro,
            PlanType.Premium => Strings.License_Desc_Premium,
            PlanType.Ultimate => Strings.License_Desc_Ultimate,
            _ => string.Empty
        };
    }

    #region Issue #237 Phase 2: プロモーションコード関連メソッド

    /// <summary>
    /// プロモーション状態を読み込みます
    /// </summary>
    private void LoadPromotionState()
    {
        var promotion = _promotionCodeService.GetCurrentPromotion();
        _activePromotion = promotion;
        HasActivePromotion = promotion?.IsValid == true;

        if (HasActivePromotion)
        {
            this.RaisePropertyChanged(nameof(PromotionExpiresDisplay));
            this.RaisePropertyChanged(nameof(PromotionPlanDisplayName));
            this.RaisePropertyChanged(nameof(PromotionAppliedMessage));
        }
    }

    /// <summary>
    /// [Issue #275再発防止] TokenUsageRepositoryからトークン使用量を読み込む
    /// LicenseState.CloudAiTokensUsedとは別データソースのため、大きい方の値を採用
    /// </summary>
    private async System.Threading.Tasks.Task LoadTokenUsageFromRepositoryAsync()
    {
        try
        {
            var usage = await _tokenTracker.GetMonthlyUsageAsync().ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // 2つのデータソース（LicenseState vs Repository）から大きい方を採用
                // - LicenseState.CloudAiTokensUsed: モック設定 / サーバー同期値
                // - TokenUsageRepository: 実際の翻訳使用量記録
                var currentValue = TokensUsed;
                var repositoryValue = usage.TotalTokensUsed;

                if (repositoryValue > currentValue)
                {
                    TokensUsed = repositoryValue;
                    // [Issue #275] LicenseManagerの内部状態も同期（他のViewModelでも正しい値を表示するため）
                    _licenseManager.SyncTokenUsage(repositoryValue);
                    _logger?.LogDebug(
                        "トークン使用量をリポジトリから更新: {Current} → {Repository}",
                        currentValue, repositoryValue);
                }
                else
                {
                    // 現在の値を維持するが、LicenseManagerにも同期（初回起動時の0対策）
                    if (currentValue > 0)
                    {
                        _licenseManager.SyncTokenUsage(currentValue);
                    }
                    _logger?.LogDebug(
                        "トークン使用量はLicenseState値を維持: LicenseState={Current}, Repository={Repository}",
                        currentValue, repositoryValue);
                }

                // [Issue #275] TokenLimitも大きい方を採用（プロモーション適用済みの場合を保護）
                // LoadCurrentState()で設定されたLicenseState.MonthlyTokenLimitが正しい値
                // usage.MonthlyLimitはGetCurrentPlanAsync経由で取得するが、タイミングによりFreeプランを返す場合がある
                var currentLimit = TokenLimit;
                var repositoryLimit = usage.MonthlyLimit;
                if (repositoryLimit > currentLimit)
                {
                    TokenLimit = repositoryLimit;
                    _logger?.LogDebug(
                        "TokenLimitをリポジトリから更新: {Current} → {Repository}",
                        currentLimit, repositoryLimit);
                }
                else if (currentLimit > repositoryLimit && repositoryLimit == 0)
                {
                    _logger?.LogDebug(
                        "TokenLimitはLicenseState値を維持（リポジトリが0）: LicenseState={Current}, Repository={Repository}",
                        currentLimit, repositoryLimit);
                }
                UsagePercentage = TokenLimit > 0 ? (double)TokensUsed / TokenLimit * 100 : 0;
                this.RaisePropertyChanged(nameof(TokenUsageDisplay));
            });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "トークン使用量の読み込みに失敗しました");
        }
    }

    /// <summary>
    /// プロモーションコードを適用します
    /// </summary>
    private async System.Threading.Tasks.Task ApplyPromotionCodeAsync()
    {
        if (string.IsNullOrWhiteSpace(PromotionCode))
            return;

        try
        {
            IsApplyingPromotion = true;
            PromotionStatusMessage = string.Empty;
            IsPromotionError = false;

            var result = await _promotionCodeService.ApplyCodeAsync(PromotionCode).ConfigureAwait(false);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PromotionStatusMessage = result.Message;
                IsPromotionError = !result.Success;

                if (result.Success)
                {
                    PromotionCode = string.Empty;
                    HasActivePromotion = true;
                    // _activePromotionはOnPromotionStateChangedで設定される
                    this.RaisePropertyChanged(nameof(PromotionExpiresDisplay));
                    this.RaisePropertyChanged(nameof(PromotionPlanDisplayName));
                    this.RaisePropertyChanged(nameof(PromotionAppliedMessage));
                    _logger?.LogInformation("プロモーションコード適用成功: Plan={Plan}", result.AppliedPlan);
                }
                else
                {
                    _logger?.LogWarning("プロモーションコード適用失敗: {ErrorCode}", result.ErrorCode);
                }
            });
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "プロモーションコード適用中にエラーが発生しました");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                PromotionStatusMessage = "予期しないエラーが発生しました。";
                IsPromotionError = true;
            });
        }
        finally
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsApplyingPromotion = false);
        }
    }

    /// <summary>
    /// プロモーション状態変更イベントハンドラ
    /// </summary>
    private void OnPromotionStateChanged(object? sender, PromotionStateChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            _activePromotion = e.NewPromotion;
            HasActivePromotion = e.NewPromotion?.IsValid == true;

            // [Issue #275] プラン表示名を再計算（プロモーションサフィックスの更新）
            var basePlanName = GetPlanDisplayName(CurrentPlan);
            PlanDisplayName = HasActivePromotion && CurrentPlan != PlanType.Free
                ? $"{basePlanName} {Strings.License_Plan_PromotionSuffix}"
                : basePlanName;

            this.RaisePropertyChanged(nameof(PromotionExpiresDisplay));
            this.RaisePropertyChanged(nameof(PromotionPlanDisplayName));
            this.RaisePropertyChanged(nameof(PromotionAppliedMessage));
            _logger?.LogDebug("プロモーション状態が変更されました: {Reason}", e.Reason);
        });
    }

    #endregion

    /// <summary>
    /// リソースを解放します
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _licenseManager.StateChanged -= OnLicenseStateChanged;
            _promotionCodeService.PromotionStateChanged -= OnPromotionStateChanged;
        }
        base.Dispose(disposing);
    }

    #endregion
}
