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
/// AdvancedSettingsViewModelのテスト
/// </summary>
public class AdvancedSettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<AdvancedSettingsViewModel>> _mockLogger;

    public AdvancedSettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<AdvancedSettingsViewModel>>();
    }

    [Fact]
    public void Constructor_WithValidSettings_InitializesCorrectly()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();

        // Act
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.EnableAdvancedFeatures.Should().BeFalse();
        viewModel.OptimizeMemoryUsage.Should().BeTrue();
        viewModel.ProcessPriority.Should().Be(ProcessPriority.Normal);
        viewModel.RetryStrategy.Should().Be(RetryStrategy.Exponential);
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AdvancedSettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void ValidateSettings_WithValidConfiguration_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ValidateSettings_WithInvalidWorkerThreadCount_ReturnsFalse()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            WorkerThreadCount = 50 // 範囲外
        };

        // Act
        var result = viewModel.ValidateSettings();

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsRetryConfigEnabled_WhenRetryStrategyNone_ReturnsFalse()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            // Act
            RetryStrategy = RetryStrategy.None
        };

        // Assert
        viewModel.IsRetryConfigEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsRetryConfigEnabled_WhenRetryStrategyLinear_ReturnsTrue()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            // Act
            RetryStrategy = RetryStrategy.Linear
        };

        // Assert
        viewModel.IsRetryConfigEnabled.Should().BeTrue();
    }

    [Fact]
    public void UpdateSettings_WithNewSettings_UpdatesCorrectly()
    {
        // Arrange
        var originalSettings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(originalSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        var newSettings = new AdvancedSettings
        {
            EnableAdvancedFeatures = true,
            ProcessPriority = ProcessPriority.High,
            WorkerThreadCount = 8,
            BufferingStrategy = BufferingStrategy.Aggressive
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        viewModel.EnableAdvancedFeatures.Should().BeTrue();
        viewModel.ProcessPriority.Should().Be(ProcessPriority.High);
        viewModel.WorkerThreadCount.Should().Be(8);
        viewModel.BufferingStrategy.Should().Be(BufferingStrategy.Aggressive);
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void CpuAffinityMaskText_WhenZero_ReturnsAuto()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            CpuAffinityMask = 0
        };

        // Act
        var text = viewModel.CpuAffinityMaskText;

        // Assert
        text.Should().Be("自動");
    }

    [Fact]
    public void CpuAffinityMaskText_WhenNonZero_ReturnsHexFormat()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            CpuAffinityMask = 15 // 0xF
        };

        // Act
        var text = viewModel.CpuAffinityMaskText;

        // Assert
        text.Should().Be("0xF");
    }

    [Fact]
    public void CurrentSettings_ReturnsCorrectConfiguration()
    {
        // Arrange
        var settings = TestDataFactory.CreateAdvancedSettings();
        var viewModel = new AdvancedSettingsViewModel(settings, _mockEventAggregator.Object, _mockLogger.Object)
        {
            EnableAdvancedFeatures = true,
            CpuAffinityMask = 7,
            MaxRetryCount = 5
        };

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.EnableAdvancedFeatures.Should().BeTrue();
        currentSettings.CpuAffinityMask.Should().Be(7);
        currentSettings.MaxRetryCount.Should().Be(5);
        currentSettings.ProcessPriority.Should().Be(settings.ProcessPriority);
    }
}
