using System;

namespace Baketa.Core.Events
{
    /// <summary>
    /// アクセシビリティ設定変更イベント
    /// </summary>
    public class AccessibilitySettingsChangedEvent : EventBase
    {
        /// <summary>
        /// アニメーション無効化フラグ
        /// </summary>
        public bool DisableAnimations { get; set; }
        
        /// <summary>
        /// ハイコントラストモードフラグ
        /// </summary>
        public bool HighContrastMode { get; set; }
        
        /// <summary>
        /// フォントサイズ倍率
        /// </summary>
        public double FontScaleFactor { get; set; }
        
        /// <summary>
        /// キーボードフォーカスを常に表示するフラグ
        /// </summary>
        public bool AlwaysShowKeyboardFocus { get; set; }
        
        /// <summary>
        /// キーボードナビゲーション速度
        /// </summary>
        public double KeyboardNavigationSpeed { get; set; }
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "AccessibilitySettingsChanged";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "UI.Settings";
    }
    
    /// <summary>
    /// フォント設定変更イベント
    /// </summary>
    public class FontSettingsChangedEvent : EventBase
    {
        /// <summary>
        /// プライマリフォントファミリー
        /// </summary>
        public string PrimaryFontFamily { get; set; } = string.Empty;
        
        /// <summary>
        /// 日本語フォントファミリー
        /// </summary>
        public string JapaneseFontFamily { get; set; } = string.Empty;
        
        /// <summary>
        /// 英語フォントファミリー
        /// </summary>
        public string EnglishFontFamily { get; set; } = string.Empty;
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "FontSettingsChanged";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "UI.Settings";
    }
    
    // NotificationEventクラスは削除 - Baketa.Core.Events.EventTypes.NotificationEventを使用
}