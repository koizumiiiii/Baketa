using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Baketa.Core.Translation.Events;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Common;

    /// <summary>
    /// 翻訳関連モデルの拡張メソッド
    /// </summary>
    public static class TranslationExtensions
    {
        /// <summary>
        /// 言語オブジェクトから言語ペアを作成します
        /// </summary>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <returns>言語ペア</returns>
        public static LanguagePair ToLanguagePair(this Models.Language sourceLanguage, Models.Language targetLanguage)
        {
            ArgumentNullException.ThrowIfNull(sourceLanguage);
            ArgumentNullException.ThrowIfNull(targetLanguage);
            
            return new LanguagePair
            {
                SourceLanguage = new Models.Language
                {
                    Code = sourceLanguage.Code,
                    DisplayName = sourceLanguage.DisplayName
                },
                TargetLanguage = new Models.Language
                {
                    Code = targetLanguage.Code,
                    DisplayName = targetLanguage.DisplayName
                }
            };
        }
        
        /// <summary>
        /// 言語ペアが一致するかを判定します
        /// </summary>
        /// <param name="pair1">言語ペア1</param>
        /// <param name="pair2">言語ペア2</param>
        /// <returns>一致する場合はtrue</returns>
        public static bool Matches(this Models.LanguagePair pair1, Models.LanguagePair pair2)
        {
            if (pair1 == null || pair2 == null)
                return false;
            
            return string.Equals(pair1.SourceLanguage.Code, pair2.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(pair1.TargetLanguage.Code, pair2.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase);
        }
        
        /// <summary>
        /// 翻訳コンテキストのハッシュ文字列を取得します
        /// </summary>
        /// <param name="context">翻訳コンテキスト</param>
        /// <returns>ハッシュ文字列</returns>
        public static string GetHashString(this Models.TranslationContext? context)
        {
            if (context == null)
            {
                return "empty";
            }
            
            var builder = new StringBuilder();
            builder.Append(context.GameProfileId ?? "default");
            builder.Append('|');
            builder.Append(context.SceneId ?? "default");
            builder.Append('|');
            builder.Append(context.DialogueId ?? "default");
            builder.Append('|');
            builder.Append(context.Priority);
            builder.Append('|');
            
            if (context.Tags.Count > 0)
            {
                var sortedTags = new List<string>(context.Tags);
                sortedTags.Sort(StringComparer.OrdinalIgnoreCase);
                builder.Append(string.Join(",", sortedTags));
            }
            else
            {
                builder.Append("notags");
            }
            
            // ハッシュ計算
            var bytes = Encoding.UTF8.GetBytes(builder.ToString());
            var hash = SHA256.HashData(bytes);
            return Convert.ToHexString(hash);
        }
        
        /// <summary>
        /// 翻訳コンテキストを複製します
        /// </summary>
        /// <param name="context">元のコンテキスト</param>
        /// <returns>複製されたコンテキスト</returns>
        public static Models.TranslationContext Clone(this Models.TranslationContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            
            var clone = new Models.TranslationContext
            {
                GameProfileId = context.GameProfileId,
                SceneId = context.SceneId,
                DialogueId = context.DialogueId,
                Priority = context.Priority,
                ScreenRegion = context.ScreenRegion
            };
            
            // タグの複製
            foreach (var tag in context.Tags)
            {
                clone.Tags.Add(tag);
            }
            
            // 追加コンテキストの複製
            foreach (var kvp in context.AdditionalContext)
            {
                clone.AdditionalContext[kvp.Key] = kvp.Value;
            }
            
            return clone;
        }
        
        /// <summary>
        /// メモリキャッシュの最大項目数を取得します
        /// </summary>
        /// <param name="options">キャッシュオプション</param>
        /// <returns>最大項目数</returns>
        public static int MemoryCacheMaxItems(this TranslationCacheOptions options)
        {
            return options?.MemoryCacheSize ?? 1000;
        }
        
        /// <summary>
        /// キャッシュの有効期限を取得します
        /// </summary>
        /// <param name="options">キャッシュオプション</param>
        /// <returns>有効期限</returns>
        public static TimeSpan? CacheExpiration(this TranslationCacheOptions options)
        {
            if (options == null || options.DefaultExpirationHours <= 0)
            {
                return null;
            }
            
            return TimeSpan.FromHours(options.DefaultExpirationHours);
        }
        
        /// <summary>
        /// Web APIのリクエストタイムアウトを取得します
        /// </summary>
        /// <param name="options">Web APIオプション</param>
        /// <returns>タイムアウト時間</returns>
        public static TimeSpan RequestTimeoutSeconds(this WebApiTranslationOptions options)
        {
            if (options == null)
            {
                return TimeSpan.FromSeconds(10);
            }
            
            return TimeSpan.FromSeconds(Math.Max(1, options.TimeoutSeconds));
        }
        
        /// <summary>
        /// User-Agentを取得します
        /// </summary>
        /// <param name="options">Web APIオプション</param>
        /// <returns>User-Agent文字列</returns>
        public static string UserAgent(this WebApiTranslationOptions options)
        {
            return "Baketa-Translator/1.0";
        }
        
        /// <summary>
        /// 翻訳開始イベントからエンジン名を取得する拡張メソッド
        /// </summary>
        /// <param name="event">翻訳開始イベント</param>
        /// <returns>エンジン名</returns>
        public static string EngineName(this TranslationStartedEvent _)
        {
            return "Unknown"; // TODO: 翻訳開始時はエンジン名が不明な場合があるためデフォルト値を返す
        }
        
        /// <summary>
        /// 翻訳完了イベントからエンジン名を取得する拡張メソッド
        /// </summary>
        /// <param name="event">翻訳完了イベント</param>
        /// <returns>エンジン名</returns>
        public static string EngineName(this TranslationCompletedEvent @event)
        {
            return @event?.TranslationEngine ?? "Unknown";
        }
        
        /// <summary>
        /// 翻訳エラーイベントからエンジン名を取得する拡張メソッド
        /// </summary>
        /// <param name="event">翻訳エラーイベント</param>
        /// <returns>エンジン名</returns>
        public static string EngineName(this TranslationErrorEvent @event)
        {
            return @event?.TranslationEngine ?? "Unknown";
        }
        
        /// <summary>
        /// 言語文字列から言語コードを取得する拡張メソッド
        /// </summary>
        /// <param name="language">言語文字列</param>
        /// <returns>言語コード</returns>
        public static string Code(this string _)
        {
            return _; // 実際には言語文字列が取得されているのでそのまま返す
        }
        
        /// <summary>
        /// Languageクラスから名前を取得する拡張メソッド
        /// </summary>
        /// <param name="language">言語オブジェクト</param>
        /// <returns>言語名</returns>
        public static string Name(this Models.Language language)
        {
            // Codeから言語名を生成
            return language?.Code ?? "Unknown";
        }
    }
