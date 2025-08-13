using System;
using System.Collections.Generic;

namespace Baketa.Core.Settings;

/// <summary>
/// ゲームプロファイル設定クラス
/// ゲーム固有の最適化設定を管理
/// </summary>
public sealed class GameProfileSettings
{
    /// <summary>
    /// プロファイルの有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "プロファイル有効", 
        Description = "このゲームプロファイルを有効にします")]
    public bool IsEnabled { get; set; } = true;
    
    /// <summary>
    /// プロファイルのアクティブ状態
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "アクティブ", 
        Description = "このプロファイルが現在アクティブかどうか")]
    public bool IsActive { get; set; }
    
    /// <summary>
    /// プロファイル名
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "プロファイル名", 
        Description = "このプロファイルの表示名")]
    public string ProfileName { get; set; } = string.Empty;
    
    /// <summary>
    /// ゲーム名（ProfileNameのエイリアス）
    /// </summary>
    public string GameName
    {
        get => ProfileName;
        set => ProfileName = value;
    }
    
    /// <summary>
    /// ゲーム実行ファイル名
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "ゲーム実行ファイル", 
        Description = "対象ゲームの実行ファイル名（例：game.exe）")]
    public string GameExecutableName { get; set; } = string.Empty;
    
    /// <summary>
    /// ゲームウィンドウタイトル
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "ウィンドウタイトル", 
        Description = "対象ゲームのウィンドウタイトル（部分一致）")]
    public string GameWindowTitle { get; set; } = string.Empty;
    
    /// <summary>
    /// ゲームのプロセス名
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "プロセス名", 
        Description = "対象ゲームのプロセス名")]
    public string GameProcessName { get; set; } = string.Empty;
    
    /// <summary>
    /// プロセス名（GameProcessNameのエイリアス）
    /// </summary>
    public string ProcessName
    {
        get => GameProcessName;
        set => GameProcessName = value;
    }
    
    /// <summary>
    /// 自動適用の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "自動適用", 
        Description = "ゲーム検出時に自動的にこのプロファイルを適用します")]
    public bool AutoApplyOnGameDetection { get; set; } = true;
    
    /// <summary>
    /// プロファイル説明
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "GameProfile", "説明", 
        Description = "このプロファイルの説明文")]
    public string Description { get; set; } = string.Empty;
    
    /// <summary>
    /// 作成日時
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "作成日時", 
        Description = "プロファイルの作成日時")]
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 最終更新日時
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "更新日時", 
        Description = "プロファイルの最終更新日時")]
    public DateTime LastModified { get; set; } = DateTime.Now;
    
    /// <summary>
    /// 使用回数
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "使用回数", 
        Description = "このプロファイルが使用された回数")]
    public int UsageCount { get; set; } = 0;
    
    /// <summary>
    /// プロファイル優先度
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "優先度", 
        Description = "複数のプロファイルが適合する場合の優先度（高い数値が優先）", 
        MinValue = 0, 
        MaxValue = 100)]
    public int Priority { get; set; } = 50;
    
    /// <summary>
    /// キャプチャ設定のオーバーライド
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "キャプチャ設定", 
        Description = "ゲーム固有のキャプチャ設定を使用します")]
    public CaptureSettings? CaptureOverrides { get; set; }
    
    /// <summary>
    /// OCR設定のオーバーライド
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "OCR設定", 
        Description = "ゲーム固有のOCR設定を使用します")]
    public OcrSettings? OcrOverrides { get; set; }
    
    /// <summary>
    /// 翻訳設定のオーバーライド
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "翻訳設定", 
        Description = "ゲーム固有の翻訳設定を使用します")]
    public TranslationSettings? TranslationOverrides { get; set; }
    
    /// <summary>
    /// オーバーレイ設定のオーバーライド
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "オーバーレイ設定", 
        Description = "ゲーム固有のオーバーレイ設定を使用します")]
    public OverlaySettings? OverlayOverrides { get; set; }
    
    /// <summary>
    /// メインUI設定のオーバーライド
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "メインUI設定", 
        Description = "ゲーム固有のメインUI設定を使用します")]
    public MainUiSettings? MainUiOverrides { get; set; }
    
    /// <summary>
    /// ゲーム固有キーワード
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "ゲーム固有キーワード", 
        Description = "このゲームで使用される特別な用語や単語")]
    public IList<string> GameSpecificKeywords { get; set; } = [];
    
    /// <summary>
    /// 翻訳除外パターン
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "翻訳除外パターン", 
        Description = "翻訳しない文字列のパターン（正規表現）")]
    public IList<string> TranslationExclusionPatterns { get; set; } = [];
    
    /// <summary>
    /// カスタム前処理フィルター
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "カスタムフィルター", 
        Description = "このゲーム専用の画像前処理フィルター設定")]
    public Dictionary<string, object> CustomPreprocessingFilters { get; set; } = [];
    
    /// <summary>
    /// パフォーマンス最適化設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "パフォーマンス最適化", 
        Description = "このゲーム用のパフォーマンス最適化設定")]
    public GamePerformanceSettings PerformanceSettings { get; set; } = new();
    
    /// <summary>
    /// ゲーム特定領域設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "GameProfile", "特定領域設定", 
        Description = "ゲーム内の特定UI要素の位置設定")]
    public IList<GameAreaSetting> GameAreas { get; set; } = [];
    
    /// <summary>
    /// 統計情報
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "GameProfile", "統計情報", 
        Description = "プロファイル使用統計（開発者向け）")]
    public GameProfileStatistics Statistics { get; set; } = new();
    
    /// <summary>
    /// 特定の設定がオーバーライドされているかを確認
    /// </summary>
    /// <param name="settingType">設定の型</param>
    /// <returns>オーバーライドされている場合はtrue</returns>
    public bool HasOverride(Type settingType)
    {
        ArgumentNullException.ThrowIfNull(settingType);
        
        return settingType.Name switch
        {
            nameof(CaptureSettings) => CaptureOverrides != null,
            nameof(OcrSettings) => OcrOverrides != null,
            nameof(TranslationSettings) => TranslationOverrides != null,
            nameof(OverlaySettings) => OverlayOverrides != null,
            nameof(MainUiSettings) => MainUiOverrides != null,
            _ => false
        };
    }
    
    /// <summary>
    /// オーバーライド設定を取得
    /// </summary>
    /// <typeparam name="T">設定の型</typeparam>
    /// <returns>オーバーライド設定、存在しない場合はnull</returns>
    public T? GetOverride<T>() where T : class
    {
        return typeof(T).Name switch
        {
            nameof(CaptureSettings) => CaptureOverrides as T,
            nameof(OcrSettings) => OcrOverrides as T,
            nameof(TranslationSettings) => TranslationOverrides as T,
            nameof(OverlaySettings) => OverlayOverrides as T,
            nameof(MainUiSettings) => MainUiOverrides as T,
            _ => null
        };
    }
}

