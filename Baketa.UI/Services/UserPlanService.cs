using System;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.License;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Events;
using Baketa.Core.License.Events;
using Baketa.Core.License.Models;
using Baketa.UI.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.UI.Services;

/// <summary>
/// ユーザープラン判定サービスの実装
/// </summary>
/// <remarks>
/// Issue #243: LicenseManagerのプラン状態と連携
/// </remarks>
public class UserPlanService : IUserPlanService, IDisposable
{
    private readonly ILogger<UserPlanService> _logger;
    private readonly TranslationUIOptions _options;
    private readonly IEventAggregator _eventAggregator;
    private readonly IUnifiedSettingsService _settingsService;
    private readonly IEventProcessor<LicenseStateChangedEvent> _licenseEventProcessor;
    private bool _disposed;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="logger">ロガー</param>
    /// <param name="options">UI設定オプション</param>
    /// <param name="eventAggregator">イベントアグリゲーター</param>
    /// <param name="licenseManager">ライセンスマネージャー</param>
    /// <param name="settingsService">統一設定サービス</param>
    public UserPlanService(
        ILogger<UserPlanService> logger,
        IOptions<TranslationUIOptions> options,
        IEventAggregator eventAggregator,
        ILicenseManager licenseManager,
        IUnifiedSettingsService settingsService)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(eventAggregator);
        ArgumentNullException.ThrowIfNull(licenseManager);
        ArgumentNullException.ThrowIfNull(settingsService);

        _logger = logger;
        _options = options.Value;
        _eventAggregator = eventAggregator;
        _settingsService = settingsService;

        // Issue #243: LicenseManagerの現在の状態から初期プランを設定
        var licensePlan = licenseManager.CurrentState.CurrentPlan;
        CurrentPlan = ConvertToPlanType(licensePlan);

        // Issue #243: LicenseStateChangedEventを購読
        _licenseEventProcessor = new InlineEventProcessor<LicenseStateChangedEvent>(OnLicenseStateChangedAsync);
        _eventAggregator.Subscribe(_licenseEventProcessor);

