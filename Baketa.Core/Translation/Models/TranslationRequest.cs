using System;
using System.Collections.Generic;
using Baketa.Core.Translation.Common;

namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳リクエストを表すクラス
    /// </summary>
    public class TranslationRequest
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト（オプション）
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// リクエストオプション
        /// </summary>
        public Dictionary<string, object?> Options { get; } = new();
        
        /// <summary>
        /// リクエストのユニークID
        /// </summary>
        public Guid RequestId { get; } = Guid.NewGuid();
        
        /// <summary>
        /// リクエストのタイムスタンプ
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TranslationRequest()
        {
        }

        /// <summary>
        /// 基本パラメータを指定して翻訳リクエストを初期化
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        public TranslationRequest(string sourceText, Language sourceLanguage, Language targetLanguage, TranslationContext? context = null)
        {
            SourceText = sourceText;
            SourceLanguage = sourceLanguage;
            TargetLanguage = targetLanguage;
            Context = context;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このリクエストのクローン</returns>
        public TranslationRequest Clone()
        {
            var clone = new TranslationRequest
            {
                SourceText = SourceText,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                Context = Context?.Clone(),
                Timestamp = Timestamp
            };

            foreach (var option in Options)
            {
                clone.Options[option.Key] = option.Value;
            }

            return clone;
        }

        /// <summary>
        /// 言語ペア
        /// </summary>
        public LanguagePair LanguagePair => new LanguagePair { SourceLanguage = SourceLanguage, TargetLanguage = TargetLanguage };

        /// <summary>
        /// キャッシュキーを生成
        /// </summary>
        /// <returns>キャッシュキー</returns>
        public string GenerateCacheKey()
        {
            string contextPart = Context?.GetHashString() ?? "no-context";
            return $"{SourceLanguage.Code}|{TargetLanguage.Code}|{contextPart}|{CleanTextForCacheKey(SourceText)}";
        }

        /// <summary>
        /// キャッシュキー用にテキストをクリーンアップ
        /// </summary>
        /// <param name="text">元のテキスト</param>
        /// <returns>クリーンアップされたテキスト</returns>
        private static string CleanTextForCacheKey(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // 余分な空白を削除して正規化
            return text.Trim().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        }
    }
}
