using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Settings;

/// <summary>
/// フィーチャーフラグサービスインターフェース
/// 機能の有効/無効を制御する
/// </summary>
public interface IFeatureFlagService
{
    /// <summary>
    /// 指定した機能が有効かどうかを判定
    /// </summary>
    /// <param name="featureName">機能名</param>
    /// <returns>機能が有効な場合true</returns>
    bool IsFeatureEnabled(string featureName);

    /// <summary>
    /// 指定したFeatureFlagSettingsプロパティが有効かどうかを判定
    /// </summary>
    /// <param name="propertyName">FeatureFlagSettingsのプロパティ名</param>
    /// <returns>機能が有効な場合true</returns>
    bool IsPropertyEnabled(string propertyName);

    /// <summary>
    /// 認証機能が有効かどうか
    /// </summary>
    bool IsAuthenticationEnabled { get; }

    /// <summary>
    /// クラウド翻訳が有効かどうか
    /// </summary>
    bool IsCloudTranslationEnabled { get; }

    /// <summary>
    /// 高度なUI機能が有効かどうか
    /// </summary>
    bool IsAdvancedUIEnabled { get; }

    /// <summary>
    /// 中国語OCRが有効かどうか
    /// </summary>
    bool IsChineseOCREnabled { get; }

    /// <summary>
    /// 使用統計収集が有効かどうか
    /// </summary>
    bool IsUsageStatisticsEnabled { get; }

    /// <summary>
    /// デバッグ機能が有効かどうか
    /// </summary>
    bool IsDebugFeaturesEnabled { get; }

    /// <summary>
    /// 自動更新が有効かどうか
    /// </summary>
    bool IsAutoUpdateEnabled { get; }

    /// <summary>
    /// フィードバック機能が有効かどうか
    /// </summary>
    bool IsFeedbackEnabled { get; }

    /// <summary>
    /// 試験的機能が有効かどうか
    /// </summary>
    bool IsExperimentalFeaturesEnabled { get; }

    /// <summary>
    /// パフォーマンス監視が有効かどうか
    /// </summary>
    bool IsPerformanceMonitoringEnabled { get; }

    /// <summary>
    /// 現在のフィーチャーフラグ設定を取得
    /// </summary>
    /// <returns>フィーチャーフラグ設定</returns>
    FeatureFlagSettings GetCurrentSettings();

    /// <summary>
    /// フィーチャーフラグ設定変更イベント
    /// </summary>
    event EventHandler<FeatureFlagChangedEventArgs>? FeatureFlagChanged;
}

/// <summary>
/// フィーチャーフラグ変更イベント引数
/// </summary>
public sealed class FeatureFlagChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更されたフィーチャーフラグ設定
    /// </summary>
    public required FeatureFlagSettings NewSettings { get; init; }

    /// <summary>
    /// 変更前のフィーチャーフラグ設定
    /// </summary>
    public required FeatureFlagSettings PreviousSettings { get; init; }

    /// <summary>
    /// 変更されたプロパティ名リスト
    /// </summary>
    public required IReadOnlyList<string> ChangedProperties { get; init; }
}