using System;
using System.Collections.Generic;
using System.Linq;

namespace Baketa.Core.Settings;

/// <summary>
/// アプリケーション設定クラス（UX改善対応版）
/// 全ての設定カテゴリを統合し、階層化設定をサポート
/// ホットキー機能は完全に削除されています
/// </summary>
public sealed class AppSettings
{
    /// <summary>
    /// 設定スキーマバージョン（マイグレーション用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "System", "スキーマバージョン", 
        Description = "設定ファイルのスキーマバージョン（システム管理用）")]
    public int SchemaVersion { get; set; } = 1;
    
    /// <summary>
    /// 設定の最終更新日時
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "System", "最終更新日時", 
        Description = "設定が最後に更新された日時（システム管理用）")]
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 設定ファイルの作成日時
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "System", "作成日時", 
        Description = "設定ファイルが作成された日時（システム管理用）")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 一般設定
    /// </summary>
    public GeneralSettings General { get; set; } = new();
    
    /// <summary>
    /// UIテーマ設定
    /// </summary>
    public ThemeSettings Theme { get; set; } = new();
    
    /// <summary>
    /// ローカライズ設定
    /// </summary>
    public LocalizationSettings Localization { get; set; } = new();
    
    /// <summary>
    /// キャプチャ設定
    /// </summary>
    public CaptureSettings Capture { get; set; } = new();
    
    /// <summary>
    /// OCR設定
    /// </summary>
    public OcrSettings Ocr { get; set; } = new();
    
    /// <summary>
    /// 翻訳設定
    /// </summary>
    public TranslationSettings Translation { get; set; } = new();
    
    /// <summary>
    /// オーバーレイ設定
    /// </summary>
    public OverlaySettings Overlay { get; set; } = new();
    
    /// <summary>
    /// メイン操作UI設定（新規追加）
    /// </summary>
    public MainUiSettings MainUi { get; set; } = new();
    
    /// <summary>
    /// 詳細設定
    /// </summary>
    public AdvancedSettings Advanced { get; set; } = new();
    
    /// <summary>
    /// ゲームプロファイル設定
    /// キー：ゲーム識別子（実行ファイル名やプロセス名）
    /// 値：そのゲーム用の設定プロファイル
    /// </summary>
    public Dictionary<string, GameProfileSettings> GameProfiles { get; set; } = [];
    
    /// <summary>
    /// 現在アクティブなゲームプロファイルID
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "アクティブプロファイル", 
        Description = "現在アクティブなゲームプロファイルのID")]
    public string? ActiveGameProfileId { get; set; }
    
    /// <summary>
    /// 設定のお気に入り（よく使う設定の記録）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "お気に入り設定", 
        Description = "よく使用される設定項目のリスト")]
    public IList<string> FavoriteSettings { get; set; } = [];
    
    /// <summary>
    /// 設定変更履歴（最新N件を保持）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "System", "変更履歴", 
        Description = "設定変更の履歴（開発者向け）")]
    public IList<SettingChangeRecord> ChangeHistory { get; set; } = [];
    
    /// <summary>
    /// バックアップ設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "System", "バックアップ設定", 
        Description = "設定ファイルのバックアップ設定")]
    public BackupSettings BackupSettings { get; set; } = new();
    
    /// <summary>
    /// 同期設定（将来の拡張用）
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "System", "同期設定", 
        Description = "設定の同期設定（将来の拡張用）")]
    public SyncSettings SyncSettings { get; set; } = new();
    
    // 注意: HotkeySettings は完全に削除されています（UX改善対応）
    
    /// <summary>
    /// 指定された設定レベルの設定のみを含む新しいインスタンスを作成します
    /// UI階層化サポート用メソッド
    /// </summary>
    /// <param name="level">取得する設定レベル</param>
    /// <returns>フィルタリングされた設定</returns>
    public AppSettings CreateFilteredSettings(SettingLevel level)
    {
        var filtered = new AppSettings();
        
        // 基本設定レベルに応じて設定をコピー
        switch (level)
        {
            case SettingLevel.Basic:
                // 基本設定のみをコピー
                filtered.General = General;
                filtered.Theme = Theme;
                filtered.Localization = Localization;
                filtered.Capture = Capture;
                filtered.Ocr = Ocr;
                filtered.Translation = Translation;
                filtered.Overlay = Overlay;
                filtered.MainUi = MainUi;
                break;
                
            case SettingLevel.Advanced:
                // 基本 + 詳細設定をコピー
                filtered.General = General;
                filtered.Theme = Theme;
                filtered.Localization = Localization;
                filtered.Capture = Capture;
                filtered.Ocr = Ocr;
                filtered.Translation = Translation;
                filtered.Overlay = Overlay;
                filtered.MainUi = MainUi;
                filtered.Advanced = Advanced;
                filtered.GameProfiles = GameProfiles;
                break;
                
            case SettingLevel.Debug:
                // 全ての設定をコピー
                return this; // 全設定を返す
        }
        
        return filtered;
    }
    
    /// <summary>
    /// 設定の妥当性を検証します
    /// </summary>
    /// <returns>検証結果</returns>
    public SettingsValidationResult ValidateSettings()
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        
        // スキーマバージョンチェック
        if (SchemaVersion > 1)
        {
            warnings.Add($"設定スキーマバージョン {SchemaVersion} は現在のアプリケーションより新しいため、一部設定が反映されない可能性があります");
        }
        
        // ゲームプロファイルの妥当性チェック
        foreach (var profile in GameProfiles.Values)
        {
            if (string.IsNullOrEmpty(profile.ProfileName))
            {
                errors.Add("ゲームプロファイルに名前が設定されていません");
            }
            
            if (string.IsNullOrEmpty(profile.GameExecutableName) && 
                string.IsNullOrEmpty(profile.GameWindowTitle) && 
                string.IsNullOrEmpty(profile.GameProcessName))
            {
                warnings.Add($"ゲームプロファイル '{profile.ProfileName}' にゲーム識別情報が設定されていません");
            }
        }
        
        // アクティブプロファイルの存在チェック
        if (!string.IsNullOrEmpty(ActiveGameProfileId) && 
            !GameProfiles.TryGetValue(ActiveGameProfileId, out _))
        {
            warnings.Add($"アクティブなゲームプロファイル '{ActiveGameProfileId}' が見つかりません");
        }
        
        return SettingsValidationResult.CreateSuccess();
    }
    
    /// <summary>
    /// 設定の統計情報を取得します
    /// </summary>
    /// <returns>統計情報</returns>
    public SettingsStatistics GetStatistics()
    {
        var settingsByCategory = new Dictionary<string, int>();
        var settingsByLevel = new Dictionary<SettingLevel, int>();
        int totalSettings = 0;
        int modifiedSettingsCount = 0;
        
        // 各設定カテゴリの統計を計算
        var settingsCategories = new (string name, object settings)[]
        {
            ("General", General),
            ("Theme", Theme),
            ("Localization", Localization),
            ("Capture", Capture),
            ("OCR", Ocr),
            ("Translation", Translation),
            ("Overlay", Overlay),
            ("MainUi", MainUi),
            ("Advanced", Advanced),
            ("Backup", BackupSettings),
            ("Sync", SyncSettings)
        };
        
        foreach (var (name, settings) in settingsCategories)
        {
            var properties = settings.GetType().GetProperties();
            var categoryCount = properties.Length;
            settingsByCategory[name] = categoryCount;
            totalSettings += categoryCount;
            
            // メタデータからレベル別の統計を計算
            foreach (var property in properties)
            {
                var metadata = property.GetCustomAttributes(typeof(SettingMetadataAttribute), false)
                    .Cast<SettingMetadataAttribute>()
                    .FirstOrDefault();
                    
                if (metadata != null)
                {
                    settingsByLevel[metadata.Level] = settingsByLevel.TryGetValue(metadata.Level, out var currentCount) ? currentCount + 1 : 1;
                }
            }
        }
        
        // システム設定の統計
        settingsByCategory["System"] = 5; // SchemaVersion, LastUpdated, CreatedAt, ActiveGameProfileId, FavoriteSettings
        totalSettings += 5;
        
        return new SettingsStatistics(
            totalSettings: totalSettings,
            settingsByCategory: settingsByCategory,
            gameProfileCount: GameProfiles.Count,
            modifiedSettingsCount: modifiedSettingsCount, // TODO: デフォルト値との比較実装
            favoriteSettingsCount: FavoriteSettings.Count,
            lastSaved: null, // TODO: ファイルサービスから取得
            lastLoaded: null, // TODO: ファイルサービスから取得
            changeHistoryCount: ChangeHistory.Count,
            backupCount: 0, // TODO: バックアップファイル数の計算
            settingsFileSizeBytes: 0, // TODO: 設定ファイルサイズの計算
            lastMigration: null, // TODO: 最後のマイグレーション日時
            currentSchemaVersion: SchemaVersion,
            averageSaveTimeMs: 0, // TODO: 保存時間の統計
            averageLoadTimeMs: 0, // TODO: 読み込み時間の統計
            settingsByLevel: settingsByLevel,
            activeGameProfileId: ActiveGameProfileId);
    }
}
