# SQLite永続化キャッシュの実装概要

SQLiteデータベースを使用した永続キャッシュの実装例（概要）です。

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