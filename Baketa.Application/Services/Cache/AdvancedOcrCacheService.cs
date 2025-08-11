using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Cache;

/// <summary>
/// Step3: Gemini推奨高度キャッシング戦略 - OCR結果の高速キャッシング
/// 画像ハッシュベースでOCR結果をキャッシュし、数ミリ秒での応答を実現
/// </summary>
public sealed class AdvancedOcrCacheService : IAdvancedOcrCacheService
{
    private readonly ILogger<AdvancedOcrCacheService> _logger;
    private readonly ConcurrentDictionary<string, CachedOcrResult> _cache;
    private readonly ConcurrentDictionary<string, DateTime> _accessTimes;
    private readonly ReaderWriterLockSlim _cleanupLock;
    private readonly System.Threading.Timer _cleanupTimer;
    
    // 🎯 Step3設定値
    private const int MaxCacheSize = 10000; // 最大キャッシュエントリ数
    private const int CacheExpiryMinutes = 60; // キャッシュ有効期限
    private const int CleanupIntervalMinutes = 10; // クリーンアップ間隔

    public AdvancedOcrCacheService(ILogger<AdvancedOcrCacheService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = new ConcurrentDictionary<string, CachedOcrResult>();
        _accessTimes = new ConcurrentDictionary<string, DateTime>();
        _cleanupLock = new ReaderWriterLockSlim();
        
        // 定期クリーンアップタイマー設定
        _cleanupTimer = new System.Threading.Timer(PerformCleanup, null, 
            TimeSpan.FromMinutes(CleanupIntervalMinutes),
            TimeSpan.FromMinutes(CleanupIntervalMinutes));

        _logger.LogInformation("🚀 AdvancedOcrCacheService initialized - Step3高度キャッシング戦略");
    }

    /// <summary>
    /// 画像のハッシュを生成（高速SHA256ベース）
    /// </summary>
    public string GenerateImageHash(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        
        var stopwatch = Stopwatch.StartNew();
        
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(imageData);
        var hash = Convert.ToBase64String(hashBytes);
        
        stopwatch.Stop();
        
        _logger.LogDebug("🔍 画像ハッシュ生成: {Hash} - 時間: {ElapsedMs}ms, サイズ: {Size}bytes", 
            hash[..12], stopwatch.ElapsedMilliseconds, imageData.Length);
        
        return hash;
    }

