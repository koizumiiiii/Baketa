# Issue 14: 翻訳キャッシュシステム

## 概要
翻訳結果をキャッシュし、パフォーマンスを向上させる機能を実装します。過去に翻訳したテキストを再度翻訳する必要がある場合に、翻訳エンジンへのリクエストを省略し、既に翻訳済みの結果を即座に返すことができるようになります。これにより、アプリケーションの応答性が向上し、API利用コストが削減され、また翻訳エンジンへの負荷も軽減されます。

## 目的・理由
翻訳キャッシュシステムは以下の理由で重要です：

1. パフォーマンスの向上：過去に翻訳したテキストの再翻訳を高速化
2. API利用コストの削減：クラウド翻訳サービスの場合、リクエスト数を最小限に抑えることで、コストを削減
3. オフライン対応の改善：インターネット接続が不安定な環境でも、キャッシュされた翻訳を表示可能
4. ユーザーエクスペリエンスの向上：翻訳の応答時間を短縮し、アプリケーションの体感速度を向上
5. バッテリー消費の削減：モバイルデバイスの場合、翻訳処理やネットワーク通信を減らすことでバッテリー寿命を延長

## 詳細
- メモリベースキャッシュの実装
- 永続化キャッシュの実装（SQLiteなど）
- キャッシュの有効期限と管理戦略の実装
- キャッシュ統計と管理機能の実装

## タスク分解
- [ ] キャッシュインターフェースの設計
  - [ ] `ITranslationCache`インターフェースの設計
  - [ ] キャッシュキー生成戦略の設計
  - [ ] キャッシュエントリモデルの設計
  - [ ] キャッシュ管理インターフェースの設計
- [ ] メモリキャッシュの実装
  - [ ] `MemoryTranslationCache`クラスの実装
  - [ ] LRU（Least Recently Used）アルゴリズムの実装
  - [ ] スレッドセーフなキャッシュアクセスの実装
  - [ ] メモリ使用量の監視と最適化
- [ ] 永続化キャッシュの実装
  - [ ] SQLiteデータベースの設計
  - [ ] `SqliteTranslationCache`クラスの実装
  - [ ] データベースのCRUD操作の最適化
  - [ ] バルク操作の実装（一括保存、一括読み込み）
- [ ] 多層キャッシュ戦略
  - [ ] メモリキャッシュと永続化キャッシュの統合
  - [ ] キャッシュ階層間のデータ同期
  - [ ] キャッシュミスとヒットの追跡
  - [ ] キャッシュウォームアップ機能
- [ ] キャッシュ管理機能
  - [ ] キャッシュサイズの制限と管理
  - [ ] キャッシュクリア機能
  - [ ] キャッシュ有効期限の設定
  - [ ] ゲームプロファイル別キャッシュ管理
- [ ] キャッシュ最適化
  - [ ] 部分一致キャッシュの検討
  - [ ] 類似度ベースのキャッシュマッチング
  - [ ] キャッシュプリフェッチ戦略
  - [ ] キャッシュコンパクション
- [ ] 統計と分析
  - [ ] キャッシュヒット率の計測
  - [ ] キャッシュパフォーマンスの監視
  - [ ] 統計UIの実装
  - [ ] キャッシュ最適化推奨の提供
- [ ] UI統合
  - [ ] 翻訳設定画面へのキャッシュ設定の統合
  - [ ] キャッシュ管理UIの実装
  - [ ] キャッシュ統計の視覚化
  - [ ] キャッシュインポート/エクスポート機能
- [ ] 単体テストの実装

