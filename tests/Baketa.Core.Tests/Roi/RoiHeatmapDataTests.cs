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

    #region [Issue #354] WithUpdatedCellsWeighted テスト

    [Fact]
    public void WithUpdatedCellsWeighted_ShouldApplyWeightToLearningRate()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var detectedCells = new[] { (row: 1, column: 1, weight: 2), (row: 2, column: 2, weight: 1) };

        // Act
        var updated = heatmap.WithUpdatedCellsWeighted(detectedCells, learningRate: 0.1f);
        // weight=2: 0.0 + 0.2 * (1.0 - 0.0) = 0.2
        // weight=1: 0.0 + 0.1 * (1.0 - 0.0) = 0.1

        // Assert
        Assert.Equal(0.2f, updated.GetValue(1, 1), precision: 4);
        Assert.Equal(0.1f, updated.GetValue(2, 2), precision: 4);
    }

    [Fact]
    public void WithUpdatedCellsWeighted_WithSameCellMultipleTimes_ShouldUseMaxWeight()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var detectedCells = new[]
        {
            (row: 1, column: 1, weight: 1),
            (row: 1, column: 1, weight: 3), // 同じセルに高い重み
            (row: 1, column: 1, weight: 2)
        };

        // Act
        var updated = heatmap.WithUpdatedCellsWeighted(detectedCells, learningRate: 0.1f);
        // 最大weight=3が適用: 0.0 + 0.3 * (1.0 - 0.0) = 0.3

        // Assert
        Assert.Equal(0.3f, updated.GetValue(1, 1), precision: 4);
    }

    [Fact]
    public void WithUpdatedCellsWeighted_NonDetectedCells_ShouldDecay()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 0.5f);
        var detectedCells = new[] { (row: 0, column: 0, weight: 1) };

        // Act
        var updated = heatmap.WithUpdatedCellsWeighted(detectedCells, learningRate: 0.1f);
        // 検出されなかったセル: 0.5 + 0.1 * (0.0 - 0.5) = 0.45

        // Assert
        Assert.Equal(0.45f, updated.GetValue(1, 1), precision: 4);
    }

    #endregion

    #region [Issue #354] WithRecordedMiss テスト

    [Fact]
    public void WithRecordedMiss_ShouldIncrementMissCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var missCells = new[] { (row: 1, column: 1) };

        // Act
        var updated = heatmap.WithRecordedMiss(missCells, resetThreshold: 3);

        // Assert
        Assert.Equal(1, updated.GetMissCount(1, 1));
    }

    [Fact]
    public void WithRecordedMiss_WhenThresholdReached_ShouldResetScore()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 0.8f);
        var missCells = new[] { (row: 1, column: 1) };

        // Act - 3回miss
        var updated = heatmap.WithRecordedMiss(missCells, resetThreshold: 3);
        updated = updated.WithRecordedMiss(missCells, resetThreshold: 3);
        updated = updated.WithRecordedMiss(missCells, resetThreshold: 3); // 閾値到達

        // Assert
        Assert.Equal(3, updated.GetMissCount(1, 1));
        Assert.Equal(0.0f, updated.GetValue(1, 1), precision: 4); // スコアがリセットされる
    }

    [Fact]
    public void WithRecordedMiss_BeforeThreshold_ShouldNotResetScore()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4, initialValue: 0.8f);
        var missCells = new[] { (row: 1, column: 1) };

        // Act - 2回miss（閾値3未満）
        var updated = heatmap.WithRecordedMiss(missCells, resetThreshold: 3);
        updated = updated.WithRecordedMiss(missCells, resetThreshold: 3);

        // Assert
        Assert.Equal(2, updated.GetMissCount(1, 1));
        Assert.Equal(0.8f, updated.GetValue(1, 1), precision: 4); // スコアは維持される
    }

    #endregion

    #region [Issue #354] WithResetMissCount テスト

    [Fact]
    public void WithResetMissCount_ShouldResetMissCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var missCells = new[] { (row: 1, column: 1), (row: 1, column: 1), (row: 1, column: 1) };
        foreach (var cell in missCells)
        {
            heatmap = heatmap.WithRecordedMiss(new[] { cell }, resetThreshold: 10);
        }
        Assert.Equal(3, heatmap.GetMissCount(1, 1));

        // Act
        var detectedCells = new[] { (row: 1, column: 1) };
        var updated = heatmap.WithResetMissCount(detectedCells);

        // Assert
        Assert.Equal(0, updated.GetMissCount(1, 1)); // リセットされる
    }

    #endregion

    #region [Issue #354] GetHighMissCells テスト

    [Fact]
    public void GetHighMissCells_ShouldReturnCellsAboveThreshold()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        // セル(1,1)に5回miss
        for (int i = 0; i < 5; i++)
        {
            heatmap = heatmap.WithRecordedMiss(new[] { (row: 1, column: 1) }, resetThreshold: 100);
        }
        // セル(2,2)に3回miss
        for (int i = 0; i < 3; i++)
        {
            heatmap = heatmap.WithRecordedMiss(new[] { (row: 2, column: 2) }, resetThreshold: 100);
        }

        // Act
        var highMissCells = heatmap.GetHighMissCells(threshold: 4);

        // Assert
        Assert.Single(highMissCells);
        Assert.Contains(highMissCells, c => c.row == 1 && c.column == 1 && c.missCount == 5);
    }

    [Fact]
    public void GetHighMissCells_WithNullMissCounts_ShouldReturnEmpty()
    {
        // Arrange - MissCountsがnullの古いフォーマット
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            MissCounts = null
        };

        // Act
        var highMissCells = heatmap.GetHighMissCells(threshold: 1);

        // Assert
        Assert.Empty(highMissCells);
    }

    #endregion

    #region [Issue #354] GetMissCount テスト

    [Fact]
    public void GetMissCount_WithValidIndex_ShouldReturnMissCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        heatmap = heatmap.WithRecordedMiss(new[] { (row: 2, column: 3) }, resetThreshold: 10);

        // Act
        var missCount = heatmap.GetMissCount(2, 3);

        // Assert
        Assert.Equal(1, missCount);
    }

    [Fact]
    public void GetMissCount_WithNullMissCounts_ShouldReturnZero()
    {
        // Arrange
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            MissCounts = null
        };

        // Act
        var missCount = heatmap.GetMissCount(1, 1);

        // Assert
        Assert.Equal(0, missCount);
    }

    [Fact]
    public void GetMissCount_WithInvalidIndex_ShouldReturnZero()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var missCount = heatmap.GetMissCount(10, 10); // 範囲外

        // Assert
        Assert.Equal(0, missCount);
    }

    #endregion

    #region [Issue #354] MissCounts検証 テスト

    [Fact]
    public void IsValid_WithInvalidMissCountsLength_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            MissCounts = new int[10] // 16ではなく10
        };

        // Act
        var result = heatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithNegativeMissCount_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var missCounts = (int[])heatmap.MissCounts!.Clone();
        missCounts[0] = -1;
        var invalidHeatmap = heatmap with { MissCounts = missCounts };

        // Act
        var result = invalidHeatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region [Issue #379] WithRecordedHit テスト

    [Fact]
    public void WithRecordedHit_ShouldIncrementHitCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var hitCells = new[] { (row: 1, column: 1) };

        // Act
        var updated = heatmap.WithRecordedHit(hitCells);

        // Assert
        Assert.NotNull(updated.HitCounts);
        Assert.Equal(1, updated.HitCounts![1 * 4 + 1]);
    }

    [Fact]
    public void WithRecordedHit_ShouldResetMissCount()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        // まずmissを記録
        heatmap = heatmap.WithRecordedMiss(new[] { (row: 1, column: 1) }, resetThreshold: 10);
        heatmap = heatmap.WithRecordedMiss(new[] { (row: 1, column: 1) }, resetThreshold: 10);
        Assert.Equal(2, heatmap.GetMissCount(1, 1));

        // Act - hitを記録
        var updated = heatmap.WithRecordedHit(new[] { (row: 1, column: 1) });

        // Assert - missカウントがリセットされる
        Assert.Equal(0, updated.GetMissCount(1, 1));
        Assert.Equal(1, updated.HitCounts![1 * 4 + 1]);
    }

    [Fact]
    public void WithRecordedHit_MultipleHits_ShouldAccumulate()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act - 3回ヒット
        heatmap = heatmap.WithRecordedHit(new[] { (row: 2, column: 3) });
        heatmap = heatmap.WithRecordedHit(new[] { (row: 2, column: 3) });
        heatmap = heatmap.WithRecordedHit(new[] { (row: 2, column: 3) });

        // Assert
        Assert.Equal(3, heatmap.HitCounts![2 * 4 + 3]);
    }

    [Fact]
    public void WithRecordedHit_WithInvalidIndex_ShouldIgnore()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Act
        var updated = heatmap.WithRecordedHit(new[] { (row: 10, column: 10) });

        // Assert - 例外なく処理される
        Assert.NotNull(updated.HitCounts);
    }

    [Fact]
    public void WithRecordedHit_WithNullHitCounts_ShouldInitialize()
    {
        // Arrange - 古いフォーマット（HitCountsがnull）
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            MissCounts = new int[16],
            HitCounts = null
        };

        // Act
        var updated = heatmap.WithRecordedHit(new[] { (row: 0, column: 0) });

        // Assert
        Assert.NotNull(updated.HitCounts);
        Assert.Equal(16, updated.HitCounts!.Length);
        Assert.Equal(1, updated.HitCounts[0]);
    }

    #endregion

    #region [Issue #379] GetHighMissRatioCells テスト

    [Fact]
    public void GetHighMissRatioCells_ShouldReturnCellsAboveRatioThreshold()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);

        // セル(1,1): hit=2, miss=8 → ratio=8/10=0.8
        // ※ WithRecordedHitはmissCountをリセットするため、hitを先に記録
        for (int i = 0; i < 2; i++)
            heatmap = heatmap.WithRecordedHit(new[] { (row: 1, column: 1) });
        for (int i = 0; i < 8; i++)
            heatmap = heatmap.WithRecordedMiss(new[] { (row: 1, column: 1) }, resetThreshold: 100);

        // セル(2,2): hit=7, miss=3 → ratio=3/10=0.3
        for (int i = 0; i < 7; i++)
            heatmap = heatmap.WithRecordedHit(new[] { (row: 2, column: 2) });
        for (int i = 0; i < 3; i++)
            heatmap = heatmap.WithRecordedMiss(new[] { (row: 2, column: 2) }, resetThreshold: 100);

        // Act - ratio >= 0.7, minSamples >= 5
        var highMissRatioCells = heatmap.GetHighMissRatioCells(0.7f, 5);

        // Assert
        Assert.Single(highMissRatioCells);
        Assert.Contains(highMissRatioCells, c => c.row == 1 && c.column == 1);
        Assert.True(highMissRatioCells[0].missRatio >= 0.7f);
    }

    [Fact]
    public void GetHighMissRatioCells_BelowMinSamples_ShouldReturnEmpty()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        // miss=2, hit=1 → total=3 < minSamples=10
        heatmap = heatmap.WithRecordedMiss(new[] { (row: 0, column: 0) }, resetThreshold: 100);
        heatmap = heatmap.WithRecordedMiss(new[] { (row: 0, column: 0) }, resetThreshold: 100);
        heatmap = heatmap.WithRecordedHit(new[] { (row: 0, column: 0) });

        // Act
        var result = heatmap.GetHighMissRatioCells(0.5f, 10);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetHighMissRatioCells_WithNullMissCounts_ShouldReturnEmpty()
    {
        // Arrange
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            MissCounts = null,
            HitCounts = new int[16]
        };

        // Act
        var result = heatmap.GetHighMissRatioCells(0.5f, 1);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void GetHighMissRatioCells_WithNullHitCounts_ShouldTreatHitsAsZero()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4) with { HitCounts = null };
        // miss=10, hit=0(null) → ratio=1.0
        for (int i = 0; i < 10; i++)
            heatmap = heatmap.WithRecordedMiss(new[] { (row: 0, column: 0) }, resetThreshold: 100);

        // WithRecordedMissがHitCountsを保持しない場合があるのでnullに再設定
        heatmap = heatmap with { HitCounts = null };

        // Act
        var result = heatmap.GetHighMissRatioCells(0.8f, 5);

        // Assert
        Assert.Single(result);
        Assert.Equal(1.0f, result[0].missRatio, precision: 4);
    }

    #endregion

    #region [Issue #379] HitCounts検証 テスト

    [Fact]
    public void IsValid_WithInvalidHitCountsLength_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            HitCounts = new int[10] // 16ではなく10
        };

        // Act
        var result = heatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithNegativeHitCount_ShouldReturnFalse()
    {
        // Arrange
        var heatmap = RoiHeatmapData.Create(4, 4);
        var hitCounts = (int[])heatmap.HitCounts!.Clone();
        hitCounts[0] = -1;
        var invalidHeatmap = heatmap with { HitCounts = hitCounts };

        // Act
        var result = invalidHeatmap.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithNullHitCounts_ShouldReturnTrue()
    {
        // Arrange - 古いフォーマット（HitCountsがnull）
        var heatmap = new RoiHeatmapData
        {
            Rows = 4,
            Columns = 4,
            Values = new float[16],
            SampleCounts = new int[16],
            HitCounts = null
        };

        // Act
        var result = heatmap.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void Create_ShouldInitializeHitCounts()
    {
        // Act
        var heatmap = RoiHeatmapData.Create(4, 4);

        // Assert
        Assert.NotNull(heatmap.HitCounts);
        Assert.Equal(16, heatmap.HitCounts!.Length);
        Assert.All(heatmap.HitCounts, c => Assert.Equal(0, c));
    }

    #endregion
}
