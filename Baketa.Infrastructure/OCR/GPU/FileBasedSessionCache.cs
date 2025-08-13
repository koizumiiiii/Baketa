using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using System.IO;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;

namespace Baketa.Infrastructure.OCR.GPU;

/// <summary>
/// ファイルベース永続セッションキャッシュ実装
/// 高速起動とセッション復元のためのディスクベースキャッシュ
/// Issue #143 Week 2 Phase 3: 永続化システム
/// </summary>
public sealed class FileBasedSessionCache : IPersistentSessionCache, IDisposable
{
    private readonly ILogger<FileBasedSessionCache> _logger;
    private readonly OcrSettings _ocrSettings;
    private readonly string _cacheRootPath;
    private readonly ConcurrentDictionary<string, SessionMetadata> _metadataCache = new();
    private readonly ConcurrentDictionary<string, DateTime> _accessTracker = new();
    private readonly System.Threading.Timer _cleanupTimer;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly object _statsLock = new();
    private bool _disposed = false;
    
    // 統計情報
    private int _hitCount = 0;
    private int _missCount = 0;
    private long _totalStoredSize = 0;

    public FileBasedSessionCache(
        ILogger<FileBasedSessionCache> logger,
        IOptions<OcrSettings> ocrSettings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ocrSettings = ocrSettings?.Value ?? throw new ArgumentNullException(nameof(ocrSettings));
        
        // キャッシュディレクトリ設定
        _cacheRootPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Baketa", "SessionCache");
        
        Directory.CreateDirectory(_cacheRootPath);
        
        // JSON設定
        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        
        // 定期クリーンアップタイマー（1時間間隔）
        _cleanupTimer = new System.Threading.Timer(CleanupCallback, null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(1));
        
        // 起動時にメタデータキャッシュを初期化
        InitializeMetadataCache();
        
        _logger.LogInformation("💾 FileBasedSessionCache初期化完了 - パス: {CachePath}", _cacheRootPath);
    }