## インターフェース設計例
```csharp
namespace Baketa.Translation.Cache
{
    /// <summary>
    /// 翻訳キャッシュインターフェース
    /// </summary>
    public interface ITranslationCache
    {
        /// <summary>
        /// キャッシュからテキストを取得します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>キャッシュされた翻訳結果、存在しない場合はnull</returns>
        Task<TranslationCacheEntry?> GetAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括取得します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>キーと翻訳結果のディクショナリ</returns>
        Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// テキストをキャッシュに保存します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="entry">キャッシュエントリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetAsync(string key, TranslationCacheEntry entry, TimeSpan? expiration = null);
        
        /// <summary>
        /// 複数のエントリを一括保存します
        /// </summary>
        /// <param name="entries">キーとエントリのディクショナリ</param>
        /// <param name="expiration">有効期限（null=無期限）</param>
        /// <returns>保存が成功したかどうか</returns>
        Task<bool> SetManyAsync(IDictionary<string, TranslationCacheEntry> entries, TimeSpan? expiration = null);
        
        /// <summary>
        /// キャッシュからアイテムを削除します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveAsync(string key);
        
        /// <summary>
        /// 複数のキーに対応するキャッシュを一括削除します
        /// </summary>
        /// <param name="keys">キャッシュキーのリスト</param>
        /// <returns>削除が成功したかどうか</returns>
        Task<bool> RemoveManyAsync(IEnumerable<string> keys);
        
        /// <summary>
        /// キャッシュをクリアします
        /// </summary>
        /// <returns>クリアが成功したかどうか</returns>
        Task<bool> ClearAsync();
        
        /// <summary>
        /// キャッシュのキー一覧を取得します
        /// </summary>
        /// <param name="pattern">検索パターン</param>
        /// <param name="maxCount">最大取得数</param>
        /// <returns>キャッシュキーのリスト</returns>
        Task<IEnumerable<string>> GetKeysAsync(string? pattern = null, int maxCount = 1000);
        
        /// <summary>
        /// キャッシュの統計情報を取得します
        /// </summary>
        /// <returns>キャッシュ統計情報</returns>
        Task<CacheStatistics> GetStatisticsAsync();
        
        /// <summary>
        /// キャッシュを最適化します
        /// </summary>
        /// <returns>最適化が成功したかどうか</returns>
        Task<bool> OptimizeAsync();
        
        /// <summary>
        /// キャッシュをエクスポートします
        /// </summary>
        /// <param name="filePath">エクスポート先ファイルパス</param>
        /// <returns>エクスポートが成功したかどうか</returns>
        Task<bool> ExportAsync(string filePath);
        
        /// <summary>
        /// キャッシュをインポートします
        /// </summary>
        /// <param name="filePath">インポート元ファイルパス</param>
        /// <param name="mergeStrategy">マージ戦略</param>
        /// <returns>インポートが成功したかどうか</returns>
        Task<bool> ImportAsync(string filePath, CacheMergeStrategy mergeStrategy = CacheMergeStrategy.ReplaceExisting);
    }
    
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
    
    /// <summary>
    /// キャッシュキーの生成に使用するユーティリティクラス
    /// </summary>
    public static class CacheKeyGenerator
    {
        /// <summary>
        /// 翻訳キャッシュキーを生成します
        /// </summary>
        /// <param name="sourceText">原文テキスト</param>
        /// <param name="sourceLanguage">元言語</param>
        /// <param name="targetLanguage">対象言語</param>
        /// <param name="engine">翻訳エンジン</param>
        /// <returns>キャッシュキー</returns>
        public static string GenerateKey(string sourceText, string sourceLanguage, string targetLanguage, string engine)
        {
            // クリーンなテキストを使用してキーを生成
            string cleanText = CleanText(sourceText);
            
            // キーを構築
            return $"{sourceLanguage}|{targetLanguage}|{engine}|{cleanText}";
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
            text = Regex.Replace(text, @"\s+", " ");
            
            // 翻訳に影響しない特殊文字を処理
            // （実装詳細は省略）
            
            return text;
        }
    }
}
```

## メモリキャッシュの実装例
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

