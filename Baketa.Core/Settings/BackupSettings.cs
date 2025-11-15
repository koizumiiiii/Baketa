using System;

namespace Baketa.Core.Settings;

/// <summary>
/// バックアップ設定
/// </summary>
public sealed class BackupSettings
{
    /// <summary>
    /// 自動バックアップの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "自動バックアップ")]
    public bool EnableAutoBackup { get; set; } = true;

    /// <summary>
    /// バックアップ間隔（時間）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "バックアップ間隔")]
    public int BackupIntervalHours { get; set; } = 24;

    /// <summary>
    /// 保持するバックアップ数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "最大バックアップ数")]
    public int MaxBackupCount { get; set; } = 10;

    /// <summary>
    /// バックアップの圧縮
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "バックアップ圧縮")]
    public bool CompressBackups { get; set; } = true;

    /// <summary>
    /// バックアップ先ディレクトリ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "バックアップ先")]
    public string BackupDirectory { get; set; } = string.Empty;
}

/// <summary>
/// 同期設定（将来の拡張用）
/// 設定の同期、クラウド連携、複数デバイス間での設定共有などを管理
/// </summary>
public sealed class SyncSettings
{
    /// <summary>
    /// 設定同期機能を有効にするか
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "同期機能",
        Description = "設定をクラウドやその他のデバイスと同期します（将来実装予定）")]
    public bool EnableSync { get; set; } = false;

    /// <summary>
    /// 自動同期を有効にするか
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "自動同期",
        Description = "設定変更時に自動的に同期を実行します")]
    public bool EnableAutoSync { get; set; } = false;

    /// <summary>
    /// 同期間隔（分）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "同期間隔",
        Description = "自動同期を実行する間隔",
        Unit = "分",
        MinValue = 5,
        MaxValue = 1440)]
    public int SyncIntervalMinutes { get; set; } = 60;

    /// <summary>
    /// 同期するカテゴリ
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "同期カテゴリ",
        Description = "同期対象の設定カテゴリ（カンマ区切り）")]
    public string SyncCategories { get; set; } = "General,Theme,Localization";

    /// <summary>
    /// 競合解決方法
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "競合解決",
        Description = "設定競合が発生した場合の解決方法",
        ValidValues = [ConflictResolution.LocalWins, ConflictResolution.RemoteWins, ConflictResolution.Manual])]
    public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Manual;

    /// <summary>
    /// 最後の同期日時
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Sync", "最終同期",
        Description = "最後に同期が実行された日時")]
    public DateTime? LastSyncTime { get; set; }

    /// <summary>
    /// 同期エンドポイントURL（将来の拡張用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Sync", "同期エンドポイント",
        Description = "同期サーバーのエンドポイントURL（開発者向け）")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1056:URI-like properties should not be strings",
        Justification = "設定値として柔軟性が必要であり、検証は別途実装されます")]
    public string? SyncEndpointUrl { get; set; }

    /// <summary>
    /// 同期トークン（認証用、将来の拡張用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Sync", "認証トークン",
        Description = "同期サービスの認証トークン（機密情報）")]
    public string? SyncToken { get; set; }

    /// <summary>
    /// デバイス識別子
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Sync", "デバイスID",
        Description = "このデバイスの一意識別子")]
    public string DeviceId { get; set; } = Environment.MachineName;

    /// <summary>
    /// 同期履歴を保持するか
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "履歴保持",
        Description = "同期履歴を保持して競合解決に使用します")]
    public bool KeepSyncHistory { get; set; } = true;

    /// <summary>
    /// 保持する同期履歴の最大件数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Sync", "履歴保持数",
        Description = "保持する同期履歴の最大件数",
        MinValue = 10,
        MaxValue = 1000)]
    public int MaxSyncHistoryCount { get; set; } = 100;
}

/// <summary>
/// 設定競合時の解決方法
/// </summary>
public enum ConflictResolution
{
    /// <summary>
    /// ローカル設定を優先
    /// </summary>
    LocalWins,

    /// <summary>
    /// リモート設定を優先
    /// </summary>
    RemoteWins,

    /// <summary>
    /// 手動で解決
    /// </summary>
    Manual
}
