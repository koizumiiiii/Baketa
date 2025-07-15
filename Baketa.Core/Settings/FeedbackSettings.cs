using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// フィードバック収集設定レコード
/// GitHub Issues API連携とプライバシー設定管理
/// </summary>
public sealed record FeedbackSettings
{
    /// <summary>
    /// フィードバック機能の有効性
    /// </summary>
    [Display(Name = "フィードバック機能", Description = "バグ報告・機能要望の送信機能")]
    public bool EnableFeedback { get; init; } = true;

    /// <summary>
    /// バグ報告の有効性
    /// </summary>
    [Display(Name = "バグ報告", Description = "バグ報告の送信機能")]
    public bool EnableBugReports { get; init; } = true;

    /// <summary>
    /// 機能要望の有効性
    /// </summary>
    [Display(Name = "機能要望", Description = "機能要望の送信機能")]
    public bool EnableFeatureRequests { get; init; } = true;

    /// <summary>
    /// 一般フィードバックの有効性
    /// </summary>
    [Display(Name = "一般フィードバック", Description = "その他フィードバックの送信機能")]
    public bool EnableGeneralFeedback { get; init; } = true;

    /// <summary>
    /// システム情報の自動収集
    /// </summary>
    [Display(Name = "システム情報収集", Description = "バグ報告時のシステム情報自動収集")]
    public bool CollectSystemInfo { get; init; } = true;

    /// <summary>
    /// GitHub APIのベースURL
    /// </summary>
    [Display(Name = "GitHub API URL", Description = "GitHub Issues APIのベースURL")]
    [Url(ErrorMessage = "有効なURLを入力してください")]
    public string GitHubApiBaseUrl { get; init; } = "https://api.github.com";

    /// <summary>
    /// リポジトリオーナー名
    /// </summary>
    [Display(Name = "リポジトリオーナー", Description = "GitHubリポジトリのオーナー名")]
    [Required(ErrorMessage = "リポジトリオーナー名は必須です")]
    public string RepositoryOwner { get; init; } = "koizumiiiii";

    /// <summary>
    /// リポジトリ名
    /// </summary>
    [Display(Name = "リポジトリ名", Description = "GitHubリポジトリ名")]
    [Required(ErrorMessage = "リポジトリ名は必須です")]
    public string RepositoryName { get; init; } = "Baketa";

    /// <summary>
    /// GitHub Personal Access Token（暗号化保存）
    /// </summary>
    [Display(Name = "GitHub Token", Description = "GitHub API認証用トークン")]
    public string? GitHubToken { get; init; }

    /// <summary>
    /// HTTPタイムアウト（秒）
    /// </summary>
    [Display(Name = "タイムアウト", Description = "API呼び出しのタイムアウト（秒）")]
    [Range(5, 300, ErrorMessage = "タイムアウトは5秒から300秒（5分）の間で設定してください")]
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// リトライ回数
    /// </summary>
    [Display(Name = "リトライ回数", Description = "API呼び出し失敗時のリトライ回数")]
    [Range(0, 5, ErrorMessage = "リトライ回数は0回から5回の間で設定してください")]
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// リトライ間隔（秒）
    /// </summary>
    [Display(Name = "リトライ間隔", Description = "リトライ間の待機時間（秒）")]
    [Range(1, 60, ErrorMessage = "リトライ間隔は1秒から60秒の間で設定してください")]
    public int RetryDelaySeconds { get; init; } = 5;

    /// <summary>
    /// ユーザーエージェント文字列
    /// </summary>
    [Display(Name = "ユーザーエージェント", Description = "GitHub APIリクエストのUser-Agent")]
    public string UserAgent { get; init; } = "Baketa-FeedbackCollector/1.0";

    /// <summary>
    /// 添付ファイルの最大サイズ（MB）
    /// </summary>
    [Display(Name = "添付ファイル最大サイズ", Description = "添付ファイルの最大サイズ（MB）")]
    [Range(1, 50, ErrorMessage = "添付ファイルサイズは1MBから50MBの間で設定してください")]
    public int MaxAttachmentSizeMb { get; init; } = 10;

    /// <summary>
    /// 許可される添付ファイル形式
    /// </summary>
    [Display(Name = "許可ファイル形式", Description = "アップロード可能なファイル形式")]
    public IReadOnlyList<string> AllowedFileTypes { get; init; } = [
        ".png", ".jpg", ".jpeg", ".gif", ".webp",  // 画像
        ".txt", ".log", ".md",                      // テキスト
        ".json", ".xml", ".csv"                     // データ
    ];

