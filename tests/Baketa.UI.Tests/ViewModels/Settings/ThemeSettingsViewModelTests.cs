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
/// ThemeSettingsViewModelのテスト
/// </summary>
public class ThemeSettingsViewModelTests
{
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<ThemeSettingsViewModel>> _mockLogger;
    private readonly ThemeSettings _testSettings;

    public ThemeSettingsViewModelTests()
    {
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<ThemeSettingsViewModel>>();
        _testSettings = TestDataFactory.CreateThemeSettings();
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Assert
        viewModel.AppTheme.Should().Be(_testSettings.AppTheme);
        viewModel.AccentColor.Should().Be(_testSettings.AccentColor);
        viewModel.FontFamily.Should().Be(_testSettings.FontFamily);
        viewModel.BaseFontSize.Should().Be(_testSettings.BaseFontSize);
        viewModel.HighContrastMode.Should().Be(_testSettings.HighContrastMode);
        viewModel.EnableDpiScaling.Should().Be(_testSettings.EnableDpiScaling);
        viewModel.CustomScaleFactor.Should().Be(_testSettings.CustomScaleFactor);
        viewModel.EnableAnimations.Should().Be(_testSettings.EnableAnimations);
        viewModel.AnimationSpeed.Should().Be(_testSettings.AnimationSpeed);
        viewModel.RoundedWindowCorners.Should().Be(_testSettings.RoundedWindowCorners);
        viewModel.EnableBlurEffect.Should().Be(_testSettings.EnableBlurEffect);
        viewModel.EnableCustomCss.Should().Be(_testSettings.EnableCustomCss);
        viewModel.CustomCssFilePath.Should().Be(_testSettings.CustomCssFilePath);
        viewModel.HasChanges.Should().BeFalse();
        viewModel.ShowAdvancedSettings.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ThemeSettingsViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ThemeSettingsViewModel(_testSettings, null!, _mockLogger.Object));
    }

    [Theory]
    [InlineData(UiTheme.Light)]
    [InlineData(UiTheme.Dark)]
    [InlineData(UiTheme.Auto)]
    public void AppTheme_AllValuesSupported_PropertyChangeSetsHasChanges(UiTheme theme)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 現在の値と異なる値を設定する必要がある
        if (viewModel.AppTheme == theme)
        {
            // 現在の値と同じ場合は、別の値に変更してからテストする
            var differentTheme = theme == UiTheme.Auto ? UiTheme.Light : UiTheme.Auto;
            viewModel.AppTheme = differentTheme;
            viewModel.HasChanges.Should().BeTrue("初回の変更でHasChangesがtrueになるべき");
            
            // HasChangesをリセットしてテストを続行
            viewModel.HasChanges = false;
        }

        // Act
        viewModel.AppTheme = theme;

        // Assert
        viewModel.AppTheme.Should().Be(theme);
        viewModel.ThemeOptions.Should().Contain(theme);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(AnimationSpeed.Slow)]
    [InlineData(AnimationSpeed.Normal)]
    [InlineData(AnimationSpeed.Fast)]
    public void AnimationSpeed_AllValuesSupported_PropertyChangeSetsHasChanges(AnimationSpeed speed)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 現在の値と異なる値を設定する必要がある
        if (viewModel.AnimationSpeed == speed)
        {
            // 現在の値と同じ場合は、別の値に変更してからテストする
            var differentSpeed = speed == AnimationSpeed.Normal ? AnimationSpeed.Fast : AnimationSpeed.Normal;
            viewModel.AnimationSpeed = differentSpeed;
            viewModel.HasChanges.Should().BeTrue("初回の変更でHasChangesがtrueになるべき");
            
            // HasChangesをリセットしてテストを続行
            viewModel.HasChanges = false;
        }

        // Act
        viewModel.AnimationSpeed = speed;

        // Assert
        viewModel.AnimationSpeed.Should().Be(speed);
        viewModel.AnimationSpeedOptions.Should().Contain(speed);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(0xFF0078D4, "#FF0078D4")] // Windows Blue
    [InlineData(0xFFFF0000, "#FFFF0000")] // Red
    [InlineData(0xFF00FF00, "#FF00FF00")] // Green
    [InlineData(0xFF0000FF, "#FF0000FF")] // Blue
    public void AccentColorHex_ReturnsCorrectFormat(uint accentColor, string expectedHex)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        viewModel.AccentColor = accentColor;

        // Act
        var hex = viewModel.AccentColorHex;

        // Assert
        hex.Should().Be(expectedHex);
    }

    [Theory]
    [InlineData(0.5, "50%")]
    [InlineData(1.0, "100%")]
    [InlineData(1.5, "150%")]
    [InlineData(2.0, "200%")]
    [InlineData(3.0, "300%")]
    public void ScaleFactorPercentage_ReturnsCorrectFormat(double factor, string expected)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        viewModel.CustomScaleFactor = factor;

        // Act
        var percentage = viewModel.ScaleFactorPercentage;

        // Assert
        percentage.Should().Be(expected);
    }

    [Theory]
    [InlineData("Yu Gothic UI")]
    [InlineData("Meiryo UI")]
    [InlineData("Microsoft YaHei UI")]
    [InlineData("Segoe UI")]
    [InlineData("Arial")]
    public void FontFamily_ValidOptions_PropertyChangeSetsHasChanges(string fontFamily)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 現在の値と異なる値を設定する必要がある
        if (viewModel.FontFamily == fontFamily)
        {
            // 現在の値と同じ場合は、別の値に変更してからテストする
            var differentFont = fontFamily == "Yu Gothic UI" ? "Arial" : "Yu Gothic UI";
            viewModel.FontFamily = differentFont;
            viewModel.HasChanges.Should().BeTrue("初回の変更でHasChangesがtrueになるべき");
            
            // HasChangesをリセットしてテストを続行
            viewModel.HasChanges = false;
        }

        // Act
        viewModel.FontFamily = fontFamily;

        // Assert
        viewModel.FontFamily.Should().Be(fontFamily);
        viewModel.FontFamilyOptions.Should().Contain(fontFamily);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(9)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    public void BaseFontSize_ValidRanges_PropertyChangeSetsHasChanges(int fontSize)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 現在の値と異なる値を設定する必要がある
        if (viewModel.BaseFontSize == fontSize)
        {
            // 現在の値と同じ場合は、別の値に変更してからテストする
            var differentFontSize = fontSize == 12 ? 16 : 12;
            viewModel.BaseFontSize = differentFontSize;
            viewModel.HasChanges.Should().BeTrue("初回の変更でHasChangesがtrueになるべき");
            
            // HasChangesをリセットしてテストを続行
            viewModel.HasChanges = false;
        }

        // Act
        viewModel.BaseFontSize = fontSize;

        // Assert
        viewModel.BaseFontSize.Should().Be(fontSize);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(ThemeSettingsViewModel.HighContrastMode))]
    [InlineData(nameof(ThemeSettingsViewModel.EnableDpiScaling))]
    [InlineData(nameof(ThemeSettingsViewModel.EnableAnimations))]
    [InlineData(nameof(ThemeSettingsViewModel.RoundedWindowCorners))]
    [InlineData(nameof(ThemeSettingsViewModel.EnableBlurEffect))]
    [InlineData(nameof(ThemeSettingsViewModel.EnableCustomCss))]
    public void BooleanPropertyChange_SetsHasChangesToTrue(string propertyName)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var property = typeof(ThemeSettingsViewModel).GetProperty(propertyName);
        var currentValue = (bool)property!.GetValue(viewModel)!;

        // Act
        property.SetValue(viewModel, !currentValue);

        // Assert
        viewModel.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ThemeOptions_ContainsAllThemes()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var expectedThemes = Enum.GetValues<UiTheme>();

        // Act & Assert
        viewModel.ThemeOptions.Should().BeEquivalentTo(expectedThemes);
    }

    [Fact]
    public void AnimationSpeedOptions_ContainsAllSpeeds()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var expectedSpeeds = Enum.GetValues<AnimationSpeed>();

        // Act & Assert
        viewModel.AnimationSpeedOptions.Should().BeEquivalentTo(expectedSpeeds);
    }

    [Fact]
    public void FontFamilyOptions_ContainsExpectedFonts()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var expectedFonts = new[] { "Yu Gothic UI", "Meiryo UI", "Microsoft YaHei UI", "Segoe UI", "Arial" };

        // Act & Assert
        viewModel.FontFamilyOptions.Should().BeEquivalentTo(expectedFonts);
    }

    [Fact]
    public void ResetToDefaultsCommand_ResetsToDefaultValues()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var defaultSettings = new ThemeSettings();
        
        // 初期値を変更
        viewModel.AppTheme = UiTheme.Light;
        viewModel.BaseFontSize = 20;
        viewModel.HighContrastMode = true;

        // Act
        viewModel.ResetToDefaultsCommand.Execute().Subscribe();

        // Assert
        viewModel.AppTheme.Should().Be(defaultSettings.AppTheme);
        viewModel.BaseFontSize.Should().Be(defaultSettings.BaseFontSize);
        viewModel.HighContrastMode.Should().Be(defaultSettings.HighContrastMode);
        viewModel.HasChanges.Should().BeTrue();
    }

    [Fact]
    public void ToggleAdvancedSettingsCommand_TogglesShowAdvancedSettings()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var initialValue = viewModel.ShowAdvancedSettings;

        // Act
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();

        // Assert
        viewModel.ShowAdvancedSettings.Should().Be(!initialValue);
    }

    [Fact]
    public void ChooseAccentColorCommand_CanExecute()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        bool canExecute = false;
        using var subscription = viewModel.ChooseAccentColorCommand.CanExecute
            .Take(1)
            .Subscribe(value => canExecute = value);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public void BrowseCssFileCommand_CanExecute()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        bool canExecute = false;
        using var subscription = viewModel.BrowseCssFileCommand.CanExecute
            .Take(1)
            .Subscribe(value => canExecute = value);

        // Assert
        canExecute.Should().BeTrue();
    }

    [Fact]
    public void CurrentSettings_ReturnsCurrentValues()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        
        // 一部の値を変更
        viewModel.AppTheme = UiTheme.Dark;
        viewModel.BaseFontSize = 16;
        viewModel.EnableAnimations = false;

        // Act
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        currentSettings.AppTheme.Should().Be(viewModel.AppTheme);
        currentSettings.BaseFontSize.Should().Be(viewModel.BaseFontSize);
        currentSettings.EnableAnimations.Should().Be(viewModel.EnableAnimations);
        currentSettings.AccentColor.Should().Be(viewModel.AccentColor);
        currentSettings.FontFamily.Should().Be(viewModel.FontFamily);
    }

    [Fact]
    public void UpdateSettings_UpdatesAllProperties()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);
        var newSettings = new ThemeSettings
        {
            AppTheme = UiTheme.Light,
            AccentColor = 0xFFFF0000,
            FontFamily = "Arial",
            BaseFontSize = 14,
            HighContrastMode = true,
            EnableDpiScaling = false,
            CustomScaleFactor = 1.5,
            EnableAnimations = false,
            AnimationSpeed = AnimationSpeed.Fast,
            RoundedWindowCorners = false,
            EnableBlurEffect = false,
            EnableCustomCss = true,
            CustomCssFilePath = @"C:\custom.css"
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        viewModel.AppTheme.Should().Be(newSettings.AppTheme);
        viewModel.AccentColor.Should().Be(newSettings.AccentColor);
        viewModel.FontFamily.Should().Be(newSettings.FontFamily);
        viewModel.BaseFontSize.Should().Be(newSettings.BaseFontSize);
        viewModel.HighContrastMode.Should().Be(newSettings.HighContrastMode);
        viewModel.EnableDpiScaling.Should().Be(newSettings.EnableDpiScaling);
        viewModel.CustomScaleFactor.Should().Be(newSettings.CustomScaleFactor);
        viewModel.EnableAnimations.Should().Be(newSettings.EnableAnimations);
        viewModel.AnimationSpeed.Should().Be(newSettings.AnimationSpeed);
        viewModel.RoundedWindowCorners.Should().Be(newSettings.RoundedWindowCorners);
        viewModel.EnableBlurEffect.Should().Be(newSettings.EnableBlurEffect);
        viewModel.EnableCustomCss.Should().Be(newSettings.EnableCustomCss);
        viewModel.CustomCssFilePath.Should().Be(newSettings.CustomCssFilePath);
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettings_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => viewModel.UpdateSettings(null!));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1.0)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    [InlineData(3.0)]
    public void CustomScaleFactor_ValidRanges_AcceptsValue(double scaleFactor)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.CustomScaleFactor = scaleFactor;

        // Assert
        viewModel.CustomScaleFactor.Should().Be(scaleFactor);
    }

    [Theory]
    [InlineData(@"C:\styles\custom.css")]
    [InlineData(@"D:\app\theme.css")]
    [InlineData("")]
    public void CustomCssFilePath_ValidPaths_AcceptsValue(string cssPath)
    {
        // Arrange
        var viewModel = new ThemeSettingsViewModel(_testSettings, _mockEventAggregator.Object, _mockLogger.Object);

        // Act
        viewModel.CustomCssFilePath = cssPath;

        // Assert
        viewModel.CustomCssFilePath.Should().Be(cssPath);
    }
}
