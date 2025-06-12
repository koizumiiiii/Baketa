namespace Baketa.Core.Settings;

/// <summary>
/// 一般設定クラス
/// アプリケーション全体の基本的な動作設定を管理
/// </summary>
public sealed class GeneralSettings
{
    /// <summary>
    /// アプリケーション起動時の自動開始
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "General", "自動開始", 
        Description = "Windowsログイン時にBaketaを自動的に開始します")]
    public bool AutoStartWithWindows { get; set; } = false;
    
    /// <summary>
    /// システムトレイに最小化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "General", "トレイ最小化", 
        Description = "ウィンドウを閉じた時にシステムトレイに最小化します")]
    public bool MinimizeToTray { get; set; } = true;
    
    /// <summary>
    /// アプリケーション終了時の確認ダイアログ表示
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "General", "終了確認", 
        Description = "アプリケーション終了時に確認ダイアログを表示します")]
    public bool ShowExitConfirmation { get; set; } = true;
    
    /// <summary>
    /// 使用統計情報の収集許可
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "General", "使用統計収集", 
        Description = "匿名の使用統計情報を収集して改善に役立てます")]
    public bool AllowUsageStatistics { get; set; } = true;
    
    /// <summary>
    /// 自動アップデート確認
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "General", "自動アップデート確認", 
        Description = "新しいバージョンが利用可能になった時に通知します")]
    public bool CheckForUpdatesAutomatically { get; set; } = true;
    
    /// <summary>
    /// パフォーマンス優先モード
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "General", "パフォーマンス優先", 
        Description = "メモリ使用量よりも処理速度を優先します")]
    public bool PerformanceMode { get; set; } = false;
    
    /// <summary>
    /// 最大メモリ使用量（MB）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "General", "最大メモリ使用量", 
        Description = "アプリケーションが使用する最大メモリ量", 
        Unit = "MB", 
        MinValue = 128, 
        MaxValue = 4096)]
    public int MaxMemoryUsageMb { get; set; } = 512;
    
    /// <summary>
    /// ログレベル
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "General", "ログレベル", 
        Description = "出力するログの詳細レベル", 
        ValidValues = new object[] { LogLevel.Error, LogLevel.Warning, LogLevel.Information, LogLevel.Debug, LogLevel.Trace })]
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
    
    /// <summary>
    /// ログファイルの保持日数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "General", "ログ保持日数", 
        Description = "ログファイルを保持する日数", 
        Unit = "日", 
        MinValue = 1, 
        MaxValue = 365)]
    public int LogRetentionDays { get; set; } = 30;
    
    /// <summary>
    /// デバッグモードの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "General", "デバッグモード", 
        Description = "デバッグ機能を有効にします（開発者向け）")]
    public bool EnableDebugMode { get; set; } = false;
    
    /// <summary>
    /// 現在アクティブなゲームプロファイルID
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "General", "アクティブプロファイル", 
        Description = "現在アクティブなゲームプロファイルのID")]
    public string? ActiveGameProfile { get; set; }
}

/// <summary>
/// ログレベル定義
/// </summary>
public enum LogLevel
{
    /// <summary>
    /// エラーのみ
    /// </summary>
    Error,
    
    /// <summary>
    /// 警告以上
    /// </summary>
    Warning,
    
    /// <summary>
    /// 情報以上
    /// </summary>
    Information,
    
    /// <summary>
    /// デバッグ以上
    /// </summary>
    Debug,
    
    /// <summary>
    /// すべて（最詳細）
    /// </summary>
    Trace
}