## SQLite永続化キャッシュの実装例（概要）
```csharp
namespace Baketa.Translation.Cache
{
    /// <summary>
    /// SQLite永続化翻訳キャッシュの実装
    /// </summary>
    public class SqliteTranslationCache : ITranslationCache, IDisposable
    {
        private readonly string _databasePath;
        private readonly SqliteConnection _connection;
        private readonly ILogger? _logger;
        private readonly MemoryTranslationCache _memoryCache;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        private bool _disposed;
        
        /// <summary>
        /// 新しいSQLite翻訳キャッシュを初期化します
        /// </summary>
        /// <param name="databasePath">データベースファイルパス</param>
        /// <param name="memoryCacheSize">メモリキャッシュのサイズ</param>
        /// <param name="logger">ロガー</param>
        public SqliteTranslationCache(string databasePath, int memoryCacheSize = 1000, ILogger? logger = null)
        {
            _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
            _logger = logger;
            
            // メモリキャッシュを初期化
            _memoryCache = new MemoryTranslationCache(memoryCacheSize, logger);
            
            // SQLite接続を作成
            _connection = new SqliteConnection($"Data Source={databasePath}");
            
            // データベースを初期化
            InitializeDatabase();
            
            _logger?.LogInformation("SQLite翻訳キャッシュが初期化されました。データベース: {DatabasePath}", databasePath);
        }
        
        /// <summary>
        /// データベースを初期化します
        /// </summary>
        private void InitializeDatabase()
        {
            try
            {
                _connection.Open();
                
                // テーブルを作成
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    CREATE TABLE IF NOT EXISTS TranslationCache (
                        Key TEXT PRIMARY KEY,
                        SourceText TEXT NOT NULL,
                        TranslatedText TEXT NOT NULL,
                        SourceLanguage TEXT NOT NULL,
                        TargetLanguage TEXT NOT NULL,
                        Engine TEXT NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        LastAccessedAt TEXT NOT NULL,
                        AccessCount INTEGER NOT NULL,
                        ExpiresAt TEXT NULL,
                        Metadata TEXT NULL
                    );
                    
                    CREATE INDEX IF NOT EXISTS idx_translation_cache_last_accessed 
                    ON TranslationCache(LastAccessedAt);
                    
                    CREATE INDEX IF NOT EXISTS idx_translation_cache_expires 
                    ON TranslationCache(ExpiresAt);
                    
                    CREATE INDEX IF NOT EXISTS idx_translation_cache_languages 
                    ON TranslationCache(SourceLanguage, TargetLanguage);
                ";
                command.ExecuteNonQuery();
                
                // クリーンアップとメンテナンスを実行
                Vacuum();
                
                _connection.Close();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "データベース初期化中にエラーが発生しました: {DatabasePath}", _databasePath);
                throw;
            }
        }
        
        /// <inheritdoc />
        public async Task<TranslationCacheEntry?> GetAsync(string key)
        {
            // まずメモリキャッシュを確認
            var entry = await _memoryCache.GetAsync(key);
            if (entry != null)
            {
                return entry;
            }
            
            // メモリになければSQLiteから取得
            try
            {
                await _connection.OpenAsync();
                
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    SELECT * FROM TranslationCache 
                    WHERE Key = @Key 
                    AND (ExpiresAt IS NULL OR ExpiresAt > datetime('now'))
                ";
                command.Parameters.AddWithValue("@Key", key);
                
                using var reader = await command.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    entry = ReadEntryFromReader(reader);
                    
                    // アクセスカウントを更新
                    await UpdateAccessCountAsync(key, entry.AccessCount + 1);
                    
                    // メモリキャッシュにも追加
                    await _memoryCache.SetAsync(key, entry);
                    
                    return entry;
                }
                
                return null;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "キャッシュエントリの取得中にエラーが発生しました: {Key}", key);
                return null;
            }
            finally
            {
                _connection.Close();
            }
        }
        
        // 他のメソッドの実装は省略
        
        /// <summary>
        /// データベースを最適化します
        /// </summary>
        private void Vacuum()
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "VACUUM;";
                command.ExecuteNonQuery();
                
                _logger?.LogDebug("データベースを最適化しました: {DatabasePath}", _databasePath);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "データベース最適化中にエラーが発生しました: {DatabasePath}", _databasePath);
            }
        }
        
        /// <summary>
        /// リーダーからエントリを読み込みます
        /// </summary>
        /// <param name="reader">SQLiteデータリーダー</param>
        /// <returns>キャッシュエントリ</returns>
        private static TranslationCacheEntry ReadEntryFromReader(SqliteDataReader reader)
        {
            var entry = new TranslationCacheEntry
            {
                SourceText = reader.GetString(reader.GetOrdinal("SourceText")),
                TranslatedText = reader.GetString(reader.GetOrdinal("TranslatedText")),
                SourceLanguage = reader.GetString(reader.GetOrdinal("SourceLanguage")),
                TargetLanguage = reader.GetString(reader.GetOrdinal("TargetLanguage")),
                Engine = reader.GetString(reader.GetOrdinal("Engine")),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("CreatedAt"))),
                LastAccessedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("LastAccessedAt"))),
                AccessCount = reader.GetInt32(reader.GetOrdinal("AccessCount"))
            };
            
            // メタデータを読み込み
            int metadataOrdinal = reader.GetOrdinal("Metadata");
            if (!reader.IsDBNull(metadataOrdinal))
            {
                string metadataJson = reader.GetString(metadataOrdinal);
                try
                {
                    var metadata = JsonSerializer.Deserialize<Dictionary<string, string>>(metadataJson);
                    if (metadata != null)
                    {
                        entry.Metadata = metadata;
                    }
                }
                catch
                {
                    // メタデータの解析に失敗した場合は無視
                }
            }
            
            return entry;
        }
        
        /// <summary>
        /// アクセスカウントを更新します
        /// </summary>
        /// <param name="key">キャッシュキー</param>
        /// <param name="newCount">新しいアクセスカウント</param>
        private async Task UpdateAccessCountAsync(string key, int newCount)
        {
            try
            {
                using var command = _connection.CreateCommand();
                command.CommandText = @"
                    UPDATE TranslationCache
                    SET AccessCount = @AccessCount,
                        LastAccessedAt = @LastAccessedAt
                    WHERE Key = @Key
                ";
                command.Parameters.AddWithValue("@Key", key);
                command.Parameters.AddWithValue("@AccessCount", newCount);
                command.Parameters.AddWithValue("@LastAccessedAt", DateTime.UtcNow.ToString("O"));
                
                await command.ExecuteNonQueryAsync();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "アクセスカウントの更新中にエラーが発生しました: {Key}", key);
            }
        }
        
        /// <inheritdoc />
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        
        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースも解放するかどうか</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _connection.Dispose();
                    _lock.Dispose();
                }
                
                _disposed = true;
            }
        }
    }
}
```

