using System;
using System.Collections.Generic;

namespace Baketa.Core.Translation.Models;

    /// <summary>
    /// 翻訳キャッシュエントリ
    /// </summary>
    public class TranslationCacheEntry
    {
        /// <summary>
        /// 翻訳元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public required string TranslatedText { get; set; }
        
        /// <summary>
        /// 翻訳元言語コード
        /// </summary>
        public required string SourceLanguage { get; set; }
        
        /// <summary>
        /// 翻訳先言語コード
        /// </summary>
        public required string TargetLanguage { get; set; }
        
        /// <summary>
        /// 使用した翻訳エンジン
        /// </summary>
        public required string Engine { get; set; }
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// アクセス回数
        /// </summary>
        public int AccessCount { get; set; } = 1;
        
        /// <summary>
        /// 最終アクセス日時
        /// </summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 有効期限
        /// </summary>
        public DateTime? ExpiresAt { get; set; }
        
        /// <summary>
        /// メタデータ
        /// </summary>
        public Dictionary<string, string> Metadata { get; } = [];
        
        /// <summary>
        /// キャッシュエントリをクローンします
        /// </summary>
        /// <returns>クローンされたキャッシュエントリ</returns>
        public TranslationCacheEntry Clone()
        {
            var clone = new TranslationCacheEntry
            {
                SourceText = SourceText,
                TranslatedText = TranslatedText,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                Engine = Engine,
                CreatedAt = CreatedAt,
                AccessCount = AccessCount,
                LastAccessedAt = LastAccessedAt,
                ExpiresAt = ExpiresAt
            };
            
            foreach (var item in Metadata)
            {
                clone.Metadata[item.Key] = item.Value;
            }
            
            return clone;
        }
    }
    
    /// <summary>
    /// キャッシュ統計情報
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// 総エントリ数
        /// </summary>
        public int TotalEntries { get; set; }
        
        /// <summary>
        /// ヒット数
        /// </summary>
        public int TotalHits { get; set; }
        
        /// <summary>
        /// ミス数
        /// </summary>
        public int TotalMisses { get; set; }
        
        /// <summary>
        /// ヒット率
        /// </summary>
        public float HitRate { get; set; }
        
        /// <summary>
        /// 最大エントリ数
        /// </summary>
        public int MaxEntries { get; set; }
        
        /// <summary>
        /// 現在のサイズ（バイト）
        /// </summary>
        public long CurrentSizeBytes { get; set; }
        
        /// <summary>
        /// 統計生成日時
        /// </summary>
        public DateTime GeneratedAt { get; set; }
    }
