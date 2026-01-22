using System;
using System.IO;

namespace Baketa.Core.Settings;

/// <summary>
/// Baketa設定ファイルのパス管理クラス
/// 設定ファイルのパスを集約管理し、重複を排除
/// </summary>
public static class BaketaSettingsPaths
{
    /// <summary>
    /// [Issue #252] メイン設定ディレクトリ（%APPDATA%\Baketa）
    /// EnhancedSettingsService と App.axaml.cs で使用
    /// </summary>
    public static string MainSettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Baketa");

    /// <summary>
    /// [Issue #252] メイン設定ファイルのパス（settings.json）
    /// アプリケーション全体で共有される設定ファイル
    /// </summary>
    public static string MainSettingsPath { get; } = Path.Combine(
        MainSettingsDirectory,
        "settings.json");

    /// <summary>
    /// [Issue #252] クラッシュレポートディレクトリのパス
    /// </summary>
    public static string CrashReportsDirectory { get; } = Path.Combine(
        MainSettingsDirectory,
        "Reports",
        "Crashes");

    /// <summary>
    /// [Issue #252] クラッシュペンディングフラグのパス
    /// 前回クラッシュを検出するためのフラグファイル
    /// </summary>
    public static string CrashPendingFlagPath { get; } = Path.Combine(
        MainSettingsDirectory,
        ".crash_pending");

    /// <summary>
    /// ユーザー設定ディレクトリのベースパス
    /// </summary>
    public static string UserSettingsDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".baketa",
        "settings");

    /// <summary>
    /// 翻訳設定ファイルのパス
    /// </summary>
    public static string TranslationSettingsPath { get; } = Path.Combine(
        UserSettingsDirectory,
        "translation-settings.json");

    /// <summary>
    /// OCR設定ファイルのパス
    /// </summary>
    public static string OcrSettingsPath { get; } = Path.Combine(
        UserSettingsDirectory,
        "ocr-settings.json");

    /// <summary>
    /// UI設定ファイルのパス
    /// </summary>
    public static string UiSettingsPath { get; } = Path.Combine(
        UserSettingsDirectory,
        "ui-settings.json");

    /// <summary>
    /// [Issue #237] プロモーション設定ファイルのパス
    /// </summary>
    public static string PromotionSettingsPath { get; } = Path.Combine(
        UserSettingsDirectory,
        "promotion-settings.json");

    /// <summary>
    /// [Issue #261] 同意設定ファイルのパス
    /// 利用規約・プライバシーポリシー同意状態を保存
    /// </summary>
    public static string ConsentSettingsPath { get; } = Path.Combine(
        UserSettingsDirectory,
        "consent-settings.json");

    /// <summary>
    /// キャッシュディレクトリのパス
    /// </summary>
    public static string CacheDirectory { get; } = Path.Combine(
        UserSettingsDirectory,
        "cache");

    /// <summary>
    /// ログファイルディレクトリのパス
    /// </summary>
    public static string LogDirectory { get; } = Path.Combine(
        UserSettingsDirectory,
        "logs");

    /// <summary>
    /// [Issue #293] ROIプロファイルディレクトリのパス
    /// ゲーム/アプリケーション別のROI学習データを保存
    /// </summary>
    public static string RoiProfilesDirectory { get; } = Path.Combine(
        UserSettingsDirectory,
        "roi-profiles");

    /// <summary>
    /// ユーザー設定ディレクトリが存在しない場合は作成する
    /// </summary>
    public static void EnsureUserSettingsDirectoryExists()
    {
        if (!Directory.Exists(UserSettingsDirectory))
        {
            Directory.CreateDirectory(UserSettingsDirectory);
        }

        if (!Directory.Exists(CacheDirectory))
        {
            Directory.CreateDirectory(CacheDirectory);
        }

        if (!Directory.Exists(LogDirectory))
        {
            Directory.CreateDirectory(LogDirectory);
        }

        // [Issue #293] ROIプロファイルディレクトリ
        if (!Directory.Exists(RoiProfilesDirectory))
        {
            Directory.CreateDirectory(RoiProfilesDirectory);
        }
    }

    /// <summary>
    /// 指定されたパスが有効な設定ファイルパスかどうかを確認
    /// </summary>
    /// <param name="filePath">確認するファイルパス</param>
    /// <returns>有効な設定ファイルパスの場合true</returns>
    public static bool IsValidSettingsPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return false;

        var normalizedPath = Path.GetFullPath(filePath);
        var normalizedBaseDir = Path.GetFullPath(UserSettingsDirectory);

        return normalizedPath.StartsWith(normalizedBaseDir, StringComparison.OrdinalIgnoreCase) &&
               Path.GetExtension(filePath).Equals(".json", StringComparison.OrdinalIgnoreCase);
    }
}
