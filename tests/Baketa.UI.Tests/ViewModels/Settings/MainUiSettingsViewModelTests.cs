using Xunit;
using Baketa.Core.Settings;
using Baketa.UI.ViewModels.Settings;
using System;
using System.Linq;
using Baketa.Core.Abstractions.Events;
using Microsoft.Extensions.Logging;
using Moq;

namespace Baketa.UI.Tests.ViewModels.Settings;

/// <summary>
/// MainUiSettingsViewModelのテストクラス
/// </summary>
public sealed class MainUiSettingsViewModelTests
{
    /// <summary>
    /// テスト用のモックオブジェクトを作成し、MainUiSettingsViewModelを生成します
    /// </summary>
    /// <param name="settings">設定データ</param>
    /// <returns>MainUiSettingsViewModelインスタンス</returns>
    private static MainUiSettingsViewModel CreateViewModel(MainUiSettings settings)
    {
        var mockEventAggregator = new Mock<IEventAggregator>();
        var mockLogger = new Mock<ILogger<MainUiSettingsViewModel>>();
        
        return new MainUiSettingsViewModel(settings, mockEventAggregator.Object, mockLogger.Object);
    }
    
    /// <summary>
    /// テスト用のモックオブジェクトを作成し、MainUiSettingsViewModelを生成します（Loggerはnull）
    /// </summary>
    /// <param name="settings">設定データ</param>
    /// <returns>MainUiSettingsViewModelインスタンス</returns>
    private static MainUiSettingsViewModel CreateViewModelWithNullLogger(MainUiSettings settings)
    {
        var mockEventAggregator = new Mock<IEventAggregator>();
        
        return new MainUiSettingsViewModel(settings, mockEventAggregator.Object, null);
    }

