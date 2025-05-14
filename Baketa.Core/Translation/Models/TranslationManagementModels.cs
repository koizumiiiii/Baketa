using System;
using System.Collections.Generic;

namespace Baketa.Core.Translation.Models
{
    /// <summary>
    /// 翻訳レコードを表すクラス
    /// </summary>
    public class TranslationRecord
    {
        /// <summary>
        /// レコードID
        /// </summary>
        public required Guid Id { get; set; }
        
        /// <summary>
        /// 元テキスト
        /// </summary>
        public required string SourceText { get; set; }
        
        /// <summary>
        /// 翻訳結果テキスト
        /// </summary>
        public required string TranslatedText { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public required Language SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public required Language TargetLanguage { get; set; }
        
        /// <summary>
        /// 使用された翻訳エンジン
        /// </summary>
        public required string TranslationEngine { get; set; }
        
        /// <summary>
        /// 翻訳コンテキスト
        /// </summary>
        public TranslationContext? Context { get; set; }
        
        /// <summary>
        /// 作成日時
        /// </summary>
        public required DateTime CreatedAt { get; set; }
        
        /// <summary>
        /// 最終更新日時
        /// </summary>
        public DateTime? UpdatedAt { get; set; }
        
        /// <summary>
        /// 使用回数
        /// </summary>
        public int UsageCount { get; set; }
        
        /// <summary>
        /// 最終使用日時
        /// </summary>
        public DateTime? LastUsedAt { get; set; }
        
        /// <summary>
        /// ユーザー編集済みフラグ
        /// </summary>
        public bool IsUserEdited { get; set; }
        
        private readonly Dictionary<string, object?> _metadata = [];
        
        /// <summary>
        /// 追加メタデータ
        /// </summary>
        public IReadOnlyDictionary<string, object?> Metadata => _metadata;

        // 必要に応じてコンストラクタを追加してください。デフォルトコンストラクタは自動的に生成されます。

        /// <summary>
        /// 翻訳レスポンスからレコードを作成
        /// </summary>
        /// <param name="response">翻訳レスポンス</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>翻訳レコード</returns>
        public static TranslationRecord FromResponse(TranslationResponse response, TranslationContext? context = null)
        {
            ArgumentNullException.ThrowIfNull(response);
            
            if (!response.IsSuccess || response.TranslatedText == null)
            {
                throw new ArgumentException("成功した翻訳レスポンスからのみレコードを作成できます", nameof(response));
            }

            var record = new TranslationRecord
            {
                Id = Guid.NewGuid(),
                SourceText = response.SourceText,
                TranslatedText = response.TranslatedText,
                SourceLanguage = response.SourceLanguage,
                TargetLanguage = response.TargetLanguage,
                TranslationEngine = response.EngineName,
                Context = context?.Clone() ?? null,
                CreatedAt = DateTime.UtcNow,
                UsageCount = 1,
                LastUsedAt = DateTime.UtcNow,
                IsUserEdited = false
            };

            // メタデータの追加
            if (response.ConfidenceScore >= 0)
            {
                (record._metadata as Dictionary<string, object?>)["ConfidenceScore"] = response.ConfidenceScore;
            }

            if (response.ProcessingTimeMs > 0)
            {
                (record._metadata as Dictionary<string, object?>)["ProcessingTimeMs"] = response.ProcessingTimeMs;
            }

            return record;
        }

        /// <summary>
        /// レコードから翻訳レスポンスを作成
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
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                EngineName = $"{TranslationEngine} (record)",
                IsSuccess = true,
                Timestamp = DateTime.UtcNow
            };

            // メタデータの追加
            if (Metadata.TryGetValue("ConfidenceScore", out var confidenceScore) && 
                confidenceScore is float confidenceScoreFloat)
            {
                response.ConfidenceScore = confidenceScoreFloat;
            }

            if (Metadata.TryGetValue("ProcessingTimeMs", out var processingTime) && 
                processingTime is long processingTimeLong)
            {
                response.ProcessingTimeMs = processingTimeLong;
            }

            var responseMeta = (response.Metadata as Dictionary<string, object?>);
            if (responseMeta != null)
            {
                responseMeta["RecordId"] = Id;
                responseMeta["RecordCreatedAt"] = CreatedAt;
                responseMeta["RecordUsageCount"] = UsageCount;
                responseMeta["IsUserEdited"] = IsUserEdited;
                responseMeta["FromRecord"] = true;
            }

            return response;
        }

