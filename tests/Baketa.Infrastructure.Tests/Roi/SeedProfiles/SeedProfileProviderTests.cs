using Baketa.Core.Models.Roi;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Roi.SeedProfiles;
using FluentAssertions;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi.SeedProfiles;

/// <summary>
/// [Issue #369] SeedProfileProvider のユニットテスト
/// </summary>
public class SeedProfileProviderTests
{
    private static RoiManagerSettings CreateDefaultSettings() => new()
    {
        Enabled = true,
        EnableSeedProfile = true,
        SeedProfileInitialDetectionCount = 50,
        SeedProfileInitialConfidenceScore = 0.6f,
        HeatmapRows = 16,
        HeatmapColumns = 16
    };

    [Fact]
    public void GetDefaultVnRegions_ShouldReturnTwoRegions()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var regions = SeedProfileProvider.GetDefaultVnRegions(settings);

        // Assert
        regions.Should().HaveCount(2);
        regions.Should().Contain(r => r.Id == "seed-main-dialog");
        regions.Should().Contain(r => r.Id == "seed-name-box");
    }

    [Fact]
    public void GetDefaultVnRegions_MainDialog_ShouldCoverLowerScreen()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var regions = SeedProfileProvider.GetDefaultVnRegions(settings);
        var mainDialog = regions.First(r => r.Id == "seed-main-dialog");

        // Assert - メインダイアログは画面下部66%〜88%をカバー
        mainDialog.NormalizedBounds.Y.Should().BeApproximately(0.66f, 0.01f);
        mainDialog.NormalizedBounds.Bottom.Should().BeApproximately(0.88f, 0.01f);
        mainDialog.RegionType.Should().Be(RoiRegionType.DialogBox);
    }

    [Fact]
    public void GetDefaultVnRegions_NameBox_ShouldBeAboveMainDialog()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var regions = SeedProfileProvider.GetDefaultVnRegions(settings);
        var nameBox = regions.First(r => r.Id == "seed-name-box");

        // Assert - 名前枠はダイアログの上（58%〜66%）
        nameBox.NormalizedBounds.Y.Should().BeApproximately(0.58f, 0.01f);
        nameBox.NormalizedBounds.Bottom.Should().BeApproximately(0.66f, 0.01f);
        nameBox.RegionType.Should().Be(RoiRegionType.Text);
    }

    [Fact]
    public void GetDefaultVnRegions_ShouldUseSettingsValues()
    {
        // Arrange
        var settings = new RoiManagerSettings
        {
            EnableSeedProfile = true,
            SeedProfileInitialDetectionCount = 100,
            SeedProfileInitialConfidenceScore = 0.8f
        };

        // Act
        var regions = SeedProfileProvider.GetDefaultVnRegions(settings);
        var mainDialog = regions.First(r => r.Id == "seed-main-dialog");

        // Assert
        mainDialog.DetectionCount.Should().Be(100);
        mainDialog.ConfidenceScore.Should().Be(0.8f);
    }

    [Fact]
    public void GetDefaultVnHeatmap_ShouldHaveCorrectSize()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var heatmap = SeedProfileProvider.GetDefaultVnHeatmap(settings);

        // Assert
        heatmap.Rows.Should().Be(16);
        heatmap.Columns.Should().Be(16);
        heatmap.Values.Length.Should().Be(256); // 16 * 16
    }

    [Fact]
    public void GetDefaultVnHeatmap_ShouldHaveHighValuesInDialogRegion()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var heatmap = SeedProfileProvider.GetDefaultVnHeatmap(settings);

        // Assert - ダイアログ領域（下部）に高い値があることを確認
        // Y=0.66〜0.88 → 16行の場合、行10〜14
        var dialogRowStart = (int)(0.66f * 16);
        var dialogRowEnd = (int)(0.88f * 16);

        for (var row = dialogRowStart; row <= dialogRowEnd && row < 16; row++)
        {
            var value = heatmap.GetValue(row, 8); // 中央列
            value.Should().BeGreaterThan(0, $"Row {row} should have positive value");
        }
    }

    [Fact]
    public void GetDefaultVnHeatmap_ShouldHaveLowValuesInTopRegion()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var heatmap = SeedProfileProvider.GetDefaultVnHeatmap(settings);

        // Assert - 上部領域（行0〜5）には値がないはず
        for (var row = 0; row < 5; row++)
        {
            var value = heatmap.GetValue(row, 8); // 中央列
            value.Should().Be(0, $"Row {row} (top region) should be 0");
        }
    }

    [Fact]
    public void CreateSeededProfile_ShouldReturnValidProfile()
    {
        // Arrange
        var settings = CreateDefaultSettings();
        var profileId = "test-profile-id";
        var name = "Test Game";
        var executablePath = @"C:\Games\test.exe";
        var windowTitle = "Test Game Window";

        // Act
        var profile = SeedProfileProvider.CreateSeededProfile(
            profileId, name, executablePath, windowTitle, settings);

        // Assert
        profile.Should().NotBeNull();
        profile.Id.Should().Be(profileId);
        profile.Name.Should().Be(name);
        profile.ExecutablePath.Should().Be(executablePath);
        profile.WindowTitlePattern.Should().Be(windowTitle);
        profile.Regions.Should().HaveCount(2);
        profile.HeatmapData.Should().NotBeNull();
        profile.IsValid().Should().BeTrue();
    }

    [Fact]
    public void CreateSeededProfile_ShouldHaveAutoLearningEnabled()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var profile = SeedProfileProvider.CreateSeededProfile(
            "id", "name", null, null, settings);

        // Assert - 学習は有効のまま（シードは初期値、実際の学習で上書きされる）
        profile.AutoLearningEnabled.Should().BeTrue();
        profile.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void GetDefaultVnRegions_AllRegions_ShouldBeValid()
    {
        // Arrange
        var settings = CreateDefaultSettings();

        // Act
        var regions = SeedProfileProvider.GetDefaultVnRegions(settings);

        // Assert
        foreach (var region in regions)
        {
            region.IsValid().Should().BeTrue($"Region {region.Id} should be valid");
            region.NormalizedBounds.IsValid().Should().BeTrue($"Region {region.Id} bounds should be valid");
        }
    }
}
