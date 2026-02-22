using System;
using System.IO;

namespace Baketa.Core.Settings;

/// <summary>
/// [Issue #459] Baketa設定ファイルのパス管理クラス
/// すべてのパスを %USERPROFILE%\.baketa に統一管理
/// </summary>
public static class BaketaSettingsPaths
{
    // ─── ベースディレクトリ ───

    /// <summary>
    /// [Issue #459] 統一ベースディレクトリ（%USERPROFILE%\.baketa）
    /// すべての設定・キャッシュ・ログの起点
    /// </summary>
    public static string BaseDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".baketa");

    /// <summary>
    /// [Obsolete] MainSettingsDirectory → BaseDirectory に統一
    /// </summary>
    [Obsolete("Use BaseDirectory instead. Will be removed in v0.4.")]
    public static string MainSettingsDirectory => BaseDirectory;

    // ─── ベースディレクトリ直下のファイル ───

    /// <summary>
    /// メイン設定ファイルのパス（settings.json）
    /// </summary>
    public static string MainSettingsPath { get; } = Path.Combine(
        BaseDirectory,
        "settings.json");

    /// <summary>
    /// 初回起動フラグファイルのパス
    /// </summary>
    public static string FirstRunFlagPath { get; } = Path.Combine(
        BaseDirectory,
        "first-run.flag");

    /// <summary>
    /// クラッシュペンディングフラグのパス
    /// </summary>
    public static string CrashPendingFlagPath { get; } = Path.Combine(
        BaseDirectory,
        ".crash_pending");

    // ─── settings/ ───

    /// <summary>
    /// [Issue #459] 設定ファイルディレクトリ（%USERPROFILE%\.baketa\settings）
    /// </summary>
    public static string SettingsDirectory { get; } = Path.Combine(
        BaseDirectory,
        "settings");

    /// <summary>
    /// [Obsolete] UserSettingsDirectory → SettingsDirectory に改名
    /// </summary>
    [Obsolete("Use SettingsDirectory instead. Will be removed in v0.4.")]
    public static string UserSettingsDirectory => SettingsDirectory;

    /// <summary>
    /// 翻訳設定ファイルのパス
    /// </summary>
    public static string TranslationSettingsPath { get; } = Path.Combine(
        SettingsDirectory,
        "translation-settings.json");

    /// <summary>
    /// OCR設定ファイルのパス
    /// </summary>
    public static string OcrSettingsPath { get; } = Path.Combine(
        SettingsDirectory,
        "ocr-settings.json");

    /// <summary>
    /// UI設定ファイルのパス
    /// </summary>
    public static string UiSettingsPath { get; } = Path.Combine(
        SettingsDirectory,
        "ui-settings.json");

    /// <summary>
    /// [Issue #237] プロモーション設定ファイルのパス
    /// </summary>
    public static string PromotionSettingsPath { get; } = Path.Combine(
        SettingsDirectory,
        "promotion-settings.json");

    /// <summary>
    /// [Issue #261] 同意設定ファイルのパス
    /// </summary>
    public static string ConsentSettingsPath { get; } = Path.Combine(
        SettingsDirectory,
        "consent-settings.json");

    // ─── cache/ ───

    /// <summary>
    /// キャッシュディレクトリのパス
    /// </summary>
    public static string CacheDirectory { get; } = Path.Combine(
        BaseDirectory,
        "cache");

    /// <summary>
    /// [Issue #459] GPU環境キャッシュファイルのパス
    /// </summary>
    public static string GpuCachePath { get; } = Path.Combine(
        CacheDirectory,
        "gpu_cache.json");

    /// <summary>
    /// [Issue #459] コンポーネントバージョンファイルのパス
    /// </summary>
    public static string ComponentVersionsPath { get; } = Path.Combine(
        CacheDirectory,
        "component-versions.json");

    // ─── logs/ ───

    /// <summary>
    /// ログファイルディレクトリのパス
    /// </summary>
    public static string LogDirectory { get; } = Path.Combine(
        BaseDirectory,
        "logs");

    // ─── reports/ ───

    /// <summary>
    /// [Issue #459] レポートディレクトリのパス
    /// </summary>
    public static string ReportsDirectory { get; } = Path.Combine(
        BaseDirectory,
        "reports");

    /// <summary>
    /// クラッシュレポートディレクトリのパス
    /// </summary>
    public static string CrashReportsDirectory { get; } = Path.Combine(
        ReportsDirectory,
        "crashes");

    // ─── metrics/ ───

    /// <summary>
    /// [Issue #459] メトリクスディレクトリのパス
    /// </summary>
    public static string MetricsDirectory { get; } = Path.Combine(
        BaseDirectory,
        "metrics");

    // ─── profiles/ ───

    /// <summary>
    /// [Issue #459] プロファイルディレクトリのパス
    /// </summary>
    public static string ProfilesDirectory { get; } = Path.Combine(
        BaseDirectory,
        "profiles");

    /// <summary>
    /// [Issue #293] ROIプロファイルディレクトリのパス
    /// </summary>
    public static string RoiProfilesDirectory { get; } = Path.Combine(
        ProfilesDirectory,
        "roi-profiles");

    /// <summary>
    /// [Issue #459] キャプチャプロファイルディレクトリのパス
    /// </summary>
    public static string CaptureProfilesDirectory { get; } = Path.Combine(
        ProfilesDirectory,
        "capture-profiles");

    /// <summary>
    /// [Issue #459] ゲームプロファイルディレクトリのパス
    /// </summary>
    public static string GameProfilesDirectory { get; } = Path.Combine(
        ProfilesDirectory,
        "game-profiles");

    // ─── license/ ───

    /// <summary>
    /// [Issue #459] ライセンスディレクトリのパス
    /// </summary>
    public static string LicenseDirectory { get; } = Path.Combine(
        BaseDirectory,
        "license");

    /// <summary>
    /// [Issue #459] ライセンスキャッシュファイルのパス
    /// </summary>
    public static string LicenseCachePath { get; } = Path.Combine(
        LicenseDirectory,
        "license-cache.json");

    /// <summary>
    /// [Issue #459] Patreon認証情報ファイルのパス
    /// </summary>
    public static string PatreonCredentialsPath { get; } = Path.Combine(
        LicenseDirectory,
        "patreon-credentials.json");

    // ─── token-usage/ ───

    /// <summary>
    /// [Issue #459] トークン使用量ディレクトリのパス
    /// </summary>
    public static string TokenUsageDirectory { get; } = Path.Combine(
        BaseDirectory,
        "token-usage");

    // ─── component-metadata/ ───

    /// <summary>
    /// [Issue #459] コンポーネントメタデータディレクトリのパス
    /// </summary>
    public static string ComponentMetadataDirectory { get; } = Path.Combine(
        BaseDirectory,
        "component-metadata");

    // ─── ユーティリティメソッド ───

    /// <summary>
    /// 必要なディレクトリを作成する
    /// </summary>
    public static void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(BaseDirectory);
        Directory.CreateDirectory(SettingsDirectory);
        Directory.CreateDirectory(CacheDirectory);
        Directory.CreateDirectory(LogDirectory);
        Directory.CreateDirectory(ReportsDirectory);
        Directory.CreateDirectory(CrashReportsDirectory);
        Directory.CreateDirectory(MetricsDirectory);
        Directory.CreateDirectory(ProfilesDirectory);
        Directory.CreateDirectory(RoiProfilesDirectory);
        Directory.CreateDirectory(CaptureProfilesDirectory);
        Directory.CreateDirectory(GameProfilesDirectory);
        Directory.CreateDirectory(LicenseDirectory);
        Directory.CreateDirectory(TokenUsageDirectory);
        Directory.CreateDirectory(ComponentMetadataDirectory);
    }

    /// <summary>
    /// [Obsolete] EnsureUserSettingsDirectoryExists → EnsureDirectoriesExist に改名
    /// </summary>
    [Obsolete("Use EnsureDirectoriesExist instead. Will be removed in v0.4.")]
    public static void EnsureUserSettingsDirectoryExists() => EnsureDirectoriesExist();

    /// <summary>
    /// 指定されたパスが有効な設定ファイルパスかどうかを確認
    /// </summary>
    public static bool IsValidSettingsPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalizedPath = Path.GetFullPath(filePath);
        var normalizedBaseDir = Path.GetFullPath(SettingsDirectory);

        return normalizedPath.StartsWith(normalizedBaseDir, StringComparison.OrdinalIgnoreCase) &&
               Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }
}
