using System;

namespace Baketa.UI.Models;

    /// <summary>
    /// 翻訳履歴アイテムのデータモデル
    /// </summary>
    internal class TranslationHistoryItem
    {
        public string Id { get; set; } = string.Empty;
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public string SourceLanguage { get; set; } = string.Empty;
        public string TargetLanguage { get; set; } = string.Empty;
        public string Engine { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }
