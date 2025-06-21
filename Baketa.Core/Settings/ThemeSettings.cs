namespace Baketa.Core.Settings;

/// <summary>
/// テーマ設定クラス
/// アプリケーションの外観とテーマ設定を管理
/// </summary>
public sealed class ThemeSettings
{
    /// <summary>
    /// アプリケーションテーマ
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Theme", "アプリケーションテーマ", 
        Description = "アプリケーション全体のテーマ", 
        ValidValues = new object[] { UiTheme.Light, UiTheme.Dark, UiTheme.Auto })]
    public UiTheme AppTheme { get; set; } = UiTheme.Auto;
    
    /// <summary>
    /// アクセントカラー（ARGB形式）
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Theme", "アクセントカラー", 
        Description = "アプリケーションのアクセントカラー（ARGB形式）")]
    public uint AccentColor { get; set; } = 0xFF0078D4; // Windows Blue
    
    /// <summary>
    /// フォントファミリー
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Theme", "フォントファミリー", 
        Description = "アプリケーションで使用するフォント")]
    public string FontFamily { get; set; } = "Yu Gothic UI";
    
    /// <summary>
    /// ベースフォントサイズ
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Theme", "ベースフォントサイズ", 
        Description = "アプリケーションの基本フォントサイズ", 
        Unit = "pt", 
        MinValue = 9, 
        MaxValue = 24)]
    public int BaseFontSize { get; set; } = 12;
    
    /// <summary>
    /// ハイコントラストモード
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Theme", "ハイコントラスト", 
        Description = "視認性向上のためのハイコントラストモード")]
    public bool HighContrastMode { get; set; } = false;
    
    /// <summary>
    /// DPIスケーリング対応
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "DPIスケーリング", 
        Description = "高DPI環境での自動スケーリングを有効にします")]
    public bool EnableDpiScaling { get; set; } = true;
    
    /// <summary>
    /// カスタムスケールファクター
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "カスタムスケール", 
        Description = "独自のスケールファクター（1.0=100%）", 
        MinValue = 0.5, 
        MaxValue = 3.0)]
    public double CustomScaleFactor { get; set; } = 1.0;
    
    /// <summary>
    /// アニメーション効果の有効化
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "アニメーション効果", 
        Description = "UI要素のアニメーション効果を有効にします")]
    public bool EnableAnimations { get; set; } = true;
    
    /// <summary>
    /// アニメーション速度
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "アニメーション速度", 
        Description = "アニメーション効果の速度調整", 
        ValidValues = new object[] { AnimationSpeed.Slow, AnimationSpeed.Normal, AnimationSpeed.Fast })]
    public AnimationSpeed AnimationSpeed { get; set; } = AnimationSpeed.Normal;
    
    /// <summary>
    /// ウィンドウの角丸効果
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "ウィンドウ角丸", 
        Description = "ウィンドウの角を丸く表示します")]
    public bool RoundedWindowCorners { get; set; } = true;
    
    /// <summary>
    /// 半透明効果（ブラー）
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Theme", "半透明効果", 
        Description = "ウィンドウ背景に半透明効果を適用します")]
    public bool EnableBlurEffect { get; set; } = true;
    
    /// <summary>
    /// カスタムCSS適用
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Theme", "カスタムCSS", 
        Description = "カスタムCSSスタイルの適用を有効にします（開発者向け）")]
    public bool EnableCustomCss { get; set; } = false;
    
    /// <summary>
    /// カスタムCSSファイルパス
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Theme", "CSSファイルパス", 
        Description = "適用するカスタムCSSファイルのパス（開発者向け）")]
    public string CustomCssFilePath { get; set; } = string.Empty;
}



/// <summary>
/// アニメーション速度定義
/// </summary>
public enum AnimationSpeed
{
    /// <summary>
    /// 低速（アクセシビリティ重視）
    /// </summary>
    Slow,
    
    /// <summary>
    /// 標準速度
    /// </summary>
    Normal,
    
    /// <summary>
    /// 高速（レスポンシブ性重視）
    /// </summary>
    Fast
}
