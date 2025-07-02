using System;

namespace Baketa.Core.Events.TranslationEvents;

    /// <summary>
    /// 翻訳完了イベント
    /// </summary>
    public class TranslationCompletedEvent : EventBase
    {
        /// <summary>
        /// 元のテキスト
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳されたテキスト
        /// </summary>
        public string TranslatedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 元の言語
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳先の言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳にかかった時間（ミリ秒）
        /// </summary>
        public long ProcessingTimeMs { get; set; }
        
        /// <summary>
        /// 翻訳ID
        /// </summary>
        public Guid TranslationId { get; set; } = Guid.NewGuid();
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationCompleted";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
    }
