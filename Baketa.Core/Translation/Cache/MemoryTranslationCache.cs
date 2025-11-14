using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Core.Translation.Common;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Translation.Cache;

/// <summary>
/// メモリ内翻訳キャッシュ
/// </summary>
public class MemoryTranslationCache : ITranslationCache
{
    private readonly ConcurrentDictionary<string, TranslationCacheEntry> _cache = new();
    private readonly TranslationCacheOptions _options;
    private readonly ILogger<MemoryTranslationCache> _logger;
    private int _hits;
    private int _misses;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="options">キャッシュオプション</param>
    /// <param name="logger">ロガー</param>
    public MemoryTranslationCache(
        TranslationCacheOptions options,
        ILogger<MemoryTranslationCache> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _logger.LogInformation("メモリキャッシュを初期化しました（最大項目数: {MaxItems}）", _options.MemoryCacheMaxItems());
    }

    /// <summary>
    /// キャッシュからエントリを取得します
    /// </summary>
    /// <param name="key">キャッシュキー</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>キャッシュエントリ（存在しない場合はnull）</returns>
    public Task<TranslationCacheEntry?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        if (_cache.TryGetValue(key, out var entry))
        {
            // 有効期限チェック
            if (IsExpired(entry))
            {
                _cache.TryRemove(key, out _);
                Interlocked.Increment(ref _misses);
                _logger.LogDebug("キャッシュキー '{Key}' は期限切れです", key);
                return Task.FromResult<TranslationCacheEntry?>(null);
            }

            // アクセス情報を更新
            entry.AccessCount++;
            entry.LastAccessedAt = DateTime.UtcNow;
            Interlocked.Increment(ref _hits);

            _logger.LogDebug("キャッシュキー '{Key}' がヒットしました", key);
            return Task.FromResult<TranslationCacheEntry?>(entry.Clone());
        }

