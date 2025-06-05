# メモリキャッシュの実装例

メモリ内で動作する翻訳キャッシュの実装例です。

```csharp
namespace Baketa.Translation.Cache
{
    /// <summary>
    /// メモリ内翻訳キャッシュの実装
    /// </summary>
    public class MemoryTranslationCache : ITranslationCache
    {
        private readonly ConcurrentDictionary<string, CacheItem> _cache = new ConcurrentDictionary<string, CacheItem>();
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private long _hits;
        private long _misses;
        private readonly int _maxItems;
        private readonly ILogger? _logger;
        
        /// <summary>
        /// 内部キャッシュアイテムクラス
        /// </summary>
        private class CacheItem
        {
            /// <summary>
            /// キャッシュエントリ
            /// </summary>
            public TranslationCacheEntry Entry { get; set; }
            
            /// <summary>
            /// 有効期限
            /// </summary>
            public DateTime? ExpiresAt { get; set; }
            
            /// <summary>
            /// 新しいキャッシュアイテムを作成します
            /// </summary>
            /// <param name="entry">キャッシュエントリ</param>
            /// <param name="expiration">有効期限（null=無期限）</param>
            public CacheItem(TranslationCacheEntry entry, TimeSpan? expiration)
            {
                Entry = entry;
                ExpiresAt = expiration.HasValue ? DateTime.UtcNow.Add(expiration.Value) : null;
            }
            
            /// <summary>
            /// キャッシュアイテムが有効かどうか
            /// </summary>
            public bool IsValid => !ExpiresAt.HasValue || ExpiresAt.Value > DateTime.UtcNow;
            
            /// <summary>
            /// アクセス情報を更新します
            /// </summary>
            public void UpdateAccess()
            {
                Entry.LastAccessedAt = DateTime.UtcNow;
                Entry.AccessCount++;
            }
        }
        
        /// <summary>
        /// 新しいメモリ翻訳キャッシュを初期化します
        /// </summary>
        /// <param name="maxItems">最大アイテム数</param>
        /// <param name="logger">ロガー</param>
        public MemoryTranslationCache(int maxItems = 10000, ILogger? logger = null)
        {
            _maxItems = maxItems;
            _logger = logger;
            
            _logger?.LogInformation("メモリ翻訳キャッシュが初期化されました。最大アイテム数: {MaxItems}", maxItems);
        }
        
        /// <inheritdoc />
        public async Task<TranslationCacheEntry?> GetAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return null;
                
            if (_cache.TryGetValue(key, out var cacheItem))
            {
                if (cacheItem.IsValid)
                {
                    // 有効なキャッシュヒット
                    Interlocked.Increment(ref _hits);
                    cacheItem.UpdateAccess();
                    
                    _logger?.LogTrace("キャッシュヒット: {Key}", key);
                    return cacheItem.Entry;
                }
                else
                {
                    // 期限切れのアイテムを削除
                    await RemoveAsync(key);
                }
            }
            
            // キャッシュミス
            Interlocked.Increment(ref _misses);
            _logger?.LogTrace("キャッシュミス: {Key}", key);
            return null;
        }
        
        /// <inheritdoc />
        public async Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys)
        {
            var result = new Dictionary<string, TranslationCacheEntry>();
            
            if (keys == null)
                return result;
                
            foreach (var key in keys)
            {
                var entry = await GetAsync(key);
                if (entry != null)
                {
                    result[key] = entry;
                }
            }
            
            return result;
        }
        
        /// <inheritdoc />
        public async Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key) || entry == null)
                return false;
                
            // セマフォを取得
            await _lock.WaitAsync();
            
            try
            {
                // キャッシュが最大サイズに達していないか確認
                if (_cache.Count >= _maxItems && !_cache.ContainsKey(key))
                {
                    // LRUアルゴリズムで最も古いアイテムを削除
                    await EvictOldestItemAsync();
                }
                
                // キャッシュにエントリを追加
                var cacheItem = new CacheItem(entry, expiration);
                _cache[key] = cacheItem;
                
                _logger?.LogTrace("キャッシュにエントリを追加: {Key}", key);
                return true;
            }
            finally
            {
                // セマフォを解放
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null)
        {
            if (entries == null || entries.Count == 0)
                return false;
                
            // セマフォを取得
            await _lock.WaitAsync();
            
            try
            {
                // 必要な空き容量を確保
                int requiredSpace = entries.Count - (_maxItems - _cache.Count);
                if (requiredSpace > 0)
                {
                    // 複数のアイテムを削除する必要がある場合
                    await EvictMultipleItemsAsync(requiredSpace);
                }
                
                // 一括追加
                foreach (var entry in entries)
                {
                    var cacheItem = new CacheItem(entry.Value, expiration);
                    _cache[entry.Key] = cacheItem;
                }
                
                _logger?.LogDebug("キャッシュに{Count}個のエントリを一括追加しました", entries.Count);
                return true;
            }
            finally
            {
                // セマフォを解放
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> RemoveAsync(string key)
        {
            if (string.IsNullOrEmpty(key))
                return false;
                
            await _lock.WaitAsync();
            
            try
            {
                return _cache.TryRemove(key, out _);
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> RemoveManyAsync(IEnumerable<string> keys)
        {
            if (keys == null)
                return false;
                
            await _lock.WaitAsync();
            
            try
            {
                int removedCount = 0;
                foreach (var key in keys)
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        removedCount++;
                    }
                }
                
                _logger?.LogDebug("キャッシュから{Count}個のエントリを一括削除しました", removedCount);
                return removedCount > 0;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> ClearAsync()
        {
            await _lock.WaitAsync();
            
            try
            {
                _cache.Clear();
                Interlocked.Exchange(ref _hits, 0);
                Interlocked.Exchange(ref _misses, 0);
                
                _logger?.LogInformation("キャッシュをクリアしました");
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<IEnumerable<string>> GetKeysAsync(string? pattern = null, int maxCount = 1000)
        {
            await _lock.WaitAsync();
            
            try
            {
                var keys = _cache.Keys.ToList();
                
                // パターンでフィルタリング
                if (!string.IsNullOrEmpty(pattern))
                {
                    Regex regex;
                    try
                    {
                        regex = new Regex(pattern, RegexOptions.IgnoreCase);
                        keys = keys.Where(k => regex.IsMatch(k)).ToList();
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "キーのフィルタリング中にエラーが発生しました: {Pattern}", pattern);
                    }
                }
                
                // 最大数を制限
                if (keys.Count > maxCount)
                {
                    keys = keys.Take(maxCount).ToList();
                }
                
                return keys;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<CacheStatistics> GetStatisticsAsync()
        {
            await _lock.WaitAsync();
            
            try
            {
                var statistics = new CacheStatistics
                {
                    ItemCount = _cache.Count,
                    Hits = _hits,
                    Misses = _misses
                };
                
                // キャッシュサイズの推定（近似値）
                long approximateSize = 0;
                var languagePairStats = new Dictionary<string, int>();
                var engineStats = new Dictionary<string, int>();
                DateTime? oldestCreated = null;
                DateTime? newestCreated = null;
                DateTime? oldestAccessed = null;
                DateTime? newestAccessed = null;
                
                foreach (var item in _cache.Values)
                {
                    var entry = item.Entry;
                    
                    // サイズの推定
                    approximateSize += EstimateEntrySize(entry);
                    
                    // 言語ペア統計
                    string langPair = $"{entry.SourceLanguage}-{entry.TargetLanguage}";
                    if (languagePairStats.ContainsKey(langPair))
                    {
                        languagePairStats[langPair]++;
                    }
                    else
                    {
                        languagePairStats[langPair] = 1;
                    }
                    
                    // エンジン統計
                    if (engineStats.ContainsKey(entry.Engine))
                    {
                        engineStats[entry.Engine]++;
                    }
                    else
                    {
                        engineStats[entry.Engine] = 1;
                    }
                    
                    // 日時の更新
                    if (!oldestCreated.HasValue || entry.CreatedAt < oldestCreated.Value)
                    {
                        oldestCreated = entry.CreatedAt;
                    }
                    
                    if (!newestCreated.HasValue || entry.CreatedAt > newestCreated.Value)
                    {
                        newestCreated = entry.CreatedAt;
                    }
                    
                    if (!oldestAccessed.HasValue || entry.LastAccessedAt < oldestAccessed.Value)
                    {
                        oldestAccessed = entry.LastAccessedAt;
                    }
                    
                    if (!newestAccessed.HasValue || entry.LastAccessedAt > newestAccessed.Value)
                    {
                        newestAccessed = entry.LastAccessedAt;
                    }
                }
                
                statistics.SizeInBytes = approximateSize;
                statistics.LanguagePairStats = languagePairStats;
                statistics.EngineStats = engineStats;
                statistics.OldestEntryCreatedAt = oldestCreated;
                statistics.NewestEntryCreatedAt = newestCreated;
                statistics.OldestAccessedAt = oldestAccessed;
                statistics.NewestAccessedAt = newestAccessed;
                
                return statistics;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> OptimizeAsync()
        {
            await _lock.WaitAsync();
            
            try
            {
                // 期限切れのアイテムを削除
                var expiredKeys = _cache
                    .Where(kv => !kv.Value.IsValid)
                    .Select(kv => kv.Key)
                    .ToList();
                    
                foreach (var key in expiredKeys)
                {
                    _cache.TryRemove(key, out _);
                }
                
                _logger?.LogInformation("キャッシュ最適化: {Count}個の期限切れエントリを削除しました", expiredKeys.Count);
                return true;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> ExportAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;
                
            await _lock.WaitAsync();
            
            try
            {
                // エクスポートするエントリを準備
                var entries = _cache
                    .Where(kv => kv.Value.IsValid)
                    .ToDictionary(kv => kv.Key, kv => kv.Value.Entry);
                    
                // JSONに変換
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                string json = JsonSerializer.Serialize(entries, options);
                
                // ファイルに保存
                await File.WriteAllTextAsync(filePath, json);
                
                _logger?.LogInformation("キャッシュをエクスポートしました: {Count}エントリ, {FilePath}", entries.Count, filePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "キャッシュのエクスポート中にエラーが発生しました: {FilePath}", filePath);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <inheritdoc />
        public async Task<bool> ImportAsync(string filePath, CacheMergeStrategy mergeStrategy = CacheMergeStrategy.ReplaceExisting)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return false;
                
            await _lock.WaitAsync();
            
            try
            {
                // ファイルから読み込み
                string json = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions();
                var importedEntries = JsonSerializer.Deserialize<Dictionary<string, TranslationCacheEntry>>(json, options);
                
                if (importedEntries == null || importedEntries.Count == 0)
                {
                    _logger?.LogWarning("インポートするエントリがありません: {FilePath}", filePath);
                    return false;
                }
                
                // マージ戦略に基づいてインポート
                int importedCount = 0;
                foreach (var kvp in importedEntries)
                {
                    string key = kvp.Key;
                    TranslationCacheEntry importedEntry = kvp.Value;
                    
                    // 既存のエントリがあるか確認
                    if (_cache.TryGetValue(key, out var existingItem))
                    {
                        switch (mergeStrategy)
                        {
                            case CacheMergeStrategy.KeepExisting:
                                // 既存のエントリを保持
                                continue;
                                
                            case CacheMergeStrategy.UseNewer:
                                // より新しいエントリを使用
                                if (existingItem.Entry.CreatedAt >= importedEntry.CreatedAt)
                                {
                                    continue;
                                }
                                break;
                                
                            case CacheMergeStrategy.UseMoreAccessed:
                                // アクセス回数が多いエントリを使用
                                if (existingItem.Entry.AccessCount >= importedEntry.AccessCount)
                                {
                                    continue;
                                }
                                break;
                                
                            case CacheMergeStrategy.ReplaceExisting:
                            default:
                                // 既存のエントリを置き換え
                                break;
                        }
                    }
                    
                    // キャッシュが最大サイズに達していないか確認
                    if (_cache.Count >= _maxItems && !_cache.ContainsKey(key))
                    {
                        // LRUアルゴリズムで最も古いアイテムを削除
                        await EvictOldestItemAsync();
                    }
                    
                    // キャッシュにエントリを追加
                    var cacheItem = new CacheItem(importedEntry, null);
                    _cache[key] = cacheItem;
                    importedCount++;
                }
                
                _logger?.LogInformation("キャッシュをインポートしました: {ImportedCount}/{TotalCount}エントリ, {FilePath}", 
                    importedCount, importedEntries.Count, filePath);
                return importedCount > 0;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "キャッシュのインポート中にエラーが発生しました: {FilePath}", filePath);
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }
        
        /// <summary>
        /// 最も古いアイテムを削除します
        /// </summary>
        private Task<bool> EvictOldestItemAsync()
        {
            if (_cache.IsEmpty)
                return Task.FromResult(false);
                
            // 最後にアクセスされた時間が最も古いアイテムを探す
            var oldest = _cache
                .OrderBy(kv => kv.Value.Entry.LastAccessedAt)
                .FirstOrDefault();
                
            if (oldest.Key != null)
            {
                _logger?.LogTrace("LRU: 最も古いアイテムを削除: {Key}, LastAccessed: {LastAccessed}", 
                    oldest.Key, oldest.Value.Entry.LastAccessedAt);
                return RemoveAsync(oldest.Key);
            }
            
            return Task.FromResult(false);
        }
        
        /// <summary>
        /// 複数のアイテムを削除します
        /// </summary>
        /// <param name="count">削除するアイテム数</param>
        private async Task<int> EvictMultipleItemsAsync(int count)
        {
            if (_cache.IsEmpty || count <= 0)
                return 0;
                
            // 最後にアクセスされた時間順にアイテムを並べる
            var orderedItems = _cache
                .OrderBy(kv => kv.Value.Entry.LastAccessedAt)
                .Take(count)
                .ToList();
                
            int removedCount = 0;
            foreach (var item in orderedItems)
            {
                if (await RemoveAsync(item.Key))
                {
                    removedCount++;
                }
            }
            
            return removedCount;
        }
        
        /// <summary>
        /// エントリのサイズを推定します（バイト単位）
        /// </summary>
        /// <param name="entry">キャッシュエントリ</param>
        /// <returns>推定サイズ（バイト）</returns>
        private static long EstimateEntrySize(TranslationCacheEntry entry)
        {
            // 文字列は2バイト/文字と仮定
            long size = 0;
            
            // 文字列フィールド
            size += (entry.SourceText?.Length ?? 0) * 2;
            size += (entry.TranslatedText?.Length ?? 0) * 2;
            size += (entry.SourceLanguage?.Length ?? 0) * 2;
            size += (entry.TargetLanguage?.Length ?? 0) * 2;
            size += (entry.Engine?.Length ?? 0) * 2;
            
            // 構造体とプリミティブ
            size += 8 * 2; // DateTime x2
            size += 4;     // AccessCount
            
            // メタデータ
            foreach (var kvp in entry.Metadata)
            {
                size += (kvp.Key?.Length ?? 0) * 2;
                size += (kvp.Value?.Length ?? 0) * 2;
            }
            
            // オブジェクトオーバーヘッド
            size += 48; // おおよそのオブジェクトヘッダーサイズ
            
            return size;
        }
    }
}
```