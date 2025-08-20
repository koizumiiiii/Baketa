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
        public static string UserAgent(this WebApiTranslationOptions _)
        {
            return "Baketa-Translator/1.0";
        }
        
        /// <summary>
        /// 翻訳開始イベントからエンジン名を取得する拡張メソッド
        /// </summary>
        /// <param name="startedEvent">翻訳開始イベント</param>
        /// <returns>エンジン名</returns>
        public static string EngineName(this TranslationStartedEvent _)
        {
            // 翻訳開始時はエンジン名が未確定の場合が多いため、デフォルト値を返す
            // 将来的にはTranslationStartedEventにEngineNameプロパティが追加される可能性がある
            return "TranslationStartedEvent";
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
        /// 同言語ペアかどうかを判定します（翻訳をスキップすべきかの判定）
        /// Issue #147 Phase 0.2 拡張: auto言語の賢い判定
        /// </summary>
        /// <param name="languagePair">言語ペア</param>
        /// <returns>同言語ペアの場合はtrue（翻訳スキップ推奨）</returns>
        public static bool IsSameLanguagePair(this Models.LanguagePair languagePair)
        {
            ArgumentNullException.ThrowIfNull(languagePair);
            ArgumentNullException.ThrowIfNull(languagePair.SourceLanguage);
            ArgumentNullException.ThrowIfNull(languagePair.TargetLanguage);

            // 言語コードの正規化（大文字小文字無視、トリム）
            var sourceCode = languagePair.SourceLanguage.Code?.Trim();
            var targetCode = languagePair.TargetLanguage.Code?.Trim();

            if (string.IsNullOrEmpty(sourceCode) || string.IsNullOrEmpty(targetCode))
            {
                return false; // 不明な言語は翻訳を試行
            }

            // 1. 厳密な同言語判定
            if (string.Equals(sourceCode, targetCode, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // 🚀 [ISSUE_147_PHASE_0_2_ENHANCED] auto言語の賢い判定
            // 2. auto → 具体的言語のパターン判定
            if (string.Equals(sourceCode, "auto", StringComparison.OrdinalIgnoreCase))
            {
                // autoが検出される可能性の高い言語が対象言語と同じ場合はスキップ
                // 実際のユースケース: 英語テキストでauto→en翻訳要求 = 無意味
                var commonAutoDetectedLanguages = new[] { "en", "ja", "zh", "zh-hans", "zh-hant" };
                
                if (commonAutoDetectedLanguages.Contains(targetCode, StringComparer.OrdinalIgnoreCase))
                {
                    // auto-en, auto-ja など、自動検出の結果と対象言語が同じになる可能性が高い
                    return true;
                }
            }

            // 3. 具体的言語 → auto のパターン判定
            if (string.Equals(targetCode, "auto", StringComparison.OrdinalIgnoreCase))
            {
                // 「en → auto」のような逆方向も無意味
                return true;
            }

            return false;
        }

        /// <summary>
        /// 翻訳をスキップすべきかどうかを判定します
        /// </summary>
        /// <param name="translationRequest">翻訳リクエスト</param>
        /// <returns>翻訳をスキップすべき場合はtrue</returns>
        public static bool ShouldSkipTranslation(this Models.TranslationRequest translationRequest)
        {
            ArgumentNullException.ThrowIfNull(translationRequest);

            // 1. 同言語ペアの場合はスキップ
            var languagePair = translationRequest.LanguagePair;
            if (languagePair.IsSameLanguagePair())
            {
                return true;
            }

            // 2. 空のテキストの場合はスキップ
            if (string.IsNullOrWhiteSpace(translationRequest.SourceText))
            {
                return true;
            }

            // 将来拡張: 他のスキップ条件をここに追加
            // - 文字数制限チェック
            // - 特定の文字パターン除外
            // - ユーザー設定によるフィルタリング

            return false;
        }

        /// <summary>
        /// 言語文字列から言語コードを取得する拡張メソッド
        /// </summary>
        /// <param name="languageString">言語文字列</param>
        /// <returns>言語コード</returns>
        public static string Code(this string languageString)
        {
            // 言語文字列が既にコード形式の場合はそのまま返す
            // 将来的には言語名からコードへの変換処理が必要になる可能性がある
            return languageString ?? "unknown";
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
