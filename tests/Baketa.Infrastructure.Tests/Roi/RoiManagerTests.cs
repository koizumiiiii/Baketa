using Baketa.Core.Abstractions.Roi;
using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi;
using Baketa.Infrastructure.Roi.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi;

/// <summary>
/// [Issue #293] RoiManagerの単体テスト
/// </summary>
public class RoiManagerTests : IDisposable
{
    private readonly Mock<ILogger<RoiManager>> _loggerMock;
    private readonly Mock<ILogger<RoiLearningEngine>> _engineLoggerMock;
    private readonly Mock<IRoiLearningEngine> _learningEngineMock;
    private readonly IOptions<RoiManagerSettings> _defaultSettings;
    private RoiManager? _manager;

    public RoiManagerTests()
    {
        _loggerMock = new Mock<ILogger<RoiManager>>();
        _engineLoggerMock = new Mock<ILogger<RoiLearningEngine>>();
        _learningEngineMock = new Mock<IRoiLearningEngine>();
        _defaultSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            AutoLearningEnabled = true,
            EnableDynamicThreshold = true,
            MinConfidenceForRegion = 0.3f,
            HighConfidenceThreshold = 0.7f,
            HighHeatmapThresholdMultiplier = 1.05f,
            LowHeatmapThresholdMultiplier = 0.95f,
            DecayIntervalSeconds = 0, // タイマーを無効化
            AutoSaveIntervalSeconds = 0 // タイマーを無効化
        });
    }

    public void Dispose()
    {
        _manager?.Dispose();
    }

    #region 初期化テスト

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Arrange & Act
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Assert
        Assert.True(_manager.IsEnabled);
        Assert.Null(_manager.CurrentProfile);
    }

    [Fact]
    public void IsEnabled_WithDisabledSettings_ShouldReturnFalse()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        // Act & Assert
        Assert.False(_manager.IsEnabled);
    }

    #endregion

    #region ComputeProfileId テスト

    [Fact]
    public void ComputeProfileId_ShouldReturnConsistentHash()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var path = @"C:\Games\MyGame.exe";

        // Act
        var id1 = _manager.ComputeProfileId(path);
        var id2 = _manager.ComputeProfileId(path);

        // Assert
        Assert.Equal(id1, id2);
        Assert.NotEmpty(id1);
    }

    [Fact]
    public void ComputeProfileId_ShouldBeCaseInsensitive()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var id1 = _manager.ComputeProfileId(@"C:\Games\MyGame.exe");
        var id2 = _manager.ComputeProfileId(@"C:\GAMES\MYGAME.EXE");

        // Assert
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void ComputeProfileId_WithEmptyPath_ShouldReturnEmptyString()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var id = _manager.ComputeProfileId("");

        // Assert
        Assert.Empty(id);
    }

    #endregion

    #region GetThresholdAt テスト

    [Fact]
    public void GetThresholdAt_WhenDisabled_ShouldReturnDefault()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        Assert.Equal(0.92f, threshold, precision: 4);
    }

    [Fact]
    public void GetThresholdAt_WithHighHeatmapValue_ShouldReturnHigherThreshold()
    {
        // Arrange
        _learningEngineMock.Setup(e => e.GetHeatmapValueAt(It.IsAny<float>(), It.IsAny<float>()))
            .Returns(0.9f); // 高ヒートマップ値

        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        // 高ヒートマップ領域: 0.92 * 1.05 = 0.966
        Assert.True(threshold > 0.92f);
    }

    [Fact]
    public void GetThresholdAt_WithLowHeatmapValue_ShouldReturnLowerThreshold()
    {
        // Arrange
        _learningEngineMock.Setup(e => e.GetHeatmapValueAt(It.IsAny<float>(), It.IsAny<float>()))
            .Returns(0.1f); // 低ヒートマップ値

        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var threshold = _manager.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        // 低ヒートマップ領域: 0.92 * 0.95 = 0.874
        Assert.True(threshold < 0.92f);
    }

    #endregion

    #region ReportTextDetection テスト

    [Fact]
    public void ReportTextDetection_WhenEnabled_ShouldCallLearningEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(bounds, 0.9f, 1),
            Times.Once);
    }

    [Fact]
    public void ReportTextDetection_WhenDisabled_ShouldNotCallLearningEngine()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = false,
            AutoLearningEnabled = true
        });
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            disabledSettings);

        var bounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(It.IsAny<NormalizedRect>(), It.IsAny<float>(), It.IsAny<int>()),
            Times.Never);
    }

    [Fact]
    public void ReportTextDetection_InExclusionZone_ShouldNotCallLearningEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // 除外ゾーンを追加
        var exclusionZone = new NormalizedRect(0.0f, 0.0f, 0.3f, 0.3f);
        _manager.AddExclusionZone(exclusionZone);

        // 除外ゾーン内のバウンズ
        var bounds = new NormalizedRect(0.1f, 0.1f, 0.1f, 0.1f);

        // Act
        _manager.ReportTextDetection(bounds, confidence: 0.9f);

        // Assert
        _learningEngineMock.Verify(
            e => e.RecordDetection(It.IsAny<NormalizedRect>(), It.IsAny<float>(), It.IsAny<int>()),
            Times.Never);
    }

    #endregion

    #region ExclusionZone テスト

    [Fact]
    public void IsInExclusionZone_WithPointInZone_ShouldReturnTrue()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));

        // Act
        var result = _manager.IsInExclusionZone(0.05f, 0.05f);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInExclusionZone_WithPointOutsideZone_ShouldReturnFalse()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));

        // Act
        var result = _manager.IsInExclusionZone(0.5f, 0.5f);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void RemoveExclusionZone_ShouldRemoveZone()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        var zone = new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f);
        _manager.AddExclusionZone(zone);
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));

        // Act
        var removed = _manager.RemoveExclusionZone(zone);

        // Assert
        Assert.True(removed);
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
    }

    #endregion

    #region ResetLearningData テスト

    [Fact]
    public void ResetLearningData_ShouldResetEngine()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        _manager.ResetLearningData(preserveExclusionZones: true);

        // Assert
        _learningEngineMock.Verify(e => e.Reset(), Times.Once);
    }

    [Fact]
    public void ResetLearningData_WithoutPreservingExclusionZones_ShouldClearZones()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        _manager.AddExclusionZone(new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f));
        Assert.True(_manager.IsInExclusionZone(0.05f, 0.05f));

        // Act
        _manager.ResetLearningData(preserveExclusionZones: false);

        // Assert
        Assert.False(_manager.IsInExclusionZone(0.05f, 0.05f));
    }

    #endregion

    #region GetAllRegions テスト

    [Fact]
    public void GetAllRegions_WithNoProfile_ShouldReturnEmptyList()
    {
        // Arrange
        _manager = new RoiManager(
            _loggerMock.Object,
            _learningEngineMock.Object,
            _defaultSettings);

        // Act
        var regions = _manager.GetAllRegions();

        // Assert
        Assert.Empty(regions);
    }

    #endregion
}
