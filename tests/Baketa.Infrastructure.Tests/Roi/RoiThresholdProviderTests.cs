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
/// [Issue #293] RoiThresholdProviderの単体テスト
/// </summary>
public class RoiThresholdProviderTests
{
    private readonly Mock<ILogger<RoiThresholdProvider>> _loggerMock;
    private readonly Mock<IRoiManager> _roiManagerMock;
    private readonly IOptions<RoiManagerSettings> _defaultSettings;

    public RoiThresholdProviderTests()
    {
        _loggerMock = new Mock<ILogger<RoiThresholdProvider>>();
        _roiManagerMock = new Mock<IRoiManager>();
        _defaultSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            EnableDynamicThreshold = true,
            HighConfidenceThreshold = 0.7f,
            HighHeatmapThresholdMultiplier = 1.05f,
            LowHeatmapThresholdMultiplier = 0.95f
        });
    }

    #region IsEnabled テスト

    [Fact]
    public void IsEnabled_WithEnabledSettings_ShouldReturnTrue()
    {
        // Arrange
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act & Assert
        Assert.True(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WithDisabledRoiManager_ShouldReturnFalse()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = false,
            EnableDynamicThreshold = true
        });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act & Assert
        Assert.False(provider.IsEnabled);
    }

    [Fact]
    public void IsEnabled_WithDisabledDynamicThreshold_ShouldReturnFalse()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = true,
            EnableDynamicThreshold = false
        });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act & Assert
        Assert.False(provider.IsEnabled);
    }

    #endregion

    #region GetThresholdAt テスト

    [Fact]
    public void GetThresholdAt_WhenDisabled_ShouldReturnDefaultThreshold()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = false,
            EnableDynamicThreshold = false
        });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act
        var threshold = provider.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        Assert.Equal(0.92f, threshold);
    }

    [Fact]
    public void GetThresholdAt_WhenEnabled_ShouldDelegateToRoiManager()
    {
        // Arrange
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);
        _roiManagerMock
            .Setup(m => m.GetThresholdAt(0.5f, 0.5f, 0.92f))
            .Returns(0.966f); // 高ヒートマップ領域の閾値

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var threshold = provider.GetThresholdAt(0.5f, 0.5f, defaultThreshold: 0.92f);

        // Assert
        Assert.Equal(0.966f, threshold, precision: 4);
        _roiManagerMock.Verify(m => m.GetThresholdAt(0.5f, 0.5f, 0.92f), Times.Once);
    }

    #endregion

    #region GetThresholdForCell テスト

    [Fact]
    public void GetThresholdForCell_ShouldCalculateCellCenterCorrectly()
    {
        // Arrange
        // RoiManagerが有効であることをモック
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);
        // 4x4グリッドの場合、セル(1, 2)の中心は (0.625, 0.375)
        _roiManagerMock
            .Setup(m => m.GetThresholdAt(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()))
            .Returns(0.95f);

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var threshold = provider.GetThresholdForCell(
            row: 1,
            column: 2,
            totalRows: 4,
            totalColumns: 4,
            defaultThreshold: 0.92f);

        // Assert
        // セル中心: (2 + 0.5) * 0.25 = 0.625, (1 + 0.5) * 0.25 = 0.375
        _roiManagerMock.Verify(
            m => m.GetThresholdAt(
                It.Is<float>(x => Math.Abs(x - 0.625f) < 0.01f),
                It.Is<float>(y => Math.Abs(y - 0.375f) < 0.01f),
                0.92f),
            Times.Once);
    }

    [Fact]
    public void GetThresholdForCell_WhenDisabled_ShouldReturnDefaultThreshold()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings
        {
            Enabled = false
        });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act
        var threshold = provider.GetThresholdForCell(1, 2, 4, 4, defaultThreshold: 0.92f);

        // Assert
        Assert.Equal(0.92f, threshold);
        _roiManagerMock.Verify(
            m => m.GetThresholdAt(It.IsAny<float>(), It.IsAny<float>(), It.IsAny<float>()),
            Times.Never);
    }

    #endregion

    #region IsHighPriorityRegion テスト

    [Fact]
    public void IsHighPriorityRegion_WithHighHeatmapValue_ShouldReturnTrue()
    {
        // Arrange
        // RoiManagerが有効であることをモック
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);

        // [Issue #293] 高ヒートマップ値をシミュレート: GetHeatmapValueAtを直接モック
        _roiManagerMock
            .Setup(m => m.GetHeatmapValueAt(0.5f, 0.5f))
            .Returns(0.8f); // 高ヒートマップ値（HighConfidenceThreshold=0.7f以上）

        _roiManagerMock
            .Setup(m => m.IsInExclusionZone(0.5f, 0.5f))
            .Returns(false);

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var isHighPriority = provider.IsHighPriorityRegion(0.5f, 0.5f);

        // Assert
        Assert.True(isHighPriority);
    }

    [Fact]
    public void IsHighPriorityRegion_WithLowHeatmapValue_ShouldReturnFalse()
    {
        // Arrange
        // RoiManagerが有効であることをモック
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);

        // [Issue #293] 低ヒートマップ値をシミュレート: GetHeatmapValueAtを直接モック
        _roiManagerMock
            .Setup(m => m.GetHeatmapValueAt(0.5f, 0.5f))
            .Returns(0.3f); // 低ヒートマップ値（HighConfidenceThreshold=0.7f未満）

        _roiManagerMock
            .Setup(m => m.IsInExclusionZone(0.5f, 0.5f))
            .Returns(false);

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var isHighPriority = provider.IsHighPriorityRegion(0.5f, 0.5f);

        // Assert
        Assert.False(isHighPriority);
    }

    [Fact]
    public void IsHighPriorityRegion_WhenDisabled_ShouldReturnFalse()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act
        var isHighPriority = provider.IsHighPriorityRegion(0.5f, 0.5f);

        // Assert
        Assert.False(isHighPriority);
    }

    #endregion

    #region GetHeatmapValueAt テスト

    [Fact]
    public void GetHeatmapValueAt_WhenDisabled_ShouldReturnZero()
    {
        // Arrange
        var disabledSettings = Options.Create(new RoiManagerSettings { Enabled = false });

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            disabledSettings);

        // Act
        var value = provider.GetHeatmapValueAt(0.5f, 0.5f);

        // Assert
        Assert.Equal(0.0f, value);
    }

    [Fact]
    public void GetHeatmapValueAt_InExclusionZone_ShouldReturnZero()
    {
        // Arrange
        _roiManagerMock
            .Setup(m => m.IsInExclusionZone(0.5f, 0.5f))
            .Returns(true);

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var value = provider.GetHeatmapValueAt(0.5f, 0.5f);

        // Assert
        Assert.Equal(0.0f, value);
    }

    [Fact]
    public void GetHeatmapValueAt_WithHighValue_ShouldReturnHighValue()
    {
        // Arrange
        // RoiManagerが有効であることをモック
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);
        // [Issue #293] GetHeatmapValueAtを直接モック
        _roiManagerMock
            .Setup(m => m.GetHeatmapValueAt(0.5f, 0.5f))
            .Returns(0.9f); // 高ヒートマップ値

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var value = provider.GetHeatmapValueAt(0.5f, 0.5f);

        // Assert
        Assert.True(value >= 0.9f);
        _roiManagerMock.Verify(m => m.GetHeatmapValueAt(0.5f, 0.5f), Times.Once);
    }

    [Fact]
    public void GetHeatmapValueAt_WithLowValue_ShouldReturnLowValue()
    {
        // Arrange
        // RoiManagerが有効であることをモック
        _roiManagerMock
            .Setup(m => m.IsEnabled)
            .Returns(true);
        // [Issue #293] GetHeatmapValueAtを直接モック
        _roiManagerMock
            .Setup(m => m.GetHeatmapValueAt(0.5f, 0.5f))
            .Returns(0.1f); // 低ヒートマップ値

        var provider = new RoiThresholdProvider(
            _loggerMock.Object,
            _roiManagerMock.Object,
            _defaultSettings);

        // Act
        var value = provider.GetHeatmapValueAt(0.5f, 0.5f);

        // Assert
        Assert.True(value <= 0.1f);
        _roiManagerMock.Verify(m => m.GetHeatmapValueAt(0.5f, 0.5f), Times.Once);
    }

    #endregion
}
