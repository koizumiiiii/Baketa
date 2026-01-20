using System;
using System.Diagnostics;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Media;
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
    private readonly IBonusTokenService? _bonusTokenService;
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

    // Issue #280+#281: ボーナストークン関連
    private long _bonusTokensRemaining;

    // Issue #280+#281 Phase 5: UX改善
    private const double LowTokenWarningThreshold = 20.0; // 残り20%で警告
    private bool _isLowTokenWarning;
    private bool _shouldShowUpgradePrompt;

    /// <summary>
    /// LicenseInfoViewModelを初期化します
    /// </summary>
    public LicenseInfoViewModel(
        ILicenseManager licenseManager,
        IPromotionCodeService promotionCodeService,
        IEventAggregator eventAggregator,
        ITokenConsumptionTracker tokenTracker,
        IBonusTokenService? bonusTokenService = null,
        ILogger<LicenseInfoViewModel>? logger = null) : base(eventAggregator)
    {
        _licenseManager = licenseManager ?? throw new ArgumentNullException(nameof(licenseManager));
        _promotionCodeService = promotionCodeService ?? throw new ArgumentNullException(nameof(promotionCodeService));
        _tokenTracker = tokenTracker ?? throw new ArgumentNullException(nameof(tokenTracker));
        _bonusTokenService = bonusTokenService;
        _logger = logger;

        // ライセンス状態変更イベントの購読
        _licenseManager.StateChanged += OnLicenseStateChanged;

        // プロモーション状態変更イベントの購読
        _promotionCodeService.PromotionStateChanged += OnPromotionStateChanged;

        // [Issue #280+#281] ボーナストークン状態変更イベントの購読
        if (_bonusTokenService != null)
        {
            _bonusTokenService.BonusTokensChanged += OnBonusTokensChanged;
            BonusTokensRemaining = _bonusTokenService.GetTotalRemainingTokens();
        }

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
    /// [Issue #296] 使用率に応じたゲージ色
    /// 0-79%: 青（デフォルト）、80-89%: 黄、90-99%: オレンジ、100%+: 赤
    /// </summary>
    public IBrush UsageGaugeBrush => UsagePercentage switch
    {
        >= 100 => new SolidColorBrush(Color.FromRgb(220, 53, 69)),   // Red (#DC3545)
        >= 90 => new SolidColorBrush(Color.FromRgb(253, 126, 20)),   // Orange (#FD7E14)
        >= 80 => new SolidColorBrush(Color.FromRgb(255, 193, 7)),    // Yellow (#FFC107)
        _ => new SolidColorBrush(Color.FromRgb(13, 110, 253))        // Blue (#0D6EFD)
    };

    /// <summary>
    /// クラウドAIアクセス権があるか
    /// </summary>
    public bool HasCloudAccess
    {
        get => _hasCloudAccess;
        private set => this.RaiseAndSetIfChanged(ref _hasCloudAccess, value);
    }

    /// <summary>
    /// [Issue #296] 有料プラン（Pro/Premium/Ultimate）またはボーナストークン保有者
    /// クォータ超過やサブスクリプション状態に関係なく判定
    /// トークン使用量ゲージの表示判定に使用
    /// [Issue #298] ボーナストークン保有者もゲージ表示対象に追加
    /// </summary>
    public bool HasPaidPlan => CurrentPlan is PlanType.Pro or PlanType.Premium or PlanType.Ultimate || HasBonusTokens;

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
    /// [Issue #280+#281] プラン枠がある場合は「使用量 / 上限」、
    /// ボーナストークンのみの場合は「残り X」を表示
    /// </summary>
    public string TokenUsageDisplay
    {
        get
        {
            if (TokenLimit > 0)
            {
                // プラン枠がある場合: "1,234,567 / 4,000,000"
                return $"{TokensUsed:N0} / {TokenLimit:N0}";
            }
            else if (BonusTokensRemaining > 0)
            {
                // ボーナストークンのみの場合: "残り 50,000,000"
                return $"残り {BonusTokensRemaining:N0}";
            }
            else
            {
                return Strings.License_LocalOnly;
            }
        }
    }

    /// <summary>
    /// [Issue #280+#281] ボーナストークン残高
    /// </summary>
    public long BonusTokensRemaining
    {
        get => _bonusTokensRemaining;
        private set => this.RaiseAndSetIfChanged(ref _bonusTokensRemaining, value);
    }

    /// <summary>
    /// [Issue #280+#281] ボーナストークン残高の表示文字列
    /// </summary>
    public string BonusTokensDisplay => BonusTokensRemaining > 0
        ? $"+{BonusTokensRemaining:N0} ボーナス"
        : string.Empty;

    /// <summary>
    /// [Issue #280+#281] ボーナストークンがあるかどうか
    /// </summary>
    public bool HasBonusTokens => BonusTokensRemaining > 0;

    /// <summary>
    /// [Issue #280+#281 Phase 5] トークン残量警告を表示すべきか
    /// 利用可能なトークン全体（プラン枠 + ボーナス）の残り20%以下で警告
    /// </summary>
    public bool IsLowTokenWarning
    {
        get => _isLowTokenWarning;
        private set => this.RaiseAndSetIfChanged(ref _isLowTokenWarning, value);
    }

    /// <summary>
    /// [Issue #280+#281 Phase 5] アップグレード導線を表示すべきか
    /// ボーナストークン枯渇 かつ Freeプランの場合に表示
    /// </summary>
    public bool ShouldShowUpgradePrompt
    {
        get => _shouldShowUpgradePrompt;
        private set => this.RaiseAndSetIfChanged(ref _shouldShowUpgradePrompt, value);
    }

    /// <summary>
    /// [Issue #280+#281 Phase 5] 利用可能なトークン合計（プラン枠残 + ボーナス）
    /// </summary>
    public long TotalAvailableTokens => Math.Max(0, TokenLimit - TokensUsed) + BonusTokensRemaining;

    /// <summary>
    /// [Issue #280+#281 Phase 5] トークン残量の表示文字列
    /// プラン枠とボーナスの内訳を表示
    /// </summary>
    public string TokenBreakdownDisplay
    {
        get
        {
            if (!HasCloudAccess && !HasBonusTokens)
                return Strings.License_LocalOnly;

            var planRemaining = Math.Max(0, TokenLimit - TokensUsed);
            var parts = new System.Collections.Generic.List<string>();

            if (TokenLimit > 0)
            {
                parts.Add($"プラン: {planRemaining:N0} / {TokenLimit:N0}");
            }

            if (BonusTokensRemaining > 0)
            {
                parts.Add($"ボーナス: {BonusTokensRemaining:N0}");
            }

            return parts.Count > 0 ? string.Join(" + ", parts) : Strings.License_LocalOnly;
        }
    }

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
        // [Issue #298] プロモーション適用中の場合はサフィックスを追加（Freeプランでも表示）
        var basePlanName = GetPlanDisplayName(state.CurrentPlan);
        PlanDisplayName = HasActivePromotion
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
        // [Issue #280+#281] プランまたはボーナストークンでCloud AIアクセス可能
        HasCloudAccess = state.CurrentPlan.HasCloudAiAccess() || HasBonusTokens;
        IsQuotaExceeded = state.IsQuotaExceeded;
        ExpirationDate = state.ExpirationDate;

        // 派生プロパティの更新通知
        this.RaisePropertyChanged(nameof(ExpirationDateDisplay));
        this.RaisePropertyChanged(nameof(TokenUsageDisplay));
        this.RaisePropertyChanged(nameof(CloudAccessDisplay));
        this.RaisePropertyChanged(nameof(UsageGaugeBrush));
        this.RaisePropertyChanged(nameof(HasPaidPlan));

        // [Issue #280+#281 Phase 5] トークン残量警告状態を更新
        UpdateTokenWarningState();
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
            // [Issue #283] ブラウザが開くのでステータスメッセージは不要
            StatusMessage = string.Empty;
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

    /// <summary>
    /// [Issue #280+#281 Phase 5] トークン残量警告とアップグレード導線の状態を更新
    /// </summary>
    private void UpdateTokenWarningState()
    {
        // 利用可能なトークン総量を計算
        var planRemaining = Math.Max(0, TokenLimit - TokensUsed);
        var totalAvailable = planRemaining + BonusTokensRemaining;

        // [Gemini Review] 警告判定ロジック修正
        // プラン枠の使用率に基づいて警告を判定（ボーナスは別枠として扱う）
        // - プラン枠がある場合: プラン枠の残量が20%以下で警告
        // - ボーナスのみの場合: ボーナスが存在し、かつ閾値（100万トークン）未満で警告
        const long BonusLowThreshold = 1_000_000; // ボーナス用の絶対値閾値

        if (TokenLimit > 0)
        {
            // プラン枠がある場合: プラン使用率で判定
            var planUsagePercentage = (double)planRemaining / TokenLimit * 100.0;
            IsLowTokenWarning = HasCloudAccess && planUsagePercentage <= LowTokenWarningThreshold;
        }
        else if (BonusTokensRemaining > 0)
        {
            // ボーナスのみの場合: 絶対値で判定
            IsLowTokenWarning = BonusTokensRemaining < BonusLowThreshold;
        }
        else
        {
            IsLowTokenWarning = false;
        }

        // アップグレード導線判定: Freeプラン かつ ボーナス枯渇 かつ プラン枠も枯渇
        ShouldShowUpgradePrompt = CurrentPlan == PlanType.Free
            && BonusTokensRemaining == 0
            && (TokenLimit == 0 || TokensUsed >= TokenLimit);

        // 派生プロパティの更新通知
        this.RaisePropertyChanged(nameof(TotalAvailableTokens));
        this.RaisePropertyChanged(nameof(TokenBreakdownDisplay));
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
                this.RaisePropertyChanged(nameof(UsageGaugeBrush));
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

            // [Issue #298] プラン表示名を再計算（プロモーションサフィックスの更新、Freeプランでも表示）
            var basePlanName = GetPlanDisplayName(CurrentPlan);
            PlanDisplayName = HasActivePromotion
                ? $"{basePlanName} {Strings.License_Plan_PromotionSuffix}"
                : basePlanName;

            this.RaisePropertyChanged(nameof(PromotionExpiresDisplay));
            this.RaisePropertyChanged(nameof(PromotionPlanDisplayName));
            this.RaisePropertyChanged(nameof(PromotionAppliedMessage));
            _logger?.LogDebug("プロモーション状態が変更されました: {Reason}", e.Reason);
        });
    }

    /// <summary>
    /// [Issue #280+#281] ボーナストークン状態変更イベントハンドラ
    /// </summary>
    private void OnBonusTokensChanged(object? sender, BonusTokensChangedEventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            BonusTokensRemaining = e.TotalRemaining;
            this.RaisePropertyChanged(nameof(BonusTokensDisplay));
            this.RaisePropertyChanged(nameof(HasBonusTokens));
            this.RaisePropertyChanged(nameof(TokenUsageDisplay));
            this.RaisePropertyChanged(nameof(HasCloudAccess));
            this.RaisePropertyChanged(nameof(CloudAccessDisplay));

            // [Issue #280+#281 Phase 5] トークン残量警告状態を更新
            UpdateTokenWarningState();

            _logger?.LogDebug("ボーナストークン状態が変更されました: {Reason}, 残高: {Remaining}",
                e.Reason, e.TotalRemaining);
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

            // [Issue #280+#281] ボーナストークンイベントの購読解除
            if (_bonusTokenService != null)
            {
                _bonusTokenService.BonusTokensChanged -= OnBonusTokensChanged;
            }
        }
        base.Dispose(disposing);
    }

    #endregion
}
