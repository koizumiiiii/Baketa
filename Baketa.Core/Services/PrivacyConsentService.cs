using System.Reflection;
using Baketa.Core.Abstractions.Privacy;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Core.Services;

/// <summary>
/// プライバシー同意管理サービス実装
/// GDPR準拠のデータ収集同意管理
/// </summary>
public sealed class PrivacyConsentService : IPrivacyConsentService
{
    private readonly IOptionsMonitor<PrivacyConsentSettings> _optionsMonitor;
    private readonly IFeatureFlagService _featureFlagService;
    private readonly ILogger<PrivacyConsentService> _logger;
    private PrivacyConsentSettings _currentSettings;

    public PrivacyConsentService(
        IOptionsMonitor<PrivacyConsentSettings> optionsMonitor,
        IFeatureFlagService featureFlagService,
        ILogger<PrivacyConsentService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _featureFlagService = featureFlagService ?? throw new ArgumentNullException(nameof(featureFlagService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _currentSettings = _optionsMonitor.CurrentValue;

        // 設定変更の監視
        _optionsMonitor.OnChange(OnSettingsChanged);

        _logger.LogInformation("PrivacyConsentService初期化完了");
        LogCurrentConsentStatus();
    }

    /// <inheritdoc />
    public bool HasFeedbackConsent => _currentSettings.FeedbackConsent && _currentSettings.IsConsentValid;

    /// <inheritdoc />
    public bool HasUsageStatisticsConsent => _currentSettings.UsageStatisticsConsent && _currentSettings.IsConsentValid;

    /// <inheritdoc />
    public bool HasCrashReportConsent => _currentSettings.CrashReportConsent && _currentSettings.IsConsentValid;

    /// <inheritdoc />
    public bool HasPerformanceMonitoringConsent => _currentSettings.PerformanceMonitoringConsent && _currentSettings.IsConsentValid;

    /// <inheritdoc />
    public bool HasConsentFor(DataCollectionType dataType)
    {
        if (!_currentSettings.IsConsentValid)
        {
            _logger.LogWarning("同意設定の有効期限が切れています。再同意が必要です。");
            return false;
        }

        return dataType switch
        {
            DataCollectionType.Feedback => HasFeedbackConsent,
            DataCollectionType.UsageStatistics => HasUsageStatisticsConsent,
            DataCollectionType.CrashReport => HasCrashReportConsent,
            DataCollectionType.PerformanceMonitoring => HasPerformanceMonitoringConsent,
            DataCollectionType.SystemInformation => _currentSettings.SystemInformationConsent,
            _ => false
        };
    }

    /// <inheritdoc />
    public async Task SetConsentAsync(DataCollectionType dataType, bool consent)
    {
        var consents = new Dictionary<DataCollectionType, bool> { { dataType, consent } };
        await SetConsentsAsync(consents).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SetConsentsAsync(Dictionary<DataCollectionType, bool> consents)
    {
        ArgumentNullException.ThrowIfNull(consents);

        var newSettings = _currentSettings.Copy();

        foreach (var (dataType, consent) in consents)
        {
            var previousConsent = HasConsentFor(dataType);

            newSettings = dataType switch
            {
                DataCollectionType.Feedback => newSettings.WithConsents(feedbackConsent: consent),
                DataCollectionType.UsageStatistics => newSettings.WithConsents(usageStatisticsConsent: consent),
                DataCollectionType.CrashReport => newSettings.WithConsents(crashReportConsent: consent),
                DataCollectionType.PerformanceMonitoring => newSettings.WithConsents(performanceMonitoringConsent: consent),
                DataCollectionType.SystemInformation => newSettings.WithConsents(systemInformationConsent: consent),
                _ => newSettings
            };

            // 同意変更イベントを発行
            OnConsentChanged(new ConsentChangedEventArgs
            {
                DataType = dataType,
                NewConsent = consent,
                PreviousConsent = previousConsent,
                Timestamp = DateTime.UtcNow,
                Reason = "User consent update"
            });

            _logger.LogInformation("同意設定を変更しました: {DataType} = {Consent}", dataType, consent);
        }

        _currentSettings = newSettings;
        LogCurrentConsentStatus();

        // TODO: 設定の永続化（ファイル保存など）
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task RevokeAllConsentsAsync()
    {
        _logger.LogInformation("全ての同意を撤回します");

        var revokedSettings = _currentSettings.WithAllConsentsRevoked();
        var dataTypes = Enum.GetValues<DataCollectionType>();

        foreach (var dataType in dataTypes)
        {
            var previousConsent = HasConsentFor(dataType);
            if (previousConsent)
            {
                OnConsentChanged(new ConsentChangedEventArgs
                {
                    DataType = dataType,
                    NewConsent = false,
                    PreviousConsent = true,
                    Timestamp = DateTime.UtcNow,
                    Reason = "All consents revoked"
                });
            }
        }

        _currentSettings = revokedSettings;
        LogCurrentConsentStatus();

        // TODO: 設定の永続化
        await Task.CompletedTask.ConfigureAwait(false);
    }

    /// <inheritdoc />
    public PrivacyConsentSettings GetCurrentConsents() => _currentSettings;

    /// <inheritdoc />
    public event EventHandler<ConsentChangedEventArgs>? ConsentChanged;

    /// <inheritdoc />
    public bool CanCollectData(DataCollectionType dataType)
    {
        // フィーチャーフラグでデータ収集が無効化されている場合
        var featureEnabled = dataType switch
        {
            DataCollectionType.Feedback => _featureFlagService.IsFeedbackEnabled,
            DataCollectionType.UsageStatistics => _featureFlagService.IsUsageStatisticsEnabled,
            DataCollectionType.PerformanceMonitoring => _featureFlagService.IsPerformanceMonitoringEnabled,
            DataCollectionType.CrashReport => true, // クラッシュレポートは常に可能
            DataCollectionType.SystemInformation => true, // システム情報は常に可能
            _ => false
        };

        if (!featureEnabled)
        {
            _logger.LogDebug("データ収集タイプ {DataType} はフィーチャーフラグで無効化されています", dataType);
            return false;
        }

        // ユーザー同意チェック
        var hasConsent = HasConsentFor(dataType);
        if (!hasConsent)
        {
            _logger.LogDebug("データ収集タイプ {DataType} にユーザー同意がありません", dataType);
        }

        return hasConsent;
    }

    /// <inheritdoc />
    public async Task<bool> ConfirmDataCollectionAsync(DataCollectionType dataType, string dataPreview)
    {
        ArgumentNullException.ThrowIfNull(dataPreview);

        // 基本的な収集可能性チェック
        if (!CanCollectData(dataType))
        {
            _logger.LogWarning("データ収集タイプ {DataType} は収集不可です", dataType);
            return false;
        }

        // データプレビューのログ出力（デバッグ用）
        _logger.LogDebug("データ収集確認: {DataType}\nプレビュー: {Preview}", dataType, dataPreview);

        // 実際のUIでの確認は上位レイヤーで実装
        // ここでは基本的な検証のみ
        if (string.IsNullOrWhiteSpace(dataPreview))
        {
            _logger.LogWarning("収集予定データが空です");
            return false;
        }

        await Task.CompletedTask.ConfigureAwait(false);
        return true;
    }

    private void OnSettingsChanged(PrivacyConsentSettings newSettings)
    {
        var previousSettings = _currentSettings;
        _currentSettings = newSettings;

        _logger.LogInformation("プライバシー同意設定が外部から変更されました");
        LogCurrentConsentStatus();

        // 個別の変更をチェックしてイベント発行
        CheckAndFireConsentChanges(previousSettings, newSettings);
    }

    private void CheckAndFireConsentChanges(PrivacyConsentSettings previous, PrivacyConsentSettings current)
    {
        var dataTypes = Enum.GetValues<DataCollectionType>();

        foreach (var dataType in dataTypes)
        {
            var previousConsent = GetConsentFromSettings(previous, dataType);
            var currentConsent = GetConsentFromSettings(current, dataType);

            if (previousConsent != currentConsent)
            {
                OnConsentChanged(new ConsentChangedEventArgs
                {
                    DataType = dataType,
                    NewConsent = currentConsent,
                    PreviousConsent = previousConsent,
                    Timestamp = DateTime.UtcNow,
                    Reason = "External settings change"
                });
            }
        }
    }

    private static bool GetConsentFromSettings(PrivacyConsentSettings settings, DataCollectionType dataType)
    {
        return dataType switch
        {
            DataCollectionType.Feedback => settings.FeedbackConsent,
            DataCollectionType.UsageStatistics => settings.UsageStatisticsConsent,
            DataCollectionType.CrashReport => settings.CrashReportConsent,
            DataCollectionType.PerformanceMonitoring => settings.PerformanceMonitoringConsent,
            DataCollectionType.SystemInformation => settings.SystemInformationConsent,
            _ => false
        };
    }

    private void OnConsentChanged(ConsentChangedEventArgs eventArgs)
    {
        ConsentChanged?.Invoke(this, eventArgs);
    }

    private void LogCurrentConsentStatus()
    {
        _logger.LogDebug("現在のプライバシー同意状況:");
        _logger.LogDebug("  フィードバック: {FeedbackConsent}", HasFeedbackConsent);
        _logger.LogDebug("  使用統計: {UsageStatsConsent}", HasUsageStatisticsConsent);
        _logger.LogDebug("  クラッシュレポート: {CrashReportConsent}", HasCrashReportConsent);
        _logger.LogDebug("  パフォーマンス監視: {PerfMonitoringConsent}", HasPerformanceMonitoringConsent);
        _logger.LogDebug("  システム情報: {SystemInfoConsent}", _currentSettings.SystemInformationConsent);
        _logger.LogDebug("  有効期限内: {IsValid}", _currentSettings.IsConsentValid);
        _logger.LogDebug("  最終更新: {LastUpdated}", _currentSettings.LastUpdated);
    }
}