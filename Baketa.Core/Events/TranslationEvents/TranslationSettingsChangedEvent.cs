using System;

namespace Baketa.Core.Events.TranslationEvents;

    /// <summary>
    /// 翻訳設定変更イベント
    /// </summary>
    public class TranslationSettingsChangedEvent : EventBase
    {
        /// <summary>
        /// 選択された翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// ターゲット言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 自動検出フラグ
        /// </summary>
        public bool AutoDetectLanguage { get; set; }
        
        /// <summary>
        /// ソース言語（自動検出が無効の場合）
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳遅延時間（ミリ秒）
        /// </summary>
        public int TranslationDelayMs { get; set; }
        
        /// <summary>
        /// 翻訳スタイル（リテラル、自然など）
        /// </summary>
        public string TranslationStyle { get; set; } = string.Empty;
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationSettingsChanged";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
    }
