using System;
using System.Drawing;
using System.Linq;
using Xunit;
using Baketa.Core.Settings;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// MainUiSettingsの単体テスト
/// </summary>
public class MainUiSettingsTests
{
    #region コンストラクタテスト

    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Act
        var settings = new MainUiSettings();

        // Assert
        Assert.Equal(new Point(50, 50), settings.PanelPosition);
        Assert.Equal(0.8, settings.PanelOpacity);
        Assert.True(settings.AutoHideWhenIdle);
        Assert.Equal(10, settings.AutoHideDelaySeconds);
        Assert.True(settings.HighlightOnHover);
        Assert.Equal(UiSize.Small, settings.PanelSize);
        Assert.True(settings.AlwaysOnTop);
        Assert.Equal(10, settings.SingleShotDisplayTime);
        Assert.True(settings.EnableDragging);
        Assert.True(settings.EnableBoundarySnap);
        Assert.Equal(20, settings.BoundarySnapDistance);
    }

    #endregion

    #region プロパティテスト

    [Fact]
    public void PanelPosition_SetValidValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings();
        var newPosition = new Point(100, 200);

        // Act
        settings.PanelPosition = newPosition;

        // Assert
        Assert.Equal(newPosition, settings.PanelPosition);
    }

    [Fact]
    public void PanelOpacity_SetValidValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            PanelOpacity = 0.5
        };

        // Assert
        Assert.Equal(0.5, settings.PanelOpacity);
    }

    [Theory]
    [InlineData(0.1)] // 最小値
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void PanelOpacity_SetValidRange_ShouldUpdateProperty(double opacity)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            PanelOpacity = opacity
        };

        // Assert
        Assert.Equal(opacity, settings.PanelOpacity);
    }

    [Theory]
    [InlineData(0.0)] // 最小値未満はクランプされる
    [InlineData(-0.1)]
    public void PanelOpacity_SetBelowMinimum_ShouldClampToMinimum(double opacity)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            PanelOpacity = opacity
        };

        // Assert
        Assert.Equal(0.1, settings.PanelOpacity); // 最小値にクランプ
    }

    [Fact]
    public void AutoHideWhenIdle_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            AutoHideWhenIdle = false
        };

        // Assert
        Assert.False(settings.AutoHideWhenIdle);
    }

    [Theory]
    [InlineData(3)] // 最小値
    [InlineData(5)]
    [InlineData(30)]
    [InlineData(60)]
    public void AutoHideDelaySeconds_SetValidValue_ShouldUpdateProperty(int delay)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            AutoHideDelaySeconds = delay
        };

        // Assert
        Assert.Equal(delay, settings.AutoHideDelaySeconds);
    }

    [Theory]
    [InlineData(1)] // 最小値未満はクランプされる
    [InlineData(2)]
    public void AutoHideDelaySeconds_SetBelowMinimum_ShouldClampToMinimum(int delay)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            AutoHideDelaySeconds = delay
        };

        // Assert
        Assert.Equal(3, settings.AutoHideDelaySeconds); // 最小値にクランプ
    }

    [Fact]
    public void HighlightOnHover_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            HighlightOnHover = false
        };

        // Assert
        Assert.False(settings.HighlightOnHover);
    }

    [Theory]
    [InlineData(UiSize.Small)]
    [InlineData(UiSize.Medium)]
    [InlineData(UiSize.Large)]
    public void PanelSize_SetValidValue_ShouldUpdateProperty(UiSize size)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            PanelSize = size
        };

        // Assert
        Assert.Equal(size, settings.PanelSize);
    }

    [Fact]
    public void AlwaysOnTop_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            AlwaysOnTop = false
        };

        // Assert
        Assert.False(settings.AlwaysOnTop);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    public void SingleShotDisplayTime_SetValidValue_ShouldUpdateProperty(int displayTime)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            SingleShotDisplayTime = displayTime
        };

        // Assert
        Assert.Equal(displayTime, settings.SingleShotDisplayTime);
    }

    [Fact]
    public void EnableDragging_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        MainUiSettings settings = new()
        {
            // Act
            EnableDragging = false
        };

        // Assert
        Assert.False(settings.EnableDragging);
    }

    [Fact]
    public void EnableBoundarySnap_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            EnableBoundarySnap = false
        };

        // Assert
        Assert.False(settings.EnableBoundarySnap);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(25)]
    [InlineData(50)]
    public void BoundarySnapDistance_SetValidValue_ShouldUpdateProperty(int distance)
    {
        // Arrange
        var settings = new MainUiSettings
        {
            // Act
            BoundarySnapDistance = distance
        };

        // Assert
        Assert.Equal(distance, settings.BoundarySnapDistance);
    }

    #endregion

    #region 設定の一貫性テスト

    [Fact]
    public void AutoHideSettings_WhenAutoHideDisabled_ShouldStillHaveValidDelay()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            AutoHideWhenIdle = false,
            AutoHideDelaySeconds = 5
        };

        // Assert
        Assert.False(settings.AutoHideWhenIdle);
        Assert.Equal(5, settings.AutoHideDelaySeconds);
    }

    [Fact]
    public void BoundarySnapSettings_WhenSnapDisabled_ShouldStillHaveValidDistance()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            EnableBoundarySnap = false,
            BoundarySnapDistance = 15
        };

        // Assert
        Assert.False(settings.EnableBoundarySnap);
        Assert.Equal(15, settings.BoundarySnapDistance);
    }

    [Fact]
    public void DraggingAndSnapSettings_ShouldBeIndependent()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            EnableDragging = false,
            EnableBoundarySnap = true
        };

        // Assert
        Assert.False(settings.EnableDragging);
        Assert.True(settings.EnableBoundarySnap);
    }

    #endregion

    #region 設定メタデータテスト

    [Fact]
    public void PanelPosition_ShouldHaveBasicLevelMetadata()
    {
        // Arrange
        var property = typeof(MainUiSettings).GetProperty(nameof(MainUiSettings.PanelPosition));

        // Act
        var attribute = property?.GetCustomAttributes(typeof(SettingMetadataAttribute), false)
            .Cast<SettingMetadataAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal(SettingLevel.Basic, attribute.Level);
        Assert.Equal("MainUi", attribute.Category);
        Assert.Equal("パネル位置", attribute.DisplayName);
    }

    [Fact]
    public void PanelOpacity_ShouldHaveBasicLevelMetadata()
    {
        // Arrange
        var property = typeof(MainUiSettings).GetProperty(nameof(MainUiSettings.PanelOpacity));

        // Act
        var attribute = property?.GetCustomAttributes(typeof(SettingMetadataAttribute), false)
            .Cast<SettingMetadataAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal(SettingLevel.Basic, attribute.Level);
        Assert.Equal("MainUi", attribute.Category);
        Assert.Equal("透明度", attribute.DisplayName);
    }

    [Fact]
    public void EnableBoundarySnap_ShouldHaveAdvancedLevelMetadata()
    {
        // Arrange
        var property = typeof(MainUiSettings).GetProperty(nameof(MainUiSettings.EnableBoundarySnap));

        // Act
        var attribute = property?.GetCustomAttributes(typeof(SettingMetadataAttribute), false)
            .Cast<SettingMetadataAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal(SettingLevel.Advanced, attribute.Level);
        Assert.Equal("MainUi", attribute.Category);
        Assert.Equal("境界スナップ", attribute.DisplayName);
    }

    [Fact]
    public void BoundarySnapDistance_ShouldHaveAdvancedLevelMetadata()
    {
        // Arrange
        var property = typeof(MainUiSettings).GetProperty(nameof(MainUiSettings.BoundarySnapDistance));

        // Act
        var attribute = property?.GetCustomAttributes(typeof(SettingMetadataAttribute), false)
            .Cast<SettingMetadataAttribute>()
            .FirstOrDefault();

        // Assert
        Assert.NotNull(attribute);
        Assert.Equal(SettingLevel.Advanced, attribute.Level);
        Assert.Equal("MainUi", attribute.Category);
        Assert.Equal("スナップ距離", attribute.DisplayName);
    }

    #endregion

    #region シリアライゼーションテスト

    [Fact]
    public void Serialization_AllProperties_ShouldRoundTrip()
    {
        // Arrange
        var originalSettings = new MainUiSettings
        {
            PanelPosition = new Point(150, 250),
            PanelOpacity = 0.7,
            AutoHideWhenIdle = false,
            AutoHideDelaySeconds = 15,
            HighlightOnHover = false,
            PanelSize = UiSize.Large,
            AlwaysOnTop = false,
            SingleShotDisplayTime = 20,
            EnableDragging = false,
            EnableBoundarySnap = false,
            BoundarySnapDistance = 30
        };

        // Act - シリアライゼーション（JSON）
        var json = System.Text.Json.JsonSerializer.Serialize(originalSettings);
        var deserializedSettings = System.Text.Json.JsonSerializer.Deserialize<MainUiSettings>(json);

        // Assert
        Assert.NotNull(deserializedSettings);
        Assert.Equal(originalSettings.PanelPosition, deserializedSettings.PanelPosition);
        Assert.Equal(originalSettings.PanelOpacity, deserializedSettings.PanelOpacity);
        Assert.Equal(originalSettings.AutoHideWhenIdle, deserializedSettings.AutoHideWhenIdle);
        Assert.Equal(originalSettings.AutoHideDelaySeconds, deserializedSettings.AutoHideDelaySeconds);
        Assert.Equal(originalSettings.HighlightOnHover, deserializedSettings.HighlightOnHover);
        Assert.Equal(originalSettings.PanelSize, deserializedSettings.PanelSize);
        Assert.Equal(originalSettings.AlwaysOnTop, deserializedSettings.AlwaysOnTop);
        Assert.Equal(originalSettings.SingleShotDisplayTime, deserializedSettings.SingleShotDisplayTime);
        Assert.Equal(originalSettings.EnableDragging, deserializedSettings.EnableDragging);
        Assert.Equal(originalSettings.EnableBoundarySnap, deserializedSettings.EnableBoundarySnap);
        Assert.Equal(originalSettings.BoundarySnapDistance, deserializedSettings.BoundarySnapDistance);
    }

    #endregion
}

