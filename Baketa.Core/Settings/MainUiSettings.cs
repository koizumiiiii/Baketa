using System;
using System.Drawing;

namespace Baketa.Core.Settings;

/// <summary>
/// メイン操作UI設定クラス（新規追加）
/// デュアルモード翻訳インターフェースの操作パネル設定を管理
/// </summary>
public sealed class MainUiSettings
{
    /// <summary>
    /// 翻訳パネルの画面上の位置
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "パネル位置", 
        Description = "翻訳パネルの画面上の位置")]
    public Point PanelPosition { get; set; } = new Point(50, 50);
    
    /// <summary>
    /// 翻訳パネルの画面上の位置（X座標）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "パネル位置X", 
        Description = "翻訳パネルの画面上のX座標位置", 
        Unit = "px", 
        MinValue = 0, 
        MaxValue = 3840)]
    public int PanelPositionX { get; set; } = 50;
    
    /// <summary>
    /// 翻訳パネルの画面上の位置（Y座標）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "パネル位置Y", 
        Description = "翻訳パネルの画面上のY座標位置", 
        Unit = "px", 
        MinValue = 0, 
        MaxValue = 2160)]
    public int PanelPositionY { get; set; } = 50;
    
    /// <summary>
    /// 翻訳パネルの透明度（0.0-1.0）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "透明度", 
        Description = "翻訳パネルの透明度（0.0=完全透明、1.0=完全不透明）", 
        MinValue = 0.1, 
        MaxValue = 1.0)]
    public double PanelOpacity { get; set; } = 0.8;
    
    /// <summary>
    /// 未使用時の自動非表示機能を使用するか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "自動非表示", 
        Description = "操作がない場合に自動的にパネルを非表示にします")]
    public bool AutoHideWhenIdle { get; set; } = true;
    
    /// <summary>
    /// 自動非表示までの時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "自動非表示時間", 
        Description = "自動非表示が実行されるまでの待機時間", 
        Unit = "秒", 
        MinValue = 3, 
        MaxValue = 300)]
    public int AutoHideDelaySeconds { get; set; } = 10;
    
    /// <summary>
    /// マウスホバー時の表示強調を行うか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "ホバー強調", 
        Description = "マウスをパネル上に移動した時に表示を強調します")]
    public bool HighlightOnHover { get; set; } = true;
    
    /// <summary>
    /// 翻訳パネルのサイズ（小・中・大）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "パネルサイズ", 
        Description = "翻訳パネルの表示サイズ", 
        ValidValues = new object[] { UiSize.Small, UiSize.Medium, UiSize.Large })]
    public UiSize PanelSize { get; set; } = UiSize.Small;
    
    /// <summary>
    /// 常に最前面に表示するか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "最前面表示", 
        Description = "翻訳パネルを常に他のウィンドウの前面に表示します", 
        WarningMessage = "無効にするとゲーム画面の下に隠れる可能性があります")]
    public bool AlwaysOnTop { get; set; } = true;
    
    /// <summary>
    /// 単発翻訳結果の表示時間（秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "単発翻訳表示時間", 
        Description = "単発翻訳結果が自動的に消えるまでの時間", 
        Unit = "秒", 
        MinValue = 3, 
        MaxValue = 60)]
    public int SingleShotDisplayTime { get; set; } = 10;
    
    /// <summary>
    /// 翻訳パネルをドラッグで移動可能にするか
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "MainUi", "ドラッグ移動", 
        Description = "翻訳パネルをマウスドラッグで移動できるようにします")]
    public bool EnableDragging { get; set; } = true;
    
    /// <summary>
    /// 翻訳パネルの境界スナップ機能を使用するか
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "MainUi", "境界スナップ", 
        Description = "パネルが画面端に近づいた時に自動的に端に吸着させます")]
    public bool EnableBoundarySnap { get; set; } = true;
    
    /// <summary>
    /// 境界スナップの距離（ピクセル）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "MainUi", "スナップ距離", 
        Description = "境界スナップが発動する画面端からの距離", 
        Unit = "px", 
        MinValue = 5, 
        MaxValue = 100)]
    public int BoundarySnapDistance { get; set; } = 20;
    
    /// <summary>
    /// パネルアニメーション効果を有効にするか
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "MainUi", "アニメーション効果", 
        Description = "パネルの表示・非表示時にアニメーション効果を適用します")]
    public bool EnableAnimations { get; set; } = true;
    
    /// <summary>
    /// アニメーション持続時間（ミリ秒）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "MainUi", "アニメーション時間", 
        Description = "アニメーション効果の持続時間", 
        Unit = "ms", 
        MinValue = 100, 
        MaxValue = 2000)]
    public int AnimationDurationMs { get; set; } = 300;
    
    /// <summary>
    /// パネルのテーマスタイル
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "MainUi", "テーマスタイル", 
        Description = "翻訳パネルの外観テーマ", 
        ValidValues = new object[] { UiTheme.Light, UiTheme.Dark, UiTheme.Auto })]
    public UiTheme ThemeStyle { get; set; } = UiTheme.Auto;
    
    /// <summary>
    /// デバッグ情報表示の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "MainUi", "デバッグ情報表示", 
        Description = "パネルにデバッグ情報を表示します（開発者向け）")]
    public bool ShowDebugInfo { get; set; } = false;
    
    /// <summary>
    /// フレームレート表示の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "MainUi", "FPS表示", 
        Description = "フレームレート情報を表示します（開発者向け）")]
    public bool ShowFrameRate { get; set; } = false;
}

/// <summary>
/// UI要素のサイズ定義
/// </summary>
public enum UiSize
{
    /// <summary>
    /// 小サイズ（コンパクト）
    /// </summary>
    Small,
    
    /// <summary>
    /// 中サイズ（標準）
    /// </summary>
    Medium,
    
    /// <summary>
    /// 大サイズ（見やすさ重視）
    /// </summary>
    Large
}
