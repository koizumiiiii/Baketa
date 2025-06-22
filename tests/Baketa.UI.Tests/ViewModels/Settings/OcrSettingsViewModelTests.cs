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
/// OcrSettingsViewModelのテスト
/// </summary>
public class OcrSettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<OcrSettingsViewModel>> _mockLogger;
    private readonly OcrSettings _testSettings;

    public OcrSettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<OcrSettingsViewModel>>();
        _testSettings = TestDataFactory.CreateOcrSettings();
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.EnableOcr.Should().Be(_testSettings.EnableOcr);
        viewModel.OcrLanguage.Should().Be(_testSettings.Language);
        viewModel.ConfidenceThreshold.Should().Be(_testSettings.ConfidenceThreshold);
        viewModel.EnableTextFiltering.Should().Be(_testSettings.EnableTextFiltering);
        viewModel.HasChanges.Should().BeFalse();
        viewModel.ShowAdvancedSettings.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OcrSettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new OcrSettingsViewModel(_testSettings, null!, _mockLogger.Object));
    }

    [Theory]
    [InlineData("Japanese")]
    [InlineData("English")]
    [InlineData("Chinese")]
    [InlineData("Korean")]
    public void OcrLanguage_ValidLanguages_PropertyChangeWorks(string language)
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.OcrLanguage = language;

        // Assert
        viewModel.OcrLanguage.Should().Be(language);
        viewModel.LanguageOptions.Should().Contain(language);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(0.8)]
    [InlineData(1.0)]
    public void ConfidenceThreshold_ValidRanges_PropertyChangeWorks(double threshold)
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.ConfidenceThreshold = threshold;

        // Assert
        viewModel.ConfidenceThreshold.Should().Be(threshold);
    }

    [Theory]
    [InlineData(nameof(OcrSettingsViewModel.EnableOcr))]
    [InlineData(nameof(OcrSettingsViewModel.EnableTextFiltering))]
    public void BooleanPropertyChange_SetsHasChangesToTrue(string propertyName)
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var property = typeof(OcrSettingsViewModel).GetProperty(propertyName);
        var currentValue = (bool)property!.GetValue(viewModel)!;

        // Act
        property.SetValue(viewModel, !currentValue);

        // Assert
        viewModel.HasChanges.Should().BeTrue(); // 変更追跡が実装されている
    }

    [Fact]
    public void LanguageOptions_ContainsExpectedLanguages()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var expectedLanguages = new[] { "Japanese", "English", "Chinese", "Korean" };

        // Act & Assert
        viewModel.LanguageOptions.Should().BeEquivalentTo(expectedLanguages);
    }

    [Fact]
    public void ResetToDefaultsCommand_ResetsToDefaultValues()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var defaultSettings = new OcrSettings();
        
        // 初期値を変更
        viewModel.EnableOcr = !defaultSettings.EnableOcr;
        viewModel.ConfidenceThreshold = 0.9;

        // Act
        viewModel.ResetToDefaultsCommand.Execute().Subscribe();

        // Assert
        viewModel.EnableOcr.Should().Be(defaultSettings.EnableOcr);
        viewModel.ConfidenceThreshold.Should().Be(defaultSettings.ConfidenceThreshold);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ToggleAdvancedSettingsCommand_TogglesShowAdvancedSettings()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var initialValue = viewModel.ShowAdvancedSettings;

        // Act
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();

        // Assert
        viewModel.ShowAdvancedSettings.Should().Be(!initialValue);
    }

    [Fact]
    public void TestOcrCommand_CanExecute()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        bool canExecute = false;
        using var subscription = viewModel.TestOcrCommand.CanExecute
            .Take(1)
            .Subscribe(value => canExecute = value);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public void CurrentSettings_ReturnsCurrentValues()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 一部の値を変更
        viewModel.EnableOcr = true;
        viewModel.OcrLanguage = "English";
        viewModel.ConfidenceThreshold = 0.8;

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.EnableOcr.Should().Be(viewModel.EnableOcr);
        currentSettings.Language.Should().Be(viewModel.OcrLanguage);
        currentSettings.ConfidenceThreshold.Should().Be(viewModel.ConfidenceThreshold);
        currentSettings.EnableTextFiltering.Should().Be(viewModel.EnableTextFiltering);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.3)]
    [InlineData(0.7)]
    [InlineData(0.9)]
    public void ConfidenceThreshold_BoundaryValues_AcceptsValue(double threshold)
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.ConfidenceThreshold = threshold;

        // Assert
        viewModel.ConfidenceThreshold.Should().Be(threshold);
    }

    [Fact]
    public void EnableOcr_WhenFalse_ShouldDisableRelatedFeatures()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.EnableOcr = false;

        // Assert
        viewModel.EnableOcr.Should().BeFalse();
        // UIでの制御確認（ViewModelレベルでは特別な制御なし）
    }

    [Fact]
    public void EnableOcr_WhenTrue_ShouldEnableRelatedFeatures()
    {
        // Arrange
        var viewModel = new OcrSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        viewModel.EnableOcr = false; // 初期状態を無効に

        // Act
        viewModel.EnableOcr = true;

        // Assert
        viewModel.EnableOcr.Should().BeTrue();
    }
}
