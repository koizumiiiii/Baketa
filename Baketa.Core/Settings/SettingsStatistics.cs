using System;
using System.Collections.Generic;

namespace Baketa.Core.Settings;

/// <summary>
/// 設定システムの統計情報
/// </summary>
/// <remarks>
/// SettingsStatisticsを初期化します
/// </remarks>
public sealed class SettingsStatistics(
    int totalSettings,
    IReadOnlyDictionary<string, int> settingsByCategory,
    int gameProfileCount,
    int modifiedSettingsCount,
    int favoriteSettingsCount,
    DateTime? lastSaved,
    DateTime? lastLoaded,
    int changeHistoryCount,
    int backupCount,
    long settingsFileSizeBytes,
    DateTime? lastMigration,
    int currentSchemaVersion,
    double averageSaveTimeMs,
    double averageLoadTimeMs,
    IReadOnlyDictionary<SettingLevel, int> settingsByLevel,
    string? activeGameProfileId)
{
    /// <summary>
    /// 総設定項目数
    /// </summary>
    public int TotalSettings { get; } = totalSettings;

    /// <summary>
    /// カテゴリ別設定数
    /// </summary>
    public IReadOnlyDictionary<string, int> SettingsByCategory { get; } = settingsByCategory ?? throw new ArgumentNullException(nameof(settingsByCategory));

    /// <summary>
    /// ゲームプロファイル数
    /// </summary>
    public int GameProfileCount { get; } = gameProfileCount;

    /// <summary>
    /// デフォルト値から変更された設定数
    /// </summary>
    public int ModifiedSettingsCount { get; } = modifiedSettingsCount;

    /// <summary>
    /// お気に入り設定数
    /// </summary>
    public int FavoriteSettingsCount { get; } = favoriteSettingsCount;

    /// <summary>
    /// 最後に保存した日時
    /// </summary>
    public DateTime? LastSaved { get; } = lastSaved;

    /// <summary>
    /// 最後に読み込んだ日時
    /// </summary>
    public DateTime? LastLoaded { get; } = lastLoaded;

    /// <summary>
    /// 変更履歴エントリ数
    /// </summary>
    public int ChangeHistoryCount { get; } = changeHistoryCount;

    /// <summary>
    /// バックアップファイル数
    /// </summary>
    public int BackupCount { get; } = backupCount;

    /// <summary>
    /// 設定ファイルサイズ（バイト）
    /// </summary>
    public long SettingsFileSizeBytes { get; } = settingsFileSizeBytes;

    /// <summary>
    /// 最後のマイグレーション日時
    /// </summary>
    public DateTime? LastMigration { get; } = lastMigration;

    /// <summary>
    /// 現在のスキーマバージョン
    /// </summary>
    public int CurrentSchemaVersion { get; } = currentSchemaVersion;

    /// <summary>
    /// 平均保存時間（ミリ秒）
    /// </summary>
    public double AverageSaveTimeMs { get; } = averageSaveTimeMs;

    /// <summary>
    /// 平均読み込み時間（ミリ秒）
    /// </summary>
    public double AverageLoadTimeMs { get; } = averageLoadTimeMs;

    /// <summary>
    /// レベル別設定数（基本・詳細・デバッグ）
    /// </summary>
    public IReadOnlyDictionary<SettingLevel, int> SettingsByLevel { get; } = settingsByLevel ?? throw new ArgumentNullException(nameof(settingsByLevel));

    /// <summary>
    /// アクティブなゲームプロファイルID
    /// </summary>
    public string? ActiveGameProfileId { get; } = activeGameProfileId;

    /// <summary>
    /// 統計生成日時
    /// </summary>
    public DateTime GeneratedAt { get; } = DateTime.Now;

    /// <summary>
    /// 空の統計情報を作成します
    /// </summary>
    /// <returns>空の統計情報</returns>
    public static SettingsStatistics CreateEmpty()
    {
        return new SettingsStatistics(
            totalSettings: 0,
            settingsByCategory: new Dictionary<string, int>(),
            gameProfileCount: 0,
            modifiedSettingsCount: 0,
            favoriteSettingsCount: 0,
            lastSaved: null,
            lastLoaded: null,
            changeHistoryCount: 0,
            backupCount: 0,
            settingsFileSizeBytes: 0,
            lastMigration: null,
            currentSchemaVersion: 0,
            averageSaveTimeMs: 0,
            averageLoadTimeMs: 0,
            settingsByLevel: new Dictionary<SettingLevel, int>(),
            activeGameProfileId: null);
    }
    
    /// <summary>
    /// 統計情報のサマリー
    /// </summary>
    public string Summary
    {
        get
        {
            return $"設定統計: {TotalSettings.ToString(System.Globalization.CultureInfo.InvariantCulture)}項目, {GameProfileCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}プロファイル, " +
                   $"{ModifiedSettingsCount.ToString(System.Globalization.CultureInfo.InvariantCulture)}変更済み, スキーマva{CurrentSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
        }
    }
    
    /// <summary>
    /// 詳細な統計情報を取得します
    /// </summary>
    /// <returns>詳細統計の辞書</returns>
    public IDictionary<string, object> GetDetailedStatistics()
    {
        return new Dictionary<string, object>
        {
            ["TotalSettings"] = TotalSettings,
            ["SettingsByCategory"] = SettingsByCategory,
            ["GameProfileCount"] = GameProfileCount,
            ["ModifiedSettingsCount"] = ModifiedSettingsCount,
            ["FavoriteSettingsCount"] = FavoriteSettingsCount,
            ["LastSaved"] = LastSaved?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "未保存",
            ["LastLoaded"] = LastLoaded?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "未読み込み",
            ["ChangeHistoryCount"] = ChangeHistoryCount,
            ["BackupCount"] = BackupCount,
            ["SettingsFileSizeKB"] = Math.Round(SettingsFileSizeBytes / 1024.0, 2),
            ["LastMigration"] = LastMigration?.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) ?? "未実行",
            ["CurrentSchemaVersion"] = CurrentSchemaVersion,
            ["AverageSaveTimeMs"] = Math.Round(AverageSaveTimeMs, 2),
            ["AverageLoadTimeMs"] = Math.Round(AverageLoadTimeMs, 2),
            ["SettingsByLevel"] = SettingsByLevel,
            ["ActiveGameProfileId"] = ActiveGameProfileId ?? "なし",
            ["GeneratedAt"] = GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
        };
    }
}
