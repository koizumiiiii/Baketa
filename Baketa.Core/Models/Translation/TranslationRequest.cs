using System;
using System.Collections.Generic;

namespace Baketa.Core.Models.Translation
{
    /// <summary>
    /// 翻訳リクエストを表すクラス
    /// </summary>
    [Obsolete("代わりに Baketa.Core.Translation.Models.TranslationRequest を使用してください。", false)]
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
        
        /// <summary>
        /// 暗黙的変換演算子 - 元のリクエストオブジェクトを新しい名前空間のオブジェクトに変換
        /// </summary>
        /// <param name="request">変換するリクエストオブジェクト</param>
        public static implicit operator Baketa.Core.Translation.Models.TranslationRequest(TranslationRequest request)
        {
            if (request == null) return null!;
            
            var newRequest = new Baketa.Core.Translation.Models.TranslationRequest
            {
                SourceText = request.SourceText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage
            };
            
            // Contextの変換 - 文字列からTranslationContextオブジェクトへ
            if (!string.IsNullOrEmpty(request.Context))
            {
                newRequest.Context = new Baketa.Core.Translation.Models.TranslationContext
                {
                    DialogueId = request.Context
                };
            }
            
            // オプションのコピー
            foreach (var option in request.Options)
            {
                newRequest.Options[option.Key] = option.Value;
            }
            
            return newRequest;
        }
        
        /// <summary>
        /// 新しい名前空間のリクエストオブジェクトに変換するメソッド
        /// （暗黙的変換演算子と同等の機能）
        /// </summary>
        /// <returns>新しい名前空間のリクエストオブジェクト</returns>
        public Baketa.Core.Translation.Models.TranslationRequest ToTranslationRequest()
        {
            return (Baketa.Core.Translation.Models.TranslationRequest)this;
        }
        
        /// <summary>
        /// 明示的変換演算子 - 新しい名前空間のリクエストオブジェクトを元のオブジェクトに変換
        /// </summary>
        /// <param name="request">変換するリクエストオブジェクト</param>
        public static explicit operator TranslationRequest(Baketa.Core.Translation.Models.TranslationRequest request)
        {
            if (request == null) return null!;
            
            var oldRequest = new TranslationRequest
            {
                SourceText = request.SourceText,
                SourceLanguage = (Language)(object)request.SourceLanguage,
                TargetLanguage = (Language)(object)request.TargetLanguage
            };
            
            // Contextの変換 - TranslationContextオブジェクトから文字列へ
            if (request.Context != null)
            {
                oldRequest.Context = request.Context.DialogueId ?? request.Context.ToString();
            }
            
            // オプションのコピー
            foreach (var option in request.Options)
            {
                oldRequest.Options[option.Key] = option.Value;
            }
            
            return oldRequest;
        }
        
        /// <summary>
        /// 新しい名前空間のリクエストオブジェクトから変換するメソッド
        /// （明示的変換演算子と同等の機能）
        /// </summary>
        /// <param name="request">新しい名前空間のリクエストオブジェクト</param>
        /// <returns>元のリクエストオブジェクト</returns>
        public static TranslationRequest FromTranslationRequest(Baketa.Core.Translation.Models.TranslationRequest request)
        {
            return (TranslationRequest)request;
        }
    }
}