    /// <summary>
    /// OCR結果をキャッシュに保存
    /// </summary>
    public void CacheResult(string imageHash, OcrResults result)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageHash);
        ArgumentNullException.ThrowIfNull(result);

        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var cached = new CachedOcrResult
            {
                Result = result,
                CachedAt = DateTime.UtcNow,
                AccessCount = 1
            };

            _cache.AddOrUpdate(imageHash, cached, (key, existing) =>
            {
                existing.Result = result;
                existing.AccessCount++;
                existing.LastAccessAt = DateTime.UtcNow;
                return existing;
            });

            _accessTimes[imageHash] = DateTime.UtcNow;
            
            stopwatch.Stop();
            
            _logger.LogDebug("💾 OCR結果キャッシュ保存: {Hash} - 時間: {ElapsedMs}ms, 認識数: {TextCount}", 
                imageHash[..12], stopwatch.ElapsedMilliseconds, result.TextRegions.Count);
            
            // 容量チェック（非同期で実行）
            _ = Task.Run(CheckCacheSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ保存エラー: {Hash}", imageHash[..12]);
        }
    }

    /// <summary>
    /// キャッシュからOCR結果を取得
    /// </summary>
    public OcrResults? GetCachedResult(string imageHash)
    {
        ArgumentException.ThrowIfNullOrEmpty(imageHash);
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            if (!_cache.TryGetValue(imageHash, out var cached))
            {
                _logger.LogDebug("🔍 キャッシュミス: {Hash}", imageHash[..12]);
                return null;
            }

            // 有効期限チェック
            if (DateTime.UtcNow - cached.CachedAt > TimeSpan.FromMinutes(CacheExpiryMinutes))
            {
                _cache.TryRemove(imageHash, out _);
                _accessTimes.TryRemove(imageHash, out _);
                _logger.LogDebug("⏰ キャッシュ期限切れ: {Hash}", imageHash[..12]);
                return null;
            }

            // アクセス情報更新
            cached.AccessCount++;
            cached.LastAccessAt = DateTime.UtcNow;
            _accessTimes[imageHash] = DateTime.UtcNow;
            
            stopwatch.Stop();
            
            _logger.LogInformation("⚡ キャッシュヒット: {Hash} - 時間: {ElapsedMs}ms, アクセス数: {AccessCount}, 認識数: {TextCount}", 
                imageHash[..12], stopwatch.ElapsedMilliseconds, cached.AccessCount, cached.Result.TextRegions.Count);
            
            return cached.Result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ キャッシュ取得エラー: {Hash}", imageHash[..12]);
            return null;
        }
    }

    /// <summary>
    /// キャッシュサイズをチェックし、必要に応じて古いエントリを削除
    /// </summary>
    private async Task CheckCacheSize()
    {
        if (_cache.Count <= MaxCacheSize) return;

        await Task.Run(() =>
        {
            _cleanupLock.EnterWriteLock();
            try
            {
                var entriesToRemove = _cache.Count - (MaxCacheSize * 3 / 4); // 75%まで削減
                if (entriesToRemove <= 0) return;

                _logger.LogInformation("🧹 キャッシュサイズ超過 - {CurrentCount}/{MaxSize}、{RemoveCount}エントリを削除", 
                    _cache.Count, MaxCacheSize, entriesToRemove);

                var sortedEntries = new List<(string Key, DateTime LastAccess)>();
                foreach (var kvp in _accessTimes)
                {
                    sortedEntries.Add((kvp.Key, kvp.Value));
                }

                // アクセス時刻でソート（古いものから削除）
                sortedEntries.Sort((a, b) => a.LastAccess.CompareTo(b.LastAccess));

                var removedCount = 0;
                foreach (var (key, _) in sortedEntries.Take(entriesToRemove))
                {
                    if (_cache.TryRemove(key, out _))
                    {
                        _accessTimes.TryRemove(key, out _);
                        removedCount++;
                    }
                }

                _logger.LogInformation("✅ キャッシュクリーンアップ完了 - {RemovedCount}エントリ削除、現在のサイズ: {CurrentSize}", 
                    removedCount, _cache.Count);
            }
            finally
            {
                _cleanupLock.ExitWriteLock();
            }
        });
    }

    /// <summary>
    /// 定期クリーンアップ（期限切れエントリの削除）
    /// </summary>
    private void PerformCleanup(object? state)
    {
        try
        {
            _cleanupLock.EnterWriteLock();
            
            var expiredKeys = new List<string>();
            var expiryCutoff = DateTime.UtcNow.AddMinutes(-CacheExpiryMinutes);
            
            foreach (var kvp in _cache)
            {
                if (kvp.Value.LastAccessAt.GetValueOrDefault(kvp.Value.CachedAt) < expiryCutoff)
                {
                    expiredKeys.Add(kvp.Key);
                }
            }

            var removedCount = 0;
            foreach (var key in expiredKeys)
            {
                if (_cache.TryRemove(key, out _))
                {
                    _accessTimes.TryRemove(key, out _);
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogInformation("🕒 定期クリーンアップ完了 - {RemovedCount}期限切れエントリ削除、現在のサイズ: {CurrentSize}",
                    removedCount, _cache.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 定期クリーンアップエラー");
        }
        finally
        {
            if (_cleanupLock.IsWriteLockHeld)
                _cleanupLock.ExitWriteLock();
        }
    }

    /// <summary>
    /// キャッシュ統計情報を取得
    /// </summary>
    public CacheStatistics GetStatistics()
    {
        _cleanupLock.EnterReadLock();
        try
        {
            var totalHits = 0L;
            var oldestEntry = DateTime.UtcNow;
            var newestEntry = DateTime.MinValue;

            foreach (var entry in _cache.Values)
            {
                totalHits += entry.AccessCount;
                
                var entryTime = entry.LastAccessAt ?? entry.CachedAt;
                if (entryTime < oldestEntry) oldestEntry = entryTime;
                if (entryTime > newestEntry) newestEntry = entryTime;
            }

            return new CacheStatistics
            {
                TotalEntries = _cache.Count,
                TotalHits = totalHits,
                OldestEntry = _cache.Count > 0 ? oldestEntry : null,
                NewestEntry = _cache.Count > 0 ? newestEntry : null,
                MaxCapacity = MaxCacheSize
            };
        }
        finally
        {
            _cleanupLock.ExitReadLock();
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        _cleanupLock?.Dispose();
        _logger.LogInformation("🗑️ AdvancedOcrCacheService disposed");
    }
}

/// <summary>
/// キャッシュされたOCR結果
/// </summary>
public sealed class CachedOcrResult
{
    public required OcrResults Result { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime? LastAccessAt { get; set; }
    public long AccessCount { get; set; }
}

/// <summary>
/// キャッシュ統計情報
/// </summary>
public sealed class CacheStatistics
{
    public int TotalEntries { get; set; }
    public long TotalHits { get; set; }
    public DateTime? OldestEntry { get; set; }
    public DateTime? NewestEntry { get; set; }
    public int MaxCapacity { get; set; }
}