using Baketa.Core.Models.Roi;
using Xunit;

namespace Baketa.Core.Tests.Roi;

/// <summary>
/// [Issue #293] RoiProfileの単体テスト
/// </summary>
public class RoiProfileTests
{
    #region IsValid テスト

    [Fact]
    public void IsValid_WithValidProfile_ShouldReturnTrue()
    {
        // Arrange
        var profile = RoiProfile.Create("test-id", "Test Profile", @"C:\Games\Game.exe");

        // Act
        var result = profile.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_WithEmptyId_ShouldReturnFalse()
    {
        // Arrange
        var profile = new RoiProfile
        {
            Id = "",
            Name = "Test Profile"
        };

        // Act
        var result = profile.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithEmptyName_ShouldReturnFalse()
    {
        // Arrange
        var profile = new RoiProfile
        {
            Id = "test-id",
            Name = ""
        };

        // Act
        var result = profile.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidRegion_ShouldReturnFalse()
    {
        // Arrange
        var invalidRegion = new RoiRegion
        {
            Id = "", // 無効
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f)
        };

        var profile = RoiProfile.Create("test-id", "Test Profile")
            .WithRegion(invalidRegion);

        // Act
        var result = profile.IsValid();

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsValid_WithInvalidExclusionZone_ShouldReturnFalse()
    {
        // Arrange
        var profile = new RoiProfile
        {
            Id = "test-id",
            Name = "Test Profile",
            ExclusionZones = [new NormalizedRect(1.5f, 0.0f, 0.2f, 0.2f)] // 無効な座標
        };

        // Act
        var result = profile.IsValid();

        // Assert
        Assert.False(result);
    }

    #endregion

    #region Create テスト

    [Fact]
    public void Create_ShouldInitializeWithCorrectValues()
    {
        // Act
        var profile = RoiProfile.Create("hash123", "My Game", @"C:\Games\mygame.exe", "My Game*");

        // Assert
        Assert.Equal("hash123", profile.Id);
        Assert.Equal("My Game", profile.Name);
        Assert.Equal(@"C:\Games\mygame.exe", profile.ExecutablePath);
        Assert.Equal("My Game*", profile.WindowTitlePattern);
        Assert.True(profile.IsEnabled);
        Assert.True(profile.AutoLearningEnabled);
        Assert.Empty(profile.Regions);
        Assert.Empty(profile.ExclusionZones);
    }

    #endregion

    #region FindRegionAt テスト

    [Fact]
    public void FindRegionAt_WithMatchingRegion_ShouldReturnRegion()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "dialog-box",
            NormalizedBounds = new NormalizedRect(0.1f, 0.7f, 0.8f, 0.2f)
        };
        var profile = RoiProfile.Create("test", "Test").WithRegion(region);

        // Act
        var found = profile.FindRegionAt(0.5f, 0.8f);

        // Assert
        Assert.NotNull(found);
        Assert.Equal("dialog-box", found.Id);
    }

    [Fact]
    public void FindRegionAt_WithNoMatchingRegion_ShouldReturnNull()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "dialog-box",
            NormalizedBounds = new NormalizedRect(0.1f, 0.7f, 0.8f, 0.2f)
        };
        var profile = RoiProfile.Create("test", "Test").WithRegion(region);

        // Act
        var found = profile.FindRegionAt(0.5f, 0.2f); // 領域外

        // Assert
        Assert.Null(found);
    }

    #endregion

    #region FindOverlappingRegions テスト

    [Fact]
    public void FindOverlappingRegions_ShouldReturnMatchingRegions()
    {
        // Arrange
        var region1 = new RoiRegion
        {
            Id = "region1",
            NormalizedBounds = new NormalizedRect(0.0f, 0.0f, 0.3f, 0.3f)
        };
        var region2 = new RoiRegion
        {
            Id = "region2",
            NormalizedBounds = new NormalizedRect(0.2f, 0.2f, 0.3f, 0.3f)
        };
        var region3 = new RoiRegion
        {
            Id = "region3",
            NormalizedBounds = new NormalizedRect(0.7f, 0.7f, 0.2f, 0.2f)
        };
        var profile = RoiProfile.Create("test", "Test")
            .WithRegion(region1)
            .WithRegion(region2)
            .WithRegion(region3);

        // Act
        var overlapping = profile.FindOverlappingRegions(
            new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f), minIoU: 0.01f).ToList();

        // Assert
        Assert.Equal(2, overlapping.Count);
        Assert.Contains(overlapping, r => r.Id == "region1");
        Assert.Contains(overlapping, r => r.Id == "region2");
    }

    #endregion

    #region IsInExclusionZone テスト

    [Fact]
    public void IsInExclusionZone_WithPointInZone_ShouldReturnTrue()
    {
        // Arrange
        var profile = new RoiProfile
        {
            Id = "test",
            Name = "Test",
            ExclusionZones = [new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f)]
        };

        // Act
        var result = profile.IsInExclusionZone(0.05f, 0.05f);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsInExclusionZone_WithPointOutsideZone_ShouldReturnFalse()
    {
        // Arrange
        var profile = new RoiProfile
        {
            Id = "test",
            Name = "Test",
            ExclusionZones = [new NormalizedRect(0.0f, 0.0f, 0.1f, 0.1f)]
        };

        // Act
        var result = profile.IsInExclusionZone(0.5f, 0.5f);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region WithRegion テスト

    [Fact]
    public void WithRegion_ShouldAddRegionAndUpdateTime()
    {
        // Arrange
        var profile = RoiProfile.Create("test", "Test");
        var region = new RoiRegion
        {
            Id = "new-region",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f)
        };

        // Act
        var updated = profile.WithRegion(region);

        // Assert
        Assert.Single(updated.Regions);
        Assert.Equal("new-region", updated.Regions[0].Id);
        Assert.True(updated.UpdatedAt >= profile.CreatedAt);
    }

    #endregion

    #region WithUpdatedRegion テスト

    [Fact]
    public void WithUpdatedRegion_WithExistingRegion_ShouldReplaceRegion()
    {
        // Arrange
        var originalRegion = new RoiRegion
        {
            Id = "region1",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f),
            DetectionCount = 5
        };
        var profile = RoiProfile.Create("test", "Test").WithRegion(originalRegion);

        var updatedRegion = originalRegion with { DetectionCount = 10 };

        // Act
        var updated = profile.WithUpdatedRegion(updatedRegion);

        // Assert
        Assert.Single(updated.Regions);
        Assert.Equal(10, updated.Regions[0].DetectionCount);
    }

    [Fact]
    public void WithUpdatedRegion_WithNewRegion_ShouldAddRegion()
    {
        // Arrange
        var existingRegion = new RoiRegion
        {
            Id = "region1",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.2f, 0.2f)
        };
        var profile = RoiProfile.Create("test", "Test").WithRegion(existingRegion);

        var newRegion = new RoiRegion
        {
            Id = "region2",
            NormalizedBounds = new NormalizedRect(0.5f, 0.5f, 0.2f, 0.2f)
        };

        // Act
        var updated = profile.WithUpdatedRegion(newRegion);

        // Assert
        Assert.Equal(2, updated.Regions.Count);
    }

    #endregion

    #region WithLearningSession テスト

    [Fact]
    public void WithLearningSession_ShouldIncrementCount()
    {
        // Arrange
        var profile = RoiProfile.Create("test", "Test");
        Assert.Equal(0, profile.TotalLearningSessionCount);

        // Act
        var updated = profile.WithLearningSession();

        // Assert
        Assert.Equal(1, updated.TotalLearningSessionCount);
    }

    #endregion
}
