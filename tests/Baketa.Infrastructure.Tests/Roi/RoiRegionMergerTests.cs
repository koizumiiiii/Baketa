using System.Drawing;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi;

/// <summary>
/// RoiRegionMergerのユニットテスト
/// </summary>
public class RoiRegionMergerTests
{
    private readonly Mock<ILogger<RoiRegionMerger>> _loggerMock;
    private readonly RoiRegionMerger _sut;

    public RoiRegionMergerTests()
    {
        _loggerMock = new Mock<ILogger<RoiRegionMerger>>();
        _sut = new RoiRegionMerger(_loggerMock.Object);
    }

    #region MergeAdjacentRegions - 基本テスト

    [Fact]
    public void MergeAdjacentRegions_WithNullInput_ReturnsEmptyList()
    {
        // Act
        var result = _sut.MergeAdjacentRegions(null!);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeAdjacentRegions_WithEmptyInput_ReturnsEmptyList()
    {
        // Arrange
        var regions = Array.Empty<Rectangle>();

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void MergeAdjacentRegions_WithSingleRegion_ReturnsSameRegion()
    {
        // Arrange
        var regions = new[] { new Rectangle(10, 10, 100, 50) };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(regions[0]);
    }

    #endregion

    #region MergeAdjacentRegions - 隣接判定テスト

    [Fact]
    public void MergeAdjacentRegions_WithOverlappingRegions_MergesIntoOne()
    {
        // Arrange
        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(50, 50, 100, 100)  // 重複あり
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(new Rectangle(0, 0, 150, 150)); // バウンディングボックス
    }

    [Fact]
    public void MergeAdjacentRegions_WithAdjacentRegions_MergesIntoOne()
    {
        // Arrange (マージン5ピクセル以内で隣接)
        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(103, 0, 100, 100)  // 3ピクセル離れている（マージン内）
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(new Rectangle(0, 0, 203, 100));
    }

    [Fact]
    public void MergeAdjacentRegions_WithSeparateRegions_ReturnsMultipleRegions()
    {
        // Arrange (マージン5ピクセル以上離れている)
        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(200, 0, 100, 100)  // 100ピクセル離れている
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void MergeAdjacentRegions_WithChainedRegions_MergesAllIntoOne()
    {
        // Arrange (A-B-Cが連鎖的に隣接)
        var regions = new[]
        {
            new Rectangle(0, 0, 50, 50),     // A
            new Rectangle(52, 0, 50, 50),    // B (Aと隣接)
            new Rectangle(104, 0, 50, 50)    // C (Bと隣接)
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(new Rectangle(0, 0, 154, 50));
    }

    #endregion

    #region MergeAdjacentRegions - 複雑なシナリオ

    [Fact]
    public void MergeAdjacentRegions_WithMultipleGroups_ReturnsSeparateGroups()
    {
        // Arrange (2つの独立したグループ)
        var regions = new[]
        {
            // グループ1
            new Rectangle(0, 0, 50, 50),
            new Rectangle(52, 0, 50, 50),
            // グループ2
            new Rectangle(0, 200, 50, 50),
            new Rectangle(52, 200, 50, 50)
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public void MergeAdjacentRegions_WithVerticallyAdjacentRegions_MergesIntoOne()
    {
        // Arrange (垂直方向に隣接)
        var regions = new[]
        {
            new Rectangle(0, 0, 100, 50),
            new Rectangle(0, 53, 100, 50)  // 3ピクセル離れている（マージン内）
        };

        // Act
        var result = _sut.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1);
        result[0].Should().Be(new Rectangle(0, 0, 100, 103));
    }

    #endregion

    #region 設定ファイルからのAdjacencyMargin

    [Fact]
    public void MergeAdjacentRegions_WithCustomAdjacencyMargin_UsesConfiguredValue()
    {
        // Arrange
        var settings = new RoiManagerSettings { AdjacencyMargin = 20 };
        var optionsMock = new Mock<IOptions<RoiManagerSettings>>();
        optionsMock.Setup(x => x.Value).Returns(settings);

        var sutWithCustomMargin = new RoiRegionMerger(_loggerMock.Object, optionsMock.Object);

        // 通常のマージン(5)では隣接しないが、カスタムマージン(20)では隣接する
        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(115, 0, 100, 100)  // 15ピクセル離れている
        };

        // Act
        var result = sutWithCustomMargin.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1); // カスタムマージン20で結合される
    }

    [Fact]
    public void MergeAdjacentRegions_WithZeroAdjacencyMargin_DoesNotMergeTouchingRectangles()
    {
        // Arrange
        // AdjacencyMargin=0の場合、接触（タッチ）しているだけの矩形は結合されない
        // （IntersectsWith()は境界が接触するだけの矩形にはfalseを返す）
        var settings = new RoiManagerSettings { AdjacencyMargin = 0 };
        var optionsMock = new Mock<IOptions<RoiManagerSettings>>();
        optionsMock.Setup(x => x.Value).Returns(settings);

        var sutWithZeroMargin = new RoiRegionMerger(_loggerMock.Object, optionsMock.Object);

        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(100, 0, 100, 100)  // ぴったり隣接（接触）
        };

        // Act
        var result = sutWithZeroMargin.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(2); // 接触しているだけでは結合されない
    }

    [Fact]
    public void MergeAdjacentRegions_WithZeroAdjacencyMargin_MergesOverlappingRectangles()
    {
        // Arrange
        // AdjacencyMargin=0でも、重複している矩形は結合される
        var settings = new RoiManagerSettings { AdjacencyMargin = 0 };
        var optionsMock = new Mock<IOptions<RoiManagerSettings>>();
        optionsMock.Setup(x => x.Value).Returns(settings);

        var sutWithZeroMargin = new RoiRegionMerger(_loggerMock.Object, optionsMock.Object);

        var regions = new[]
        {
            new Rectangle(0, 0, 100, 100),
            new Rectangle(50, 0, 100, 100)  // 50ピクセル重複
        };

        // Act
        var result = sutWithZeroMargin.MergeAdjacentRegions(regions);

        // Assert
        result.Should().HaveCount(1); // 重複しているので結合
        result[0].Should().Be(new Rectangle(0, 0, 150, 100));
    }

    #endregion

    #region パフォーマンス関連

    [Fact]
    public void MergeAdjacentRegions_WithManyRegions_CompletesInReasonableTime()
    {
        // Arrange (多数の領域でもO(n²)で完了することを確認)
        var regions = Enumerable.Range(0, 100)
            .Select(i => new Rectangle(i * 200, 0, 50, 50)) // 離れた領域
            .ToArray();

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = _sut.MergeAdjacentRegions(regions);
        stopwatch.Stop();

        // Assert
        result.Should().HaveCount(100); // すべて独立
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000); // 1秒以内
    }

    #endregion
}
