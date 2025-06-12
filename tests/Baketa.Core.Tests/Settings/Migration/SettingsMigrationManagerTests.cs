using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Baketa.Core.Settings.Migration;

namespace Baketa.Core.Tests.Settings.Migration;

/// <summary>
/// SettingsMigrationManagerの単体テスト
/// </summary>
public class SettingsMigrationManagerTests
{
    private readonly Mock<ILogger<SettingsMigrationManager>> _mockLogger;
    private readonly SettingsMigrationManager _manager;

    public SettingsMigrationManagerTests()
    {
        _mockLogger = new Mock<ILogger<SettingsMigrationManager>>();
        _manager = new SettingsMigrationManager(_mockLogger.Object);
    }

    #region コンストラクタテスト

    [Fact]
    public void Constructor_WithValidLogger_ShouldSucceed()
    {
        // Arrange & Act
        var manager = new SettingsMigrationManager(_mockLogger.Object);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => new SettingsMigrationManager(null!));
    }

    #endregion

    #region LatestSchemaVersionテスト

    [Fact]
    public void LatestSchemaVersion_ShouldReturnPositiveValue()
    {
        // Arrange & Act
        var version = _manager.LatestSchemaVersion;

        // Assert
        Assert.True(version > 0);
    }

    #endregion

    #region RequiresMigrationテスト

    [Fact]
    public void RequiresMigration_WithOlderVersion_ShouldReturnTrue()
    {
        // Arrange
        var currentVersion = 0;

        // Act
        var requiresMigration = _manager.RequiresMigration(currentVersion);

        // Assert
        Assert.True(requiresMigration);
    }

    [Fact]
    public void RequiresMigration_WithLatestVersion_ShouldReturnFalse()
    {
        // Arrange
        var currentVersion = _manager.LatestSchemaVersion;

        // Act
        var requiresMigration = _manager.RequiresMigration(currentVersion);

        // Assert
        Assert.False(requiresMigration);
    }

    [Fact]
    public void RequiresMigration_WithFutureVersion_ShouldReturnFalse()
    {
        // Arrange
        var currentVersion = _manager.LatestSchemaVersion + 1;

        // Act
        var requiresMigration = _manager.RequiresMigration(currentVersion);

        // Assert
        Assert.False(requiresMigration);
    }

    [Fact]
    public void RequiresMigration_WithNegativeVersion_ShouldReturnTrue()
    {
        // Arrange
        var currentVersion = -1;

        // Act
        var requiresMigration = _manager.RequiresMigration(currentVersion);

        // Assert
        Assert.True(requiresMigration);
    }

    #endregion

    #region ExecuteMigrationAsyncテスト

    [Fact]
    public async Task ExecuteMigrationAsync_WithValidSettings_ShouldReturnSuccess()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", 0 },
            { "SomeOldSetting", "value" }
        };

        // Act
        var result = await _manager.ExecuteMigrationAsync(settings, 0);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FinalSettings);
        Assert.True(result.TotalExecutionTimeMs >= 0);
        Assert.NotEmpty(result.StepResults);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_WithCurrentVersion_ShouldSkipMigration()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", _manager.LatestSchemaVersion }
        };

        // Act
        var result = await _manager.ExecuteMigrationAsync(settings, _manager.LatestSchemaVersion);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FinalSettings);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public async Task ExecuteMigrationAsync_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            _manager.ExecuteMigrationAsync(null!, 0));
    }

    [Fact]
    public async Task ExecuteMigrationAsync_WithInvalidVersion_ShouldHandleGracefully()
    {
        // Arrange
        var settings = new Dictionary<string, object?>();

        // Act
        var result = await _manager.ExecuteMigrationAsync(settings, -1);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FinalSettings);
    }

    #endregion

    #region GetAvailableMigrationsテスト

    [Fact]
    public void GetAvailableMigrations_ShouldReturnNonEmptyList()
    {
        // Arrange & Act
        var migrations = _manager.GetAvailableMigrations();

        // Assert
        Assert.NotNull(migrations);
        Assert.NotEmpty(migrations);
    }

    [Fact]
    public void GetAvailableMigrations_ShouldReturnSortedMigrations()
    {
        // Arrange & Act
        var migrations = _manager.GetAvailableMigrations();

        // Assert
        Assert.NotNull(migrations);
        for (int i = 1; i < migrations.Count; i++)
        {
            Assert.True(migrations[i-1].FromVersion <= migrations[i].FromVersion);
        }
    }

    #endregion
}

