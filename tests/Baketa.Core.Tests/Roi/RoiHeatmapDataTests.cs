using Baketa.Core.Models.Roi;
using Xunit;

namespace Baketa.Core.Tests.Roi;

/// <summary>
/// [Issue #293] RoiHeatmapDataの単体テスト
/// </summary>
public class RoiHeatmapDataTests
{
    #region Create テスト

    [Fact]
    public void Create_ShouldInitializeWithCorrectSize()
    {
        // Act
        var heatmap = RoiHeatmapData.Create(8, 16);

        // Assert
        Assert.Equal(8, heatmap.Rows);
        Assert.Equal(16, heatmap.Columns);
        Assert.Equal(128, heatmap.Values.Length);
        Assert.Equal(128, heatmap.SampleCounts.Length);
    }

    [Fact]
    public void Create_WithInitialValue_ShouldFillValues()
    {
        // Act
        var heatmap = RoiHeatmapData.Create(4, 4, 0.5f);

        // Assert
        Assert.All(heatmap.Values, v => Assert.Equal(0.5f, v, precision: 4));
    }

    [Fact]
    public void Create_WithInvalidRows_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => RoiHeatmapData.Create(0, 4));
    }

    [Fact]
    public void Create_WithInvalidColumns_ShouldThrow()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => RoiHeatmapData.Create(4, 0));
    }

    #endregion

    #region IsValid テスト

    [Fact]
    public void IsValid_WithValidHeatmap_ShouldReturnTrue()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var result = heatmap.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithInvalidValuesLength_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[10], // 16ではなく10
            SampleCounts = new int[16]
        };

        // Act
        var result = heatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithOutOfRangeValue_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var values = (float[])heatmap.Values.Clone();
        values[0] = 1.5f; // 無効
        var invalidHeatmap = heatmap with { Values = values };

        // Act
        var result = invalidHeatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetValue/GetSampleCount テスト

    [Fact]
    public void GetValue_WithValidIndex_ShouldReturnValue()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        heatmap = heatmap.WithUpdatedCell(1, 2, detected: true, learningRate: 1.0f);

        // Act
        var value = heatmap.GetValue(1, 2);

        // Assert
        Assert.Equal(1.0f, value, precision: 4);
    }

    [Fact]
    public void GetValue_WithInvalidIndex_ShouldReturnZero()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var value = heatmap.GetValue(10, 10); // 範囲外

        // Assert
        Assert.Equal(0.0f, value, precision: 4);
    }

    [Fact]
    public void GetSampleCount_ShouldReturnCorrectCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        heatmap = heatmap.WithUpdatedCell(1, 1, detected: true);
        heatmap = heatmap.WithUpdatedCell(1, 1, detected: true);
        heatmap = heatmap.WithUpdatedCell(1, 1, detected: false);

        // Act
        var count = heatmap.GetSampleCount(1, 1);

        // Assert
        Assert.Equal(3, count);
    }

    #endregion

    #region GetCellIndex テスト

    [Theory]
    [InlineData(0.0f, 0.0f, 0, 0)]
    [InlineData(0.99f, 0.99f, 3, 3)]
    [InlineData(0.5f, 0.5f, 2, 2)]
    [InlineData(0.25f, 0.75f, 3, 1)]
    public void GetCellIndex_ShouldReturnCorrectIndex(
        float normalizedX, float normalizedY, int expectedRow, int expectedColumn)
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var (row, column) = heatmap.GetCellIndex(normalizedX, normalizedY);

        // Assert
        Assert.Equal(expectedRow, row);
        Assert.Equal(expectedColumn, column);
    }

    #endregion

    #region GetValueAt テスト

    [Fact]
    public void GetValueAt_ShouldReturnCorrectValue()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        heatmap = heatmap.WithUpdatedCell(2, 2, detected: true, learningRate: 1.0f);

        // Act
        var value = heatmap.GetValueAt(0.5f, 0.5f); // (2, 2) セル

        // Assert
        Assert.Equal(1.0f, value, precision: 4);
    }

    #endregion

    #region GetAverageValueForRegion テスト

    [Fact]
    public void GetAverageValueForRegion_ShouldReturnCorrectAverage()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        // セル (0,0), (0,1), (1,0), (1,1) を更新（全てdetected=true）
        heatmap = heatmap.WithUpdatedCell(0, 0, detected: true, learningRate: 1.0f);
        heatmap = heatmap.WithUpdatedCell(0, 1, detected: true, learningRate: 1.0f);
        heatmap = heatmap.WithUpdatedCell(1, 0, detected: true, learningRate: 1.0f);
        heatmap = heatmap.WithUpdatedCell(1, 1, detected: true, learningRate: 1.0f);

        // Act
        // 正規化座標 (0.0, 0.0) ~ (0.49, 0.49) はセル (0,0) ~ (1,1) にマップされる
        var average = heatmap.GetAverageValueForRegion(
            new NormalizedRect(0.0f, 0.0f, 0.49f, 0.49f));

        // Assert
        // セル (0,0) ~ (1,1) の 4 セルが全て 1.0 なので平均は 1.0
        Assert.Equal(1.0f, average, precision: 4);
    }

    #endregion

    #region GetHighValueCells テスト

    [Fact]
    public void GetHighValueCells_ShouldReturnCellsAboveThreshold()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        heatmap = heatmap.WithUpdatedCell(0, 0, detected: true, learningRate: 1.0f);
        heatmap = heatmap.WithUpdatedCell(1, 1, detected: true, learningRate: 0.7f);
        heatmap = heatmap.WithUpdatedCell(2, 2, detected: true, learningRate: 0.3f);

        // Act
        var highValueCells = heatmap.GetHighValueCells(threshold: 0.5f);

        // Assert
        Assert.Equal(2, highValueCells.Length);
        Assert.Contains(highValueCells, c => c.row == 0 && c.column == 0);
        Assert.Contains(highValueCells, c => c.row == 1 && c.column == 1);
    }

    #endregion

    #region WithUpdatedCell テスト

    [Fact]
    public void WithUpdatedCell_ShouldApplyExponentialMovingAverage()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 0.5f);

        // Act
        var updated = heatmap.WithUpdatedCell(1, 1, detected: true, learningRate: 0.2f);
        // 期待値: 0.5 + 0.2 * (1.0 - 0.5) = 0.5 + 0.1 = 0.6

        // Assert
        Assert.Equal(0.6f, updated.GetValue(1, 1), precision: 4);
    }

    [Fact]
    public void WithUpdatedCell_WithDetectionFalse_ShouldDecreaseValue()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 0.5f);

        // Act
        var updated = heatmap.WithUpdatedCell(1, 1, detected: false, learningRate: 0.2f);
        // 期待値: 0.5 + 0.2 * (0.0 - 0.5) = 0.5 - 0.1 = 0.4

        // Assert
        Assert.Equal(0.4f, updated.GetValue(1, 1), precision: 4);
    }

    [Fact]
    public void WithUpdatedCell_ShouldIncrementTotalSamples()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        Assert.Equal(0, heatmap.TotalSamples);

        // Act
        var updated = heatmap.WithUpdatedCell(0, 0, detected: true);

        // Assert
        Assert.Equal(1, updated.TotalSamples);
    }

    [Fact]
    public void WithUpdatedCell_WithInvalidIndex_ShouldReturnSameHeatmap()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var result = heatmap.WithUpdatedCell(10, 10, detected: true);

        // Assert
        Assert.Same(heatmap, result);
    }

    #endregion

    #region WithUpdatedCells テスト

    [Fact]
    public void WithUpdatedCells_ShouldUpdateAllCells()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var detectedCells = new[] { (0, 0), (1, 1), (2, 2) };

        // Act
        var updated = heatmap.WithUpdatedCells(detectedCells, learningRate: 1.0f);

        // Assert
        Assert.Equal(1.0f, updated.GetValue(0, 0), precision: 4);
        Assert.Equal(1.0f, updated.GetValue(1, 1), precision: 4);
        Assert.Equal(1.0f, updated.GetValue(2, 2), precision: 4);
        Assert.Equal(0.0f, updated.GetValue(3, 3), precision: 4); // 検出なしセル
    }

    #endregion

    #region WithDecay テスト

    [Fact]
    public void WithDecay_ShouldReduceAllValues()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 1.0f);

        // Act
        var decayed = heatmap.WithDecay(decayRate: 0.1f);
        // 期待値: 1.0 * (1.0 - 0.1) = 0.9

        // Assert
        Assert.All(decayed.Values, v => Assert.Equal(0.9f, v, precision: 4));
    }

    [Fact]
    public void WithDecay_ShouldUpdateTimestamp()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var originalTime = heatmap.LastUpdatedAt;

        // Act
        System.Threading.Thread.Sleep(10);
        var decayed = heatmap.WithDecay();

        // Assert
        Assert.True(decayed.LastUpdatedAt > originalTime);
    }

    #endregion
}
