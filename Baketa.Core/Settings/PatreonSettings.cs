namespace Baketa.Core.Settings;

/// <summary>
/// Patreon連携の設定
/// </summary>
public sealed class PatreonSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "Patreon";

    /// <summary>
    /// Patreon OAuth Client ID
    /// </summary>
    /// <remarks>
    /// 公開可能な値。Client Secretは中継サーバーで管理。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Client ID",
        Description = "Patreon OAuth クライアントID")]
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// 中継サーバーのベースURL
    /// </summary>
    /// <remarks>
    /// Cloudflare Workers等でホストされる中継サーバー。
    /// Client Secretを隠蔽し、トークン交換を代行する。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Relay Server URL",
        Description = "Patreon認証中継サーバーのURL")]
    public string RelayServerUrl { get; set; } = "https://baketa-relay.workers.dev";

    /// <summary>
    /// OAuth リダイレクトURI
    /// </summary>
    /// <remarks>
    /// Patreonはカスタムスキーム(baketa://)非対応のため、ローカルHTTPサーバーを使用。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Redirect URI",
        Description = "OAuth認証後のリダイレクト先URI")]
    public string RedirectUri { get; set; } = "http://localhost:8080/patreon/callback";

    // Issue #125: StandardTierId削除（Standardプラン廃止）

    /// <summary>
    /// Patreon Tier ID マッピング（Pro プラン）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Pro Tier ID",
        Description = "Proプラン ($3) に対応するPatreon Tier ID")]
    public string ProTierId { get; set; } = string.Empty;

    /// <summary>
    /// Patreon Tier ID マッピング（Premia プラン）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Premia Tier ID",
        Description = "Premiaプラン ($5) に対応するPatreon Tier ID")]
    public string PremiaTierId { get; set; } = string.Empty;

    /// <summary>
    /// API結果のキャッシュ有効期間（分）
    /// </summary>
    /// <remarks>
    /// Patreon APIのレート制限対策。1時間推奨。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Cache Duration",
        Description = "Patreon APIレスポンスのキャッシュ時間（分）")]
    public int CacheDurationMinutes { get; set; } = 60;

    /// <summary>
    /// オフライングレースピリオド（日）
    /// </summary>
    /// <remarks>
    /// サブスクリプション有効期限内であれば、この期間オフラインでも使用可能。
    /// Patreonは自動更新のため、更新通知は不要。
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "Patreon", "Offline Grace Period",
        Description = "オフライン状態で有料機能を維持する最大日数")]
    public int OfflineGracePeriodDays { get; set; } = 7;

    /// <summary>
    /// アクセストークンの有効期限マージン（秒）
    /// </summary>
    /// <remarks>
    /// トークン更新を期限の少し前に行うためのマージン。
    /// </remarks>
    [SettingMetadata(SettingLevel.Debug, "Patreon", "Token Refresh Margin",
        Description = "アクセストークン更新の余裕時間（秒）")]
    public int TokenRefreshMarginSeconds { get; set; } = 300;

    /// <summary>
    /// デバッグモード
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Patreon", "Debug Mode",
        Description = "Patreon連携のデバッグログを有効化")]
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// 設定を検証
    /// </summary>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // Client IDは必須（モックモード以外）
        if (string.IsNullOrWhiteSpace(ClientId))
        {
            warnings.Add("Patreon Client IDが設定されていません。Patreon連携は無効です。");
        }

        // 中継サーバーURL検証（HTTPS必須）
        if (!string.IsNullOrWhiteSpace(RelayServerUrl))
        {
            if (!Uri.TryCreate(RelayServerUrl, UriKind.Absolute, out var uri))
            {
                errors.Add("中継サーバーURLは有効なURLである必要があります");
            }
            else if (uri.Scheme != Uri.UriSchemeHttps)
            {
                errors.Add("中継サーバーURLはHTTPSである必要があります（セキュリティ要件）");
            }
        }
        else if (!string.IsNullOrWhiteSpace(ClientId))
        {
            errors.Add("中継サーバーURLが設定されていません");
        }

        // キャッシュ時間検証
        if (CacheDurationMinutes < 5 || CacheDurationMinutes > 1440)
        {
            warnings.Add("キャッシュ時間は5分から1440分（24時間）の間で設定してください");
        }

        // グレースピリオド検証
        if (OfflineGracePeriodDays < 1 || OfflineGracePeriodDays > 30)
        {
            warnings.Add("オフライングレースピリオドは1日から30日の間で設定してください");
        }

        // Tier ID検証（少なくとも1つは設定推奨）
        // Issue #125: StandardTierId削除（Standardプラン廃止）
        if (string.IsNullOrWhiteSpace(ProTierId) &&
            string.IsNullOrWhiteSpace(PremiaTierId))
        {
            warnings.Add("Patreon Tier IDが設定されていません。有料プランの判定ができません。");
        }

        return errors.Count > 0
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}