        /// <summary>
        /// キャッシュエントリからレコードを作成
        /// </summary>
        /// <param name="entry">キャッシュエントリ</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <returns>翻訳レコード</returns>
        public static TranslationRecord FromCacheEntry(TranslationCacheEntry entry, TranslationContext? context = null)
        {
        ArgumentNullException.ThrowIfNull(entry);
        
        var record = new TranslationRecord
            {
                Id = Guid.NewGuid(),
                SourceText = entry.SourceText,
                TranslatedText = entry.TranslatedText,
                SourceLanguage = Language.FromCode(entry.SourceLanguage),
                TargetLanguage = Language.FromCode(entry.TargetLanguage),
                TranslationEngine = entry.Engine,
                Context = context?.Clone() ?? null,
                CreatedAt = entry.CreatedAt,
                UsageCount = entry.AccessCount,
                LastUsedAt = entry.LastAccessedAt,
                IsUserEdited = false
            };

            // メタデータの追加
            if (entry.Metadata.TryGetValue("ConfidenceScore", out var confidenceScoreStr) && 
                float.TryParse(confidenceScoreStr, out var confidenceScore))
            {
                (record._metadata as Dictionary<string, object?>)["ConfidenceScore"] = confidenceScore;
            }

            if (entry.Metadata.TryGetValue("ProcessingTimeMs", out var processingTimeStr) && 
                long.TryParse(processingTimeStr, out var processingTime))
            {
                (record._metadata as Dictionary<string, object?>)["ProcessingTimeMs"] = processingTime;
            }

            return record;
        }