    /// <summary>
    /// レート制限回避の最小間隔（分）
    /// </summary>
    [Display(Name = "送信間隔", Description = "連続送信防止の最小間隔（分）")]
    [Range(1, 60, ErrorMessage = "送信間隔は1分から60分の間で設定してください")]
    public int MinSubmissionIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Discord Webhook URL（オプション）
    /// </summary>
    [Display(Name = "Discord Webhook", Description = "Discord通知用WebhookURL（オプション）")]
    [Url(ErrorMessage = "有効なWebhook URLを入力してください")]
    public string? DiscordWebhookUrl { get; init; }

    /// <summary>
    /// Discord通知の有効性
    /// </summary>
    [Display(Name = "Discord通知", Description = "Discordへの通知送信")]
    public bool EnableDiscordNotifications { get; init; } = false;

    /// <summary>
    /// メトリクス収集の有効性
    /// </summary>
    [Display(Name = "メトリクス収集", Description = "フィードバック送信のメトリクス収集")]
    public bool EnableMetrics { get; init; } = true;

    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    /// <returns>検証結果</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(RepositoryOwner) &&
               !string.IsNullOrWhiteSpace(RepositoryName) &&
               TimeoutSeconds > 0 &&
               MaxAttachmentSizeMb > 0 &&
               MinSubmissionIntervalMinutes > 0;
    }

    /// <summary>
    /// GitHub Issues APIのフルURLを構築
    /// </summary>
    /// <returns>API URL</returns>
    public string GetIssuesApiUrl()
    {
        var baseUrl = GitHubApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/repos/{RepositoryOwner}/{RepositoryName}/issues";
    }

    /// <summary>
    /// 指定したIssue番号のAPIURLを構築
    /// </summary>
    /// <param name="issueNumber">Issue番号</param>
    /// <returns>API URL</returns>
    public string GetIssueApiUrl(int issueNumber)
    {
        var baseUrl = GitHubApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/repos/{RepositoryOwner}/{RepositoryName}/issues/{issueNumber}";
    }

    /// <summary>
    /// GitHub Issue作成用のWebURLを構築
    /// </summary>
    /// <param name="template">テンプレート種別</param>
    /// <returns>Web URL</returns>
    public string GetIssueCreationWebUrl(string? template = null)
    {
        var baseUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}/issues/new";
        return string.IsNullOrEmpty(template) ? baseUrl : $"{baseUrl}?template={template}";
    }

    /// <summary>
    /// αテスト用設定を作成
    /// </summary>
    /// <returns>αテスト設定</returns>
    public static FeedbackSettings CreateAlphaTestSettings() => new()
    {
        EnableFeedback = true,
        EnableBugReports = true,
        EnableFeatureRequests = true,
        EnableGeneralFeedback = true,
        CollectSystemInfo = true,
        MinSubmissionIntervalMinutes = 2, // 短い間隔（αテスト用）
        EnableDiscordNotifications = false, // Discord通知は無効
        UserAgent = "Baketa-AlphaTest-FeedbackCollector/1.0"
    };

    /// <summary>
    /// 本番用設定を作成
    /// </summary>
    /// <returns>本番設定</returns>
    public static FeedbackSettings CreateProductionSettings() => new()
    {
        EnableFeedback = true,
        EnableBugReports = true,
        EnableFeatureRequests = true,
        EnableGeneralFeedback = false, // 本番では一般フィードバック無効
        CollectSystemInfo = true,
        MinSubmissionIntervalMinutes = 10, // 長い間隔（本番用）
        EnableDiscordNotifications = true, // Discord通知有効
        UserAgent = "Baketa-FeedbackCollector/1.0"
    };

    /// <summary>
    /// 開発用設定を作成（ローカル開発・テスト用）
    /// </summary>
    /// <returns>開発設定</returns>
    public static FeedbackSettings CreateDevelopmentSettings() => new()
    {
        EnableFeedback = false, // 開発中は無効
        EnableBugReports = false,
        EnableFeatureRequests = false,
        EnableGeneralFeedback = false,
        CollectSystemInfo = false,
        UserAgent = "Baketa-Development-FeedbackCollector/1.0"
    };
}