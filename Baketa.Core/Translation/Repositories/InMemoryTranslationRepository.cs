using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Models;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Repositories;

    /// <summary>
    /// インメモリ翻訳リポジトリ
    /// </summary>
    public class InMemoryTranslationRepository : ITranslationRepository
    {
        private readonly Dictionary<Guid, TranslationRecord> _records = [];
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
            return SaveRecordWithStrategyAsync(record, MergeStrategy.Overwrite, cancellationToken);
        }
        
        /// <summary>
        /// 翻訳レコードをマージ戦略を指定して保存します
        /// </summary>
        /// <param name="record">翻訳レコード</param>
        /// <param name="strategy">マージ戦略</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>完了タスク</returns>
        public Task SaveRecordWithStrategyAsync(TranslationRecord record, MergeStrategy strategy, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(record);
            
            try
            {
                // 既存の翻訳レコードを検索（同じテキストと言語の組み合わせ）
                var existingRecord = _records.Values
                    .Where(r => r.SourceText == record.SourceText && 
                            r.SourceLanguage.Code.Equals(record.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase) && 
                            r.TargetLanguage.Code.Equals(record.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase) && 
                            r.Id != record.Id)
                    .FirstOrDefault();
                
                // 既存レコードがない場合はそのまま保存
                if (existingRecord == null)
                {
                    _records[record.Id] = record.Clone();
                    _logger.LogDebug("レコード {Id} を保存しました", record.Id);
                    return Task.CompletedTask;
                }
                
                // 既存レコードがある場合は指定されたマージ戦略に基づいて処理
                switch (strategy)
                {
                    case MergeStrategy.Overwrite:
                        // 上書きモード：単純に新しいレコードで置き換え
                        _records[record.Id] = record.Clone();
                        _logger.LogDebug("レコード {Id} を既存レコードを上書きして保存しました", record.Id);
                        break;
                    
                    case MergeStrategy.KeepExisting:
                        // 既存レコードを保持し、何もしない
                        _logger.LogDebug("レコード {Id} は既存レコードを優先する設定のため保存されませんでした", record.Id);
                        break;
                    
                    case MergeStrategy.KeepNewer:
                        // 新しい方を保持
                        var newRecordTime = record.UpdatedAt ?? record.CreatedAt;
                        var existingRecordTime = existingRecord.UpdatedAt ?? existingRecord.CreatedAt;
                        
                        if (newRecordTime > existingRecordTime)
                        {
                            _records[record.Id] = record.Clone();
                            _logger.LogDebug("レコード {Id} は既存レコードより新しいため保存されました", record.Id);
                        }
                        else
                        {
                            _logger.LogDebug("レコード {Id} は既存レコードより古いため保存されませんでした", record.Id);
                        }
                        break;
                    
                    case MergeStrategy.PreferUserEdited:
                        // ユーザー編集済みを優先
                        if (record.IsUserEdited && !existingRecord.IsUserEdited)
                        {
                            _records[record.Id] = record.Clone();
                            _logger.LogDebug("レコード {Id} はユーザー編集済みのため保存されました", record.Id);
                        }
                        else if (!record.IsUserEdited && existingRecord.IsUserEdited)
                        {
                            _logger.LogDebug("レコード {Id} は既存レコードがユーザー編集済みのため保存されませんでした", record.Id);
                        }
                        else
                        {
                            // 両方ともユーザー編集済みか、両方とも未編集の場合は新しい方を使用
                            var newTime = record.UpdatedAt ?? record.CreatedAt;
                            var existingTime = existingRecord.UpdatedAt ?? existingRecord.CreatedAt;
                            
                            if (newTime > existingTime)
                            {
                                _records[record.Id] = record.Clone();
                                _logger.LogDebug("レコード {Id} は既存レコードより新しいため保存されました", record.Id);
                            }
                            else
                            {
                                _logger.LogDebug("レコード {Id} は既存レコードより古いため保存されませんでした", record.Id);
                            }
                        }
                        break;
                }
                
                return Task.CompletedTask;
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "レコード {Id} の保存がキャンセルされました", record.Id);
                throw;
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogError(ex, "レコード {Id} の保存中に無効な操作が発生しました", record.Id);
                throw;
            }
            catch (ArgumentException ex)
            {
                _logger.LogError(ex, "レコード {Id} の保存中に引数が無効です", record.Id);
                throw;
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "レコード {Id} の保存中にエラーが発生しました", record.Id);
                throw;
            }
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
            var candidateRecords = _records.Values
                .Where(r => 
                    r.SourceText == sourceText && 
                    r.SourceLanguage.Code.Equals(sourceLanguage.Code, StringComparison.OrdinalIgnoreCase) &&
                    r.TargetLanguage.Code.Equals(targetLanguage.Code, StringComparison.OrdinalIgnoreCase))
                .ToList();

            // レコードが見つからない場合
            if (candidateRecords.Count == 0)
            {
                _logger.LogDebug("テキスト「{Text}」の翻訳レコードは見つかりませんでした", sourceText);
                return Task.FromResult<TranslationRecord?>(null);
            }
            
            // コンテキストが指定されている場合、コンテキストでフィルタリング
            TranslationRecord? bestMatch = null;
            
            if (context != null)
            {
                // コンテキストが完全に一致するレコードを探す
                var exactContextMatch = candidateRecords
                    .Where(r => r.Context != null && r.Context.Equals(context))
                    .OrderByDescending(r => r.UsageCount)
                    .ThenByDescending(r => r.LastUsedAt)
                    .FirstOrDefault();
                
                if (exactContextMatch != null)
                {
                    bestMatch = exactContextMatch;
                    _logger.LogDebug("テキスト「{Text}」のコンテキスト完全一致レコードを見つけました", sourceText);
                }
                else
                {
                        // コンテキストが部分一致するレコードを検索
                        var matches = candidateRecords
                            .Where(r => r.Context != null && IsPartialContextMatch(r.Context, context))
                            .ToList();
                    
                    // スコア計算してソートした結果から最初のレコードを選択
                    var scoredMatches = matches.Select(r => new {
                        r,  // Record = r を簡略化
                        Score = CalculateContextMatchScore(r.Context!, context),
                        r.UsageCount,  // UsageCount = r.UsageCount を簡略化
                        r.LastUsedAt   // LastUsedAt = r.LastUsedAt を簡略化
                    }).ToList();
                    
                    bestMatch = scoredMatches
                        .OrderByDescending(x => x.Score)
                        .ThenByDescending(x => x.UsageCount)
                        .ThenByDescending(x => x.LastUsedAt)
                        .Select(x => x.r)
                        .FirstOrDefault();
                    
                    if (bestMatch != null)
                    {
                        _logger.LogDebug("テキスト「{Text}」のコンテキスト部分一致レコードを見つけました", sourceText);
                    }
                }
            }
            
            // コンテキストがないか、コンテキスト一致が見つからなかった場合
            if (bestMatch == null)
            {
                // 最も使用回数が多いレコードを選択
                bestMatch = candidateRecords
                    .OrderByDescending(r => r.UsageCount)
                    .ThenByDescending(r => r.LastUsedAt)
                    .FirstOrDefault();
                
                _logger.LogDebug("テキスト「{Text}」の最適なレコードを選択しました", sourceText);
            }
            
            return Task.FromResult<TranslationRecord?>(bestMatch?.Clone());
        }
        
        /// <summary>
        /// 部分的なコンテキスト一致を確認します
        /// </summary>
        /// <param name="recordContext">レコードのコンテキスト</param>
        /// <param name="queryContext">クエリのコンテキスト</param>
        /// <returns>部分一致した場合はtrue</returns>
        private static bool IsPartialContextMatch(TranslationContext recordContext, TranslationContext queryContext)
        {
            // ゲームプロファイルIDの一致
            if (!string.IsNullOrEmpty(recordContext.GameProfileId) && 
                !string.IsNullOrEmpty(queryContext.GameProfileId) && 
                recordContext.GameProfileId == queryContext.GameProfileId)
            {
                return true;
            }
            
            // シーンIDの一致
            if (!string.IsNullOrEmpty(recordContext.SceneId) && 
                !string.IsNullOrEmpty(queryContext.SceneId) && 
                recordContext.SceneId == queryContext.SceneId)
            {
                return true;
            }
            
            // 会話IDの一致
            if (!string.IsNullOrEmpty(recordContext.DialogueId) && 
                !string.IsNullOrEmpty(queryContext.DialogueId) && 
                recordContext.DialogueId == queryContext.DialogueId)
            {
                return true;
            }
            
            // レコード内のタグを確認
            if (recordContext.Tags.Count > 0 && queryContext.Tags.Count > 0)
            {
                // より効率的なHashSetを使用して一度だけ列挙
                var recordTagSet = new HashSet<string>(recordContext.Tags, StringComparer.OrdinalIgnoreCase);
                var queryTagSet = new List<string>(queryContext.Tags); // 一度リスト化
                
                // 一つでも共通のタグがあればtrue
                return queryTagSet.Any(recordTagSet.Contains);
            }
            
            return false;
        }
        
        /// <summary>
        /// コンテキストの一致スコアを計算します
        /// </summary>
        /// <param name="recordContext">レコードのコンテキスト</param>
        /// <param name="queryContext">クエリのコンテキスト</param>
        /// <returns>一致スコア（0～100）</returns>
        private static int CalculateContextMatchScore(TranslationContext recordContext, TranslationContext queryContext)
        {
            ArgumentNullException.ThrowIfNull(recordContext);
            ArgumentNullException.ThrowIfNull(queryContext);
            
            int score = 0;
            int maxScore = 0;
            
            // ゲームプロファイルIDの一致（30点）
            if (!string.IsNullOrEmpty(queryContext.GameProfileId))
            {
                maxScore += 30;
                if (!string.IsNullOrEmpty(recordContext.GameProfileId) && 
                    recordContext.GameProfileId == queryContext.GameProfileId)
                {
                    score += 30;
                }
            }
            
            // シーンIDの一致（25点）
            if (!string.IsNullOrEmpty(queryContext.SceneId))
            {
                maxScore += 25;
                if (!string.IsNullOrEmpty(recordContext.SceneId) && 
                    recordContext.SceneId == queryContext.SceneId)
                {
                    score += 25;
                }
            }
            
            // 会話IDの一致（20点）
            if (!string.IsNullOrEmpty(queryContext.DialogueId))
            {
                maxScore += 20;
                if (!string.IsNullOrEmpty(recordContext.DialogueId) && 
                    recordContext.DialogueId == queryContext.DialogueId)
                {
                    score += 20;
                }
            }
            
            // タグの一致（タグ1つにつき5点、最大20点）
            if (queryContext.Tags.Count > 0)
            {
                int tagMaxScore = Math.Min(queryContext.Tags.Count * 5, 20);
                maxScore += tagMaxScore;
                
                // 大文字小文字を区別しない比較を使用して最初からHashSetで管理
                var recordTagSet = new HashSet<string>(recordContext.Tags, StringComparer.OrdinalIgnoreCase);
                var queryTagSet = new HashSet<string>(queryContext.Tags, StringComparer.OrdinalIgnoreCase);
                
                // 共通要素の数をカウントする
                int matchingTags = recordTagSet.Count(queryTagSet.Contains);
                
                // マッチしたタグ数に応じたスコア（最大はタグMaxScore）
                int tagScore = Math.Min(matchingTags * 5, tagMaxScore);
                score += tagScore;
            }
            
            // スコアを正規化（0～100）
            return maxScore == 0 ? 0 : (score * 100) / maxScore;
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
            
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // リストを事前に固定し、検索条件を適用して複数列挙の可能性を排除
                List<TranslationRecord> allRecords = [
                    .. _records.Values
                ];
                var results = allRecords.AsEnumerable();
                
                // テキストパターンフィルター
                if (!string.IsNullOrEmpty(query.TextPattern))
                {
                    results = results.Where(r => r.SourceText.Contains(query.TextPattern, StringComparison.OrdinalIgnoreCase));
                }
                
                // 言語フィルター
                if (query.SourceLanguage != null)
                {
                    results = results.Where(r => r.SourceLanguage.Code.Equals(query.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase));
                }
                
                if (query.TargetLanguage != null)
                {
                    results = results.Where(r => r.TargetLanguage.Code.Equals(query.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase));
                }
                
                // エンジン名フィルター
                if (!string.IsNullOrEmpty(query.EngineName))
                {
                    results = results.Where(r => r.TranslationEngine.Contains(query.EngineName, StringComparison.OrdinalIgnoreCase));
                }
                
                // ゲームプロファイルIDフィルター
                if (!string.IsNullOrEmpty(query.GameProfileId))
                {
                    results = results.Where(r => r.Context != null && 
                                              r.Context.GameProfileId != null && 
                                              r.Context.GameProfileId.Equals(query.GameProfileId, StringComparison.OrdinalIgnoreCase));
                }
                
                // タグフィルター
                if (query.Tags.Count > 0)
                {
                    // タグ一覧を作成し、複数列挙を回避
                    HashSet<string> queryTagSet = query.Tags.Count > 0 
                        ? new HashSet<string>(query.Tags, StringComparer.OrdinalIgnoreCase) 
                        : [];
                    
                    // 各レコードのタグを効率的にチェック
                    results = results.Where(r => {
                    // リストを作成して抽出した要素を格納
                    List<string> recordTags = r.Context?.Tags.Count > 0 ? [.. r.Context.Tags] : [];
                    return recordTags.Any(tag => queryTagSet.Contains(tag));
                    });
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
                
                // 一度リスト化して確定させてからソート処理する
                var filteredRecords = results.ToList();
                
                // 並べ替え
                IEnumerable<TranslationRecord> sortedRecords = query.SortField.ToUpperInvariant() switch
                {
                    "CREATEDAT" => query.SortAscending 
                        ? filteredRecords.OrderBy(r => r.CreatedAt) 
                        : filteredRecords.OrderByDescending(r => r.CreatedAt),
                    "LASTUSEDAT" => query.SortAscending 
                        ? filteredRecords.OrderBy(r => r.LastUsedAt) 
                        : filteredRecords.OrderByDescending(r => r.LastUsedAt),
                    "USAGECOUNT" => query.SortAscending 
                        ? filteredRecords.OrderBy(r => r.UsageCount) 
                        : filteredRecords.OrderByDescending(r => r.UsageCount),
                    "SOURCETEXT" => query.SortAscending 
                        ? filteredRecords.OrderBy(r => r.SourceText) 
                        : filteredRecords.OrderByDescending(r => r.SourceText),
                    _ => query.SortAscending 
                        ? filteredRecords.OrderBy(r => r.CreatedAt) 
                        : filteredRecords.OrderByDescending(r => r.CreatedAt)
                };
                
                // ページング
                var pagedRecords = sortedRecords.Skip(query.Offset).Take(query.Limit).ToList();
                
                // 結果を取得
                var finalResults = pagedRecords.Select(r => r.Clone()).ToList();
                _logger.LogDebug("{Count} 件のレコードが検索条件に一致しました", finalResults.Count);
                
                return Task.FromResult<IReadOnlyList<TranslationRecord>>(finalResults);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("検索操作がキャンセルされました");
                return Task.FromResult(Array.Empty<TranslationRecord>() as IReadOnlyList<TranslationRecord>);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "検索中に予期しないエラーが発生しました");
                return Task.FromResult(Array.Empty<TranslationRecord>() as IReadOnlyList<TranslationRecord>);
            }
        }
        
        /// <summary>
        /// いずれかのタグが一致するか確認します
        /// </summary>
        /// <param name="recordTags">レコードのタグリスト</param>
        /// <param name="queryTags">クエリのタグリスト</param>
        /// <returns>いずれかのタグが一致する場合はtrue</returns>
        private static bool HasAnyMatchingTag(IEnumerable<string> recordTags, IEnumerable<string> queryTags)
        {
            var recordTagList = recordTags != null ? new List<string>(recordTags) : [];
            var queryTagList = queryTags != null ? new List<string>(queryTags) : [];
            
            // 空の場合は早期返却
            if (recordTagList.Count == 0 || queryTagList.Count == 0)
                return false;
            
            // 大文字小文字を区別しないHashSetを作成
            var recordTagSet = recordTagList.Count > 0 ? new HashSet<string>(recordTagList, StringComparer.OrdinalIgnoreCase) : [];
            
            // いずれかのクエリタグがrecordTagSetに含まれるかチェック
            return queryTagList.Any(recordTagSet.Contains);
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
            
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                // 変数宣言の分解を適用
                List<TranslationRecord> filteredRecords;
                if (options.StartDate.HasValue || options.EndDate.HasValue)
                {
                // 期間でフィルターがある場合のみコピーしてフィルタリング
                    filteredRecords = [
                        .. _records.Values.Where(r => 
                            (!options.StartDate.HasValue || r.CreatedAt >= options.StartDate.Value) &&
                            (!options.EndDate.HasValue || r.CreatedAt <= options.EndDate.Value))
                    ];
                }
                else
                {
                    filteredRecords = [.. _records.Values];
                }
                
                // 統計の作成
                var statistics = new TranslationStatistics
                {
                    TotalRecords = filteredRecords.Count,
                    UserEditedRecords = filteredRecords.Count(r => r.IsUserEdited),
                    GeneratedAt = DateTime.UtcNow
                };
                
                // 平均翻訳時間の計算（メタデータから取得）
                if (options.IncludePerformanceStats)
                {
                    var processingTimes = new List<float>();
                    foreach (var record in filteredRecords)
                    {
                        if (record.Metadata.TryGetValue("ProcessingTimeMs", out var time) && time is long timeValue)
                        {
                            processingTimes.Add((float)timeValue);
                        }
                    }
                    
                    statistics.AverageTranslationTimeMs = processingTimes.Count > 0 
                        ? processingTimes.Average() 
                        : 0f;
                    
                    // キャッシュヒット率の計算（メタデータから取得）
                    int cacheHits = filteredRecords.Count(r => 
                        r.Metadata.TryGetValue("FromCache", out var fromCache) && 
                        fromCache is bool fc && fc);
                    
                    statistics.CacheHitRate = filteredRecords.Count > 0 
                        ? (float)cacheHits / filteredRecords.Count 
                        : 0f;
                }
                
                // 言語ペア別統計
                if (options.IncludeLanguagePairStats)
                {
                    foreach (var group in filteredRecords.GroupBy(r => 
                        $"{r.SourceLanguage.Code.ToUpperInvariant()}-{r.TargetLanguage.Code.ToUpperInvariant()}"))
                    {
                        statistics.AddLanguagePairCount(group.Key, group.Count());
                    }
                }
                
                // エンジン別統計
                if (options.IncludeEngineStats)
                {
                    foreach (var group in filteredRecords.GroupBy(r => r.TranslationEngine))
                    {
                        statistics.AddEngineCount(group.Key, group.Count());
                    }
                }
                
                // ゲームプロファイル別統計
                if (options.IncludeGameProfileStats)
                {
                    var gameProfiles = filteredRecords
                        .Where(r => r.Context != null && !string.IsNullOrEmpty(r.Context.GameProfileId))
                        .Select(r => r.Context!.GameProfileId!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    
                foreach (var profileId in gameProfiles)
                {
                    int count = filteredRecords.Count(r => 
                            r.Context != null && 
                                !string.IsNullOrEmpty(r.Context.GameProfileId) && 
                                StringComparer.OrdinalIgnoreCase.Equals(r.Context.GameProfileId, profileId));
                                
                            statistics.AddGameProfileCount(profileId, count);
                        }
                    }
                
                // タグ別統計
                if (options.IncludeTagStats)
                {
                    Dictionary<string, int> allTags = [];
                    
                    // 一度メモリに保持して複数列挙を回避
                    List<TranslationRecord> recordsWithTags = [
                        .. filteredRecords.Where(r => r.Context != null && r.Context.Tags.Count > 0)
                    ];
                
            foreach (var record in recordsWithTags)
            {
                        foreach (var tag in record.Context!.Tags)
                        {
                            allTags[tag] = allTags.TryGetValue(tag, out int count) ? count + 1 : 1;
                        }
                    }
                    
                    foreach (var tag in allTags)
                    {
                        statistics.AddTagCount(tag.Key, tag.Value);
                    }
                }
                
                // 時間帯別統計
                if (options.IncludeTimeFrameStats && filteredRecords.Count > 0)
                {
                    var timeFrames = GenerateTimeFrames(filteredRecords, options);
                    foreach (var frame in timeFrames)
                    {
                        statistics.AddTimeFrameCount(frame.Key, frame.Value);
                    }
                }
                
                _logger.LogDebug("翻訳統計を生成しました（総数: {Count}件）", statistics.TotalRecords);
                return Task.FromResult(statistics);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "翻訳統計の生成がキャンセルされました");
                return Task.FromResult(new TranslationStatistics { GeneratedAt = DateTime.UtcNow });
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "翻訳統計の生成中にエラーが発生しました");
                return Task.FromResult(new TranslationStatistics { GeneratedAt = DateTime.UtcNow });
            }
        }
        
        /// <summary>
        /// 時間帯別統計用のデータを生成します
        /// </summary>
        /// <param name="records">翻訳レコードリスト</param>
        /// <param name="options">統計オプション</param>
        /// <returns>時間帯別の記録数</returns>
        private static Dictionary<string, int> GenerateTimeFrames(List<TranslationRecord> records, StatisticsOptions options)
        {
            Dictionary<string, int> result = [];
            
            // 開始日と終了日を計算（空のリストチェックを追加）
            DateTime startDate = options.StartDate ?? (records.Count > 0 ? records.Min(r => r.CreatedAt).Date : DateTime.UtcNow);
            DateTime endDate = options.EndDate ?? DateTime.UtcNow;
            
            // 日付の差
            int totalDays = (int)(endDate - startDate).TotalDays + 1;
            
            // 適切な時間間隔を決定
            if (totalDays <= 7) // 1週間以内
            {
                // 時間単位でグルーピング用のクエリ
                var recordsList = records.ToList();

                // 要素の取得を静的ローカル関数として定義
                static int GetHour(TranslationRecord r) => r.CreatedAt.Hour;

                var hourlyGroups = recordsList
                    .GroupBy(r => new { r.CreatedAt.Date, Hour = GetHour(r) })
                    .Select(g => new { 
                        TimeFrame = $"{g.Key.Date:yyyy-MM-dd} {g.Key.Hour:D2}:00", 
                        Count = g.Count() 
                    })
                    .OrderBy(g => g.TimeFrame)
                    .ToList();
                
                foreach (var group in hourlyGroups)
                {
                    result[group.TimeFrame] = group.Count;
                }
            }
            else if (totalDays <= 31) // 1ヶ月以内
            {
                // 日付単位でグルーピング用のクエリ
                var recordsList = records.ToList();
                var dailyGroups = recordsList
                    .GroupBy(r => r.CreatedAt.Date)
                    .Select(g => new { TimeFrame = $"{g.Key:yyyy-MM-dd}", Count = g.Count() })
                    .OrderBy(g => g.TimeFrame)
                    .ToList();
                
                foreach (var group in dailyGroups)
                {
                    result[group.TimeFrame] = group.Count;
                }
            }
            else if (totalDays <= 365) // 1年以内
            {
                // 週単位でグルーピング用のクエリ
                var recordsList = records.ToList();
                var weeklyGroups = recordsList
                    .GroupBy(r => $"{r.CreatedAt.Year}-W{GetIso8601WeekOfYear(r.CreatedAt)}")
                    .Select(g => new { TimeFrame = g.Key, Count = g.Count() })
                    .OrderBy(g => g.TimeFrame)
                    .ToList();
                
                foreach (var group in weeklyGroups)
                {
                    result[group.TimeFrame] = group.Count;
                }
            }
            else
            {
                // 月単位でグルーピング用のクエリ
                var recordsList = records.ToList();
                var monthlyGroups = recordsList
                    .GroupBy(r => new { r.CreatedAt.Year, r.CreatedAt.Month })
                    .Select(g => new { TimeFrame = $"{g.Key.Year}-{g.Key.Month:D2}", Count = g.Count() })
                    .OrderBy(g => g.TimeFrame)
                    .ToList();
                
                foreach (var group in monthlyGroups)
                {
                    result[group.TimeFrame] = group.Count;
                }
            }
            
            return result;
        }
        
        /// <summary>
        /// ISO 8601形式で週番号を取得します
        /// </summary>
        /// <param name="date">日付</param>
        /// <returns>週番号</returns>
        private static int GetIso8601WeekOfYear(DateTime date)
        {
            // ISO 8601: 週の始まりは月曜日、運年の最初の週は1月4日を含む週
            var cal = System.Globalization.CultureInfo.InvariantCulture.Calendar;
            
            // 調整した日付の週番号を返す
            return cal.GetWeekOfYear(date, 
                System.Globalization.CalendarWeekRule.FirstFourDayWeek, 
                DayOfWeek.Monday);
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
            
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                
                if (options.ClearAll)
                {
                    var count = _records.Count;
                    _records.Clear();
                    _logger.LogDebug("すべてのレコード ({Count}件) をクリアしました", count);
                    return Task.FromResult(true);
                }
                
                var keysToRemove = _records.Values
                    .Where(record => 
                        (options.GameProfileId is null or "" ||  // ゲームプロファイルIDフィルター
                         (record.Context != null && 
                          !string.IsNullOrEmpty(record.Context.GameProfileId) &&
                          record.Context.GameProfileId.Equals(options.GameProfileId, StringComparison.OrdinalIgnoreCase))) &&
                        (!options.PreserveUserEdited || !record.IsUserEdited) &&  // ユーザー編集済みレコードの保持
                        (options.SourceLanguage == null ||  // 言語フィルター
                         record.SourceLanguage.Code.Equals(options.SourceLanguage.Code, StringComparison.OrdinalIgnoreCase)) &&
                        (options.TargetLanguage == null || 
                         record.TargetLanguage.Code.Equals(options.TargetLanguage.Code, StringComparison.OrdinalIgnoreCase)) &&
                        (options.EngineName is null or "" ||  // エンジン名フィルター
                         record.TranslationEngine.Contains(options.EngineName, StringComparison.OrdinalIgnoreCase)) &&
                        (!options.OlderThan.HasValue || record.CreatedAt < options.OlderThan.Value)  // 日時フィルター
                    )
                    .Select(record => record.Id)
                    .ToList();
                
                // 収集したキーを削除
                foreach (var key in keysToRemove)
                {
                    _records.Remove(key);
                }
                
                _logger.LogDebug("{Count} 件のレコードをクリアしました", keysToRemove.Count);
                return Task.FromResult(true);
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "キャッシュクリア操作がキャンセルされました");
                return Task.FromResult(false);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException && ex is not StackOverflowException)
            {
                _logger.LogError(ex, "キャッシュクリア中にエラーが発生しました");
                return Task.FromResult(false);
            }
        }
    }