/// <summary>
/// UiSizeの単体テスト
/// </summary>
public class UiSizeTests
{
    #region 列挙値テスト

    [Fact]
    public void UiSize_ShouldHaveExpectedValues()
    {
        // Assert
        Assert.True(Enum.IsDefined<UiSize>(UiSize.Small));
        Assert.True(Enum.IsDefined<UiSize>(UiSize.Medium));
        Assert.True(Enum.IsDefined<UiSize>(UiSize.Large));
    }

    [Fact]
    public void UiSize_ShouldHaveCorrectCount()
    {
        // Act
        var values = Enum.GetValues<UiSize>();

        // Assert
        Assert.Equal(3, values.Length);
    }

    [Theory]
    [InlineData(UiSize.Small, 0)]
    [InlineData(UiSize.Medium, 1)]
    [InlineData(UiSize.Large, 2)]
    public void UiSize_ShouldHaveExpectedNumericValues(UiSize size, int expectedValue)
    {
        // Assert
        Assert.Equal(expectedValue, (int)size);
    }

    #endregion

    #region 文字列変換テスト

    [Theory]
    [InlineData(UiSize.Small, "Small")]
    [InlineData(UiSize.Medium, "Medium")]
    [InlineData(UiSize.Large, "Large")]
    public void ToString_ShouldReturnExpectedString(UiSize size, string expectedString)
    {
        // Act
        var result = size.ToString();

        // Assert
        Assert.Equal(expectedString, result);
    }