    /// <summary>
    /// 初期化時に設定データが正しく設定されることをテスト
    /// </summary>
    [Fact]
    public void Constructor_WithValidSettings_SetsPropertiesCorrectly()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            PanelOpacity = 0.7,
            AutoHideWhenIdle = false,
            AlwaysOnTop = false,
            PanelSize = UiSize.Large,
            ThemeStyle = UiTheme.Dark
        };

        // Act
        var viewModel = CreateViewModel(settings);

        // Assert
        Assert.Equal(0.7, viewModel.PanelOpacity);
        Assert.False(viewModel.AutoHideWhenIdle);
        Assert.False(viewModel.AlwaysOnTop);
        Assert.Equal(UiSize.Large, viewModel.PanelSize);
        Assert.Equal(UiTheme.Dark, viewModel.ThemeStyle);
        Assert.False(viewModel.HasChanges);
        Assert.False(viewModel.ShowAdvancedSettings);
    }

    /// <summary>
    /// null設定で初期化した場合に例外がスローされることをテスト
    /// </summary>
    [Fact]
    public void Constructor_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange
        var mockEventAggregator = new Mock<IEventAggregator>();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new MainUiSettingsViewModel(null!, mockEventAggregator.Object, null));
    }

    /// <summary>
    /// プロパティ変更時にHasChangesがtrueになることをテスト
    /// </summary>
    [Fact]
    public void PropertyChange_SetsHasChangesToTrue()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);
        
        // HasChangesの初期状態確認
        Assert.False(viewModel.HasChanges);

        // Act
        viewModel.PanelOpacity = 0.5;

        // Assert
        Assert.True(viewModel.HasChanges);
        Assert.Equal(0.5, viewModel.PanelOpacity);
    }

    /// <summary>
    /// 複数プロパティ変更時にすべて追跡されることをテスト
    /// </summary>
    [Fact]
    public void MultiplePropertyChanges_AllTracked()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        viewModel.PanelOpacity = 0.6;
        viewModel.AutoHideWhenIdle = false;
        viewModel.PanelSize = UiSize.Large;
        viewModel.ThemeStyle = UiTheme.Light;

        // Assert
        Assert.True(viewModel.HasChanges);
        Assert.Equal(0.6, viewModel.PanelOpacity);
        Assert.False(viewModel.AutoHideWhenIdle);
        Assert.Equal(UiSize.Large, viewModel.PanelSize);
        Assert.Equal(UiTheme.Light, viewModel.ThemeStyle);
    }

    /// <summary>
    /// ShowAdvancedSettingsトグルが正しく動作することをテスト
    /// </summary>
    [Fact]
    public void ShowAdvancedSettings_ToggleWorks()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);
        
        Assert.False(viewModel.ShowAdvancedSettings);

        // Act
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();

        // Assert
        Assert.True(viewModel.ShowAdvancedSettings);
        
        // もう一度トグル
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();
        Assert.False(viewModel.ShowAdvancedSettings);
    }

    /// <summary>
    /// ResetToDefaultsコマンドが正しく動作することをテスト
    /// </summary>
    [Fact]
    public void ResetToDefaultsCommand_ResetsToDefaultValues()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            PanelOpacity = 0.3,
            AutoHideWhenIdle = false,
            AlwaysOnTop = false,
            PanelSize = UiSize.Large,
            ThemeStyle = UiTheme.Light
        };
        var viewModel = CreateViewModel(settings);
        var defaultSettings = new MainUiSettings();

        // Act
        viewModel.ResetToDefaultsCommand.Execute().Subscribe();

        // Assert
        Assert.Equal(defaultSettings.PanelOpacity, viewModel.PanelOpacity);
        Assert.Equal(defaultSettings.AutoHideWhenIdle, viewModel.AutoHideWhenIdle);
        Assert.Equal(defaultSettings.AlwaysOnTop, viewModel.AlwaysOnTop);
        Assert.Equal(defaultSettings.PanelSize, viewModel.PanelSize);
        Assert.Equal(defaultSettings.ThemeStyle, viewModel.ThemeStyle);
        Assert.True(viewModel.HasChanges); // リセット後は変更ありフラグが立つ
    }

    /// <summary>
    /// PanelSizesプロパティがすべてのUiSize値を含むことをテスト
    /// </summary>
    [Fact]
    public void PanelSizes_ContainsAllUiSizeValues()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        var panelSizes = viewModel.PanelSizes;

        // Assert
        Assert.Contains(UiSize.Small, panelSizes);
        Assert.Contains(UiSize.Medium, panelSizes);
        Assert.Contains(UiSize.Large, panelSizes);
        Assert.Equal(3, panelSizes.Count);
    }

    /// <summary>
    /// ThemeOptionsプロパティがすべてのUiTheme値を含むことをテスト
    /// </summary>
    [Fact]
    public void ThemeOptions_ContainsAllUiThemeValues()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        var themeOptions = viewModel.ThemeOptions;

        // Assert
        Assert.Contains(UiTheme.Light, themeOptions);
        Assert.Contains(UiTheme.Dark, themeOptions);
        Assert.Contains(UiTheme.Auto, themeOptions);
        Assert.Equal(3, themeOptions.Count);
    }

    /// <summary>
    /// PanelOpacityPercentageプロパティが正しくフォーマットされることをテスト
    /// </summary>
    [Fact]
    public void PanelOpacityPercentage_FormatsCorrectly()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act & Assert
        viewModel.PanelOpacity = 0.5;
        Assert.Equal("50%", viewModel.PanelOpacityPercentage);

        viewModel.PanelOpacity = 0.75;
        Assert.Equal("75%", viewModel.PanelOpacityPercentage);

        viewModel.PanelOpacity = 1.0;
        Assert.Equal("100%", viewModel.PanelOpacityPercentage);
    }

    /// <summary>
    /// CurrentSettingsプロパティが正しい設定データを返すことをテスト
    /// </summary>
    [Fact]
    public void CurrentSettings_ReturnsCorrectData()
    {
        // Arrange
        var originalSettings = new MainUiSettings
        {
            PanelOpacity = 0.6,
            AutoHideWhenIdle = false,
            PanelSize = UiSize.Large
        };
        var viewModel = CreateViewModel(originalSettings);

        // Act
        viewModel.PanelOpacity = 0.9;
        viewModel.AlwaysOnTop = false;
        var currentSettings = viewModel.CurrentSettings;

        // Assert
        Assert.Equal(0.9, currentSettings.PanelOpacity);
        Assert.False(currentSettings.AutoHideWhenIdle);
        Assert.False(currentSettings.AlwaysOnTop);
        Assert.Equal(UiSize.Large, currentSettings.PanelSize);
    }

    /// <summary>
    /// UpdateSettingsメソッドが正しく動作することをテスト
    /// </summary>
    [Fact]
    public void UpdateSettings_UpdatesAllProperties()
    {
        // Arrange
        var initialSettings = new MainUiSettings();
        var viewModel = CreateViewModel(initialSettings);
        
        var newSettings = new MainUiSettings
        {
            PanelOpacity = 0.4,
            AutoHideWhenIdle = false,
            AlwaysOnTop = false,
            PanelSize = UiSize.Large,
            ThemeStyle = UiTheme.Dark
        };

        // Act
        viewModel.UpdateSettings(newSettings);

        // Assert
        Assert.Equal(0.4, viewModel.PanelOpacity);
        Assert.False(viewModel.AutoHideWhenIdle);
        Assert.False(viewModel.AlwaysOnTop);
        Assert.Equal(UiSize.Large, viewModel.PanelSize);
        Assert.Equal(UiTheme.Dark, viewModel.ThemeStyle);
        Assert.False(viewModel.HasChanges); // UpdateSettings後は変更フラグがリセットされる
    }

    /// <summary>
    /// UpdateSettingsメソッドでnullを渡すと例外がスローされることをテスト
    /// </summary>
    [Fact]
    public void UpdateSettings_WithNull_ThrowsArgumentNullException()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => viewModel.UpdateSettings(null!));
    }

    /// <summary>
    /// 数値プロパティの境界値テスト
    /// </summary>
    [Theory]
    [InlineData(0.1)] // 最小値
    [InlineData(0.5)] // 中間値
    [InlineData(1.0)] // 最大値
    public void PanelOpacity_BoundaryValues_AcceptedCorrectly(double opacity)
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        viewModel.PanelOpacity = opacity;

        // Assert
        Assert.Equal(opacity, viewModel.PanelOpacity);
    }

    /// <summary>
    /// AutoHideDelaySecondsの境界値テスト
    /// </summary>
    [Theory]
    [InlineData(3)]   // 最小値
    [InlineData(150)] // 中間値
    [InlineData(300)] // 最大値
    public void AutoHideDelaySeconds_BoundaryValues_AcceptedCorrectly(int delaySeconds)
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        viewModel.AutoHideDelaySeconds = delaySeconds;

        // Assert
        Assert.Equal(delaySeconds, viewModel.AutoHideDelaySeconds);
    }

    /// <summary>
    /// バッキングフィールドが正しく更新されることをテスト
    /// </summary>
    [Fact]
    public void BackingFields_UpdateCorrectly()
    {
        // Arrange
        var settings = new MainUiSettings();
        var viewModel = CreateViewModel(settings);

        // Act
        viewModel.PanelOpacity = 0.7;
        viewModel.EnableAnimations = false;
        viewModel.ThemeStyle = UiTheme.Dark;

        // Assert - CurrentSettingsで確認
        var currentSettings = viewModel.CurrentSettings;
        Assert.Equal(0.7, currentSettings.PanelOpacity);
        Assert.False(currentSettings.EnableAnimations);
        Assert.Equal(UiTheme.Dark, currentSettings.ThemeStyle);
    }

    /// <summary>
    /// 初期化時の変更追跡が正しく動作することをテスト
    /// </summary>
    [Fact]
    public void Initialization_DoesNotTriggerChangeTracking()
    {
        // Arrange & Act
        var settings = new MainUiSettings { PanelOpacity = 0.5 };
        var viewModel = CreateViewModel(settings);

        // Assert - 初期化時は変更フラグが立たない
        Assert.False(viewModel.HasChanges);
        Assert.Equal(0.5, viewModel.PanelOpacity);
    }
}
