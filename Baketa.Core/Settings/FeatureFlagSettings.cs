using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// フィーチャーフラグ設定クラス
/// αテスト・βテスト・本番での機能制御を行う
/// </summary>
public sealed class FeatureFlagSettings
{
    /// <summary>
    /// 認証・アカウント機能の有効性
    /// </summary>
    [Display(Name = "認証システム", Description = "ユーザー登録・ログイン機能")]
    public bool EnableAuthenticationFeatures { get; init; } = false;

    /// <summary>
    /// クラウド翻訳サービスの有効性
    /// </summary>
    [Display(Name = "クラウド翻訳", Description = "Gemini等のクラウド翻訳エンジン")]
    public bool EnableCloudTranslation { get; init; } = false;

    /// <summary>
    /// 高度なUI機能の有効性
    /// </summary>
    [Display(Name = "高度UI", Description = "プロファイル管理・詳細設定等")]
    public bool EnableAdvancedUIFeatures { get; init; } = false;

    /// <summary>
    /// OCR中国語対応の有効性
    /// </summary>
    [Display(Name = "中国語OCR", Description = "繁体字・簡体字OCR対応")]
    public bool EnableChineseOCR { get; init; } = false;

    /// <summary>
    /// 使用統計収集の有効性
    /// </summary>
    [Display(Name = "使用統計", Description = "匿名使用統計の収集")]
    public bool EnableUsageStatistics { get; init; } = false;

    /// <summary>
    /// デバッグ機能の有効性
    /// </summary>
    [Display(Name = "デバッグ機能", Description = "開発者向けデバッグ情報")]
    public bool EnableDebugFeatures { get; init; } = false;

    /// <summary>
    /// 自動更新機能の有効性
    /// </summary>
    [Display(Name = "自動更新", Description = "アプリケーション自動更新")]
    public bool EnableAutoUpdate { get; init; } = true;

    /// <summary>
    /// フィードバック機能の有効性
    /// </summary>
    [Display(Name = "フィードバック", Description = "バグレポート・要望送信")]
    public bool EnableFeedbackFeatures { get; init; } = true;

    /// <summary>
    /// 試験的機能の有効性
    /// </summary>
    [Display(Name = "試験的機能", Description = "ベータ版機能・実験的機能")]
    public bool EnableExperimentalFeatures { get; init; } = false;

    /// <summary>
    /// パフォーマンス監視の有効性
    /// </summary>
    [Display(Name = "パフォーマンス監視", Description = "処理時間・リソース使用量監視")]
    public bool EnablePerformanceMonitoring { get; init; } = true;
}