    [Theory]
    [InlineData("Small", UiSize.Small)]
    [InlineData("Medium", UiSize.Medium)]
    [InlineData("Large", UiSize.Large)]
    public void Parse_WithValidString_ShouldReturnExpectedValue(string input, UiSize expectedSize)
    {
        // Act
        var result = Enum.Parse<UiSize>(input);

        // Assert
        Assert.Equal(expectedSize, result);
    }

    [Fact]
    public void Parse_WithInvalidString_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() => Enum.Parse<UiSize>("Invalid"));
    }

    #endregion
}

/// <summary>
/// AppSettingsの統合テスト（MainUiSettings含む）
/// </summary>
public class AppSettingsIntegrationTests
{
    #region MainUiSettingsプロパティテスト

    [Fact]
    public void AppSettings_ShouldHaveMainUiProperty()
    {
        // Act
        var appSettings = new AppSettings();

        // Assert
        Assert.NotNull(appSettings.MainUi);
        Assert.IsType<MainUiSettings>(appSettings.MainUi);
    }

    [Fact]
    public void AppSettings_MainUiProperty_ShouldHaveDefaultValues()
    {
        // Act
        var appSettings = new AppSettings();

        // Assert
        var mainUi = appSettings.MainUi;
        Assert.Equal(new Point(50, 50), mainUi.PanelPosition);
        Assert.Equal(0.8, mainUi.PanelOpacity);
        Assert.True(mainUi.AutoHideWhenIdle);
        Assert.Equal(10, mainUi.AutoHideDelaySeconds);
    }

    [Fact]
    public void AppSettings_MainUiProperty_SetValue_ShouldUpdateProperty()
    {
        // Arrange
        var appSettings = new AppSettings();
        var newMainUi = new MainUiSettings
        {
            PanelPosition = new Point(200, 300),
            PanelOpacity = 0.6
        };

        // Act
        appSettings.MainUi = newMainUi;

        // Assert
        Assert.Equal(newMainUi, appSettings.MainUi);
        Assert.Equal(new Point(200, 300), appSettings.MainUi.PanelPosition);
        Assert.Equal(0.6, appSettings.MainUi.PanelOpacity);
    }

    #endregion

    #region シリアライゼーション統合テスト

    [Fact]
    public void AppSettings_WithMainUi_ShouldSerializeAndDeserialize()
    {
        // Arrange
        var originalSettings = new AppSettings();
        originalSettings.MainUi.PanelPosition = new Point(100, 150);
        originalSettings.MainUi.PanelOpacity = 0.9;
        originalSettings.MainUi.PanelSize = UiSize.Medium;

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(originalSettings);
        var deserializedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        Assert.NotNull(deserializedSettings);
        Assert.NotNull(deserializedSettings.MainUi);
        Assert.Equal(originalSettings.MainUi.PanelPosition, deserializedSettings.MainUi.PanelPosition);
        Assert.Equal(originalSettings.MainUi.PanelOpacity, deserializedSettings.MainUi.PanelOpacity);
        Assert.Equal(originalSettings.MainUi.PanelSize, deserializedSettings.MainUi.PanelSize);
    }

    #endregion
}
