using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// プライバシー同意設定レコード
/// GDPR準拠のデータ収集同意状態を管理
/// </summary>
public sealed record PrivacyConsentSettings
{
    /// <summary>
    /// フィードバック送信同意
    /// </summary>
    [Display(Name = "フィードバック送信", Description = "バグレポート・要望の送信")]
    public bool FeedbackConsent { get; init; } = false;

    /// <summary>
    /// 使用統計収集同意
    /// </summary>
    [Display(Name = "使用統計収集", Description = "匿名化された使用状況データの収集")]
    public bool UsageStatisticsConsent { get; init; } = false;

    /// <summary>
    /// クラッシュレポート送信同意
    /// </summary>
    [Display(Name = "クラッシュレポート", Description = "アプリケーション異常終了時の情報送信")]
    public bool CrashReportConsent { get; init; } = false;

    /// <summary>
    /// パフォーマンス監視同意
    /// </summary>
    [Display(Name = "パフォーマンス監視", Description = "処理時間・リソース使用量の監視")]
    public bool PerformanceMonitoringConsent { get; init; } = false;

    /// <summary>
    /// システム情報収集同意
    /// </summary>
    [Display(Name = "システム情報", Description = "OS・ハードウェア構成情報の収集")]
    public bool SystemInformationConsent { get; init; } = false;

    /// <summary>
    /// 同意日時
    /// </summary>
    public DateTime? ConsentDate { get; init; }

    /// <summary>
    /// 同意取得時のプライバシーポリシーバージョン
    /// </summary>
    public string? PrivacyPolicyVersion { get; init; }

    /// <summary>
    /// 最終更新日時
    /// </summary>
    public DateTime LastUpdated { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 同意が有効期限内かどうか（GDPR要件：定期的な再同意）
    /// </summary>
    public bool IsConsentValid => ConsentDate.HasValue && 
                                  ConsentDate.Value.AddYears(2) > DateTime.UtcNow;

    /// <summary>
    /// 何らかのデータ収集に同意しているかどうか
    /// </summary>
    public bool HasAnyConsent => FeedbackConsent || 
                                UsageStatisticsConsent || 
                                CrashReportConsent || 
                                PerformanceMonitoringConsent || 
                                SystemInformationConsent;

    /// <summary>
    /// 同意設定のコピーを作成（更新用）
    /// </summary>
    /// <returns>新しい設定インスタンス</returns>
    public PrivacyConsentSettings Copy() => this;

    /// <summary>
    /// 指定した同意を変更した新しい設定を作成
    /// </summary>
    /// <param name="feedbackConsent">フィードバック同意</param>
    /// <param name="usageStatisticsConsent">使用統計同意</param>
    /// <param name="crashReportConsent">クラッシュレポート同意</param>
    /// <param name="performanceMonitoringConsent">パフォーマンス監視同意</param>
    /// <param name="systemInformationConsent">システム情報同意</param>
    /// <returns>更新された設定</returns>
    public PrivacyConsentSettings WithConsents(
        bool? feedbackConsent = null,
        bool? usageStatisticsConsent = null,
        bool? crashReportConsent = null,
        bool? performanceMonitoringConsent = null,
        bool? systemInformationConsent = null) => this with
    {
        FeedbackConsent = feedbackConsent ?? FeedbackConsent,
        UsageStatisticsConsent = usageStatisticsConsent ?? UsageStatisticsConsent,
        CrashReportConsent = crashReportConsent ?? CrashReportConsent,
        PerformanceMonitoringConsent = performanceMonitoringConsent ?? PerformanceMonitoringConsent,
        SystemInformationConsent = systemInformationConsent ?? SystemInformationConsent,
        LastUpdated = DateTime.UtcNow,
        ConsentDate = DateTime.UtcNow
    };

    /// <summary>
    /// 全ての同意を撤回した新しい設定を作成
    /// </summary>
    /// <returns>全同意撤回済み設定</returns>
    public PrivacyConsentSettings WithAllConsentsRevoked() => this with
    {
        FeedbackConsent = false,
        UsageStatisticsConsent = false,
        CrashReportConsent = false,
        PerformanceMonitoringConsent = false,
        SystemInformationConsent = false,
        LastUpdated = DateTime.UtcNow
    };
}