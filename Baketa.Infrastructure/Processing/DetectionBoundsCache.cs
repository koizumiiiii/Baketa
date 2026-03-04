using System.Collections.Concurrent;
using System.Drawing;
using Baketa.Core.Abstractions.Processing;

namespace Baketa.Infrastructure.Processing;

/// <summary>
/// [Issue #500] Detection-Onlyフィルタ用バウンディングボックスキャッシュ実装
/// Singleton登録でサイクル間のDetection矩形+pHashを保持
/// </summary>
public sealed class DetectionBoundsCache : IDetectionBoundsCache
{
    private readonly ConcurrentDictionary<string, DetectionCacheEntry> _cache = new();

    public DetectionCacheEntry? GetPreviousEntry(string contextId)
        => _cache.TryGetValue(contextId, out var entry) ? entry : null;

    public void UpdateEntry(string contextId, DetectionCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _cache[contextId] = entry;
    }

    public Rectangle[]? GetPreviousBounds(string contextId)
        => GetPreviousEntry(contextId)?.Bounds;

    public void UpdateBounds(string contextId, Rectangle[] bounds)
        => UpdateEntry(contextId, new DetectionCacheEntry(bounds, []));

    public void ClearContext(string contextId)
        => _cache.TryRemove(contextId, out _);

    public void ClearAll()
        => _cache.Clear();
}
