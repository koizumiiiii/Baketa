using Baketa.Core.Settings;

namespace Baketa.Core.Abstractions.Privacy;

/// <summary>
/// プライバシー同意管理サービスインターフェース
/// GDPR準拠のデータ収集同意管理を提供
/// </summary>
public interface IPrivacyConsentService
{
    /// <summary>
    /// フィードバック送信に対する同意状態
    /// </summary>
    bool HasFeedbackConsent { get; }

    /// <summary>
    /// 使用統計収集に対する同意状態
    /// </summary>
    bool HasUsageStatisticsConsent { get; }

    /// <summary>
    /// クラッシュレポート送信に対する同意状態
    /// </summary>
    bool HasCrashReportConsent { get; }

    /// <summary>
    /// パフォーマンス監視に対する同意状態
    /// </summary>
    bool HasPerformanceMonitoringConsent { get; }

    /// <summary>
    /// 指定したデータ収集タイプに対する同意を確認
    /// </summary>
    /// <param name="dataType">データ収集タイプ</param>
    /// <returns>同意している場合true</returns>
    bool HasConsentFor(DataCollectionType dataType);

    /// <summary>
    /// 指定したデータ収集タイプに対する同意を設定
    /// </summary>
    /// <param name="dataType">データ収集タイプ</param>
    /// <param name="consent">同意状態</param>
    /// <returns>同意設定タスク</returns>
    Task SetConsentAsync(DataCollectionType dataType, bool consent);

    /// <summary>
    /// 複数のデータ収集タイプに対する同意を一括設定
    /// </summary>
    /// <param name="consents">同意設定辞書</param>
    /// <returns>同意設定タスク</returns>
    Task SetConsentsAsync(Dictionary<DataCollectionType, bool> consents);

    /// <summary>
    /// 全ての同意を撤回
    /// </summary>
    /// <returns>同意撤回タスク</returns>
    Task RevokeAllConsentsAsync();

    /// <summary>
    /// 現在の同意設定を取得
    /// </summary>
    /// <returns>同意設定</returns>
    PrivacyConsentSettings GetCurrentConsents();

    /// <summary>
    /// 同意設定が変更されたときのイベント
    /// </summary>
    event EventHandler<ConsentChangedEventArgs>? ConsentChanged;

    /// <summary>
    /// 指定したデータ収集が可能かどうかを確認
    /// (フィーチャーフラグと同意の両方をチェック)
    /// </summary>
    /// <param name="dataType">データ収集タイプ</param>
    /// <returns>データ収集可能な場合true</returns>
    bool CanCollectData(DataCollectionType dataType);

    /// <summary>
    /// データ収集実行前の最終確認
    /// </summary>
    /// <param name="dataType">データ収集タイプ</param>
    /// <param name="dataPreview">収集予定データのプレビュー</param>
    /// <returns>実行可能な場合true</returns>
    Task<bool> ConfirmDataCollectionAsync(DataCollectionType dataType, string dataPreview);
}

/// <summary>
/// データ収集タイプ
/// </summary>
public enum DataCollectionType
{
    /// <summary>
    /// フィードバック・バグレポート
    /// </summary>
    Feedback,

    /// <summary>
    /// 使用統計・分析データ
    /// </summary>
    UsageStatistics,

    /// <summary>
    /// クラッシュレポート・エラーログ
    /// </summary>
    CrashReport,

    /// <summary>
    /// パフォーマンス監視データ
    /// </summary>
    PerformanceMonitoring,

    /// <summary>
    /// ユーザー設定・環境情報
    /// </summary>
    SystemInformation
}

/// <summary>
/// 同意変更イベント引数
/// </summary>
public sealed class ConsentChangedEventArgs : EventArgs
{
    /// <summary>
    /// 変更されたデータ収集タイプ
    /// </summary>
    public required DataCollectionType DataType { get; init; }

    /// <summary>
    /// 新しい同意状態
    /// </summary>
    public required bool NewConsent { get; init; }

    /// <summary>
    /// 変更前の同意状態
    /// </summary>
    public required bool PreviousConsent { get; init; }

    /// <summary>
    /// 変更日時
    /// </summary>
    public required DateTime Timestamp { get; init; }

    /// <summary>
    /// 変更理由・コンテキスト
    /// </summary>
    public string? Reason { get; init; }
}
