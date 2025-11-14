using System.Drawing;
using Baketa.Core.Abstractions.OCR.TextDetection;
using Xunit;

namespace Baketa.Core.Tests.OCR.TextDetection;

public class TextRegionTests
{
    [Fact]
    public void Constructor_Default_InitializesProperties()
    {
        // Arrange & Act
        var region = new TextRegion();

        // Assert
        Assert.Equal(Rectangle.Empty, region.Bounds);
        Assert.Equal(0.0f, region.ConfidenceScore);
        Assert.Equal(TextRegionType.Unknown, region.RegionType);
        Assert.NotNull(region.Metadata);
        Assert.Null(region.ProcessedImage);
        Assert.Null(region.Contour);
        Assert.NotEqual(default, region.RegionId);
    }

    [Fact]
    public void Constructor_WithBoundsAndScore_InitializesProperties()
    {
        // Arrange
        var bounds = new Rectangle(10, 20, 100, 50);
        var score = 0.75f;

        // Act
        var region = new TextRegion(bounds, score);

        // Assert
        Assert.Equal(bounds, region.Bounds);
        Assert.Equal(score, region.ConfidenceScore);
        Assert.Equal(TextRegionType.Unknown, region.RegionType);
    }

    [Fact]
    public void Constructor_WithBoundsScoreAndType_InitializesProperties()
    {
        // Arrange
        var bounds = new Rectangle(10, 20, 100, 50);
        var score = 0.75f;
        var type = TextRegionType.Paragraph;

        // Act
        var region = new TextRegion(bounds, score, type);

        // Assert
        Assert.Equal(bounds, region.Bounds);
        Assert.Equal(score, region.ConfidenceScore);
        Assert.Equal(type, region.RegionType);
    }

    [Fact]
    public void CalculateOverlapRatio_NoOverlap_ReturnsZero()
    {
        // Arrange
        var region1 = new TextRegion(new Rectangle(0, 0, 50, 50), 0.8f);
        var region2 = new TextRegion(new Rectangle(100, 100, 50, 50), 0.7f);

        // Act
        var overlap = region1.CalculateOverlapRatio(region2);

        // Assert
        Assert.Equal(0.0f, overlap);
    }

    [Fact]
    public void CalculateOverlapRatio_WithOverlap_ReturnsCorrectRatio()
    {
        // Arrange
        var region1 = new TextRegion(new Rectangle(0, 0, 100, 100), 0.8f);
        var region2 = new TextRegion(new Rectangle(50, 50, 100, 100), 0.7f);

        // Expected:
        // Intersection: Rectangle(50, 50, 50, 50) = 2500 sq units
        // Union: 100*100 + 100*100 - 2500 = 10000 + 10000 - 2500 = 17500
        // IoU = 2500 / 17500 = 0.142857...

        // Act
        var overlap = region1.CalculateOverlapRatio(region2);

        // Assert
        Assert.InRange(overlap, 0.14f, 0.15f);
    }

    [Fact]
    public void Overlaps_WithHighOverlap_ReturnsTrue()
    {
        // Arrange
        var region1 = new TextRegion(new Rectangle(0, 0, 100, 100), 0.8f);
        var region2 = new TextRegion(new Rectangle(10, 10, 80, 80), 0.7f);

        // Act
        var overlaps = region1.Overlaps(region2, 0.3f);

        // Assert
        Assert.True(overlaps);
    }

    [Fact]
    public void Overlaps_WithLowOverlap_ReturnsFalse()
    {
        // Arrange
        var region1 = new TextRegion(new Rectangle(0, 0, 100, 100), 0.8f);
        var region2 = new TextRegion(new Rectangle(90, 90, 20, 20), 0.7f);

        // Act
        var overlaps = region1.Overlaps(region2, 0.3f);

        // Assert
        Assert.False(overlaps);
    }

    [Fact]
    public void ToString_ReturnsFormattedString()
    {
        // Arrange
        var region = new TextRegion(new Rectangle(10, 20, 100, 50), 0.75f, TextRegionType.Title);
        var idStart = region.RegionId.ToString()[..8];

        // Act
        var result = region.ToString();

        // Assert
        Assert.Contains(idStart, result);
        Assert.Contains("10, 20, 100, 50", result);
        Assert.Contains("Title", result);
        Assert.Contains("0.75", result);
    }
}
