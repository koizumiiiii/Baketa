#pragma warning disable CS0618 // Type or member is obsolete
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.ViewModels.Settings;
using Baketa.UI.Tests.TestUtilities;

namespace Baketa.UI.Tests.ViewModels.Settings;

/// <summary>
/// OverlaySettingsViewModelのテスト
/// </summary>
public class OverlaySettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<OverlaySettingsViewModel>> _mockLogger;

    public OverlaySettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<OverlaySettingsViewModel>>();
    }

    [Fact]
    public void Constructor_WithValidSettings_InitializesCorrectly()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();

        // Act
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.IsEnabled.Should().BeTrue();
        viewModel.Opacity.Should().Be(0.9);
        viewModel.FontSize.Should().Be(14);
        viewModel.EnableAutoHideForSingleShot.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OverlaySettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void ValidateSettings_WithValidConfiguration_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSettings_WithInvalidOpacity_ReturnsFalse()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            Opacity = 0.05 // 範囲外
        };

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsFixedPositionEnabled_WhenPositionModeFixed_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            // Act
            PositionMode = OverlayPositionMode.Fixed
        };

        // Assert
        viewModel.IsFixedPositionEnabled.Should().BeTrue();
    }

    [Fact]
    public void IsFixedPositionEnabled_WhenPositionModeNearText_ReturnsFalse()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            // Act
            PositionMode = OverlayPositionMode.NearText
        };

        // Assert
        viewModel.IsFixedPositionEnabled.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettings_WithNewSettings_UpdatesCorrectly()
    {
        // Arrange
        var originalSettings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(originalSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        var newSettings = new OverlaySettings
        {
            IsEnabled = false,
            Opacity = 0.5,
            FontSize = 20,
            AutoHideDelayForSingleShot = 15
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        viewModel.IsEnabled.Should().BeFalse();
        viewModel.Opacity.Should().Be(0.5);
        viewModel.FontSize.Should().Be(20);
        viewModel.AutoHideDelayForSingleShot.Should().Be(15);
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void OpacityPercentage_ReturnsCorrectFormat()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            Opacity = 0.75
        };

        // Act
        var percentage = viewModel.OpacityPercentage;

        // Assert
        percentage.Should().Be("75%");
    }

    [Fact]
    public void CurrentSettings_ReturnsCorrectConfiguration()
    {
        // Arrange
        var settings = TestDataFactory.CreateOverlaySettings();
        var viewModel = new OverlaySettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            Opacity = 0.8,
            FontSize = 18,
            PositionMode = OverlayPositionMode.MouseCursor
        };

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.Opacity.Should().Be(0.8);
        currentSettings.FontSize.Should().Be(18);
        currentSettings.PositionMode.Should().Be(OverlayPositionMode.MouseCursor);
        currentSettings.IsEnabled.Should().Be(settings.IsEnabled);
    }
}
