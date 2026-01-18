using Baketa.Core.Settings;
using Xunit;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// [Issue #302] ImageChangeDetectionSettingsの単体テスト
/// 下部ゾーン高感度化機能を含む設定のテスト
/// </summary>
public class ImageChangeDetectionSettingsTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithDefaultSettings_ShouldReturnTrue()
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings();

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithInvalidLowerZoneSimilarityThreshold_ShouldReturnFalse()
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            LowerZoneSimilarityThreshold = 1.5f // 1.0を超える無効な値
        };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidLowerZoneRatio_ShouldReturnFalse()
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            LowerZoneRatio = -0.1f // 負の値は無効
        };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithZeroLowerZoneRatio_ShouldReturnFalse()
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            LowerZoneRatio = 0.0f // 0は無効（0より大きい必要がある）
        };

        // Act
        var result = settings.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetThresholdForRow テスト

    [Theory]
    [InlineData(0, 4, 0.98f)]  // 上部行: 通常閾値
    [InlineData(1, 4, 0.98f)]  // 上部行: 通常閾値
    [InlineData(2, 4, 0.98f)]  // 上部行: 通常閾値
    [InlineData(3, 4, 0.995f)] // 下部行（25%）: 高感度閾値
    public void GetThresholdForRow_WithLowerZoneEnabled_ShouldReturnCorrectThreshold(
        int row, int totalRows, float expectedThreshold)
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            EnableLowerZoneHighSensitivity = true,
            GridBlockSimilarityThreshold = 0.98f,
            LowerZoneSimilarityThreshold = 0.995f,
            LowerZoneRatio = 0.25f
        };

        // Act
        var result = settings.GetThresholdForRow(row, totalRows);

        // Assert
        Assert.Equal(expectedThreshold, result, precision: 4);
    }

    [Theory]
    [InlineData(0, 4)]
    [InlineData(1, 4)]
    [InlineData(2, 4)]
    [InlineData(3, 4)]
    public void GetThresholdForRow_WithLowerZoneDisabled_ShouldAlwaysReturnNormalThreshold(
        int row, int totalRows)
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            EnableLowerZoneHighSensitivity = false,
            GridBlockSimilarityThreshold = 0.98f,
            LowerZoneSimilarityThreshold = 0.995f,
            LowerZoneRatio = 0.25f
        };

        // Act
        var result = settings.GetThresholdForRow(row, totalRows);

        // Assert
        Assert.Equal(0.98f, result, precision: 4);
    }

    [Theory]
    [InlineData(0, 8, 0.98f)]  // 上部行
    [InlineData(5, 8, 0.98f)]  // 境界直前
    [InlineData(6, 8, 0.995f)] // 下部ゾーン開始（25% = 2行）
    [InlineData(7, 8, 0.995f)] // 最下行
    public void GetThresholdForRow_With8x8Grid_ShouldReturnCorrectThreshold(
        int row, int totalRows, float expectedThreshold)
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            EnableLowerZoneHighSensitivity = true,
            GridBlockSimilarityThreshold = 0.98f,
            LowerZoneSimilarityThreshold = 0.995f,
            LowerZoneRatio = 0.25f
        };

        // Act
        var result = settings.GetThresholdForRow(row, totalRows);

        // Assert
        Assert.Equal(expectedThreshold, result, precision: 4);
    }

    [Fact]
    public void GetThresholdForRow_WithZeroTotalRows_ShouldReturnNormalThreshold()
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            EnableLowerZoneHighSensitivity = true,
            GridBlockSimilarityThreshold = 0.98f,
            LowerZoneSimilarityThreshold = 0.995f
        };

        // Act
        var result = settings.GetThresholdForRow(0, 0);

        // Assert
        Assert.Equal(0.98f, result, precision: 4);
    }

    [Theory]
    [InlineData(0.50f, 2, 4)] // 50%: 行2-3が下部ゾーン
    [InlineData(0.35f, 2, 4)] // 35%: 行2-3が下部ゾーン（intキャスト）
    [InlineData(0.20f, 3, 4)] // 20%: 行3のみが下部ゾーン
    public void GetThresholdForRow_WithDifferentLowerZoneRatio_ShouldReturnCorrectThreshold(
        float lowerZoneRatio, int expectedLowerZoneStartRow, int totalRows)
    {
        // Arrange
        var settings = new ImageChangeDetectionSettings
        {
            EnableLowerZoneHighSensitivity = true,
            GridBlockSimilarityThreshold = 0.98f,
            LowerZoneSimilarityThreshold = 0.995f,
            LowerZoneRatio = lowerZoneRatio
        };

        // Act & Assert
        for (int row = 0; row < totalRows; row++)
        {
            var result = settings.GetThresholdForRow(row, totalRows);
            var expectedThreshold = row >= expectedLowerZoneStartRow ? 0.995f : 0.98f;
            Assert.Equal(expectedThreshold, result, precision: 4);
        }
    }

    #endregion

    #region ファクトリーメソッド テスト

    [Fact]
    public void CreateDevelopmentSettings_ShouldHaveLowerZoneEnabled()
    {
        // Act
        var settings = ImageChangeDetectionSettings.CreateDevelopmentSettings();

        // Assert
        Assert.True(settings.EnableLowerZoneHighSensitivity);
        Assert.Equal(0.995f, settings.LowerZoneSimilarityThreshold, precision: 4);
        Assert.Equal(0.25f, settings.LowerZoneRatio, precision: 4);
    }

    [Fact]
    public void CreateHighSensitivitySettings_ShouldHaveWiderLowerZone()
    {
        // Act
        var settings = ImageChangeDetectionSettings.CreateHighSensitivitySettings();

        // Assert
        Assert.True(settings.EnableLowerZoneHighSensitivity);
        Assert.Equal(0.998f, settings.LowerZoneSimilarityThreshold, precision: 4);
        Assert.Equal(0.35f, settings.LowerZoneRatio, precision: 4); // より広い範囲
    }

    [Fact]
    public void CreateLowSensitivitySettings_ShouldHaveNarrowerLowerZone()
    {
        // Act
        var settings = ImageChangeDetectionSettings.CreateLowSensitivitySettings();

        // Assert
        Assert.True(settings.EnableLowerZoneHighSensitivity);
        Assert.Equal(0.99f, settings.LowerZoneSimilarityThreshold, precision: 4);
        Assert.Equal(0.20f, settings.LowerZoneRatio, precision: 4); // より狭い範囲
    }

    [Fact]
    public void CreateProductionSettings_ShouldHaveDefaultLowerZone()
    {
        // Act
        var settings = ImageChangeDetectionSettings.CreateProductionSettings();

        // Assert
        Assert.True(settings.EnableLowerZoneHighSensitivity);
        Assert.Equal(0.995f, settings.LowerZoneSimilarityThreshold, precision: 4);
        Assert.Equal(0.25f, settings.LowerZoneRatio, precision: 4);
    }

    #endregion
}
