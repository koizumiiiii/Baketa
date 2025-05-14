using System;
using System.Collections.Generic;
using System.Globalization;

namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳キャッシュエントリクラス
    /// </summary>
    public class TranslationCacheEntry
    {
        /// <summary>
        /// 原文テキスト
        /// </summary>
        public string SourceText { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳されたテキスト
        /// </summary>
        public string TranslatedText { get; set; } = string.Empty;
        
        /// <summary>
        /// 元言語
        /// </summary>
        public string SourceLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public string TargetLanguage { get; set; } = string.Empty;
        
        /// <summary>
        /// 翻訳エンジン
        /// </summary>
        public string Engine { get; set; } = string.Empty;
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// 最終アクセス日時
        /// </summary>
        public DateTime LastAccessedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// アクセス回数
        /// </summary>
        public int AccessCount { get; set; } = 1;
        
        /// <summary>
        /// メタデータ
        /// </summary>
        public Dictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// デフォルトコンストラクタ
        /// </summary>
        public TranslationCacheEntry()
        {
        }

        /// <summary>
        /// 翻訳レスポンスからキャッシュエントリを作成
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        /// <returns>キャッシュエントリ</returns>
        public static TranslationCacheEntry FromResponse(TranslationResponse response)
        {
            ArgumentNullException.ThrowIfNull(response);

            if (!response.IsSuccess || response.TranslatedText == null)
            {
                throw new ArgumentException("成功した翻訳レスポンスからのみキャッシュエントリを作成できます", nameof(response));
            }

            var entry = new TranslationCacheEntry
            {
                SourceText = response.SourceText,
                TranslatedText = response.TranslatedText,
                SourceLanguage = response.SourceLanguage.Code,
                TargetLanguage = response.TargetLanguage.Code,
                Engine = response.EngineName,
                CreatedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow,
                AccessCount = 1
            };

            // メタデータの追加
            if (response.ConfidenceScore >= 0)
            {
                entry.Metadata["ConfidenceScore"] = response.ConfidenceScore.ToString("F2", CultureInfo.InvariantCulture);
            }

            if (response.ProcessingTimeMs > 0)
            {
                entry.Metadata["ProcessingTimeMs"] = response.ProcessingTimeMs.ToString(CultureInfo.InvariantCulture);
            }

            return entry;
        }

        /// <summary>
        /// キャッシュエントリから翻訳レスポンスを作成
        /// </summary>
        /// <param name="request">元のリクエスト</param>
        /// <returns>翻訳レスポンス</returns>
        public TranslationResponse ToResponse(TranslationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var response = new TranslationResponse
            {
                RequestId = request.RequestId,
                SourceText = SourceText,
                TranslatedText = TranslatedText,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                EngineName = $"{Engine} (cached)",
                IsSuccess = true,
                Timestamp = DateTime.UtcNow
            };

            // メタデータの追加
            if (Metadata.TryGetValue("ConfidenceScore", out var confidenceScoreStr) && 
                float.TryParse(confidenceScoreStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var confidenceScore))
            {
                response.ConfidenceScore = confidenceScore;
                response.Metadata["ConfidenceScore"] = confidenceScore;
            }

            if (Metadata.TryGetValue("ProcessingTimeMs", out var processingTimeStr) && 
                long.TryParse(processingTimeStr, NumberStyles.Any, CultureInfo.InvariantCulture, out var processingTime))
            {
                response.ProcessingTimeMs = processingTime;
                response.Metadata["ProcessingTimeMs"] = processingTime;
            }

            response.Metadata["CacheCreatedAt"] = CreatedAt;
            response.Metadata["CacheAccessCount"] = AccessCount;
            response.Metadata["FromCache"] = true;

            return response;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このエントリのクローン</returns>
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
                LastAccessedAt = LastAccessedAt,
                AccessCount = AccessCount
            };

            foreach (var item in Metadata)
            {
                clone.Metadata[item.Key] = item.Value;
            }

            return clone;
        }
    }

    /// <summary>
    /// キャッシュ統計情報クラス
    /// </summary>
    public class CacheStatistics
    {
        /// <summary>
        /// キャッシュアイテム数
        /// </summary>
        public int ItemCount { get; set; }
        
        /// <summary>
        /// キャッシュサイズ（バイト）
        /// </summary>
        public long SizeInBytes { get; set; }
        
        /// <summary>
        /// キャッシュヒット数
        /// </summary>
        public long Hits { get; set; }
        
        /// <summary>
        /// キャッシュミス数
        /// </summary>
        public long Misses { get; set; }
        
        /// <summary>
        /// ヒット率
        /// </summary>
        public double HitRate => Hits + Misses == 0 ? 0 : (double)Hits / (Hits + Misses);
        
        /// <summary>
        /// 最も古いエントリの作成日時
        /// </summary>
        public DateTime? OldestEntryCreatedAt { get; set; }
        
        /// <summary>
        /// 最も新しいエントリの作成日時
        /// </summary>
        public DateTime? NewestEntryCreatedAt { get; set; }
        
        /// <summary>
        /// 最も古いアクセス日時
        /// </summary>
        public DateTime? OldestAccessedAt { get; set; }
        
        /// <summary>
        /// 最も新しいアクセス日時
        /// </summary>
        public DateTime? NewestAccessedAt { get; set; }
        
        /// <summary>
        /// 言語ペア統計
        /// </summary>
        public Dictionary<string, int> LanguagePairStats { get; set; } = new Dictionary<string, int>();
        
        /// <summary>
        /// エンジン統計
        /// </summary>
        public Dictionary<string, int> EngineStats { get; set; } = new Dictionary<string, int>();
    }
    
    /// <summary>
    /// キャッシュマージ戦略列挙型
    /// </summary>
    public enum CacheMergeStrategy
    {
        /// <summary>
        /// 既存のエントリを置き換える
        /// </summary>
        ReplaceExisting,
        
        /// <summary>
        /// 既存のエントリを保持する
        /// </summary>
        KeepExisting,
        
        /// <summary>
        /// より新しいエントリを使用
        /// </summary>
        UseNewer,
        
        /// <summary>
        /// アクセス回数が多いエントリを使用
        /// </summary>
        UseMoreAccessed
    }
}
