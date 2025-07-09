using System.ComponentModel;
using System.ComponentModel.DataAnnotations;

namespace Baketa.Core.Settings;

/// <summary>
/// 更新チェック設定レコード
/// GitHub Releases API連携の設定管理
/// </summary>
public sealed record UpdateCheckSettings
{
    /// <summary>
    /// 自動更新チェックの有効性
    /// </summary>
    [Display(Name = "自動更新チェック", Description = "バックグラウンドでの定期的な更新チェック")]
    public bool EnableAutoCheck { get; init; } = true;

    /// <summary>
    /// 更新チェックの間隔（時間）
    /// </summary>
    [Display(Name = "チェック間隔", Description = "自動更新チェックの実行間隔（時間）")]
    [Range(1, 168, ErrorMessage = "チェック間隔は1時間から168時間（1週間）の間で設定してください")]
    public int CheckIntervalHours { get; init; } = 24;

    /// <summary>
    /// プレリリース版を含めるかどうか
    /// </summary>
    [Display(Name = "プレリリース版", Description = "αテスト・βテスト版を更新対象に含める")]
    public bool IncludePrerelease { get; init; } = false;

    /// <summary>
    /// 更新通知の表示
    /// </summary>
    [Display(Name = "更新通知", Description = "利用可能な更新の通知表示")]
    public bool ShowUpdateNotifications { get; init; } = true;

    /// <summary>
    /// GitHub APIのベースURL
    /// </summary>
    [Display(Name = "GitHub API URL", Description = "GitHub Releases APIのベースURL")]
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
    /// HTTPタイムアウト（秒）
    /// </summary>
    [Display(Name = "タイムアウト", Description = "API呼び出しのタイムアウト（秒）")]
    [Range(5, 300, ErrorMessage = "タイムアウトは5秒から300秒（5分）の間で設定してください")]
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// キャッシュ有効期間（分）
    /// </summary>
    [Display(Name = "キャッシュ期間", Description = "更新情報のキャッシュ保持期間（分）")]
    [Range(5, 1440, ErrorMessage = "キャッシュ期間は5分から1440分（24時間）の間で設定してください")]
    public int CacheExpiryMinutes { get; init; } = 60;

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
    /// オフライン時のフォールバック動作
    /// </summary>
    [Display(Name = "オフライン時動作", Description = "ネットワーク接続不可時の動作")]
    public OfflineBehavior OfflineBehavior { get; init; } = OfflineBehavior.UseCache;

    /// <summary>
    /// ユーザーエージェント文字列
    /// </summary>
    [Display(Name = "ユーザーエージェント", Description = "GitHub APIリクエストのUser-Agent")]
    public string UserAgent { get; init; } = "Baketa-UpdateChecker/1.0";

    /// <summary>
    /// メトリクス収集の有効性
    /// </summary>
    [Display(Name = "メトリクス収集", Description = "更新チェックのメトリクス収集")]
    public bool EnableMetrics { get; init; } = true;

    /// <summary>
    /// 最小更新間隔（時間）- スパム防止
    /// </summary>
    [Display(Name = "最小間隔", Description = "連続更新チェックの最小間隔（時間）")]
    [Range(0.1, 24, ErrorMessage = "最小間隔は6分（0.1時間）から24時間の間で設定してください")]
    public double MinimumCheckIntervalHours { get; init; } = 0.5; // 30分

    /// <summary>
    /// 設定の妥当性を検証
    /// </summary>
    /// <returns>検証結果</returns>
    public bool IsValid()
    {
        return !string.IsNullOrWhiteSpace(RepositoryOwner) &&
               !string.IsNullOrWhiteSpace(RepositoryName) &&
               CheckIntervalHours >= MinimumCheckIntervalHours &&
               TimeoutSeconds > 0 &&
               CacheExpiryMinutes > 0;
    }

    /// <summary>
    /// GitHub Releases APIのフルURLを構築
    /// </summary>
    /// <returns>API URL</returns>
    public string GetReleasesApiUrl()
    {
        var baseUrl = GitHubApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/repos/{RepositoryOwner}/{RepositoryName}/releases";
    }

    /// <summary>
    /// 指定したリリースタグのAPIURLを構築
    /// </summary>
    /// <param name="tag">リリースタグ</param>
    /// <returns>API URL</returns>
    public string GetReleaseByTagApiUrl(string tag)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tag);
        var baseUrl = GitHubApiBaseUrl.TrimEnd('/');
        return $"{baseUrl}/repos/{RepositoryOwner}/{RepositoryName}/releases/tags/{tag}";
    }

    /// <summary>
    /// αテスト用設定を作成
    /// </summary>
    /// <returns>αテスト設定</returns>
    public static UpdateCheckSettings CreateAlphaTestSettings() => new()
    {
        IncludePrerelease = true,
        CheckIntervalHours = 6, // 6時間間隔（αテストでは頻繁に）
        ShowUpdateNotifications = true,
        EnableAutoCheck = true,
        CacheExpiryMinutes = 30, // 短いキャッシュ期間
        UserAgent = "Baketa-AlphaTest-UpdateChecker/1.0"
    };

    /// <summary>
    /// 本番用設定を作成
    /// </summary>
    /// <returns>本番設定</returns>
    public static UpdateCheckSettings CreateProductionSettings() => new()
    {
        IncludePrerelease = false,
        CheckIntervalHours = 24, // 日次チェック
        ShowUpdateNotifications = true,
        EnableAutoCheck = true,
        CacheExpiryMinutes = 120, // 2時間キャッシュ
        UserAgent = "Baketa-UpdateChecker/1.0"
    };
}

/// <summary>
/// オフライン時のフォールバック動作
/// </summary>
public enum OfflineBehavior
{
    /// <summary>
    /// キャッシュを使用
    /// </summary>
    [Description("キャッシュ使用")]
    UseCache,

    /// <summary>
    /// エラーとして扱う
    /// </summary>
    [Description("エラー扱い")]
    FailWithError,

    /// <summary>
    /// 静かに失敗（ログのみ）
    /// </summary>
    [Description("静かに失敗")]
    SilentFail,

    /// <summary>
    /// 後で再試行
    /// </summary>
    [Description("後で再試行")]
    RetryLater
}