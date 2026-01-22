using Baketa.Core.Models.Roi;
using Xunit;

namespace Baketa.Core.Tests.Roi;

/// <summary>
/// [Issue #293] RoiRegionの単体テスト
/// </summary>
public class RoiRegionTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithValidSettings_ShouldReturnTrue()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region-001",
            NormalizedBounds = new NormalizedRect(0.1f, 0.8f, 0.8f, 0.15f),
            ConfidenceScore = 0.85f,
            HeatmapValue = 0.7f,
            DetectionCount = 10
        };

        // Act
        var result = region.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithEmptyId_ShouldReturnFalse()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "",
            NormalizedBounds = new NormalizedRect(0.1f, 0.8f, 0.8f, 0.15f),
            ConfidenceScore = 0.85f
        };

        // Act
        var result = region.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidConfidenceScore_ShouldReturnFalse()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            ConfidenceScore = 1.5f // 無効
        };

        // Act
        var result = region.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithNegativeDetectionCount_ShouldReturnFalse()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            DetectionCount = -1 // 無効
        };

        // Act
        var result = region.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidCustomThreshold_ShouldReturnFalse()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            CustomThreshold = 1.5f // 無効
        };

        // Act
        var result = region.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ToAbsoluteRect テスト

    [Fact]
    public void ToAbsoluteRect_ShouldConvertCorrectly()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.2f, 0.3f, 0.4f)
        };

        // Act
        var rect = region.ToAbsoluteRect(1920, 1080);

        // Assert
        Assert.Equal(192, rect.X);
        Assert.Equal(216, rect.Y);
        Assert.Equal(576, rect.Width);
        Assert.Equal(432, rect.Height);
    }

    #endregion

    #region WithDetection テスト

    [Fact]
    public void WithDetection_ShouldIncrementCountAndUpdateTime()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            DetectionCount = 5
        };

        // Act
        var updated = region.WithDetection();

        // Assert
        Assert.Equal(6, updated.DetectionCount);
        Assert.True(updated.LastDetectedAt > DateTime.MinValue);
    }

    #endregion

    #region WithConfidence テスト

    [Fact]
    public void WithConfidence_ShouldUpdateConfidenceValues()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            ConfidenceScore = 0.5f,
            ConfidenceLevel = RoiConfidenceLevel.Low
        };

        // Act
        var updated = region.WithConfidence(0.9f, RoiConfidenceLevel.High);

        // Assert
        Assert.Equal(0.9f, updated.ConfidenceScore, precision: 4);
        Assert.Equal(RoiConfidenceLevel.High, updated.ConfidenceLevel);
    }

    [Fact]
    public void WithConfidence_ShouldClampOutOfRangeValues()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "test-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f)
        };

        // Act
        var updated = region.WithConfidence(1.5f, RoiConfidenceLevel.High);

        // Assert
        Assert.Equal(1.0f, updated.ConfidenceScore, precision: 4);
    }

    #endregion

    #region ApproximatelyEquals テスト

    [Theory]
    [InlineData(0.5f, 0.5f, true)]
    [InlineData(0.5f, 0.5000001f, true)]
    [InlineData(0.5f, 0.51f, false)]
    [InlineData(0.0f, 0.0000001f, true)]
    public void ApproximatelyEquals_ShouldCompareWithEpsilon(float a, float b, bool expected)
    {
        // Act
        var result = RoiRegion.ApproximatelyEquals(a, b);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion
}

