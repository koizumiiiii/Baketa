namespace Baketa.Core.Settings;

/// <summary>
/// ライセンス管理システムの設定
/// </summary>
public sealed class LicenseSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "License";

    /// <summary>
    /// ライセンスAPIエンドポイント
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "License", "API Endpoint",
        Description = "ライセンス検証サーバーのエンドポイントURL")]
    public string ApiEndpoint { get; set; } = "https://api.baketa.app/v1/license";

    /// <summary>
    /// キャッシュ有効期限（分）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "License", "Cache Duration",
        Description = "ライセンス状態のローカルキャッシュ有効期限（分）")]
    public int CacheExpirationMinutes { get; set; } = 60;

    /// <summary>
    /// オフライン許容期間（時間）
    /// </summary>
    /// <remarks>
    /// 有効期限内であればオフラインでも使用可能なため、この設定は参考値として使用
    /// </remarks>
    [SettingMetadata(SettingLevel.Advanced, "License", "Offline Grace Period",
        Description = "オフライン状態でのキャッシュ最大有効期間（時間）")]
    public int OfflineGracePeriodHours { get; set; } = 72;

    /// <summary>
    /// トークン使用量警告閾値（%）
    /// [Issue #78 Phase 5] 80%到達で警告表示
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "License", "Token Warning Threshold",
        Description = "トークン使用量がこの割合に達したら警告を表示")]
    public int TokenWarningThresholdPercent { get; set; } = 80;

    /// <summary>
    /// トークン使用量危険閾値（%）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "License", "Token Critical Threshold",
        Description = "トークン使用量がこの割合に達したら危険警告を表示")]
    public int TokenCriticalThresholdPercent { get; set; } = 90;

    /// <summary>
    /// プラン期限切れ警告日数
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "License", "Expiration Warning Days",
        Description = "プラン有効期限の何日前から警告を表示するか")]
    public int PlanExpirationWarningDays { get; set; } = 7;

    /// <summary>
    /// ライセンス検証レート制限（リクエスト/分）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Refresh Rate Limit",
        Description = "ライセンス検証の最大リクエスト数/分")]
    public int RefreshRateLimitPerMinute { get; set; } = 10;

    /// <summary>
    /// クラウドAI翻訳レート制限（リクエスト/分）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Cloud AI Rate Limit",
        Description = "クラウドAI翻訳の最大リクエスト数/分")]
    public int CloudAiRateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// バックグラウンド更新間隔（分）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "License", "Background Refresh Interval",
        Description = "バックグラウンドでのライセンス状態更新間隔（分）")]
    public int BackgroundRefreshIntervalMinutes { get; set; } = 30;

    /// <summary>
    /// デバッグモード有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Enable Debug Mode",
        Description = "ライセンスデバッグログを有効化")]
    public bool EnableDebugMode { get; set; }

    /// <summary>
    /// モックモード有効化（開発用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Enable Mock Mode",
        Description = "開発用：モックライセンスAPIを使用")]
    public bool EnableMockMode { get; set; }

    /// <summary>
    /// モックモード時のプランタイプ
    /// </summary>
    // Issue #257: プラン構成変更に対応
    [SettingMetadata(SettingLevel.Debug, "License", "Mock Plan Type",
        Description = "モックモード時のテスト用プランタイプ（0=Free, 1=Pro, 2=Premium, 3=Ultimate）")]
    public int MockPlanType { get; set; } = 1; // デフォルトはProプラン

    /// <summary>
    /// モックモード時のトークン使用量
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Mock Token Usage",
        Description = "モックモード時のテスト用トークン使用量")]
    public long MockTokenUsage { get; set; }

    #region プロモーションコード関連 (Issue #237 Phase 2)

    /// <summary>
    /// プロモーションコードAPIエンドポイント
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "License", "Promotion API Endpoint",
        Description = "プロモーションコード検証サーバーのエンドポイントURL")]
    public string PromotionApiEndpoint { get; set; } = "https://api.baketa.app/api/promotion/redeem";

    /// <summary>
    /// 適用済みプロモーションコード（DPAPI暗号化）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Applied Promotion Code",
        Description = "適用済みプロモーションコード（内部使用）")]
    public string? AppliedPromotionCode { get; set; }

    /// <summary>
    /// プロモーションで適用されたプラン
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Promotion Plan Type",
        Description = "プロモーションで適用されたプラン（内部使用）")]
    public int? PromotionPlanType { get; set; }

    /// <summary>
    /// プロモーション有効期限（ISO 8601形式）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Promotion Expires At",
        Description = "プロモーション有効期限（内部使用）")]
    public string? PromotionExpiresAt { get; set; }

    /// <summary>
    /// プロモーション適用日時（ISO 8601形式）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Promotion Applied At",
        Description = "プロモーション適用日時（内部使用）")]
    public string? PromotionAppliedAt { get; set; }

    /// <summary>
    /// 最終オンライン検証日時（時計巻き戻し対策）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "License", "Last Online Verification",
        Description = "最終オンライン検証日時（内部使用）")]
    public string? LastOnlineVerification { get; set; }

    #endregion

    /// <summary>
    /// 設定を検証
    /// </summary>
    /// <returns>検証結果</returns>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        // APIエンドポイント検証
        if (!EnableMockMode && string.IsNullOrWhiteSpace(ApiEndpoint))
        {
            errors.Add("ライセンスAPIエンドポイントが設定されていません");
        }

        if (!string.IsNullOrWhiteSpace(ApiEndpoint) && !Uri.TryCreate(ApiEndpoint, UriKind.Absolute, out _))
        {
            errors.Add("ライセンスAPIエンドポイントは有効なURLである必要があります");
        }

        // キャッシュ期限検証
        if (CacheExpirationMinutes < 5 || CacheExpirationMinutes > 1440)
        {
            warnings.Add("キャッシュ有効期限は5分から1440分（24時間）の間で設定してください");
        }

        // 閾値検証
        if (TokenWarningThresholdPercent < 50 || TokenWarningThresholdPercent > 95)
        {
            warnings.Add("トークン警告閾値は50%から95%の間で設定してください");
        }

        if (TokenCriticalThresholdPercent < TokenWarningThresholdPercent ||
            TokenCriticalThresholdPercent > 99)
        {
            warnings.Add("トークン危険閾値は警告閾値より大きく99%以下で設定してください");
        }

        // レート制限検証
        if (RefreshRateLimitPerMinute < 1 || RefreshRateLimitPerMinute > 60)
        {
            warnings.Add("ライセンス検証レート制限は1から60の間で設定してください");
        }

        if (CloudAiRateLimitPerMinute < 10 || CloudAiRateLimitPerMinute > 120)
        {
            warnings.Add("クラウドAIレート制限は10から120の間で設定してください");
        }

        // 期限警告日数検証
        if (PlanExpirationWarningDays < 1 || PlanExpirationWarningDays > 30)
        {
            warnings.Add("プラン期限切れ警告日数は1から30の間で設定してください");
        }

        // オフライン許容期間検証
        if (OfflineGracePeriodHours < 1 || OfflineGracePeriodHours > 168)
        {
            warnings.Add("オフライン許容期間は1から168時間（1週間）の間で設定してください");
        }

        // バックグラウンド更新間隔検証
        if (BackgroundRefreshIntervalMinutes < 5 || BackgroundRefreshIntervalMinutes > 120)
        {
            warnings.Add("バックグラウンド更新間隔は5から120分の間で設定してください");
        }

        // モックモード検証
        if (EnableMockMode)
        {
            warnings.Add("モックモードが有効です。本番環境では無効にしてください。");
        }

        if (MockPlanType < 0 || MockPlanType > 3)
        {
            errors.Add("モックプランタイプは0から3の間で設定してください");
        }

        return errors.Count > 0
            ? SettingsValidationResult.CreateFailure(errors, warnings)
            : SettingsValidationResult.CreateSuccess(warnings);
    }
}
