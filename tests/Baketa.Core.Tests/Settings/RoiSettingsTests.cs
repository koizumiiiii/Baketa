using Baketa.Core.Settings;
using Xunit;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// [Issue #293] ROI関連設定の単体テスト
/// </summary>
public class RoiManagerSettingsTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithDefaultSettings_ShouldReturnTrue()
    {
        // Arrange
        var settings = new RoiManagerSettings();

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithInvalidHeatmapRows_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiManagerSettings { HeatmapRows = 0 };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidLearningRate_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiManagerSettings { LearningRate = 1.5f };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithMinConfidenceGreaterThanHighConfidence_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiManagerSettings
        {
            MinConfidenceForRegion = 0.8f,
            HighConfidenceThreshold = 0.5f
        };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithTooLargeHeatmapRows_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiManagerSettings { HeatmapRows = 100 };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region ファクトリーメソッド テスト

    [Fact]
    public void CreateDefault_ShouldReturnValidSettings()
    {
        // Act
        var settings = RoiManagerSettings.CreateDefault();

        // Assert
        Assert.True(settings.IsValid());
        Assert.False(settings.Enabled); // デフォルトは無効
    }

    [Fact]
    public void CreateHighSensitivity_ShouldHaveHigherLearningRate()
    {
        // Act
        var settings = RoiManagerSettings.CreateHighSensitivity();

        // Assert
        Assert.True(settings.IsValid());
        Assert.True(settings.Enabled);
        Assert.Equal(0.2f, settings.LearningRate, precision: 4);
        Assert.True(settings.EnableDynamicThreshold);
    }

    [Fact]
    public void CreateStable_ShouldHaveLowerLearningRate()
    {
        // Act
        var settings = RoiManagerSettings.CreateStable();

        // Assert
        Assert.True(settings.IsValid());
        Assert.True(settings.Enabled);
        Assert.Equal(0.05f, settings.LearningRate, precision: 4);
    }

    #endregion
}

/// <summary>
/// [Issue #293] RoiGatekeeperSettingsの単体テスト
/// </summary>
public class RoiGatekeeperSettingsTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithDefaultSettings_ShouldReturnTrue()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings();

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithInvalidShortTextThreshold_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings { ShortTextThreshold = 0 };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithShortTextThresholdGreaterThanLongText_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings
        {
            ShortTextThreshold = 100,
            LongTextThreshold = 50
        };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidChangeThreshold_ShouldReturnFalse()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings { ShortTextChangeThreshold = 1.5f };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetThresholdForTextLength テスト

    [Fact]
    public void GetThresholdForTextLength_WithShortText_ShouldReturnShortTextThreshold()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings
        {
            ShortTextThreshold = 20,
            ShortTextChangeThreshold = 0.3f,
            MediumTextChangeThreshold = 0.15f,
            LongTextThreshold = 100,
            LongTextChangeThreshold = 0.08f
        };

        // Act
        var threshold = settings.GetThresholdForTextLength(10);

        // Assert
        Assert.Equal(0.3f, threshold, precision: 4);
    }

    [Fact]
    public void GetThresholdForTextLength_WithLongText_ShouldReturnLongTextThreshold()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings
        {
            ShortTextThreshold = 20,
            ShortTextChangeThreshold = 0.3f,
            MediumTextChangeThreshold = 0.15f,
            LongTextThreshold = 100,
            LongTextChangeThreshold = 0.08f
        };

        // Act
        var threshold = settings.GetThresholdForTextLength(150);

        // Assert
        Assert.Equal(0.08f, threshold, precision: 4);
    }

    [Fact]
    public void GetThresholdForTextLength_WithMediumText_ShouldInterpolate()
    {
        // Arrange
        var settings = new RoiGatekeeperSettings
        {
            ShortTextThreshold = 20,
            ShortTextChangeThreshold = 0.3f,
            LongTextThreshold = 100,
            LongTextChangeThreshold = 0.1f
        };

        // Act
        // テキスト長60は、20-100の範囲で50%の位置
        var threshold = settings.GetThresholdForTextLength(60);
        // 期待値: 0.3 + 0.5 * (0.1 - 0.3) = 0.3 - 0.1 = 0.2

        // Assert
        Assert.Equal(0.2f, threshold, precision: 4);
    }

    [Theory]
    [InlineData(20, 0.3f)]   // 境界: ShortTextThreshold
    [InlineData(100, 0.08f)] // 境界: LongTextThreshold
    public void GetThresholdForTextLength_AtBoundaries_ShouldReturnCorrectThreshold(
        int textLength, float expectedThreshold)
    {
        // Arrange
        var settings = new RoiGatekeeperSettings
        {
            ShortTextThreshold = 20,
            ShortTextChangeThreshold = 0.3f,
            LongTextThreshold = 100,
            LongTextChangeThreshold = 0.08f
        };

        // Act
        var threshold = settings.GetThresholdForTextLength(textLength);

        // Assert
        Assert.Equal(expectedThreshold, threshold, precision: 4);
    }

    #endregion

    #region ファクトリーメソッド テスト

    [Fact]
    public void CreateDefault_ShouldReturnValidSettings()
    {
        // Act
        var settings = RoiGatekeeperSettings.CreateDefault();

        // Assert
        Assert.True(settings.IsValid());
        Assert.False(settings.Enabled); // デフォルトは無効
    }

    [Fact]
    public void CreateTokenSaving_ShouldHaveHigherThresholds()
    {
        // Act
        var settings = RoiGatekeeperSettings.CreateTokenSaving();

        // Assert
        Assert.True(settings.IsValid());
        Assert.True(settings.Enabled);
        Assert.True(settings.ShortTextChangeThreshold > 0.3f);
    }

    [Fact]
    public void CreateHighSensitivity_ShouldHaveLowerThresholds()
    {
        // Act
        var settings = RoiGatekeeperSettings.CreateHighSensitivity();

        // Assert
        Assert.True(settings.IsValid());
        Assert.True(settings.Enabled);
        Assert.True(settings.ShortTextChangeThreshold < 0.3f);
        Assert.Equal(1, settings.MinTextLength);
    }

    #endregion
}
