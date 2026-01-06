namespace Baketa.Core.Settings;

/// <summary>
/// 利用規約・プライバシーポリシー同意設定
/// [Issue #261] GDPR/CCPA準拠のクリックラップ同意管理
/// </summary>
public sealed class ConsentSettings
{
    /// <summary>
    /// 設定セクション名
    /// </summary>
    public const string SectionName = "Consent";

    /// <summary>
    /// 現在の利用規約バージョン
    /// </summary>
    public const string CurrentTermsVersion = "2026-01";

    /// <summary>
    /// 現在のプライバシーポリシーバージョン
    /// </summary>
    public const string CurrentPrivacyVersion = "2026-01";

    #region プライバシーポリシー同意（初回起動時）

    /// <summary>
    /// プライバシーポリシーに同意済みか
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Privacy Policy Accepted",
        Description = "プライバシーポリシーに同意済みか")]
    public bool HasAcceptedPrivacyPolicy { get; set; }

    /// <summary>
    /// 同意したプライバシーポリシーのバージョン
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Privacy Policy Version",
        Description = "同意したプライバシーポリシーのバージョン")]
    public string? PrivacyPolicyVersion { get; set; }

    /// <summary>
    /// プライバシーポリシー同意日時（UTC）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Privacy Accepted At",
        Description = "プライバシーポリシー同意日時（ISO 8601形式）")]
    public string? PrivacyAcceptedAt { get; set; }

    #endregion

    #region 利用規約同意（アカウント作成時）

    /// <summary>
    /// 利用規約に同意済みか
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Terms of Service Accepted",
        Description = "利用規約に同意済みか")]
    public bool HasAcceptedTermsOfService { get; set; }

    /// <summary>
    /// 同意した利用規約のバージョン
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Terms Version",
        Description = "同意した利用規約のバージョン")]
    public string? TermsOfServiceVersion { get; set; }

    /// <summary>
    /// 利用規約同意日時（UTC）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Consent", "Terms Accepted At",
        Description = "利用規約同意日時（ISO 8601形式）")]
    public string? TermsAcceptedAt { get; set; }

    #endregion

    #region 検証メソッド

    /// <summary>
    /// プライバシーポリシーへの再同意が必要か
    /// </summary>
    public bool NeedsPrivacyPolicyReConsent =>
        !HasAcceptedPrivacyPolicy ||
        string.IsNullOrEmpty(PrivacyPolicyVersion) ||
        PrivacyPolicyVersion != CurrentPrivacyVersion;

    /// <summary>
    /// 利用規約への再同意が必要か
    /// </summary>
    public bool NeedsTermsOfServiceReConsent =>
        !HasAcceptedTermsOfService ||
        string.IsNullOrEmpty(TermsOfServiceVersion) ||
        TermsOfServiceVersion != CurrentTermsVersion;

    /// <summary>
    /// 初回起動時の同意（プライバシーポリシー）が必要か
    /// </summary>
    public bool NeedsInitialConsent => NeedsPrivacyPolicyReConsent;

    /// <summary>
    /// アカウント作成に必要な同意が完了しているか
    /// </summary>
    public bool CanCreateAccount =>
        HasAcceptedPrivacyPolicy &&
        HasAcceptedTermsOfService &&
        !NeedsPrivacyPolicyReConsent &&
        !NeedsTermsOfServiceReConsent;

    #endregion

    #region 同意記録メソッド

    /// <summary>
    /// プライバシーポリシー同意を記録
    /// </summary>
    public void AcceptPrivacyPolicy()
    {
        HasAcceptedPrivacyPolicy = true;
        PrivacyPolicyVersion = CurrentPrivacyVersion;
        PrivacyAcceptedAt = DateTime.UtcNow.ToString("O");
    }

    /// <summary>
    /// 利用規約同意を記録
    /// </summary>
    public void AcceptTermsOfService()
    {
        HasAcceptedTermsOfService = true;
        TermsOfServiceVersion = CurrentTermsVersion;
        TermsAcceptedAt = DateTime.UtcNow.ToString("O");
    }

    /// <summary>
    /// 両方の同意を記録（アカウント作成時）
    /// </summary>
    public void AcceptAll()
    {
        AcceptPrivacyPolicy();
        AcceptTermsOfService();
    }

    #endregion

    /// <summary>
    /// 設定を検証
    /// </summary>
    public SettingsValidationResult ValidateSettings()
    {
        var warnings = new List<string>();

        if (HasAcceptedPrivacyPolicy && string.IsNullOrEmpty(PrivacyPolicyVersion))
        {
            warnings.Add("プライバシーポリシー同意のバージョン情報がありません");
        }

        if (HasAcceptedTermsOfService && string.IsNullOrEmpty(TermsOfServiceVersion))
        {
            warnings.Add("利用規約同意のバージョン情報がありません");
        }

        return SettingsValidationResult.CreateSuccess(warnings);
    }
}

/// <summary>
/// 同意タイプ
/// </summary>
public enum ConsentType
{
    /// <summary>
    /// プライバシーポリシー
    /// </summary>
    PrivacyPolicy,

    /// <summary>
    /// 利用規約
    /// </summary>
    TermsOfService
}
