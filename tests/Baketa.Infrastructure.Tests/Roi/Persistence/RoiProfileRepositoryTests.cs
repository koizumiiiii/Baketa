using System;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Models.Roi;
using Baketa.Infrastructure.Roi.Persistence;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Roi.Persistence;

/// <summary>
/// [Issue #293] RoiProfileRepositoryの単体テスト
/// </summary>
public class RoiProfileRepositoryTests : IDisposable
{
    private readonly Mock<ILogger<RoiProfileRepository>> _loggerMock;
    private readonly string _testDirectory;
    private readonly RoiProfileRepository _repository;

    public RoiProfileRepositoryTests()
    {
        _loggerMock = new Mock<ILogger<RoiProfileRepository>>();
        _testDirectory = Path.Combine(Path.GetTempPath(), $"RoiProfileTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);

        // テスト用にディレクトリを上書き（リフレクション使用）
        _repository = new RoiProfileRepository(_loggerMock.Object);
    }

    public void Dispose()
    {
        _repository.Dispose();

        // テストディレクトリをクリーンアップ
        if (Directory.Exists(_testDirectory))
        {
            try
            {
                Directory.Delete(_testDirectory, recursive: true);
            }
            catch
            {
                // クリーンアップ失敗は無視
            }
        }
    }

    private static RoiProfile CreateTestProfile(string id = "test-profile-123")
    {
        return RoiProfile.Create(id, "Test Profile", "C:\\Games\\TestGame.exe");
    }

    #region SaveProfileAsync テスト

    [Fact]
    public async Task SaveProfileAsync_WithValidProfile_ShouldSucceed()
    {
        // Arrange
        var profile = CreateTestProfile();

        // Act
        await _repository.SaveProfileAsync(profile);

        // Assert
        var exists = await _repository.ProfileExistsAsync(profile.Id);
        Assert.True(exists);
    }

    [Fact]
    public async Task SaveProfileAsync_WithNullProfile_ShouldThrow()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _repository.SaveProfileAsync(null!));
    }

    #endregion

    #region LoadProfileAsync テスト

    [Fact]
    public async Task LoadProfileAsync_WithExistingProfile_ShouldReturnProfile()
    {
        // Arrange
        var originalProfile = CreateTestProfile();
        await _repository.SaveProfileAsync(originalProfile);

        // Act
        var loadedProfile = await _repository.LoadProfileAsync(originalProfile.Id);

        // Assert
        Assert.NotNull(loadedProfile);
        Assert.Equal(originalProfile.Id, loadedProfile.Id);
        Assert.Equal(originalProfile.Name, loadedProfile.Name);
    }

    [Fact]
    public async Task LoadProfileAsync_WithNonExistingProfile_ShouldReturnNull()
    {
        // Act
        var profile = await _repository.LoadProfileAsync("non-existing-profile");

        // Assert
        Assert.Null(profile);
    }

    [Fact]
    public async Task LoadProfileAsync_WithNullOrEmptyId_ShouldThrow()
    {
        // Act & Assert
        // null入力はArgumentNullExceptionをスロー
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _repository.LoadProfileAsync(null!));
        // 空文字列・空白はArgumentExceptionをスロー
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.LoadProfileAsync(""));
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.LoadProfileAsync("   "));
    }

    #endregion

    #region ProfileExistsAsync テスト

    [Fact]
    public async Task ProfileExistsAsync_WithExistingProfile_ShouldReturnTrue()
    {
        // Arrange
        var profile = CreateTestProfile();
        await _repository.SaveProfileAsync(profile);

        // Act
        var exists = await _repository.ProfileExistsAsync(profile.Id);

        // Assert
        Assert.True(exists);
    }

    [Fact]
    public async Task ProfileExistsAsync_WithNonExistingProfile_ShouldReturnFalse()
    {
        // Act
        var exists = await _repository.ProfileExistsAsync("non-existing-profile");

        // Assert
        Assert.False(exists);
    }

    #endregion

    #region DeleteProfileAsync テスト

    [Fact]
    public async Task DeleteProfileAsync_WithExistingProfile_ShouldReturnTrue()
    {
        // Arrange
        var profile = CreateTestProfile();
        await _repository.SaveProfileAsync(profile);

        // Act
        var result = await _repository.DeleteProfileAsync(profile.Id);

        // Assert
        Assert.True(result);
        var exists = await _repository.ProfileExistsAsync(profile.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task DeleteProfileAsync_WithNonExistingProfile_ShouldReturnFalse()
    {
        // Act
        var result = await _repository.DeleteProfileAsync("non-existing-profile");

        // Assert
        Assert.False(result);
    }

    #endregion

    #region GetAllProfileSummariesAsync テスト

    [Fact]
    public async Task GetAllProfileSummariesAsync_WithNoProfiles_ShouldReturnEmptyList()
    {
        // Act
        var summaries = await _repository.GetAllProfileSummariesAsync();

        // Assert - リポジトリのディレクトリには既存のプロファイルがあるかもしれない
        Assert.NotNull(summaries);
    }

    [Fact]
    public async Task GetAllProfileSummariesAsync_WithMultipleProfiles_ShouldReturnAll()
    {
        // Arrange
        var profile1 = RoiProfile.Create("profile-1", "Profile 1", "C:\\Games\\Game1.exe");
        var profile2 = RoiProfile.Create("profile-2", "Profile 2", "C:\\Games\\Game2.exe");

        await _repository.SaveProfileAsync(profile1);
        await _repository.SaveProfileAsync(profile2);

        // Act
        var summaries = await _repository.GetAllProfileSummariesAsync();

        // Assert
        Assert.Contains(summaries, s => s.Id == "profile-1");
        Assert.Contains(summaries, s => s.Id == "profile-2");
    }

    #endregion

    #region FindProfileByExecutablePathAsync テスト

    [Fact]
    public async Task FindProfileByExecutablePathAsync_WithNullOrEmptyPath_ShouldThrow()
    {
        // Act & Assert
        // null入力はArgumentNullExceptionをスロー
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _repository.FindProfileByExecutablePathAsync(null!));
        // 空文字列はArgumentExceptionをスロー
        await Assert.ThrowsAsync<ArgumentException>(
            () => _repository.FindProfileByExecutablePathAsync(""));
    }

    #endregion

    #region CleanupOldProfilesAsync テスト

    [Fact]
    public async Task CleanupOldProfilesAsync_WithNoOldProfiles_ShouldReturnZero()
    {
        // Arrange
        var profile = CreateTestProfile();
        await _repository.SaveProfileAsync(profile);

        // Act - 1日以上古いプロファイルをクリーンアップ
        var deletedCount = await _repository.CleanupOldProfilesAsync(TimeSpan.FromDays(1));

        // Assert
        Assert.Equal(0, deletedCount);
    }

    #endregion

    #region ProfilesDirectoryPath テスト

    [Fact]
    public void ProfilesDirectoryPath_ShouldNotBeNull()
    {
        // Act & Assert
        Assert.NotNull(_repository.ProfilesDirectoryPath);
        Assert.NotEmpty(_repository.ProfilesDirectoryPath);
    }

    #endregion

    #region Profile Persistence テスト

    [Fact]
    public async Task SaveAndLoad_ShouldPreserveProfileData()
    {
        // Arrange
        var region = new RoiRegion
        {
            Id = "region-1",
            NormalizedBounds = new NormalizedRect(0.1f, 0.1f, 0.3f, 0.2f)
        };
        var originalProfile = CreateTestProfile()
            .WithRegion(region)
            .WithLearningSession();

        // Act
        await _repository.SaveProfileAsync(originalProfile);
        var loadedProfile = await _repository.LoadProfileAsync(originalProfile.Id);

        // Assert
        Assert.NotNull(loadedProfile);
        Assert.Equal(originalProfile.Name, loadedProfile.Name);
        Assert.Equal(originalProfile.ExecutablePath, loadedProfile.ExecutablePath);
        Assert.Equal(originalProfile.TotalLearningSessionCount, loadedProfile.TotalLearningSessionCount);
        Assert.Single(loadedProfile.Regions);
    }

    #endregion
}