## 実装上の注意点
- キャッシュキーの生成は単純かつ一貫した方法で行い、テキストの小さな違いでも別々のキーにならないように注意する
- メモリキャッシュは高速アクセスのために使用し、永続化キャッシュは長期保存のために使用する多層キャッシュ設計を検討する
- スレッドセーフな実装を行い、複数のスレッドから同時にアクセスされても安全に動作するようにする
- キャッシュサイズが無制限に増加しないように、LRU（Least Recently Used）などのアルゴリズムでキャッシュを管理する
- SQLiteデータベースのインデックス最適化やバキューム処理などのメンテナンス処理を定期的に実行する
- キャッシュヒット率などのパフォーマンス指標を記録し、キャッシュの効果を測定する
- アプリケーションの起動時にはメモリキャッシュのウォームアップを行い、よく使用される翻訳をメモリにプリロードする
- 大量の翻訳キャッシュを効率的に処理するためのバルク操作やバッチ処理を実装する
- ユーザーにとって使いやすいキャッシュ管理UIを提供し、キャッシュサイズや有効期限などの設定を簡単に変更できるようにする
- キャッシュの共有機能を提供し、ユーザー間でキャッシュを交換できるようにする

## 関連Issue/参考
- 親Issue: なし（これが親Issue）
- 関連: #9 翻訳システム基盤の構築
- 関連: #12 設定画面
- 参照: E:\dev\Baketa\docs\3-architecture\translation-system\translation-cache.md
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (3.2 非同期/同期メソッドのペア)
- 参照: E:\dev\Baketa\docs\2-development\coding-standards\csharp-standards.md (4.2 リソース解放とDisposable)

## マイルストーン
マイルストーン4: 機能拡張と最適化

## ラベル
- `type: feature`
- `priority: low`
- `component: translation`