    public async Task<CacheStoreResult> StoreSessionAsync(string cacheKey, SessionCacheData sessionData, SessionMetadata metadata, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("💾 セッションキャッシュ保存開始: {CacheKey}", cacheKey);
            
            var keyHash = GenerateKeyHash(cacheKey);
            var dataFilePath = GetDataFilePath(keyHash);
            var metadataFilePath = GetMetadataFilePath(keyHash);
            var tempDataPath = dataFilePath + ".tmp";
            var tempMetadataPath = metadataFilePath + ".tmp";
            
            await _fileLock.WaitAsync(cancellationToken);
            
            try
            {
                var existingSize = 0L;
                var overwroteExisting = System.IO.File.Exists(dataFilePath);
                if (overwroteExisting)
                {
                    existingSize = new System.IO.FileInfo(dataFilePath).Length;
                }
                
                // セッションデータの保存
                await SaveSessionDataToFile(tempDataPath, sessionData, cancellationToken);
                
                // メタデータの保存
                metadata.LastAccessedAt = DateTime.UtcNow;
                await SaveMetadataToFile(tempMetadataPath, metadata, cancellationToken);
                
                // アトミックな置換
                System.IO.File.Move(tempDataPath, dataFilePath, true);
                System.IO.File.Move(tempMetadataPath, metadataFilePath, true);
                
                // キャッシュ更新
                _metadataCache.AddOrUpdate(cacheKey, metadata, (_, _) => metadata);
                _accessTracker.AddOrUpdate(cacheKey, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
                
                var storedSize = new System.IO.FileInfo(dataFilePath).Length;
                
                lock (_statsLock)
                {
                    _totalStoredSize = _totalStoredSize - existingSize + storedSize;
                }
                
                stopwatch.Stop();
                
                var result = new CacheStoreResult
                {
                    IsSuccessful = true,
                    StoreDuration = stopwatch.Elapsed,
                    StoredSize = storedSize,
                    OverwroteExisting = overwroteExisting
                };
                
                _logger.LogDebug("✅ セッションキャッシュ保存完了: {CacheKey} - サイズ: {Size}B, 時間: {Duration}ms",
                    cacheKey, storedSize, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            finally
            {
                _fileLock.Release();
                
                // 一時ファイルのクリーンアップ
                try
                {
                    if (System.IO.File.Exists(tempDataPath)) System.IO.File.Delete(tempDataPath);
                    if (System.IO.File.Exists(tempMetadataPath)) System.IO.File.Delete(tempMetadataPath);
                }
                catch (Exception cleanupEx)
                {
                    _logger.LogWarning(cleanupEx, "一時ファイルクリーンアップ警告: {CacheKey}", cacheKey);
                }
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ セッションキャッシュ保存失敗: {CacheKey}", cacheKey);
            
            return new CacheStoreResult
            {
                IsSuccessful = false,
                StoreDuration = stopwatch.Elapsed,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<CacheRetrieveResult> RetrieveSessionAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            _logger.LogDebug("📥 セッションキャッシュ取得開始: {CacheKey}", cacheKey);
            
            var keyHash = GenerateKeyHash(cacheKey);
            var dataFilePath = GetDataFilePath(keyHash);
            var metadataFilePath = GetMetadataFilePath(keyHash);
            
            if (!System.IO.File.Exists(dataFilePath) || !System.IO.File.Exists(metadataFilePath))
            {
                lock (_statsLock)
                {
                    _missCount++;
                }
                
                stopwatch.Stop();
                return new CacheRetrieveResult
                {
                    IsSuccessful = false,
                    RetrieveDuration = stopwatch.Elapsed,
                    ErrorMessage = "キャッシュエントリが見つかりません",
                    HitRatio = CalculateHitRatio()
                };
            }
            
            await _fileLock.WaitAsync(cancellationToken);
            
            try
            {
                // メタデータを読み込み、有効期限をチェック
                var metadata = await LoadMetadataFromFile(metadataFilePath, cancellationToken);
                if (metadata.ExpiresAt < DateTime.UtcNow)
                {
                    _logger.LogDebug("⏰ キャッシュエントリが期限切れ: {CacheKey}", cacheKey);
                    
                    // 期限切れファイルを削除
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            System.IO.File.Delete(dataFilePath);
                            System.IO.File.Delete(metadataFilePath);
                        }
                        catch (Exception deleteEx)
                        {
                            _logger.LogWarning(deleteEx, "期限切れファイル削除警告: {CacheKey}", cacheKey);
                        }
                    }, cancellationToken);
                    
                    lock (_statsLock)
                    {
                        _missCount++;
                    }
                    
                    stopwatch.Stop();
                    return new CacheRetrieveResult
                    {
                        IsSuccessful = false,
                        RetrieveDuration = stopwatch.Elapsed,
                        ErrorMessage = "キャッシュエントリが期限切れです",
                        HitRatio = CalculateHitRatio()
                    };
                }
                
                // セッションデータを読み込み
                var sessionData = await LoadSessionDataFromFile(dataFilePath, cancellationToken);
                
                // メタデータを更新
                metadata.LastAccessedAt = DateTime.UtcNow;
                metadata.UsageCount++;
                
                // 非同期でメタデータを更新保存
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SaveMetadataToFile(metadataFilePath, metadata, CancellationToken.None);
                        _metadataCache.AddOrUpdate(cacheKey, metadata, (_, _) => metadata);
                        _accessTracker.AddOrUpdate(cacheKey, DateTime.UtcNow, (_, _) => DateTime.UtcNow);
                    }
                    catch (Exception updateEx)
                    {
                        _logger.LogWarning(updateEx, "メタデータ更新警告: {CacheKey}", cacheKey);
                    }
                }, cancellationToken);
                
                lock (_statsLock)
                {
                    _hitCount++;
                }
                
                stopwatch.Stop();
                
                var result = new CacheRetrieveResult
                {
                    IsSuccessful = true,
                    SessionData = sessionData,
                    Metadata = metadata,
                    RetrieveDuration = stopwatch.Elapsed,
                    HitRatio = CalculateHitRatio()
                };
                
                _logger.LogDebug("✅ セッションキャッシュ取得完了: {CacheKey} - 時間: {Duration}ms",
                    cacheKey, stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ セッションキャッシュ取得失敗: {CacheKey}", cacheKey);
            
            lock (_statsLock)
            {
                _missCount++;
            }
            
            return new CacheRetrieveResult
            {
                IsSuccessful = false,
                RetrieveDuration = stopwatch.Elapsed,
                ErrorMessage = ex.Message,
                HitRatio = CalculateHitRatio()
            };
        }
    }

