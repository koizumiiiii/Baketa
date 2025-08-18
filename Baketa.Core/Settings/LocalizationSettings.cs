using System.Globalization;

namespace Baketa.Core.Settings;

/// <summary>
/// ローカライズ設定クラス
/// 言語・地域・文字エンコーディング設定を管理
/// </summary>
public sealed class LocalizationSettings
{
    /// <summary>
    /// アプリケーションの表示言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Localization", "表示言語", 
        Description = "アプリケーションのユーザーインターフェース言語", 
        ValidValues = ["ja-JP", "en-US", "zh-CN", "zh-TW", "ko-KR"])]
    public string UiLanguage { get; set; } = "ja-JP";
    
    /// <summary>
    /// デフォルトの翻訳元言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Localization", "翻訳元言語", 
        Description = "自動検出時のフォールバック言語", 
        ValidValues = ["ja", "en", "zh", "ko", "auto"])]
    public string DefaultSourceLanguage { get; set; } = "en";
    
    /// <summary>
    /// デフォルトの翻訳先言語
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Localization", "翻訳先言語", 
        Description = "翻訳の出力言語", 
        ValidValues = ["ja", "en", "zh-cn", "zh-tw", "ko"])]
    public string DefaultTargetLanguage { get; set; } = "ja";
    
    /// <summary>
    /// 日付・時刻の表示形式
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Localization", "日付時刻形式", 
        Description = "日付と時刻の表示形式", 
        ValidValues = [DateTimeFormat.System, DateTimeFormat.ISO8601, DateTimeFormat.US, DateTimeFormat.European])]
    public DateTimeFormat DateTimeFormat { get; set; } = DateTimeFormat.System;
    
    /// <summary>
    /// 数値の表示形式
    /// </summary>
    [SettingMetadata(SettingLevel.Basic, "Localization", "数値形式", 
        Description = "数値や通貨の表示形式", 
        ValidValues = [NumberFormat.System, NumberFormat.Invariant, NumberFormat.Japanese, NumberFormat.English])]
    public NumberFormat NumberFormat { get; set; } = NumberFormat.System;
    
    /// <summary>
    /// テキストの読み方向
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Localization", "テキスト方向", 
        Description = "テキストの読み方向", 
        ValidValues = [TextDirection.LeftToRight, TextDirection.RightToLeft, TextDirection.Auto])]
    public TextDirection TextDirection { get; set; } = TextDirection.LeftToRight;
    
    /// <summary>
    /// 文字エンコーディング
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Localization", "文字エンコーディング", 
        Description = "ファイル入出力時の文字エンコーディング", 
        ValidValues = ["UTF-8", "UTF-16", "Shift_JIS", "EUC-JP"])]
    public string DefaultEncoding { get; set; } = "UTF-8";
    
    /// <summary>
    /// タイムゾーン設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Localization", "タイムゾーン", 
        Description = "使用するタイムゾーン", 
        ValidValues = ["System", "UTC", "Asia/Tokyo", "America/New_York", "Europe/London", "Asia/Shanghai"])]
    public string TimeZone { get; set; } = "System";
    
    /// <summary>
    /// フォントレンダリング設定
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Localization", "フォントレンダリング", 
        Description = "多言語フォントのレンダリング設定", 
        ValidValues = [FontRendering.System, FontRendering.ClearType, FontRendering.Optimized])]
    public FontRendering FontRendering { get; set; } = FontRendering.System;
    
    /// <summary>
    /// 自動言語検出の精度
    /// </summary>
    [SettingMetadata(SettingLevel.Advanced, "Localization", "言語検出精度", 
        Description = "自動言語検出の精度レベル", 
        ValidValues = [LanguageDetectionAccuracy.Fast, LanguageDetectionAccuracy.Balanced, LanguageDetectionAccuracy.Accurate])]
    public LanguageDetectionAccuracy LanguageDetectionAccuracy { get; set; } = LanguageDetectionAccuracy.Balanced;
    
    /// <summary>
    /// 言語リソースの事前読み込み
    /// </summary>
    [SettingMetadata(SettingLevel.Debug, "Localization", "リソース事前読み込み", 
        Description = "言語リソースを起動時に事前読み込みします（開発者向け）")]
    public bool PreloadLanguageResources { get; set; } = false;
}

/// <summary>
/// 日付時刻表示形式
/// </summary>
public enum DateTimeFormat
{
    /// <summary>
    /// システム設定に従う
    /// </summary>
    System,
    
    /// <summary>
    /// ISO 8601形式
    /// </summary>
    ISO8601,
    
    /// <summary>
    /// アメリカ式（MM/dd/yyyy）
    /// </summary>
    US,
    
    /// <summary>
    /// ヨーロッパ式（dd/MM/yyyy）
    /// </summary>
    European
}

/// <summary>
/// 数値表示形式
/// </summary>
public enum NumberFormat
{
    /// <summary>
    /// システム設定に従う
    /// </summary>
    System,
    
    /// <summary>
    /// インバリアント形式（ピリオド区切り）
    /// </summary>
    Invariant,
    
    /// <summary>
    /// 日本式（カンマ区切り）
    /// </summary>
    Japanese,
    
    /// <summary>
    /// 英語式（カンマ区切り）
    /// </summary>
    English
}

/// <summary>
/// テキスト読み方向
/// </summary>
public enum TextDirection
{
    /// <summary>
    /// 左から右（LTR）
    /// </summary>
    LeftToRight,
    
    /// <summary>
    /// 右から左（RTL）
    /// </summary>
    RightToLeft,
    
    /// <summary>
    /// 自動検出
    /// </summary>
    Auto
}

/// <summary>
/// フォントレンダリング方式
/// </summary>
public enum FontRendering
{
    /// <summary>
    /// システム標準
    /// </summary>
    System,
    
    /// <summary>
    /// ClearType（Windows）
    /// </summary>
    ClearType,
    
    /// <summary>
    /// 最適化済み
    /// </summary>
    Optimized
}

/// <summary>
/// 言語検出精度レベル
/// </summary>
public enum LanguageDetectionAccuracy
{
    /// <summary>
    /// 高速（精度より速度を重視）
    /// </summary>
    Fast,
    
    /// <summary>
    /// バランス（精度と速度のバランス）
    /// </summary>
    Balanced,
    
    /// <summary>
    /// 高精度（速度より精度を重視）
    /// </summary>
    Accurate
}
