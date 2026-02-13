using System;
using System.Collections.Concurrent;
using System.IO.Hashing;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Translation.Abstractions;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.Cache;

/// <summary>
/// [Issue #415] XxHash64ベースのCloud翻訳結果キャッシュ
/// Fork-Join段階で画像ハッシュを比較し、画面に変化がなければCloud APIコールをスキップ
/// </summary>
public sealed class CloudTranslationCache : ICloudTranslationCache
{
    private readonly ConcurrentDictionary<IntPtr, CacheEntry> _cache = new();
    private readonly ILogger<CloudTranslationCache> _logger;
    private const int CacheTtlSeconds = 30;
    private const int ChecksumSampleSize = 4096;

    public CloudTranslationCache(ILogger<CloudTranslationCache> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public long ComputeImageHash(ReadOnlyMemory<byte> imageData)
    {
        var imageSpan = imageData.Span;
        if (imageSpan.IsEmpty) return 0;

        var xxHash = new XxHash64();

        // 先頭サンプル
        var headLength = Math.Min(imageSpan.Length, ChecksumSampleSize);
        xxHash.Append(imageSpan[..headLength]);

        // 中央サンプル（重複を避けるため、十分な長さがある場合のみ）
        if (imageSpan.Length > ChecksumSampleSize * 3)
        {
            var midStart = imageSpan.Length / 2 - ChecksumSampleSize / 2;
            var midLength = Math.Min(ChecksumSampleSize, imageSpan.Length - midStart);
            xxHash.Append(imageSpan.Slice(midStart, midLength));
        }

        // 末尾サンプル（テキスト領域を含む可能性が高い）
        var tailStart = Math.Max(headLength, imageSpan.Length - ChecksumSampleSize);
        if (tailStart < imageSpan.Length)
        {
            xxHash.Append(imageSpan[tailStart..]);
        }

        return (long)xxHash.GetCurrentHashAsUInt64();
    }

    /// <inheritdoc />
    public bool TryGetCachedResult(IntPtr windowHandle, long imageHash,
        out FallbackTranslationResult? result)
    {
        if (_cache.TryGetValue(windowHandle, out var entry) &&
            entry.ImageHash == imageHash &&
            (DateTime.UtcNow - entry.CachedAt).TotalSeconds < CacheTtlSeconds)
        {
            result = entry.Result;
            _logger.LogDebug(
                "[Issue #415] キャッシュヒット: WindowHandle=0x{Handle:X}, Hash={Hash}",
                windowHandle.ToInt64(), imageHash);
            return true;
        }

        result = null;
        return false;
    }

    /// <inheritdoc />
    public void CacheResult(IntPtr windowHandle, long imageHash,
        FallbackTranslationResult result)
    {
        _cache[windowHandle] = new CacheEntry(imageHash, result, DateTime.UtcNow);
        _logger.LogDebug(
            "[Issue #415] キャッシュ更新: WindowHandle=0x{Handle:X}, Hash={Hash}",
            windowHandle.ToInt64(), imageHash);
    }

    /// <inheritdoc />
    public void ClearWindow(IntPtr windowHandle)
    {
        _cache.TryRemove(windowHandle, out _);
    }

    /// <inheritdoc />
    public void ClearAll()
    {
        var count = _cache.Count;
        _cache.Clear();
        if (count > 0)
        {
            _logger.LogDebug("[Issue #415] キャッシュ全クリア: {Count}件削除", count);
        }
    }

    private sealed record CacheEntry(long ImageHash, FallbackTranslationResult Result, DateTime CachedAt);
}