    public async Task<bool> ExistsAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var keyHash = GenerateKeyHash(cacheKey);
            var dataFilePath = GetDataFilePath(keyHash);
            var metadataFilePath = GetMetadataFilePath(keyHash);
            
            if (!System.IO.File.Exists(dataFilePath) || !System.IO.File.Exists(metadataFilePath))
            {
                return false;
            }
            
            // 期限切れチェック
            var metadata = await LoadMetadataFromFile(metadataFilePath, cancellationToken);
            return metadata.ExpiresAt >= DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "キャッシュ存在確認中に警告: {CacheKey}", cacheKey);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string cacheKey, CancellationToken cancellationToken = default)
    {
        try
        {
            var keyHash = GenerateKeyHash(cacheKey);
            var dataFilePath = GetDataFilePath(keyHash);
            var metadataFilePath = GetMetadataFilePath(keyHash);
            
            await _fileLock.WaitAsync(cancellationToken);
            
            try
            {
                var existed = System.IO.File.Exists(dataFilePath);
                
                if (existed)
                {
                    var fileInfo = new System.IO.FileInfo(dataFilePath);
                    var size = fileInfo.Length;
                    
                    System.IO.File.Delete(dataFilePath);
                    if (System.IO.File.Exists(metadataFilePath))
                    {
                        System.IO.File.Delete(metadataFilePath);
                    }
                    
                    _metadataCache.TryRemove(cacheKey, out _);
                    _accessTracker.TryRemove(cacheKey, out _);
                    
                    lock (_statsLock)
                    {
                        _totalStoredSize -= size;
                    }
                    
                    _logger.LogDebug("🗑️ キャッシュエントリ削除: {CacheKey}", cacheKey);
                }
                
                return existed;
            }
            finally
            {
                _fileLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュエントリ削除失敗: {CacheKey}", cacheKey);
            return false;
        }
    }

    public async Task<int> CleanupExpiredEntriesAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var removedCount = 0;
        var freedSize = 0L;
        
        try
        {
            _logger.LogDebug("🧹 期限切れキャッシュクリーンアップ開始");
            
            var currentTime = DateTime.UtcNow;
            var expiredKeys = new List<string>();
            
            // 期限切れエントリを特定
            await foreach (var (key, metadata) in GetAllMetadataAsync(cancellationToken))
            {
                if (metadata.ExpiresAt < currentTime)
                {
                    expiredKeys.Add(key);
                }
            }
            
            // 期限切れエントリを削除
            foreach (var key in expiredKeys)
            {
                try
                {
                    var keyHash = GenerateKeyHash(key);
                    var dataFilePath = GetDataFilePath(keyHash);
                    
                    if (System.IO.File.Exists(dataFilePath))
                    {
                        var size = new System.IO.FileInfo(dataFilePath).Length;
                        freedSize += size;
                    }
                    
                    if (await RemoveAsync(key, cancellationToken))
                    {
                        removedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "期限切れエントリ削除警告: {Key}", key);
                }
            }
            
            stopwatch.Stop();
            
            _logger.LogInformation("✅ 期限切れキャッシュクリーンアップ完了 - 削除数: {Count}, 解放サイズ: {Size}B, 時間: {Duration}ms",
                removedCount, freedSize, stopwatch.ElapsedMilliseconds);
            
            return removedCount;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ 期限切れキャッシュクリーンアップ失敗");
            return removedCount;
        }
    }

    public async Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var totalEntries = _metadataCache.Count;
            var expiredCount = 0;
            var currentTime = DateTime.UtcNow;
            
            await foreach (var (_, metadata) in GetAllMetadataAsync(cancellationToken))
            {
                if (metadata.ExpiresAt < currentTime)
                {
                    expiredCount++;
                }
            }
            
            lock (_statsLock)
            {
                return new CacheStatistics
                {
                    TotalEntries = totalEntries,
                    UsedSize = _totalStoredSize,
                    HitCount = _hitCount,
                    MissCount = _missCount,
                    ExpiredEntries = expiredCount,
                    LastOptimizationTime = null // 実装後に設定
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ統計取得失敗");
            return new CacheStatistics();
        }
    }

    public async Task<CacheOptimizationResult> OptimizeCacheAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var actions = new List<string>();
        var removedEntries = 0;
        var freedSize = 0L;
        
        try
        {
            _logger.LogInformation("⚡ キャッシュ最適化開始");
            
            // 1. 期限切れエントリの削除
            var expiredRemoved = await CleanupExpiredEntriesAsync(cancellationToken);
            if (expiredRemoved > 0)
            {
                removedEntries += expiredRemoved;
                actions.Add($"期限切れエントリ {expiredRemoved} 件を削除");
            }
            
            // 2. 低優先度かつ長期未使用エントリの削除
            var unusedThreshold = DateTime.UtcNow.AddDays(-7);
            var lowPriorityRemoved = await RemoveLowPriorityUnusedEntries(unusedThreshold, cancellationToken);
            if (lowPriorityRemoved > 0)
            {
                removedEntries += lowPriorityRemoved;
                actions.Add($"低優先度未使用エントリ {lowPriorityRemoved} 件を削除");
            }
            
            // 3. ディスク容量チェックとサイズベース削除
            var diskSpaceOptimization = await OptimizeDiskSpace(cancellationToken);
            removedEntries += diskSpaceOptimization.removedCount;
            freedSize += diskSpaceOptimization.freedSize;
            if (diskSpaceOptimization.removedCount > 0)
            {
                actions.Add($"ディスク容量最適化で {diskSpaceOptimization.removedCount} 件削除");
            }
            
            stopwatch.Stop();
            
            var result = new CacheOptimizationResult
            {
                OptimizationExecuted = actions.Count > 0,
                RemovedEntries = removedEntries,
                FreedSize = freedSize,
                OptimizationDuration = stopwatch.Elapsed,
                ExecutedActions = actions,
                EstimatedPerformanceImprovement = removedEntries > 0 ? 0.1 : 0.0
            };
            
            _logger.LogInformation("✅ キャッシュ最適化完了 - 削除: {Count}件, 解放: {Size}B, 時間: {Duration}ms",
                removedEntries, freedSize, stopwatch.ElapsedMilliseconds);
            
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ キャッシュ最適化失敗");
            
            return new CacheOptimizationResult
            {
                OptimizationExecuted = false,
                OptimizationDuration = stopwatch.Elapsed
            };
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableKeysAsync(string? pattern = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var keys = new List<string>();
            
            await foreach (var (key, metadata) in GetAllMetadataAsync(cancellationToken))
            {
                if (metadata.ExpiresAt >= DateTime.UtcNow)
                {
                    if (pattern == null || key.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        keys.Add(key);
                    }
                }
            }
            
            return keys.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 利用可能キー取得失敗");
            return Array.Empty<string>();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _cleanupTimer?.Dispose();
        _fileLock?.Dispose();
        _disposed = true;
        
        _logger.LogInformation("🧹 FileBasedSessionCache リソース解放完了");
    }

    private void InitializeMetadataCache()
    {
        try
        {
            var metadataFiles = Directory.GetFiles(_cacheRootPath, "*.metadata.json");
            var loadedCount = 0;
            
            foreach (var metadataFile in metadataFiles)
            {
                try
                {
                    var metadata = LoadMetadataFromFileSync(metadataFile);
                    var keyHash = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(metadataFile));
                    var cacheKey = ReverseKeyHash(keyHash); // 実際は逆変換不可なので別途管理が必要
                    
                    if (metadata.ExpiresAt >= DateTime.UtcNow)
                    {
                        _metadataCache.TryAdd(cacheKey, metadata);
                        loadedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "メタデータ読み込み警告: {File}", metadataFile);
                }
            }
            
            _logger.LogInformation("📋 メタデータキャッシュ初期化完了 - 読み込み: {Count}件", loadedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ メタデータキャッシュ初期化失敗");
        }
    }

    private string GenerateKeyHash(string key)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hashBytes)[..16]; // 最初の16文字を使用
    }

    private string ReverseKeyHash(string hash)
    {
        // ハッシュから元のキーは復元できないため、別途マッピングを管理する必要がある
        // ここではプレースホルダー実装
        return hash;
    }

    private string GetDataFilePath(string keyHash) =>
        Path.Combine(_cacheRootPath, $"{keyHash}.session.bin");

    private string GetMetadataFilePath(string keyHash) =>
        Path.Combine(_cacheRootPath, $"{keyHash}.metadata.json");

    private async Task SaveSessionDataToFile(string filePath, SessionCacheData sessionData, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(sessionData, _jsonOptions);
        await System.IO.File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private async Task SaveMetadataToFile(string filePath, SessionMetadata metadata, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(metadata, _jsonOptions);
        await System.IO.File.WriteAllTextAsync(filePath, json, cancellationToken);
    }

    private async Task<SessionCacheData> LoadSessionDataFromFile(string filePath, CancellationToken cancellationToken)
    {
        var json = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<SessionCacheData>(json, _jsonOptions) ?? new SessionCacheData();
    }

    private async Task<SessionMetadata> LoadMetadataFromFile(string filePath, CancellationToken cancellationToken)
    {
        var json = await System.IO.File.ReadAllTextAsync(filePath, cancellationToken);
        return JsonSerializer.Deserialize<SessionMetadata>(json, _jsonOptions) ?? new SessionMetadata();
    }

    private SessionMetadata LoadMetadataFromFileSync(string filePath)
    {
        var json = System.IO.File.ReadAllText(filePath);
        return JsonSerializer.Deserialize<SessionMetadata>(json, _jsonOptions) ?? new SessionMetadata();
    }

    private double CalculateHitRatio()
    {
        lock (_statsLock)
        {
            var total = _hitCount + _missCount;
            return total > 0 ? (double)_hitCount / total : 0.0;
        }
    }

    private async IAsyncEnumerable<(string key, SessionMetadata metadata)> GetAllMetadataAsync(CancellationToken cancellationToken)
    {
        foreach (var kvp in _metadataCache)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return (kvp.Key, kvp.Value);
        }
        
        await Task.CompletedTask;
    }

    private async Task<int> RemoveLowPriorityUnusedEntries(DateTime unusedThreshold, CancellationToken cancellationToken)
    {
        var removedCount = 0;
        var keysToRemove = new List<string>();
        
        await foreach (var (key, metadata) in GetAllMetadataAsync(cancellationToken))
        {
            if (metadata.Priority == CachePriority.Low && 
                metadata.LastAccessedAt < unusedThreshold)
            {
                keysToRemove.Add(key);
            }
        }
        
        foreach (var key in keysToRemove)
        {
            if (await RemoveAsync(key, cancellationToken))
            {
                removedCount++;
            }
        }
        
        return removedCount;
    }

    private async Task<(int removedCount, long freedSize)> OptimizeDiskSpace(CancellationToken cancellationToken)
    {
        await Task.Delay(10, cancellationToken); // プレースホルダー実装
        
        // 実装: ディスク容量チェックとサイズベース最適化
        // 例: 使用可能容量が10%以下の場合、古いエントリから削除
        
        return (0, 0L);
    }

    private void CleanupCallback(object? state)
    {
        try
        {
            if (_disposed) return;
            
            _ = Task.Run(async () =>
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
                    await CleanupExpiredEntriesAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "定期クリーンアップ中に警告が発生");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "クリーンアップタイマーでエラーが発生");
        }
    }
}