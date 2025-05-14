using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Repositories
{
    /// <summary>
    /// インメモリ翻訳リポジトリ
    /// </summary>
    public class InMemoryTranslationRepository : ITranslationRepository
    {
        private readonly Dictionary<Guid, TranslationRecord> _records = new();
        private readonly ILogger<InMemoryTranslationRepository> _logger;

        /// <summary>
        /// コンストラクタ
        /// </summary>
        /// <param name="logger">ロガー</param>
        public InMemoryTranslationRepository(ILogger<InMemoryTranslationRepository> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 翻訳レコードを保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>完了タスク</returns>
        public Task SaveRecordAsync(TranslationRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            
            _records[record.Id] = record.Clone();
            _logger.LogDebug("レコード {Id} を保存しました", record.Id);
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// 翻訳レコードを更新します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>完了タスク</returns>
        public Task UpdateRecordAsync(TranslationRecord record, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            
            if (_records.ContainsKey(record.Id))
            {
                _records[record.Id] = record.Clone();
                _logger.LogDebug("レコード {Id} を更新しました", record.Id);
            }
            else
            {
                _logger.LogWarning("更新対象のレコード {Id} が見つかりません", record.Id);
            }
            
            return Task.CompletedTask;
        }

        /// <summary>
        /// 翻訳レコードを取得します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳レコード（存在しない場合はnull）</returns>
        public Task<TranslationRecord?> GetRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            if (_records.TryGetValue(id, out var record))
            {
                _logger.LogDebug("レコード {Id} を取得しました", id);
                return Task.FromResult<TranslationRecord?>(record.Clone());
            }
            
            _logger.LogDebug("レコード {Id} は存在しません", id);
            return Task.FromResult<TranslationRecord?>(null);
        }

        /// <summary>
        /// 一致する翻訳レコードを検索します
        /// </summary>
        /// <param name="sourceText">元テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="context">翻訳コンテキスト（オプション）</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>一致する翻訳レコード（存在しない場合はnull）</returns>
        public Task<TranslationRecord?> FindMatchingRecordAsync(
            string sourceText,
            Language sourceLanguage,
            Language targetLanguage,
            TranslationContext? context = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(sourceText);
            ArgumentNullException.ThrowIfNull(sourceLanguage);
            ArgumentNullException.ThrowIfNull(targetLanguage);
            
            // 言語とテキストで一致するレコードを検索
            var matchingRecord = _records.Values
                .Where(r => 
                    r.SourceText == sourceText && 
                    r.SourceLanguage.Code == sourceLanguage.Code &&
                    r.TargetLanguage.Code == targetLanguage.Code)
                .OrderByDescending(r => r.LastUsedAt)
                .FirstOrDefault();
            
            if (matchingRecord != null)
            {
                _logger.LogDebug("テキスト「{Text}」の翻訳レコードを見つけました", sourceText);
                return Task.FromResult<TranslationRecord?>(matchingRecord.Clone());
            }
            
            _logger.LogDebug("テキスト「{Text}」の翻訳レコードは見つかりませんでした", sourceText);
            return Task.FromResult<TranslationRecord?>(null);
        }

        /// <summary>
        /// 翻訳レコードを検索します
        /// </summary>
        /// <param name="query">検索クエリ</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>検索結果</returns>
        public Task<IReadOnlyList<TranslationRecord>> SearchRecordsAsync(
            TranslationSearchQuery query,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            
            // クエリに基づいて検索
            var results = _records.Values.AsEnumerable();
            
            // テキストパターンフィルター
            if (!string.IsNullOrEmpty(query.TextPattern))
            {
                results = results.Where(r => r.SourceText.Contains(query.TextPattern, StringComparison.OrdinalIgnoreCase));
            }
            
            // 言語フィルター
            if (query.SourceLanguage != null)
            {
                results = results.Where(r => r.SourceLanguage.Code == query.SourceLanguage.Code);
            }
            
            if (query.TargetLanguage != null)
            {
                results = results.Where(r => r.TargetLanguage.Code == query.TargetLanguage.Code);
            }
            
            // エンジン名フィルター
            if (!string.IsNullOrEmpty(query.EngineName))
            {
                results = results.Where(r => r.TranslationEngine.Contains(query.EngineName, StringComparison.OrdinalIgnoreCase));
            }
            
            // ユーザー編集済みフィルター
            if (query.IsUserEdited.HasValue)
            {
                results = results.Where(r => r.IsUserEdited == query.IsUserEdited.Value);
            }
            
            // 作成日時フィルター
            if (query.CreatedAfter.HasValue)
            {
                results = results.Where(r => r.CreatedAt >= query.CreatedAfter.Value);
            }
            
            if (query.CreatedBefore.HasValue)
            {
                results = results.Where(r => r.CreatedAt <= query.CreatedBefore.Value);
            }
            
            // 並べ替え
            results = query.SortField.ToUpperInvariant() switch
            {
                "CREATEDAT" => query.SortAscending 
                    ? results.OrderBy(r => r.CreatedAt) 
                    : results.OrderByDescending(r => r.CreatedAt),
                "LASTUSEDAT" => query.SortAscending 
                    ? results.OrderBy(r => r.LastUsedAt) 
                    : results.OrderByDescending(r => r.LastUsedAt),
                "USAGECOUNT" => query.SortAscending 
                    ? results.OrderBy(r => r.UsageCount) 
                    : results.OrderByDescending(r => r.UsageCount),
                "SOURCETEXT" => query.SortAscending 
                    ? results.OrderBy(r => r.SourceText) 
                    : results.OrderByDescending(r => r.SourceText),
                _ => query.SortAscending 
                    ? results.OrderBy(r => r.CreatedAt) 
                    : results.OrderByDescending(r => r.CreatedAt)
            };
            
            // ページング
            results = results.Skip(query.Offset).Take(query.Limit);
            
            // 結果を取得
            var finalResults = results.Select(r => r.Clone()).ToList();
            _logger.LogDebug("{Count} 件のレコードが検索条件に一致しました", finalResults.Count);
            
            return Task.FromResult<IReadOnlyList<TranslationRecord>>(finalResults);
        }

        /// <summary>
        /// 翻訳レコードを削除します
        /// </summary>
        /// <param name="id">レコードID</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>削除に成功した場合はtrue</returns>
        public Task<bool> DeleteRecordAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var result = _records.Remove(id);
            
            if (result)
            {
                _logger.LogDebug("レコード {Id} を削除しました", id);
            }
            else
            {
                _logger.LogDebug("レコード {Id} は存在しないため削除できませんでした", id);
            }
            
            return Task.FromResult(result);
        }

        /// <summary>
        /// 翻訳統計を取得します
        /// </summary>
        /// <param name="options">統計オプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>翻訳統計</returns>
        public Task<TranslationStatistics> GetStatisticsAsync(
            StatisticsOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new StatisticsOptions();
            List<TranslationRecord> filteredRecords = new List<TranslationRecord>(_records.Values);
            
            // 期間フィルター
            if (options.StartDate.HasValue)
            {
                filteredRecords = filteredRecords.Where(r => r.CreatedAt >= options.StartDate.Value).ToList();
            }
            
            if (options.EndDate.HasValue)
            {
                filteredRecords = filteredRecords.Where(r => r.CreatedAt <= options.EndDate.Value).ToList();
            }
            
            // 統計の作成
            var statistics = new TranslationStatistics
            {
                TotalRecords = filteredRecords.Count,
                UserEditedRecords = filteredRecords.Count(r => r.IsUserEdited),
                // 平均翻訳時間（メタデータから取得）
                AverageTranslationTimeMs = filteredRecords
                    .Where(r => r.Metadata.TryGetValue("ProcessingTimeMs", out var time) && time is long)
                    .Select(r => (float)(long)r.Metadata["ProcessingTimeMs"]!)
                    .DefaultIfEmpty(0)
                    .Average()
            };
            
            // 言語ペア別統計
            if (options.IncludeLanguagePairStats)
            {
                foreach (var group in filteredRecords.GroupBy(r => $"{r.SourceLanguage.Code}-{r.TargetLanguage.Code}"))
                {
                    ((Dictionary<string, int>)statistics.RecordsByLanguagePair)[group.Key] = group.Count();
                }
            }
            
            // エンジン別統計
            if (options.IncludeEngineStats)
            {
                foreach (var group in filteredRecords.GroupBy(r => r.TranslationEngine))
                {
                    ((Dictionary<string, int>)statistics.RecordsByEngine)[group.Key] = group.Count();
                }
            }
            
            _logger.LogDebug("翻訳統計を生成しました（総数: {Count}件）", statistics.TotalRecords);
            return Task.FromResult(statistics);
        }

        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <param name="options">クリアオプション</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>クリアに成功した場合はtrue</returns>
        public Task<bool> ClearCacheAsync(
            CacheClearOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new CacheClearOptions { ClearAll = true };
            
            if (options.ClearAll)
            {
                var count = _records.Count;
                _records.Clear();
                _logger.LogDebug("すべてのレコード ({Count}件) をクリアしました", count);
                return Task.FromResult(true);
            }
            
            // 削除対象のキーを収集
            var keysToRemove = new List<Guid>();
            
            foreach (var record in _records.Values)
            {
                var shouldRemove = true;
                
                // ユーザー編集済みレコードの保持
                if (options.PreserveUserEdited && record.IsUserEdited)
                {
                    shouldRemove = false;
                }
                
                // 言語フィルター
                if (options.SourceLanguage != null && record.SourceLanguage.Code != options.SourceLanguage.Code)
                {
                    shouldRemove = false;
                }
                
                if (options.TargetLanguage != null && record.TargetLanguage.Code != options.TargetLanguage.Code)
                {
                    shouldRemove = false;
                }
                
                // エンジン名フィルター
                if (!string.IsNullOrEmpty(options.EngineName) && 
                    !record.TranslationEngine.Contains(options.EngineName, StringComparison.OrdinalIgnoreCase))
                {
                    shouldRemove = false;
                }
                
                // 日時フィルター
                if (options.OlderThan.HasValue && record.CreatedAt >= options.OlderThan.Value)
                {
                    shouldRemove = false;
                }
                
                if (shouldRemove)
                {
                    keysToRemove.Add(record.Id);
                }
            }
            
            // 収集したキーを削除
            foreach (var key in keysToRemove)
            {
                _records.Remove(key);
            }
            
            _logger.LogDebug("{Count} 件のレコードをクリアしました", keysToRemove.Count);
            return Task.FromResult(true);
        }
    }
}