/// <summary>
/// ゲームパフォーマンス設定
/// </summary>
public sealed class GamePerformanceSettings
{
    /// <summary>
    /// フレームレート制限
    /// </summary>
    public int? MaxFrameRate { get; set; }
    
    /// <summary>
    /// CPU使用率制限（パーセント）
    /// </summary>
    public int? MaxCpuUsagePercent { get; set; }
    
    /// <summary>
    /// メモリ使用量制限（MB）
    /// </summary>
    public int? MaxMemoryUsageMb { get; set; }
    
    /// <summary>
    /// 省電力モード
    /// </summary>
    public bool PowerSaveMode { get; set; }
    
    /// <summary>
    /// 最適化レベル
    /// </summary>
    public OptimizationLevel OptimizationLevel { get; set; } = OptimizationLevel.Balanced;
}

/// <summary>
/// ゲーム内領域設定
/// </summary>
public sealed class GameAreaSetting
{
    /// <summary>
    /// 領域名
    /// </summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>
    /// 領域の種類
    /// </summary>
    public GameAreaType Type { get; set; } = GameAreaType.Text;
    
    /// <summary>
    /// X座標
    /// </summary>
    public int X { get; set; }
    
    /// <summary>
    /// Y座標
    /// </summary>
    public int Y { get; set; }
    
    /// <summary>
    /// 幅
    /// </summary>
    public int Width { get; set; }
    
    /// <summary>
    /// 高さ
    /// </summary>
    public int Height { get; set; }
    
    /// <summary>
    /// 有効化
    /// </summary>
    public bool IsEnabled { get; set; }
    
    /// <summary>
    /// 優先度
    /// </summary>
    public int Priority { get; set; }
}

/// <summary>
/// ゲームプロファイル統計情報
/// </summary>
public sealed class GameProfileStatistics
{
    /// <summary>
    /// 最終使用日時
    /// </summary>
    public DateTime? LastUsed { get; set; }
    
    /// <summary>
    /// 総使用時間（秒）
    /// </summary>
    public long TotalUsageTimeSeconds { get; set; }
    
    /// <summary>
    /// 翻訳成功回数
    /// </summary>
    public int SuccessfulTranslations { get; set; }
    
    /// <summary>
    /// 翻訳失敗回数
    /// </summary>
    public int FailedTranslations { get; set; }
    
    /// <summary>
    /// 平均翻訳時間（ミリ秒）
    /// </summary>
    public double AverageTranslationTimeMs { get; set; }
    
    /// <summary>
    /// OCR認識精度（平均）
    /// </summary>
    public double AverageOcrAccuracy { get; set; }
}

/// <summary>
/// 最適化レベル
/// </summary>
public enum OptimizationLevel
{
    /// <summary>
    /// 省エネ優先
    /// </summary>
    PowerSave,
    
    /// <summary>
    /// バランス
    /// </summary>
    Balanced,
    
    /// <summary>
    /// パフォーマンス優先
    /// </summary>
    Performance,
    
    /// <summary>
    /// 最高性能
    /// </summary>
    Maximum
}

/// <summary>
/// ゲーム内領域の種類
/// </summary>
public enum GameAreaType
{
    /// <summary>
    /// テキスト領域
    /// </summary>
    Text,
    
    /// <summary>
    /// ダイアログボックス
    /// </summary>
    Dialog,
    
    /// <summary>
    /// メニュー
    /// </summary>
    Menu,
    
    /// <summary>
    /// ステータス表示
    /// </summary>
    Status,
    
    /// <summary>
    /// チャット
    /// </summary>
    Chat,
    
    /// <summary>
    /// インベントリ
    /// </summary>
    Inventory,
    
    /// <summary>
    /// その他
    /// </summary>
    Other
}
