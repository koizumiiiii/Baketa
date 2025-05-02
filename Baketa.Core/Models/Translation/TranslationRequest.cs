using System;
using System.Collections.Generic;

namespace Baketa.Core.Models.Translation
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
        /// ゲームタイトルやジャンルなど、翻訳の文脈を提供する情報
        /// </summary>
        public string? Context { get; set; }

        /// <summary>
        /// リクエストオプション
        /// 例: 
        /// - "MaxLength": 翻訳結果の最大長 (int)
        /// - "PreserveFormatting": 書式を保持するか (bool)
        /// - "Formality": フォーマリティレベル ("formal", "informal") (string)
        /// - "Domain": 翻訳ドメイン ("game", "tech", "general") (string)
        /// - "Priority": 処理優先度 ("high", "normal", "low") (string)
        /// </summary>
        public Dictionary<string, object?> Options { get; } = new();

        /// <summary>
        /// リクエストのユニークID
        /// </summary>
        public Guid RequestId { get; } = Guid.NewGuid();

        /// <summary>
        /// シンプルな翻訳リクエストを作成します
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <returns>翻訳リクエストインスタンス</returns>
        public static TranslationRequest Create(string sourceText, Language sourceLanguage, Language targetLanguage)
        {
            return new TranslationRequest
            {
                SourceText = sourceText,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage
            };
        }

        /// <summary>
        /// コンテキスト付きの翻訳リクエストを作成します
        /// </summary>
        /// <param name="sourceText">翻訳元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト</param>
        /// <returns>翻訳リクエストインスタンス</returns>
        public static TranslationRequest CreateWithContext(
            string sourceText,
            Language sourceLanguage,
            Language targetLanguage,
            string context)
        {
            return new TranslationRequest
            {
                SourceText = sourceText,
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Context = context
            };
        }
    }
}
