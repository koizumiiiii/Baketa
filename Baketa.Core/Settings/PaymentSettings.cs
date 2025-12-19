namespace Baketa.Core.Settings;

/// <summary>
/// 決済システムの設定
/// </summary>
public sealed class PaymentSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "Payment";

    /// <summary>
    /// Supabase Edge Functions のベースURL
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Edge Functions URL",
        Description = "Supabase Edge FunctionsのベースURL")]
    public string EdgeFunctionsUrl { get; set; } = string.Empty;

    /// <summary>
    /// FastSpring ストアフロントURL（フォールバック用）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Storefront URL",
        Description = "FastSpringストアフロントのURL")]
    public string StorefrontUrl { get; set; } = "https://baketa.onfastspring.com";

    /// <summary>
    /// チェックアウトセッションのタイムアウト（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Checkout Timeout",
        Description = "チェックアウトセッション作成のタイムアウト（秒）")]
    public int CheckoutTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// API呼び出しの最大リトライ回数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Max Retries",
        Description = "API呼び出しの最大リトライ回数")]
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// リトライ間隔（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Retry Delay",
        Description = "リトライ間隔（ミリ秒）")]
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// モックモード有効化（開発用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Payment", "Enable Mock Mode",
        Description = "開発用：モック決済サービスを使用")]
    public bool EnableMockMode { get; set; }

    /// <summary>
    /// 決済履歴のデフォルト取得件数
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Payment", "Default History Limit",
        Description = "決済履歴のデフォルト取得件数")]
    public int DefaultHistoryLimit { get; set; } = 10;

    /// <summary>
    /// カスタマーポータルURLの有効期限（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Payment", "Portal URL Expiry",
        Description = "セキュアポータルURLの有効期限（秒）")]
    public int PortalUrlExpirySeconds { get; set; } = 300;

    /// <summary>
    /// 設定を検証
    /// </summary>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // EdgeFunctionsUrl検証（モックモード以外）
        if (!EnableMockMode && string.IsNullOrWhiteSpace(EdgeFunctionsUrl))
        {
            warnings.Add("Edge Functions URLが設定されていません。決済機能を使用するには設定が必要です。");
        }

        if (!string.IsNullOrWhiteSpace(EdgeFunctionsUrl) && !Uri.TryCreate(EdgeFunctionsUrl, UriKind.Absolute, out _))
        {
            errors.Add("Edge Functions URLは有効なURLである必要があります");
        }

        // StorefrontUrl検証
        if (!string.IsNullOrWhiteSpace(StorefrontUrl) && !Uri.TryCreate(StorefrontUrl, UriKind.Absolute, out _))
        {
            errors.Add("Storefront URLは有効なURLである必要があります");
        }

        // タイムアウト検証
        if (CheckoutTimeoutSeconds < 10 || CheckoutTimeoutSeconds > 120)
        {
            warnings.Add("チェックアウトタイムアウトは10から120秒の間で設定してください");
        }

        // リトライ検証
        if (MaxRetries < 0 || MaxRetries > 10)
        {
            warnings.Add("最大リトライ回数は0から10の間で設定してください");
        }

        if (RetryDelayMs < 100 || RetryDelayMs > 10000)
        {
            warnings.Add("リトライ間隔は100から10000ミリ秒の間で設定してください");
        }

        // モックモード警告
        if (EnableMockMode)
        {
            warnings.Add("決済モックモードが有効です。本番環境では無効にしてください。");
        }

        return errors.Count > 0
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}
