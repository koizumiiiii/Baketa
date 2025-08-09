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