        /// <summary>
        /// クローンを作成
        /// </summary>
        /// <returns>このレコードのクローン</returns>
        public TranslationRecord Clone()
        {
        var clone = new TranslationRecord
        {
                Id = Id,
                SourceText = SourceText,
                TranslatedText = TranslatedText,
                SourceLanguage = SourceLanguage,
                TargetLanguage = TargetLanguage,
                TranslationEngine = TranslationEngine,
                Context = Context?.Clone() ?? null,
                CreatedAt = CreatedAt,
                UpdatedAt = UpdatedAt,
                UsageCount = UsageCount,
                LastUsedAt = LastUsedAt,
                IsUserEdited = IsUserEdited
            };

            foreach (var item in Metadata)
            {
                (clone._metadata as Dictionary<string, object?>)[item.Key] = item.Value;
            }

            return clone;
        }
    }

    /// <summary>
    /// 翻訳検索クエリを表すクラス
    /// </summary>
    public class TranslationSearchQuery
    {
        /// <summary>
        /// テキスト検索パターン
        /// </summary>
        public string? TextPattern { get; set; }
        
        /// <summary>
        /// 元言語フィルター
        /// </summary>
        public Language? SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語フィルター
        /// </summary>
        public Language? TargetLanguage { get; set; }
        
        /// <summary>
        /// エンジン名フィルター
        /// </summary>
        public string? EngineName { get; set; }
        
        /// <summary>
        /// ゲームプロファイルIDフィルター
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// タグフィルター
        /// </summary>
        private readonly List<string> _tags = [];
        
        /// <summary>
        /// タグフィルター
        /// </summary>
        public IReadOnlyList<string> Tags => _tags;
        
        /// <summary>
        /// 作成日時範囲の開始
        /// </summary>
        public DateTime? CreatedAfter { get; set; }
        
        /// <summary>
        /// 作成日時範囲の終了
        /// </summary>
        public DateTime? CreatedBefore { get; set; }
        
        /// <summary>
        /// ユーザー編集済みフィルター
        /// </summary>
        public bool? IsUserEdited { get; set; }
        
        /// <summary>
        /// 最大結果数
        /// </summary>
        public int Limit { get; set; } = 100;
        
        /// <summary>
        /// 結果オフセット
        /// </summary>
        public int Offset { get; set; }
        
        /// <summary>
        /// 並べ替えフィールド
        /// </summary>
        public string SortField { get; set; } = "CreatedAt";
        
        /// <summary>
        /// 昇順か降順か
        /// </summary>
        public bool SortAscending { get; set; }
    }

    /// <summary>
    /// 翻訳統計を表すクラス
    /// </summary>
    public class TranslationStatistics
    {
        // デフォルトコンストラクタは自動的に生成されます。プロパティのデフォルト値は宣言時に設定済み。

        /// <summary>
        /// 総翻訳レコード数
        /// </summary>
        public int TotalRecords { get; set; }
        
        /// <summary>
        /// ユーザー編集済みレコード数
        /// </summary>
        public int UserEditedRecords { get; set; }
        
        private readonly Dictionary<string, int> _recordsByLanguagePair = [];
        private readonly Dictionary<string, int> _recordsByEngine = [];
        private readonly Dictionary<string, int> _recordsByGameProfile = [];
        private readonly Dictionary<string, int> _recordsByTag = [];
        private readonly Dictionary<string, int> _recordsByTimeFrame = [];
        
        /// <summary>
        /// 言語ペア別統計
        /// </summary>
        public IReadOnlyDictionary<string, int> RecordsByLanguagePair => _recordsByLanguagePair;
        
        /// <summary>
        /// エンジン別統計
        /// </summary>
        public IReadOnlyDictionary<string, int> RecordsByEngine => _recordsByEngine;
        
        /// <summary>
        /// ゲームプロファイル別統計
        /// </summary>
        public IReadOnlyDictionary<string, int> RecordsByGameProfile => _recordsByGameProfile;
        
        /// <summary>
        /// タグ別統計
        /// </summary>
        public IReadOnlyDictionary<string, int> RecordsByTag => _recordsByTag;
        
        /// <summary>
        /// 時間帯別統計
        /// </summary>
        public IReadOnlyDictionary<string, int> RecordsByTimeFrame => _recordsByTimeFrame;
        
        /// <summary>
        /// キャッシュヒット率
        /// </summary>
        public float CacheHitRate { get; set; }
        
        /// <summary>
        /// 平均翻訳時間（ミリ秒）
        /// </summary>
        public float AverageTranslationTimeMs { get; set; }
        
        /// <summary>
        /// 統計の生成日時
        /// </summary>
        public DateTime GeneratedAt { get; set; }

        /// <summary>
        /// 言語ペア別統計に項目を追加
        /// </summary>
        /// <param name="key">言語ペアキー</param>
        /// <param name="count">カウント数</param>
        public void AddLanguagePairCount(string key, int count)
        {
            _recordsByLanguagePair[key] = count;
        }

        /// <summary>
        /// エンジン別統計に項目を追加
        /// </summary>
        /// <param name="key">エンジン名</param>
        /// <param name="count">カウント数</param>
        public void AddEngineCount(string key, int count)
        {
            _recordsByEngine[key] = count;
        }

        /// <summary>
        /// ゲームプロファイル別統計に項目を追加
        /// </summary>
        /// <param name="key">プロファイルID</param>
        /// <param name="count">カウント数</param>
        public void AddGameProfileCount(string key, int count)
        {
            _recordsByGameProfile[key] = count;
        }

        /// <summary>
        /// タグ別統計に項目を追加
        /// </summary>
        /// <param name="key">タグ名</param>
        /// <param name="count">カウント数</param>
        public void AddTagCount(string key, int count)
        {
            _recordsByTag[key] = count;
        }

        /// <summary>
        /// 時間帯別統計に項目を追加
        /// </summary>
        /// <param name="key">時間帯</param>
        /// <param name="count">カウント数</param>
        public void AddTimeFrameCount(string key, int count)
        {
            _recordsByTimeFrame[key] = count;
        }
    }

    /// <summary>
    /// 統計オプションを表すクラス
    /// </summary>
    public class StatisticsOptions
    {
        // デフォルトコンストラクタは自動的に生成されます。プロパティのデフォルト値は宣言時に設定済み。

        /// <summary>
        /// 指定期間の開始日時
        /// </summary>
        public DateTime? StartDate { get; set; }
        
        /// <summary>
        /// 指定期間の終了日時
        /// </summary>
        public DateTime? EndDate { get; set; }
        
        /// <summary>
        /// エンジン別統計を含めるか
        /// </summary>
        public bool IncludeEngineStats { get; set; } = true;
        
        /// <summary>
        /// 言語ペア別統計を含めるか
        /// </summary>
        public bool IncludeLanguagePairStats { get; set; } = true;
        
        /// <summary>
        /// ゲームプロファイル別統計を含めるか
        /// </summary>
        public bool IncludeGameProfileStats { get; set; } = true;
        
        /// <summary>
        /// タグ別統計を含めるか
        /// </summary>
        public bool IncludeTagStats { get; set; } = true;
        
        /// <summary>
        /// 時間帯別統計を含めるか
        /// </summary>
        public bool IncludeTimeFrameStats { get; set; }
        
        /// <summary>
        /// パフォーマンス統計を含めるか
        /// </summary>
        public bool IncludePerformanceStats { get; set; } = true;
    }

    /// <summary>
    /// キャッシュクリアオプションを表すクラス
    /// </summary>
    public class CacheClearOptions
    {
        // デフォルトコンストラクタは自動的に生成されます。プロパティのデフォルト値は宣言時に設定済み。
        
        /// <summary>
        /// ゲームプロファイルID
        /// </summary>
        public string? GameProfileId { get; set; }
        
        /// <summary>
        /// 元言語
        /// </summary>
        public Language? SourceLanguage { get; set; }
        
        /// <summary>
        /// 対象言語
        /// </summary>
        public Language? TargetLanguage { get; set; }
        
        /// <summary>
        /// エンジン名
        /// </summary>
        public string? EngineName { get; set; }
        
        /// <summary>
        /// 指定日時より古いレコードを削除
        /// </summary>
        public DateTime? OlderThan { get; set; }
        
        /// <summary>
        /// ユーザー編集済みレコードを保持するか
        /// </summary>
        public bool PreserveUserEdited { get; set; } = true;
        
        /// <summary>
        /// 全てのキャッシュをクリアするか
        /// </summary>
        public bool ClearAll { get; set; }
    }

    /// <summary>
    /// データベースマージ戦略
    /// </summary>
    public enum MergeStrategy
    {
        /// <summary>
        /// 既存レコードを上書き
        /// </summary>
        Overwrite,
        
        /// <summary>
        /// 既存レコードを保持
        /// </summary>
        KeepExisting,
        
        /// <summary>
        /// 新しい方を保持
        /// </summary>
        KeepNewer,
        
        /// <summary>
        /// ユーザー編集済みを優先
        /// </summary>
        PreferUserEdited
    }
}