/// <summary>
/// [Issue #293] NormalizedRectの単体テスト
/// </summary>
public class NormalizedRectTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithValidRect_ShouldReturnTrue()
    {
        // Arrange
        var rect = new NormalizedRect(0.1f, 0.2f, 0.3f, 0.4f);

        // Act
        var result = rect.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithFullScreenRect_ShouldReturnTrue()
    {
        // Arrange
        var rect = new NormalizedRect(0.0f, 0.0f, 1.0f, 1.0f);

        // Act
        var result = rect.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithOutOfBoundsX_ShouldReturnFalse()
    {
        // Arrange
        var rect = new NormalizedRect(1.1f, 0.2f, 0.3f, 0.4f);

        // Act
        var result = rect.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithNegativeWidth_ShouldReturnFalse()
    {
        // Arrange
        var rect = new NormalizedRect(0.1f, 0.2f, -0.1f, 0.4f);

        // Act
        var result = rect.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithOverflowingBounds_ShouldReturnFalse()
    {
        // Arrange
        var rect = new NormalizedRect(0.8f, 0.8f, 0.5f, 0.5f); // X+Width > 1.0

        // Act
        var result = rect.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region 座標変換テスト

    [Fact]
    public void ToAbsoluteRect_ShouldConvertCorrectly()
    {
        // Arrange
        var rect = new NormalizedRect(0.25f, 0.5f, 0.5f, 0.25f);

        // Act
        var absolute = rect.ToAbsoluteRect(1920, 1080);

        // Assert
        Assert.Equal(480, absolute.X);
        Assert.Equal(540, absolute.Y);
        Assert.Equal(960, absolute.Width);
        Assert.Equal(270, absolute.Height);
    }

    [Fact]
    public void FromAbsoluteRect_ShouldConvertCorrectly()
    {
        // Arrange
        var absoluteRect = new Baketa.Core.Models.Primitives.Rect(480, 540, 960, 270);

        // Act
        var normalized = NormalizedRect.FromAbsoluteRect(absoluteRect, 1920, 1080);

        // Assert
        Assert.Equal(0.25f, normalized.X, precision: 4);
        Assert.Equal(0.5f, normalized.Y, precision: 4);
        Assert.Equal(0.5f, normalized.Width, precision: 4);
        Assert.Equal(0.25f, normalized.Height, precision: 4);
    }

    [Fact]
    public void FromAbsoluteRect_WithZeroScreenSize_ShouldThrow()
    {
        // Arrange
        var absoluteRect = new Baketa.Core.Models.Primitives.Rect(100, 100, 200, 200);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            NormalizedRect.FromAbsoluteRect(absoluteRect, 0, 1080));
    }

    #endregion

    #region Intersects テスト

    [Fact]
    public void Intersects_WithOverlappingRects_ShouldReturnTrue()
    {
        // Arrange
        var rect1 = new NormalizedRect(0.1f, 0.1f, 0.3f, 0.3f);
        var rect2 = new NormalizedRect(0.2f, 0.2f, 0.3f, 0.3f);

        // Act
        var result = rect1.Intersects(rect2);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Intersects_WithNonOverlappingRects_ShouldReturnFalse()
    {
        // Arrange
        var rect1 = new NormalizedRect(0.1f, 0.1f, 0.1f, 0.1f);
        var rect2 = new NormalizedRect(0.5f, 0.5f, 0.1f, 0.1f);

        // Act
        var result = rect1.Intersects(rect2);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Contains テスト

    [Fact]
    public void Contains_WithContainedRect_ShouldReturnTrue()
    {
        // Arrange
        var outer = new NormalizedRect(0.1f, 0.1f, 0.5f, 0.5f);
        var inner = new NormalizedRect(0.2f, 0.2f, 0.2f, 0.2f);

        // Act
        var result = outer.Contains(inner);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Contains_WithPartiallyContainedRect_ShouldReturnFalse()
    {
        // Arrange
        var outer = new NormalizedRect(0.1f, 0.1f, 0.3f, 0.3f);
        var partial = new NormalizedRect(0.2f, 0.2f, 0.3f, 0.3f);

        // Act
        var result = outer.Contains(partial);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region CalculateIoU テスト

    [Fact]
    public void CalculateIoU_WithIdenticalRects_ShouldReturnOne()
    {
        // Arrange
        var rect = new NormalizedRect(0.2f, 0.2f, 0.3f, 0.3f);

        // Act
        var iou = rect.CalculateIoU(rect);

        // Assert
        Assert.Equal(1.0f, iou, precision: 4);
    }

    [Fact]
    public void CalculateIoU_WithNonOverlappingRects_ShouldReturnZero()
    {
        // Arrange
        var rect1 = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        var rect2 = new NormalizedRect(0.5f, 0.5f, 0.1f, 0.1f);

        // Act
        var iou = rect1.CalculateIoU(rect2);

        // Assert
        Assert.Equal(0.0f, iou, precision: 4);
    }

    [Fact]
    public void CalculateIoU_WithPartialOverlap_ShouldReturnCorrectValue()
    {
        // Arrange
        // rect1: 0.0-0.2 x 0.0-0.2 = area 0.04
        // rect2: 0.1-0.3 x 0.1-0.3 = area 0.04
        // intersection: 0.1-0.2 x 0.1-0.2 = area 0.01
        // union: 0.04 + 0.04 - 0.01 = 0.07
        // IoU: 0.01 / 0.07 ≈ 0.1429
        var rect1 = new NormalizedRect(0.0f, 0.0f, 0.2f, 0.2f);
        var rect2 = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        var iou = rect1.CalculateIoU(rect2);

        // Assert
        Assert.True(iou > 0.14f && iou < 0.15f);
    }

    #endregion

    #region プロパティテスト

    [Fact]
    public void Properties_ShouldCalculateCorrectly()
    {
        // Arrange
        var rect = new NormalizedRect(0.1f, 0.2f, 0.3f, 0.4f);

        // Act & Assert
        Assert.Equal(0.4f, rect.Right, precision: 4);
        Assert.Equal(0.6f, rect.Bottom, precision: 4);
        Assert.Equal(0.25f, rect.CenterX, precision: 4);
        Assert.Equal(0.4f, rect.CenterY, precision: 4);
        Assert.Equal(0.12f, rect.Area, precision: 4);
        Assert.False(rect.IsEmpty);
    }

    [Fact]
    public void IsEmpty_WithZeroWidth_ShouldReturnTrue()
    {
        // Arrange
        var rect = new NormalizedRect(0.1f, 0.2f, 0.0f, 0.4f);

        // Act
        var result = rect.IsEmpty;

        // Assert
        Assert.True(result);
    }

    #endregion
}
