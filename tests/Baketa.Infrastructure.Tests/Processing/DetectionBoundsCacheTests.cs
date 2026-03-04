using System.Drawing;
using Baketa.Core.Abstractions.Processing;
using Baketa.Infrastructure.Processing;
using Xunit;

namespace Baketa.Infrastructure.Tests.Processing;

/// <summary>
/// [Issue #500] DetectionBoundsCache のユニットテスト
/// </summary>
public class DetectionBoundsCacheTests
{
    private readonly DetectionBoundsCache _cache = new();

    [Fact]
    public void GetPreviousBounds_NoEntry_ReturnsNull()
    {
        var result = _cache.GetPreviousBounds("unknown-context");

        Assert.Null(result);
    }

    [Fact]
    public void UpdateBounds_ThenGet_ReturnsBounds()
    {
        var bounds = new[] { new Rectangle(10, 20, 100, 50) };

        _cache.UpdateBounds("ctx1", bounds);
        var result = _cache.GetPreviousBounds("ctx1");

        Assert.NotNull(result);
        Assert.Single(result);
        Assert.Equal(new Rectangle(10, 20, 100, 50), result[0]);
    }

    [Fact]
    public void UpdateBounds_OverwritesPreviousEntry()
    {
        var bounds1 = new[] { new Rectangle(0, 0, 10, 10) };
        var bounds2 = new[] { new Rectangle(50, 50, 200, 100), new Rectangle(300, 300, 150, 75) };

        _cache.UpdateBounds("ctx1", bounds1);
        _cache.UpdateBounds("ctx1", bounds2);
        var result = _cache.GetPreviousBounds("ctx1");

        Assert.NotNull(result);
        Assert.Equal(2, result.Length);
    }

    [Fact]
    public void ClearContext_RemovesSpecificContext()
    {
        _cache.UpdateBounds("ctx1", [new Rectangle(0, 0, 10, 10)]);
        _cache.UpdateBounds("ctx2", [new Rectangle(20, 20, 30, 30)]);

        _cache.ClearContext("ctx1");

        Assert.Null(_cache.GetPreviousBounds("ctx1"));
        Assert.NotNull(_cache.GetPreviousBounds("ctx2"));
    }

    [Fact]
    public void ClearAll_RemovesAllContexts()
    {
        _cache.UpdateBounds("ctx1", [new Rectangle(0, 0, 10, 10)]);
        _cache.UpdateBounds("ctx2", [new Rectangle(20, 20, 30, 30)]);
        _cache.UpdateBounds("ctx3", [new Rectangle(40, 40, 50, 50)]);

        _cache.ClearAll();

        Assert.Null(_cache.GetPreviousBounds("ctx1"));
        Assert.Null(_cache.GetPreviousBounds("ctx2"));
        Assert.Null(_cache.GetPreviousBounds("ctx3"));
    }

    // ========================================
    // [Issue #500] DetectionCacheEntry型テスト
    // ========================================

    [Fact]
    public void GetPreviousEntry_NoEntry_ReturnsNull()
    {
        var result = _cache.GetPreviousEntry("unknown-context");

        Assert.Null(result);
    }

    [Fact]
    public void UpdateEntry_ThenGetEntry_ReturnsEntryWithBoundsAndHashes()
    {
        var bounds = new[] { new Rectangle(10, 20, 100, 50) };
        var hashes = new[] { "abc123" };
        var entry = new DetectionCacheEntry(bounds, hashes);

        _cache.UpdateEntry("ctx1", entry);
        var result = _cache.GetPreviousEntry("ctx1");

        Assert.NotNull(result);
        Assert.Single(result.Bounds);
        Assert.Equal(new Rectangle(10, 20, 100, 50), result.Bounds[0]);
        Assert.Single(result.RegionHashes);
        Assert.Equal("abc123", result.RegionHashes[0]);
    }

    [Fact]
    public void UpdateEntry_OverwritesPreviousEntry()
    {
        var entry1 = new DetectionCacheEntry(
            [new Rectangle(0, 0, 10, 10)],
            ["hash1"]);
        var entry2 = new DetectionCacheEntry(
            [new Rectangle(50, 50, 200, 100), new Rectangle(300, 300, 150, 75)],
            ["hashA", "hashB"]);

        _cache.UpdateEntry("ctx1", entry1);
        _cache.UpdateEntry("ctx1", entry2);
        var result = _cache.GetPreviousEntry("ctx1");

        Assert.NotNull(result);
        Assert.Equal(2, result.Bounds.Length);
        Assert.Equal(2, result.RegionHashes.Length);
        Assert.Equal("hashA", result.RegionHashes[0]);
    }

    [Fact]
    public void UpdateBounds_BackwardCompat_CreatesEntryWithEmptyHashes()
    {
        var bounds = new[] { new Rectangle(10, 20, 100, 50) };

        _cache.UpdateBounds("ctx1", bounds);
        var entry = _cache.GetPreviousEntry("ctx1");

        Assert.NotNull(entry);
        Assert.Single(entry.Bounds);
        Assert.Empty(entry.RegionHashes);
    }

    [Fact]
    public void ClearContext_RemovesEntry()
    {
        _cache.UpdateEntry("ctx1", new DetectionCacheEntry(
            [new Rectangle(0, 0, 10, 10)], ["h1"]));

        _cache.ClearContext("ctx1");

        Assert.Null(_cache.GetPreviousEntry("ctx1"));
    }

    [Fact]
    public void UpdateEntry_NullEntry_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _cache.UpdateEntry("ctx1", null!));
    }
}