        Interlocked.Increment(ref _misses);
        _logger.LogDebug("キャッシュキー '{Key}' はミスしました", key);
        return Task.FromResult<TranslationCacheEntry?>(null);
    }

    /// <summary>
    /// 複数のキャッシュエントリを取得します
    /// </summary>
    /// <param name="keys">キャッシュキーのコレクション</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>キャッシュエントリの辞書</returns>
    public Task<IDictionary<string, TranslationCacheEntry>> GetManyAsync(
        IEnumerable<string> keys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        var result = new Dictionary<string, TranslationCacheEntry>();

        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            if (_cache.TryGetValue(key, out var entry) && !IsExpired(entry))
            {
                // アクセス情報を更新
                entry.AccessCount++;
                entry.LastAccessedAt = DateTime.UtcNow;
                Interlocked.Increment(ref _hits);

                result[key] = entry.Clone();
            }
            else
            {
                Interlocked.Increment(ref _misses);
            }
        }

        _logger.LogDebug("{Found} / {Total} のキャッシュエントリが見つかりました", result.Count, keys);
        return Task.FromResult<IDictionary<string, TranslationCacheEntry>>(result);
    }

    /// <summary>
    /// キャッシュにエントリを保存します
    /// </summary>
    /// <param name="key">キャッシュキー</param>
    /// <param name="entry">キャッシュエントリ</param>
    /// <param name="expiration">有効期限（null=デフォルト）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>保存に成功した場合はtrue</returns>
    public Task<bool> SetAsync(
        string key,
        TranslationCacheEntry entry,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(entry);

        // キャッシュが最大容量に達した場合、何もしない
        if (_cache.Count >= _options.MemoryCacheMaxItems() && !_cache.ContainsKey(key))
        {
            _logger.LogWarning("キャッシュが最大容量 ({MaxItems}) に達しました", _options.MemoryCacheMaxItems());
            return Task.FromResult(false);
        }

        // 有効期限を設定
        var expirationTime = expiration ?? _options.CacheExpiration();
        entry.ExpiresAt = expirationTime.HasValue ? DateTime.UtcNow.Add(expirationTime.Value) : null;

        // キャッシュに保存
        _cache[key] = entry.Clone();
        _logger.LogDebug("キー '{Key}' をキャッシュに保存しました", key);

        return Task.FromResult(true);
    }

    /// <summary>
    /// 複数のキャッシュエントリを保存します
    /// </summary>
    /// <param name="entries">キャッシュエントリの辞書</param>
    /// <param name="expiration">有効期限（null=デフォルト）</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>保存に成功した場合はtrue</returns>
    public Task<bool> SetManyAsync(
        IDictionary<string, TranslationCacheEntry> entries,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entries);

        if (entries.Count == 0)
        {
            return Task.FromResult(true);
        }

        // キャッシュが最大容量に達した場合のチェック
        var newKeysCount = 0;
        foreach (var key in entries.Keys)
        {
            if (!_cache.ContainsKey(key))
            {
                newKeysCount++;
            }
        }

        if (_cache.Count + newKeysCount > _options.MemoryCacheMaxItems())
        {
            _logger.LogWarning(
                "キャッシュが最大容量 ({MaxItems}) を超えるため、一部のエントリは保存されません",
                _options.MemoryCacheMaxItems());
        }

        // 有効期限を設定
        var expirationTime = expiration ?? _options.CacheExpiration();
        DateTime? expiresAt = null;
        if (expirationTime.HasValue)
        {
            expiresAt = DateTime.UtcNow.Add(expirationTime.Value);
        }

        var savedCount = 0;
        foreach (var (key, entry) in entries)
        {
            if (string.IsNullOrEmpty(key) || entry == null)
            {
                continue;
            }

            // キャッシュが最大容量に達した場合、既存のキーのみ更新
            if (_cache.Count >= _options.MemoryCacheMaxItems() && !_cache.ContainsKey(key))
            {
                continue;
            }

            // 有効期限を設定
            var clonedEntry = entry.Clone();
            clonedEntry.ExpiresAt = expiresAt;

            // キャッシュに保存
            _cache[key] = clonedEntry;
            savedCount++;
        }

        _logger.LogDebug("{Saved} / {Total} のエントリをキャッシュに保存しました", savedCount, entries.Count);
        return Task.FromResult(true);
    }

    /// <summary>
    /// キャッシュからエントリを削除します
    /// </summary>
    /// <param name="key">キャッシュキー</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>削除に成功した場合はtrue</returns>
    public Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);

        var result = _cache.TryRemove(key, out _);

        if (result)
        {
            _logger.LogDebug("キー '{Key}' をキャッシュから削除しました", key);
        }
        else
        {
            _logger.LogDebug("キー '{Key}' はキャッシュに存在しません", key);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// キャッシュをクリアします
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>クリアに成功した場合はtrue</returns>
    public Task<bool> ClearAsync(CancellationToken cancellationToken = default)
    {
        var count = _cache.Count;
        _cache.Clear();
        _hits = 0;
        _misses = 0;

        _logger.LogInformation("キャッシュをクリアしました（{Count} エントリ）", count);
        return Task.FromResult(true);
    }

    /// <summary>
    /// キャッシュの統計情報を取得します
    /// </summary>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>キャッシュ統計情報</returns>
    public Task<CacheStatistics> GetStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var totalEntries = _cache.Count;
        var totalHits = _hits;
        var totalMisses = _misses;
        var hitRate = totalHits + totalMisses > 0 ? (float)totalHits / (totalHits + totalMisses) : 0f;

        var stats = new CacheStatistics
        {
            TotalEntries = totalEntries,
            TotalHits = totalHits,
            TotalMisses = totalMisses,
            HitRate = hitRate,
            MaxEntries = _options.MemoryCacheMaxItems(),
            CurrentSizeBytes = EstimateCurrentSizeBytes(),
            GeneratedAt = DateTime.UtcNow
        };

        _logger.LogDebug("キャッシュ統計: エントリ数={Entries}, ヒット率={HitRate:P2}", totalEntries, hitRate);
        return Task.FromResult(stats);
    }

    /// <summary>
    /// キャッシュエントリが期限切れかどうかを確認します
    /// </summary>
    /// <param name="entry">キャッシュエントリ</param>
    /// <returns>期限切れの場合はtrue</returns>
    private static bool IsExpired(TranslationCacheEntry entry) => entry.ExpiresAt != null && entry.ExpiresAt < DateTime.UtcNow;

    /// <summary>
    /// 現在のキャッシュサイズを推定します
    /// </summary>
    /// <returns>推定バイト数</returns>
    private long EstimateCurrentSizeBytes()
    {
        // 簡易的な推定
        const int baseEntrySize = 200; // エントリごとの基本サイズ（推定）
        const int averageStringSize = 100; // 文字列サイズの平均（推定）

        long total = 0;
        foreach (var entry in _cache.Values)
        {
            // 基本サイズ + ソーステキストサイズ + 翻訳テキストサイズ
            total += baseEntrySize;
            total += entry.SourceText?.Length * 2 ?? 0; // UTF-16 文字 = 2 bytes
            total += entry.TranslatedText?.Length * 2 ?? 0;

            // メタデータ
            total += entry.Metadata.Count * averageStringSize;
        }

        return total;
    }
}
