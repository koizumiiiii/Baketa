using System;

namespace Baketa.Core.Events.TranslationEvents
{
    /// <summary>
    /// 翻訳エラーイベント
    /// </summary>
    public class TranslationErrorEvent : EventBase
    {
        /// <summary>
        /// エラーメッセージ
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;
        
        /// <summary>
        /// エラー発生時の原文
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// エラーコード（存在する場合）
        /// </summary>
        public string? ErrorCode { get; set; }
        
        /// <summary>
        /// 関連例外
        /// </summary>
        public Exception? Exception { get; set; }
        
        /// <summary>
        /// 翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "TranslationError";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Translation";
    }
}