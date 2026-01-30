using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi;

/// <summary>
/// [Issue #293] RoiLearningEngineの単体テスト
/// </summary>
public class RoiLearningEngineTests
{
    private readonly Mock<ILogger<RoiLearningEngine>> _loggerMock;
    private readonly IOptions<RoiManagerSettings> _defaultSettings;

    public RoiLearningEngineTests()
    {
        _loggerMock = new Mock<ILogger<RoiLearningEngine>>();
        _defaultSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            HeatmapRows = 8,
            HeatmapColumns = 8,
            LearningRate = 0.2f,
            MinConfidenceForRegion = 0.3f,
            HighConfidenceThreshold = 0.7f
        });
    }

    #region 初期化テスト

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);

        // Assert
        Assert.NotNull(engine.CurrentHeatmap);
        Assert.Equal(8, engine.CurrentHeatmap!.Rows);
        Assert.Equal(8, engine.CurrentHeatmap.Columns);
        Assert.True(engine.IsLearningEnabled);
    }

    [Fact]
    public void CurrentHeatmap_ShouldBeValid()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);

        // Act
        var heatmap = engine.CurrentHeatmap;

        // Assert
        Assert.NotNull(heatmap);
        Assert.True(heatmap!.IsValid());
    }

    #endregion

    #region RecordDetection テスト

    [Fact]
    public void RecordDetection_WithValidBounds_ShouldUpdateHeatmap()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        engine.RecordDetection(bounds, confidence: 0.9f);

        // Assert
        var value = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.True(value > 0.0f);
    }

    [Fact]
    public void RecordDetection_WhenLearningDisabled_ShouldNotUpdateHeatmap()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        engine.IsLearningEnabled = false;
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        var initialValue = engine.GetHeatmapValueAt(0.15f, 0.15f);

        // Act
        engine.RecordDetection(bounds, confidence: 0.9f);

        // Assert
        var newValue = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.Equal(initialValue, newValue, precision: 4);
    }

    [Fact]
    public void RecordDetection_WithInvalidBounds_ShouldNotThrow()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var invalidBounds = new NormalizedRect(1.5f, 0.1f, 0.2f, 0.2f); // X > 1.0

        // Act & Assert
        var exception = Record.Exception(() => engine.RecordDetection(invalidBounds, confidence: 0.9f));
        Assert.Null(exception);
    }

    #endregion

    #region RecordDetections テスト

    [Fact]
    public void RecordDetections_WithMultipleDetections_ShouldUpdateAllCells()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var detections = new[]
        {
            (new NormalizedRect(0.0f, 0.0f, 0.2f, 0.2f), 0.9f),
            (new NormalizedRect(0.5f, 0.5f, 0.2f, 0.2f), 0.8f)
        };

        // Act
        engine.RecordDetections(detections);

        // Assert
        var value1 = engine.GetHeatmapValueAt(0.1f, 0.1f);
        var value2 = engine.GetHeatmapValueAt(0.6f, 0.6f);
        Assert.True(value1 > 0.0f);
        Assert.True(value2 > 0.0f);
    }

    #endregion

    #region GenerateRegions テスト

    [Fact]
    public void GenerateRegions_WithHighValueCells_ShouldReturnRegions()
    {
        // Arrange
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 1.0f, // 即座に最大値になる
            MinConfidenceForRegion = 0.3f,
            MinRegionSize = 0.01f
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);

        // 特定の領域に集中して検出を記録
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.3f, 0.3f);
        for (var i = 0; i < 5; i++)
        {
            engine.RecordDetection(bounds, 0.9f);
        }

        // Act
        var regions = engine.GenerateRegions(minConfidence: 0.3f, minHeatmapValue: 0.3f);

        // Assert
        Assert.NotEmpty(regions);
    }

    [Fact]
    public void GenerateRegions_WithNoHighValueCells_ShouldReturnEmptyList()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);

        // Act
        var regions = engine.GenerateRegions(minConfidence: 0.9f, minHeatmapValue: 0.9f);

        // Assert
        Assert.Empty(regions);
    }

    #endregion

    #region ApplyDecay テスト

    [Fact]
    public void ApplyDecay_ShouldReduceValues()
    {
        // Arrange
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 1.0f,
            DecayRate = 0.1f
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);

        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        engine.RecordDetection(bounds, 0.9f);
        var valueBefore = engine.GetHeatmapValueAt(0.15f, 0.15f);

        // Act
        engine.ApplyDecay();

        // Assert
        var valueAfter = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.True(valueAfter < valueBefore);
    }

    #endregion

    #region Reset テスト

    [Fact]
    public void Reset_ShouldClearAllData()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        engine.RecordDetection(bounds, 0.9f);
        var valueBefore = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.True(valueBefore > 0.0f);

        // Act
        engine.Reset();

        // Assert
        var valueAfter = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.Equal(0.0f, valueAfter, precision: 4);
    }

    #endregion

    #region Export/Import テスト

    [Fact]
    public void ExportHeatmap_ShouldReturnValidData()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        engine.RecordDetection(bounds, 0.9f);

        // Act
        var exported = engine.ExportHeatmap();

        // Assert
        Assert.NotNull(exported);
        Assert.True(exported.IsValid());
    }

    [Fact]
    public void ImportHeatmap_ShouldReplaceCurrentData()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var importData = RoiHeatmapData.Create(8, 8, initialValue: 0.5f);

        // Act
        engine.ImportHeatmap(importData);

        // Assert
        var value = engine.GetHeatmapValueAt(0.5f, 0.5f);
        Assert.Equal(0.5f, value, precision: 4);
    }

    [Fact]
    public void ImportHeatmap_WithNullData_ShouldThrow()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => engine.ImportHeatmap(null!));
    }

    #endregion

    #region GetStatistics テスト

    [Fact]
    public void GetStatistics_ShouldReturnCorrectStats()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        engine.RecordDetection(bounds, 0.9f);
        engine.RecordDetection(bounds, 0.8f);
        engine.RecordNoDetection(bounds);

        // Act
        var stats = engine.GetStatistics();

        // Assert
        Assert.Equal(3, stats.TotalSamples);
        Assert.Equal(2, stats.PositiveSamples);
        Assert.Equal(1, stats.NegativeSamples);
        Assert.NotNull(stats.LastLearningAt);
        Assert.NotNull(stats.LearningStartedAt);
    }

    #endregion

    #region [Issue #354] RecordDetectionsWithWeight テスト

    [Fact]
    public void RecordDetectionsWithWeight_ShouldApplyWeightToLearning()
    {
        // Arrange
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 0.1f
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);

        var detections = new[]
        {
            (new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f), 0.9f, 2), // weight=2
            (new NormalizedRect(0.5f, 0.5f, 0.2f, 0.2f), 0.8f, 1)  // weight=1
        };

        // Act
        engine.RecordDetectionsWithWeight(detections);

        // Assert
        var value1 = engine.GetHeatmapValueAt(0.15f, 0.15f); // weight=2 region
        var value2 = engine.GetHeatmapValueAt(0.55f, 0.55f); // weight=1 region
        Assert.True(value1 > value2); // 重みが大きい方がより学習される
    }

    [Fact]
    public void RecordDetectionsWithWeight_WhenLearningDisabled_ShouldNotUpdate()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        engine.IsLearningEnabled = false;
        var initialValue = engine.GetHeatmapValueAt(0.15f, 0.15f);

        var detections = new[]
        {
            (new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f), 0.9f, 2)
        };

        // Act
        engine.RecordDetectionsWithWeight(detections);

        // Assert
        var newValue = engine.GetHeatmapValueAt(0.15f, 0.15f);
        Assert.Equal(initialValue, newValue, precision: 4);
    }

    #endregion

    #region [Issue #354] RecordMiss テスト

    [Fact]
    public void RecordMiss_ShouldReturnExclusionCandidates_WhenThresholdExceeded()
    {
        // Arrange
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 0.1f,
            ConsecutiveMissThresholdForExclusion = 3 // 3回missで除外候補
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act - 3回miss
        var result1 = engine.RecordMiss(bounds);
        var result2 = engine.RecordMiss(bounds);
        var result3 = engine.RecordMiss(bounds); // 閾値到達

        // Assert
        Assert.Empty(result1); // 1回目は候補なし
        Assert.Empty(result2); // 2回目も候補なし
        Assert.NotEmpty(result3); // 3回目で除外候補発生
    }

    [Fact]
    public void RecordMiss_WhenLearningDisabled_ShouldReturnEmpty()
    {
        // Arrange
        var engine = new RoiLearningEngine(_loggerMock.Object, _defaultSettings);
        engine.IsLearningEnabled = false;
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        var result = engine.RecordMiss(bounds);

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public void RecordMiss_CumulativeCount_ShouldReachThreshold()
    {
        // Arrange
        // NOTE: RecordDetectionはmissカウントを自動リセットしない設計。
        // missカウントのリセットはヒートマップの学習成功時に行われる（別フロー）
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 0.1f,
            ConsecutiveMissThresholdForExclusion = 5
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // 5回miss（閾値に到達）
        var results = new List<IReadOnlyList<NormalizedRect>>();
        for (int i = 0; i < 5; i++)
        {
            results.Add(engine.RecordMiss(bounds));
        }

        // Assert - 最初の4回は候補なし、5回目で除外候補発生
        for (int i = 0; i < 4; i++)
        {
            Assert.Empty(results[i]);
        }
        Assert.NotEmpty(results[4]);
    }

    #endregion

    #region [Issue #354] RecordDetection with weight テスト

    [Fact]
    public void RecordDetection_WithWeight_ShouldApplyWeightToLearning()
    {
        // Arrange
        var settings = Options.Create(new RoiManagerSettings
        {
            HeatmapRows = 4,
            HeatmapColumns = 4,
            LearningRate = 0.1f
        });
        var engine = new RoiLearningEngine(_loggerMock.Object, settings);

        var bounds1 = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);
        var bounds2 = new NormalizedRect(0.5f, 0.5f, 0.2f, 0.2f);

        // Act
        engine.RecordDetection(bounds1, 0.9f, weight: 3);
        engine.RecordDetection(bounds2, 0.9f, weight: 1);

        // Assert
        var value1 = engine.GetHeatmapValueAt(0.15f, 0.15f);
        var value2 = engine.GetHeatmapValueAt(0.55f, 0.55f);
        Assert.True(value1 > value2); // 重みが大きい方がより学習される
    }

    #endregion
}