/// <summary>
/// 個別マイグレーションのテスト
/// </summary>
public class V0ToV1MigrationTests
{
    private readonly V0ToV1Migration _migration;

    public V0ToV1MigrationTests()
    {
        _migration = new V0ToV1Migration();
    }

    #region プロパティテスト

    [Fact]
    public void FromVersion_ShouldReturn0()
    {
        // Assert
        Assert.Equal(0, _migration.FromVersion);
    }

    [Fact]
    public void ToVersion_ShouldReturn1()
    {
        // Assert
        Assert.Equal(1, _migration.ToVersion);
    }

    [Fact]
    public void Description_ShouldNotBeEmpty()
    {
        // Assert
        Assert.False(string.IsNullOrEmpty(_migration.Description));
    }

    #endregion

    #region MigrateAsyncテスト

    [Fact]
    public async Task MigrateAsync_WithValidSettings_ShouldMigrateSuccessfully()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", 0 },
            { "Hotkey.Enabled", true },
            { "Hotkey.ModifierKeys", 2 },
            { "Hotkey.Key", 65 },
            { "General.Language", "ja" },
            { "OCR.Engine", "PaddleOCR" }
        };

        // Act
        var result = await _migration.MigrateAsync(settings);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MigratedSettings);
        
        // ホットキー設定が削除されていることを確認
        Assert.DoesNotContain("Hotkey.Enabled", result.MigratedSettings.Keys);
        Assert.DoesNotContain("Hotkey.ModifierKeys", result.MigratedSettings.Keys);
        Assert.DoesNotContain("Hotkey.Key", result.MigratedSettings.Keys);
        
        // 他の設定は保持されていることを確認
        Assert.Contains("General.Language", result.MigratedSettings.Keys);
        Assert.Contains("OCR.Engine", result.MigratedSettings.Keys);
        
        // バージョンが更新されていることを確認
        Assert.Equal(1, result.MigratedSettings["Version"]);
    }

    [Fact]
    public async Task MigrateAsync_WithoutHotkeySettings_ShouldMigrateSuccessfully()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", 0 },
            { "General.Language", "en" },
            { "OCR.Engine", "Tesseract" }
        };

        // Act
        var result = await _migration.MigrateAsync(settings);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MigratedSettings);
        Assert.Equal("en", result.MigratedSettings["General.Language"]);
        Assert.Equal("Tesseract", result.MigratedSettings["OCR.Engine"]);
        Assert.Equal(1, result.MigratedSettings["Version"]);
    }

    [Fact]
    public async Task MigrateAsync_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _migration.MigrateAsync(null!));
    }

    [Fact]
    public async Task MigrateAsync_WithEmptySettings_ShouldMigrateSuccessfully()
    {
        // Arrange
        var settings = new Dictionary<string, object?>();

        // Act
        var result = await _migration.MigrateAsync(settings);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.MigratedSettings);
        Assert.Equal(1, result.MigratedSettings["Version"]);
    }

    #endregion

    #region IsApplicableテスト

    [Fact]
    public void IsApplicable_WithVersion0_ShouldReturnTrue()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", 0 }
        };

        // Act
        var isApplicable = _migration.IsApplicable(settings);

        // Assert
        Assert.True(isApplicable);
    }

    [Fact]
    public void IsApplicable_WithVersion1_ShouldReturnFalse()
    {
        // Arrange
        var settings = new Dictionary<string, object?>
        {
            { "Version", 1 }
        };

        // Act
        var isApplicable = _migration.IsApplicable(settings);

        // Assert
        Assert.False(isApplicable);
    }

    [Fact]
    public void IsApplicable_WithoutVersion_ShouldReturnTrue()
    {
        // Arrange
        var settings = new Dictionary<string, object?>();

        // Act
        var isApplicable = _migration.IsApplicable(settings);

        // Assert
        Assert.True(isApplicable);
    }

    [Fact]
    public void IsApplicable_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _migration.IsApplicable(null!));
    }

    #endregion
}
