using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ReactiveUI.Testing;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.ViewModels.Settings;
using Baketa.UI.Tests.TestUtilities;
using System.Reactive.Linq;

namespace Baketa.UI.Tests.ViewModels.Settings;

/// <summary>
/// GeneralSettingsViewModelのテスト
/// </summary>
public class GeneralSettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<GeneralSettingsViewModel>> _mockLogger;
    private readonly GeneralSettings _testSettings;

    public GeneralSettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<GeneralSettingsViewModel>>();
        _testSettings = TestDataFactory.CreateGeneralSettings();
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.AutoStartWithWindows.Should().Be(_testSettings.AutoStartWithWindows);
        viewModel.MinimizeToTray.Should().Be(_testSettings.MinimizeToTray);
        viewModel.ShowExitConfirmation.Should().Be(_testSettings.ShowExitConfirmation);
        viewModel.AllowUsageStatistics.Should().Be(_testSettings.AllowUsageStatistics);
        viewModel.CheckForUpdatesAutomatically.Should().Be(_testSettings.CheckForUpdatesAutomatically);
        viewModel.PerformanceMode.Should().Be(_testSettings.PerformanceMode);
        viewModel.MaxMemoryUsageMb.Should().Be(_testSettings.MaxMemoryUsageMb);
        viewModel.LogLevel.Should().Be(_testSettings.LogLevel);
        viewModel.LogRetentionDays.Should().Be(_testSettings.LogRetentionDays);
        viewModel.EnableDebugMode.Should().Be(_testSettings.EnableDebugMode);
        viewModel.HasChanges.Should().BeFalse();
        viewModel.ShowAdvancedSettings.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new GeneralSettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new GeneralSettingsViewModel(_testSettings, null!, _mockLogger.Object));
    }

    [Theory]
    [InlineData(nameof(GeneralSettingsViewModel.AutoStartWithWindows))]
    [InlineData(nameof(GeneralSettingsViewModel.MinimizeToTray))]
    [InlineData(nameof(GeneralSettingsViewModel.ShowExitConfirmation))]
    [InlineData(nameof(GeneralSettingsViewModel.AllowUsageStatistics))]
    [InlineData(nameof(GeneralSettingsViewModel.CheckForUpdatesAutomatically))]
    [InlineData(nameof(GeneralSettingsViewModel.PerformanceMode))]
    [InlineData(nameof(GeneralSettingsViewModel.EnableDebugMode))]
    public void BooleanPropertyChange_SetsHasChangesToTrue(string propertyName)
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var property = typeof(GeneralSettingsViewModel).GetProperty(propertyName);
        var currentValue = (bool)property!.GetValue(viewModel)!;

        // Act
        property.SetValue(viewModel, !currentValue);

        // Assert
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(GeneralSettingsViewModel.MaxMemoryUsageMb), 1024)]
    [InlineData(nameof(GeneralSettingsViewModel.LogRetentionDays), 60)]
    public void IntegerPropertyChange_SetsHasChangesToTrue(string propertyName, int newValue)
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var property = typeof(GeneralSettingsViewModel).GetProperty(propertyName);

        // Act
        property!.SetValue(viewModel, newValue);

        // Assert
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(LogLevel.Error)]
    [InlineData(LogLevel.Warning)]
    [InlineData(LogLevel.Information)]
    [InlineData(LogLevel.Debug)]
    [InlineData(LogLevel.Trace)]
    public void LogLevel_PropertyChange_SetsHasChangesToTrue(LogLevel newLogLevel)
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 現在の値と異なる値を設定する必要がある
        if (viewModel.LogLevel == newLogLevel)
        {
            // 現在の値と同じ場合は、別の値に変更してからテストする
            var differentLogLevel = newLogLevel == LogLevel.Information ? LogLevel.Debug : LogLevel.Information;
            viewModel.LogLevel = differentLogLevel;
            viewModel.HasChanges.Should().BeTrue("初回の変更でHasChangesがtrueになるべき");
            
            // HasChangesをリセットしてテストを続行
            viewModel.HasChanges = false;
        }

        // Act
        viewModel.LogLevel = newLogLevel;

        // Assert
        viewModel.LogLevel.Should().Be(newLogLevel);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void LogLevelOptions_ContainsAllLogLevels()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var expectedLogLevels = Enum.GetValues<LogLevel>();

        // Act & Assert
        viewModel.LogLevelOptions.Should().BeEquivalentTo(expectedLogLevels);
    }

    [Fact]
    public void ResetToDefaultsCommand_ResetsToDefaultValues()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var defaultSettings = new GeneralSettings();
        
        // 初期値を変更
        viewModel.AutoStartWithWindows = !defaultSettings.AutoStartWithWindows;
        viewModel.MaxMemoryUsageMb = defaultSettings.MaxMemoryUsageMb + 100;

        // Act
        viewModel.ResetToDefaultsCommand.Execute().Subscribe();

        // Assert
        viewModel.AutoStartWithWindows.Should().Be(defaultSettings.AutoStartWithWindows);
        viewModel.MaxMemoryUsageMb.Should().Be(defaultSettings.MaxMemoryUsageMb);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ToggleAdvancedSettingsCommand_TogglesShowAdvancedSettings()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var initialValue = viewModel.ShowAdvancedSettings;

        // Act
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();

        // Assert
        viewModel.ShowAdvancedSettings.Should().Be(!initialValue);
    }

    [Fact]
    public void OpenLogFolderCommand_CanExecute()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        bool canExecute = false;
        using var subscription = viewModel.OpenLogFolderCommand.CanExecute
            .Take(1)
            .Subscribe(value => canExecute = value);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public void CurrentSettings_ReturnsCurrentValues()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 一部の値を変更
        viewModel.AutoStartWithWindows = true;
        viewModel.MaxMemoryUsageMb = 1024;
        viewModel.LogLevel = LogLevel.Debug;

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.AutoStartWithWindows.Should().Be(viewModel.AutoStartWithWindows);
        currentSettings.MaxMemoryUsageMb.Should().Be(viewModel.MaxMemoryUsageMb);
        currentSettings.LogLevel.Should().Be(viewModel.LogLevel);
        currentSettings.MinimizeToTray.Should().Be(viewModel.MinimizeToTray);
        currentSettings.ShowExitConfirmation.Should().Be(viewModel.ShowExitConfirmation);
    }

    [Fact]
    public void UpdateSettings_UpdatesAllProperties()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var newSettings = new GeneralSettings
        {
            AutoStartWithWindows = true,
            MinimizeToTray = false,
            ShowExitConfirmation = false,
            MaxMemoryUsageMb = 2048,
            LogLevel = LogLevel.Error,
            LogRetentionDays = 90
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        viewModel.AutoStartWithWindows.Should().Be(newSettings.AutoStartWithWindows);
        viewModel.MinimizeToTray.Should().Be(newSettings.MinimizeToTray);
        viewModel.ShowExitConfirmation.Should().Be(newSettings.ShowExitConfirmation);
        viewModel.MaxMemoryUsageMb.Should().Be(newSettings.MaxMemoryUsageMb);
        viewModel.LogLevel.Should().Be(newSettings.LogLevel);
        viewModel.LogRetentionDays.Should().Be(newSettings.LogRetentionDays);
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettings_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => viewModel.UpdateSettings(null!));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    public void MaxMemoryUsageMb_ValidRanges_AcceptsValue(int memoryMb)
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.MaxMemoryUsageMb = memoryMb;

        // Assert
        viewModel.MaxMemoryUsageMb.Should().Be(memoryMb);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(90)]
    [InlineData(365)]
    public void LogRetentionDays_ValidRanges_AcceptsValue(int days)
    {
        // Arrange
        var viewModel = new GeneralSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.LogRetentionDays = days;

        // Assert
        viewModel.LogRetentionDays.Should().Be(days);
    }
}
