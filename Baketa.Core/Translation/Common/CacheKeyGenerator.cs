using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Baketa.Core.Translation.Models;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;


namespace Baketa.Core.Translation.Common;

    /// <summary>
    /// キャッシュキーの生成に使用するユーティリティクラス
    /// </summary>
    public static partial class CacheKeyGenerator
    {
        // コンパイル時に正規表現をキャッシュするための静的パターン
        [GeneratedRegex("\\s+")]
        private static partial Regex WhitespacePattern();

        /// <summary>
        /// 翻訳リクエストからキャッシュキーを生成します
        /// </summary>
        /// <param name="request">翻訳リクエスト</param>
        /// <returns>一意のキャッシュキー</returns>
        public static string GenerateKey(TranslationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            // クリーンなテキストを使用してキーを生成
            string cleanText = CleanText(request.SourceText);
            
            // コンテキストハッシュを取得
            string contextHash = request.Context?.GetHashString() ?? "no-context";
            
            // キーを構築
            return $"{request.SourceLanguage.Code}|{request.TargetLanguage.Code}|{contextHash}|{cleanText}";
        }
        
        /// <summary>
        /// 翻訳パラメータからキャッシュキーを生成します
        /// </summary>
        /// <param name="sourceText">原文テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>一意のキャッシュキー</returns>
        public static string GenerateKey(string sourceText, Language sourceLanguage, Language targetLanguage, TranslationContext? context = null)
        {
            if (string.IsNullOrEmpty(sourceText))
                throw new ArgumentException("ソーステキストは空にできません", nameof(sourceText));
            
            ArgumentNullException.ThrowIfNull(sourceLanguage);
            ArgumentNullException.ThrowIfNull(targetLanguage);

            // クリーンなテキストを使用してキーを生成
            string cleanText = CleanText(sourceText);
            
            // コンテキストハッシュを取得
            string contextHash = context?.GetHashString() ?? "no-context";
            
            // キーを構築
            return $"{sourceLanguage.Code}|{targetLanguage.Code}|{contextHash}|{cleanText}";
        }
        
        /// <summary>
        /// 翻訳パラメータからキャッシュキーを生成します（エンジン名含む）
        /// </summary>
        /// <param name="sourceText">原文テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="engine">翻訳エンジン</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>一意のキャッシュキー</returns>
        public static string GenerateKey(string sourceText, string sourceLanguage, string targetLanguage, string engine, TranslationContext? context = null)
        {
            if (string.IsNullOrEmpty(sourceText))
                throw new ArgumentException("ソーステキストは空にできません", nameof(sourceText));
            
            if (string.IsNullOrEmpty(sourceLanguage))
                throw new ArgumentException("ソース言語は空にできません", nameof(sourceLanguage));
            
            if (string.IsNullOrEmpty(targetLanguage))
                throw new ArgumentException("ターゲット言語は空にできません", nameof(targetLanguage));
            
            if (string.IsNullOrEmpty(engine))
                throw new ArgumentException("エンジン名は空にできません", nameof(engine));

            // クリーンなテキストを使用してキーを生成
            string cleanText = CleanText(sourceText);
            
            // コンテキストハッシュを取得
            string contextHash = context?.GetHashString() ?? "no-context";
            
            // キーを構築
            return $"{sourceLanguage}|{targetLanguage}|{engine}|{contextHash}|{cleanText}";
        }
        
        /// <summary>
        /// キャッシュキー用にテキストをクリーンアップします
        /// </summary>
        /// <param name="text">クリーンアップするテキスト</param>
        /// <returns>クリーンアップされたテキスト</returns>
        private static string CleanText(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;
                
            // 余分な空白を削除して正規化
            text = text.Trim();
            text = WhitespacePattern().Replace(text, " ");
            
            // 長いテキストの場合はハッシュ化
            if (text.Length > 100)
            {
                // ハッシュの生成に効率的なメソッドを使用
                byte[] textBytes = Encoding.UTF8.GetBytes(text);
                byte[] hashBytes = SHA256.HashData(textBytes);
                string hashString = Convert.ToBase64String(hashBytes)
                                        .Replace("+", "-", StringComparison.Ordinal)
                                        .Replace("/", "_", StringComparison.Ordinal)
                                        .Replace("=", "", StringComparison.Ordinal);
                
                // キャッシュキーがユニークになるよう短いプレフィックスも含める
                return $"{text[..50]}...{hashString}";
            }
            
            return text;
        }

        /// <summary>
        /// キャッシュキーからコンポーネントを抽出します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>(ソース言語, ターゲット言語, コンテキストハッシュ, テキスト)のタプル</returns>
        public static (string sourceLanguage, string targetLanguage, string contextHash, string text) ParseKey(string key)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("キーは空にできません", nameof(key));

            string[] parts = key.Split('|');
            if (parts.Length < 4)
                throw new ArgumentException("キーのフォーマットが不正です", nameof(key));

            return (parts[0], parts[1], parts[2], parts[3]);
        }
    }
