using System;
using System.Linq;
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
/// CaptureSettingsViewModelのテスト
/// </summary>
public class CaptureSettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<CaptureSettingsViewModel>> _mockLogger;

    public CaptureSettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<CaptureSettingsViewModel>>();
    }

    [Fact]
    public void Constructor_WithValidSettings_InitializesCorrectly()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();

        // Act
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.IsEnabled.Should().BeTrue();
        viewModel.CaptureIntervalMs.Should().Be(500);
        viewModel.CaptureQuality.Should().Be(85);
        viewModel.AutoDetectCaptureArea.Should().BeTrue();
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CaptureSettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void ValidateSettings_WithValidConfiguration_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSettings_WithInvalidCaptureInterval_ReturnsFalse()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            CaptureIntervalMs = 50 // 範囲外
        };

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettings_WithNewSettings_UpdatesCorrectly()
    {
        // Arrange
        var originalSettings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(originalSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        var newSettings = new CaptureSettings
        {
            IsEnabled = false,
            CaptureIntervalMs = 1000,
            CaptureQuality = 50
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        viewModel.IsEnabled.Should().BeFalse();
        viewModel.CaptureIntervalMs.Should().Be(1000);
        viewModel.CaptureQuality.Should().Be(50);
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void IsFixedAreaEnabled_WhenAutoDetectDisabled_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            // Act
            AutoDetectCaptureArea = false
        };

        // Assert
        viewModel.IsFixedAreaEnabled.Should().BeTrue();
    }

    [Fact]
    public void CurrentSettings_ReturnsCorrectConfiguration()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            CaptureIntervalMs = 750,
            CaptureQuality = 90
        };

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.CaptureIntervalMs.Should().Be(750);
        currentSettings.CaptureQuality.Should().Be(90);
        currentSettings.IsEnabled.Should().Be(settings.IsEnabled);
    }

    [Fact]
    public void SelectedMonitorOption_WithAutoSelectMode_ReturnsCorrectOption()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        settings.TargetMonitor = -1; // 自動選択
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        var selectedOption = viewModel.SelectedMonitorOption;

        // Assert
        selectedOption.Should().NotBeNull();
        selectedOption!.Index.Should().Be(-1);
        selectedOption.Name.Should().Be("自動選択");
    }

    [Fact]
    public void SelectedMonitorOption_WithPrimaryMonitor_ReturnsCorrectOption()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            TargetMonitor = 0 // プライマリモニター
        };

        // Act
        var selectedOption = viewModel.SelectedMonitorOption;

        // Assert
        selectedOption.Should().NotBeNull();
        selectedOption!.Index.Should().Be(0);
        selectedOption.Name.Should().Be("プライマリモニター");
    }

    [Fact]
    public void SelectedMonitorOption_WhenSet_UpdatesTargetMonitor()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);
        var targetOption = viewModel.MonitorOptions.First(m => m.Index == 1);

        // Act
        viewModel.SelectedMonitorOption = targetOption;

        // Assert
        viewModel.TargetMonitor.Should().Be(1);
    }

    [Fact]
    public void MonitorOptions_ContainsExpectedOptions()
    {
        // Arrange
        var settings = TestDataFactory.CreateCaptureSettings();
        var viewModel = new CaptureSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        var options = viewModel.MonitorOptions;

        // Assert
        options.Should().NotBeEmpty();
        options.Should().Contain(o => o.Index == -1 && o.Name == "自動選択");
        options.Should().Contain(o => o.Index == 0 && o.Name == "プライマリモニター");
    }
}
