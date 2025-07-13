using System.Reflection;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Baketa.Core.Services;

/// <summary>
/// フィーチャーフラグサービス実装
/// 設定に基づいて機能の有効/無効を制御
/// </summary>
public sealed class FeatureFlagService : IFeatureFlagService
{
    private readonly IOptionsMonitor<FeatureFlagSettings> _optionsMonitor;
    private readonly ILogger<FeatureFlagService> _logger;
    private FeatureFlagSettings _currentSettings;

    public FeatureFlagService(
        IOptionsMonitor<FeatureFlagSettings> optionsMonitor,
        ILogger<FeatureFlagService> logger)
    {
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        _currentSettings = _optionsMonitor.CurrentValue;
        
        // 設定変更の監視
        _optionsMonitor.OnChange((settings, _) => OnSettingsChanged(settings));
        
        _logger.LogInformation("FeatureFlagService初期化完了");
        LogCurrentFeatureFlags();
    }

    /// <inheritdoc />
    public bool IsFeatureEnabled(string featureName)
    {
        if (string.IsNullOrWhiteSpace(featureName))
            return false;

        return featureName.ToLowerInvariant() switch
        {
            "authentication" or "auth" => IsAuthenticationEnabled,
            "cloudtranslation" or "cloud" => IsCloudTranslationEnabled,
            "advancedui" or "advanced" => IsAdvancedUIEnabled,
            "chineseocr" or "chinese" => IsChineseOCREnabled,
            "usagestatistics" or "statistics" => IsUsageStatisticsEnabled,
            "debug" or "debugging" => IsDebugFeaturesEnabled,
            "autoupdate" or "update" => IsAutoUpdateEnabled,
            "feedback" => IsFeedbackEnabled,
            "experimental" => IsExperimentalFeaturesEnabled,
            "performance" or "monitoring" => IsPerformanceMonitoringEnabled,
            _ => false
        };
    }

    /// <inheritdoc />
    public bool IsPropertyEnabled(string propertyName)
    {
        if (string.IsNullOrWhiteSpace(propertyName))
            return false;

        var property = typeof(FeatureFlagSettings).GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (property?.PropertyType == typeof(bool))
        {
            return (bool)(property.GetValue(_currentSettings) ?? false);
        }

        _logger.LogWarning("プロパティ '{PropertyName}' が見つからないか、bool型ではありません", propertyName);
        return false;
    }

    /// <inheritdoc />
    public bool IsAuthenticationEnabled => _currentSettings.EnableAuthenticationFeatures;

    /// <inheritdoc />
    public bool IsCloudTranslationEnabled => _currentSettings.EnableCloudTranslation;

    /// <inheritdoc />
    public bool IsAdvancedUIEnabled => _currentSettings.EnableAdvancedUIFeatures;

    /// <inheritdoc />
    public bool IsChineseOCREnabled => _currentSettings.EnableChineseOCR;

    /// <inheritdoc />
    public bool IsUsageStatisticsEnabled => _currentSettings.EnableUsageStatistics;

    /// <inheritdoc />
    public bool IsDebugFeaturesEnabled => _currentSettings.EnableDebugFeatures;

    /// <inheritdoc />
    public bool IsAutoUpdateEnabled => _currentSettings.EnableAutoUpdate;

    /// <inheritdoc />
    public bool IsFeedbackEnabled => _currentSettings.EnableFeedbackFeatures;

    /// <inheritdoc />
    public bool IsExperimentalFeaturesEnabled => _currentSettings.EnableExperimentalFeatures;

    /// <inheritdoc />
    public bool IsPerformanceMonitoringEnabled => _currentSettings.EnablePerformanceMonitoring;

    /// <inheritdoc />
    public FeatureFlagSettings GetCurrentSettings() => _currentSettings;

    /// <inheritdoc />
    public event EventHandler<FeatureFlagChangedEventArgs>? FeatureFlagChanged;

    private void OnSettingsChanged(FeatureFlagSettings newSettings)
    {
        var previousSettings = _currentSettings;
        var changedProperties = GetChangedProperties(previousSettings, newSettings);
        
        _currentSettings = newSettings;
        
        if (changedProperties.Count > 0)
        {
            _logger.LogInformation("フィーチャーフラグ設定が変更されました: {ChangedProperties}", 
                string.Join(", ", changedProperties));
            
            LogCurrentFeatureFlags();
            
            var eventArgs = new FeatureFlagChangedEventArgs
            {
                NewSettings = newSettings,
                PreviousSettings = previousSettings,
                ChangedProperties = changedProperties
            };
            
            FeatureFlagChanged?.Invoke(this, eventArgs);
        }
    }

    private static List<string> GetChangedProperties(FeatureFlagSettings previous, FeatureFlagSettings current)
    {
        var changedProperties = new List<string>();
        
        var properties = typeof(FeatureFlagSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool));
        
        foreach (var property in properties)
        {
            var previousValue = (bool)(property.GetValue(previous) ?? false);
            var currentValue = (bool)(property.GetValue(current) ?? false);
            
            if (previousValue != currentValue)
            {
                changedProperties.Add(property.Name);
            }
        }
        
        return changedProperties;
    }

    private void LogCurrentFeatureFlags()
    {
        _logger.LogDebug("現在のフィーチャーフラグ状態:");
        _logger.LogDebug("  認証システム: {AuthEnabled}", IsAuthenticationEnabled);
        _logger.LogDebug("  クラウド翻訳: {CloudEnabled}", IsCloudTranslationEnabled);
        _logger.LogDebug("  高度UI: {AdvancedUIEnabled}", IsAdvancedUIEnabled);
        _logger.LogDebug("  中国語OCR: {ChineseOCREnabled}", IsChineseOCREnabled);
        _logger.LogDebug("  使用統計: {StatsEnabled}", IsUsageStatisticsEnabled);
        _logger.LogDebug("  デバッグ機能: {DebugEnabled}", IsDebugFeaturesEnabled);
        _logger.LogDebug("  自動更新: {UpdateEnabled}", IsAutoUpdateEnabled);
        _logger.LogDebug("  フィードバック: {FeedbackEnabled}", IsFeedbackEnabled);
        _logger.LogDebug("  試験的機能: {ExperimentalEnabled}", IsExperimentalFeaturesEnabled);
        _logger.LogDebug("  パフォーマンス監視: {PerfEnabled}", IsPerformanceMonitoringEnabled);
    }
}