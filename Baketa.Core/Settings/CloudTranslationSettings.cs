namespace Baketa.Core.Settings;

/// <summary>
/// Cloud AI翻訳の設定
/// </summary>
public sealed class CloudTranslationSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "CloudTranslation";

    /// <summary>
    /// Relay ServerのベースURL
    /// </summary>
    /// <remarks>
    /// PatreonSettingsのRelayServerUrlと同一だが、Cloud AI翻訳用として明示的に定義。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Relay Server URL",
        Description = "Cloud AI翻訳中継サーバーのURL")]
    public string RelayServerUrl { get; set; } = "https://baketa-relay.suke009.workers.dev";

    /// <summary>
    /// API Key（クライアント認証用）
    /// </summary>
    /// <remarks>
    /// appsettings.jsonから読み込まれる。セキュリティのためSecrets管理を推奨。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "API Key",
        Description = "Relay Server認証用のAPIキー")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// リクエストタイムアウト（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Timeout",
        Description = "翻訳リクエストのタイムアウト時間（秒）")]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大リトライ回数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Max Retries",
        Description = "リトライ可能エラー時の最大リトライ回数")]
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// リトライ間隔（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Retry Delay",
        Description = "リトライ間の待機時間（ミリ秒）")]
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Primary翻訳エンジンのプロバイダーID
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Primary Provider",
        Description = "プライマリCloud AIプロバイダー（gemini/openai）")]
    public string PrimaryProviderId { get; set; } = "gemini";

    /// <summary>
    /// Secondary翻訳エンジンのプロバイダーID
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Cloud Translation", "Secondary Provider",
        Description = "セカンダリCloud AIプロバイダー（gemini/openai）")]
    public string SecondaryProviderId { get; set; } = "openai";

    /// <summary>
    /// Cloud AI翻訳を有効化するか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Cloud Translation", "Enabled",
        Description = "Cloud AI翻訳機能を有効化")]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// デバッグモード
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Cloud Translation", "Debug Mode",
        Description = "Cloud AI翻訳のデバッグログを有効化")]
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// Direct APIモードを使用するか（開発・テスト用）
    /// </summary>
    /// <remarks>
    /// trueの場合、Relay Serverを経由せずに直接Gemini APIを呼び出します。
    /// Patreon認証をバイパスするため、開発・テスト目的でのみ使用してください。
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Cloud Translation", "Direct API Mode",
        Description = "Relay Serverを経由せずに直接Gemini APIを使用（開発用）")]
    public bool UseDirectApiMode { get; set; }

    /// <summary>
    /// Direct APIモード用のGemini APIキー
    /// </summary>
    /// <remarks>
    /// UseDirectApiMode=true の場合に使用されます。
    /// セキュリティのため appsettings.Local.json で管理し、.gitignore に追加してください。
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Cloud Translation", "Direct Gemini API Key",
        Description = "Direct APIモード用のGemini APIキー")]
    public string DirectGeminiApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Direct APIモード用のOpenAI APIキー
    /// </summary>
    /// <remarks>
    /// UseDirectApiMode=true の場合にSecondary翻訳エンジンとして使用されます。
    /// セキュリティのため appsettings.Local.json で管理し、.gitignore に追加してください。
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Cloud Translation", "Direct OpenAI API Key",
        Description = "Direct APIモード用のOpenAI APIキー（フォールバック用）")]
    public string DirectOpenAIApiKey { get; set; } = string.Empty;

    /// <summary>
    /// 設定を検証
    /// </summary>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // URL検証
        if (string.IsNullOrWhiteSpace(RelayServerUrl))
        {
            errors.Add("Relay Server URLが設定されていません");
        }
        else if (!Uri.TryCreate(RelayServerUrl, UriKind.Absolute, out var uri))
        {
            errors.Add("Relay Server URLは有効なURLである必要があります");
        }
        else if (uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Relay Server URLはHTTPSである必要があります");
        }

        // タイムアウト検証
        if (TimeoutSeconds < 5 || TimeoutSeconds > 120)
        {
            warnings.Add("タイムアウトは5秒から120秒の間で設定してください");
        }

        // リトライ検証
        if (MaxRetries < 0 || MaxRetries > 5)
        {
            warnings.Add("リトライ回数は0から5の間で設定してください");
        }

        // プロバイダー検証
        if (!string.IsNullOrEmpty(PrimaryProviderId) &&
            PrimaryProviderId != "gemini" && PrimaryProviderId != "openai")
        {
            errors.Add("Primary Providerはgeminiまたはopenaiである必要があります");
        }

        return errors.Count > 0
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}