        _logger.LogInformation(
            "UserPlanService initialized with plan: {Plan} (from LicensePlan: {LicensePlan})",
            CurrentPlan,
            licensePlan);
    }

    /// <inheritdoc />
    public UserPlanType CurrentPlan { get; private set; }

    /// <inheritdoc />
    public bool CanUseCloudOnlyEngine => CurrentPlan == UserPlanType.Premium;

    /// <inheritdoc />
    public bool IsMonthlyLimitExceeded => MonthlyUsageCount >= MonthlyLimit;

    /// <inheritdoc />
    public int MonthlyUsageCount
    {
        get
        {
            // 現在は固定値を返す（将来的には実際の使用量を追跡）
            return CurrentPlan == UserPlanType.Free ? 150 : 0;
        }
    }

    /// <inheritdoc />
    public int MonthlyLimit
    {
        get
        {
            return CurrentPlan switch
            {
                UserPlanType.Free => 500,
                UserPlanType.Premium => int.MaxValue,
                _ => 0
            };
        }
    }

    /// <inheritdoc />
    public UserPlanDetails GetPlanDetails()
    {
        return CurrentPlan switch
        {
            UserPlanType.Free => new UserPlanDetails(
                UserPlanType.Free,
                "無料プラン",
                "LocalOnlyエンジンのみ利用可能。月500回まで翻訳可能。",
                500,
                false,
                null),

            UserPlanType.Premium => new UserPlanDetails(
                UserPlanType.Premium,
                "プレミアムプラン",
                "LocalOnly・CloudOnlyエンジン両方利用可能。無制限翻訳。",
                int.MaxValue,
                true,
                DateTime.UtcNow.AddMonths(1)),

            _ => throw new InvalidOperationException($"Unknown plan type: {CurrentPlan}")
        };
    }

    /// <inheritdoc />
    public event EventHandler<UserPlanChangedEventArgs>? PlanChanged;

    /// <summary>
    /// プランを変更する（テスト・管理用）
    /// </summary>
    /// <param name="newPlan">新しいプランタイプ</param>
    public void ChangePlan(UserPlanType newPlan)
    {
        if (CurrentPlan == newPlan)
            return;

        var oldPlan = CurrentPlan;
        CurrentPlan = newPlan;

        _logger.LogInformation("Plan changed from {OldPlan} to {NewPlan}", oldPlan, newPlan);

        PlanChanged?.Invoke(this, new UserPlanChangedEventArgs(oldPlan, newPlan));
    }

    /// <summary>
    /// プレミアムプランをシミュレート（開発・テスト用）
    /// </summary>
    public void SimulatePremiumPlan()
    {
        ChangePlan(UserPlanType.Premium);
    }

    /// <summary>
    /// 無料プランをシミュレート（開発・テスト用）
    /// </summary>
    public void SimulateFreePlan()
    {
        ChangePlan(UserPlanType.Free);
    }

    #region Issue #243: License連携

    /// <summary>
    /// LicenseStateChangedイベントハンドラ
    /// </summary>
    private async Task OnLicenseStateChangedAsync(LicenseStateChangedEvent evt)
    {
        if (evt?.NewState == null)
        {
            _logger.LogWarning("LicenseStateChangedEvent received with null state");
            return;
        }

        var oldPlan = CurrentPlan;
        var newPlan = ConvertToPlanType(evt.NewState.CurrentPlan);

        if (oldPlan != newPlan)
        {
            _logger.LogInformation(
                "🔄 License plan changed: {OldPlan} -> {NewPlan} (Reason: {Reason})",
                oldPlan,
                newPlan,
                evt.Reason);

            ChangePlan(newPlan);

            // Issue #243: プランアップグレード時にCloud AI翻訳を自動有効化
            if (oldPlan == UserPlanType.Free && newPlan == UserPlanType.Premium)
            {
                await EnableCloudAiTranslationAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Cloud AI翻訳を有効化
    /// </summary>
    private async Task EnableCloudAiTranslationAsync()
    {
        try
        {
            var currentSettings = _settingsService.GetTranslationSettings();

            // 既に有効な場合はスキップ
            if (currentSettings.EnableCloudAiTranslation)
            {
                _logger.LogDebug("Cloud AI翻訳は既に有効です");
                return;
            }

            // 新しい設定を作成（EnableCloudAiTranslation = true）
            var newSettings = new CloudAiEnabledTranslationSettings(currentSettings);
            await _settingsService.UpdateTranslationSettingsAsync(newSettings).ConfigureAwait(false);

            _logger.LogInformation("🎉 Cloud AI翻訳を自動で有効化しました（プランアップグレード）");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cloud AI翻訳の自動有効化に失敗しました");
        }
    }

    /// <summary>
    /// Cloud AI翻訳を有効化した設定ラッパー
    /// </summary>
    private sealed class CloudAiEnabledTranslationSettings : ITranslationSettings
    {
        private readonly ITranslationSettings _baseSettings;

        public CloudAiEnabledTranslationSettings(ITranslationSettings baseSettings)
        {
            _baseSettings = baseSettings;
        }

        public bool AutoDetectSourceLanguage => _baseSettings.AutoDetectSourceLanguage;
        public string DefaultSourceLanguage => _baseSettings.DefaultSourceLanguage;
        public string DefaultTargetLanguage => _baseSettings.DefaultTargetLanguage;
        public string DefaultEngine => _baseSettings.DefaultEngine;
        public bool UseLocalEngine => false; // Cloud AI使用時はfalse
        public double ConfidenceThreshold => _baseSettings.ConfidenceThreshold;
        public int TimeoutMs => _baseSettings.TimeoutMs;
        public int OverlayFontSize => _baseSettings.OverlayFontSize;
        public bool EnableCloudAiTranslation => true; // 有効化
    }

    /// <summary>
    /// PlanType（License）をUserPlanType（UI）に変換
    /// </summary>
    private static UserPlanType ConvertToPlanType(PlanType licensePlan)
    {
        // ProまたはStandard以上はPremium扱い（CloudOnly利用可能）
        return licensePlan switch
        {
            PlanType.Pro => UserPlanType.Premium,
            PlanType.Standard => UserPlanType.Premium,
            _ => UserPlanType.Free
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _eventAggregator.Unsubscribe(_licenseEventProcessor);
        _logger.LogDebug("UserPlanService disposed");
    }

    #endregion

    /// <summary>
    /// インラインイベントプロセッサ
    /// </summary>
    private sealed class InlineEventProcessor<TEvent> : IEventProcessor<TEvent>
        where TEvent : IEvent
    {
        private readonly Func<TEvent, Task> _handler;

        public InlineEventProcessor(Func<TEvent, Task> handler)
        {
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public int Priority => 100;
        public bool SynchronousExecution => false;
        public Task HandleAsync(TEvent eventData) => _handler(eventData);
    }
}
