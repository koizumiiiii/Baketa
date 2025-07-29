#pragma warning disable CS0618 // Type or member is obsolete
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;
using Baketa.Core.Services;

namespace Baketa.Core.Tests.Services;

/// <summary>
/// EnhancedSettingsServiceの包括的テスト
/// ファイルI/O、JSON処理、エラーハンドリング、設定変更ロジックをテスト
/// </summary>
public sealed class EnhancedSettingsServiceTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly Mock<ILogger<EnhancedSettingsService>> _mockLogger;
    private readonly Mock<ISettingMetadataService> _mockMetadataService;
    private readonly Mock<ISettingsMigrationManager> _mockMigrationManager;

    public EnhancedSettingsServiceTests()
    {
        // テスト用一時ディレクトリの作成（プロセスIDとスレッドIDを含めてユニーク性を確保）
        var processId = Environment.ProcessId;
        var threadId = Environment.CurrentManagedThreadId;
        var uniqueId = $"{processId}_{threadId}_{Guid.NewGuid():N}";
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"BaketaSettingsTest_{uniqueId}", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempSettingsPath)!);

        // モックの初期化
        _mockLogger = new Mock<ILogger<EnhancedSettingsService>>();
        _mockMetadataService = new Mock<ISettingMetadataService>();
        _mockMigrationManager = new Mock<ISettingsMigrationManager>();
    }

    /// <summary>
    /// Root cause solution: Enhanced service creation with proper initialization validation
    /// </summary>
    private EnhancedSettingsService CreateService()
    {
        // Validate mock dependencies before proceeding
        ArgumentNullException.ThrowIfNull(_mockLogger?.Object, nameof(_mockLogger));
        ArgumentNullException.ThrowIfNull(_mockMetadataService?.Object, nameof(_mockMetadataService));
        ArgumentNullException.ThrowIfNull(_mockMigrationManager?.Object, nameof(_mockMigrationManager));
        
        var uniquePath = Path.Combine(Path.GetDirectoryName(_tempSettingsPath)!, $"settings_{Guid.NewGuid():N}.json");
        
        // Ensure directory exists before creating service
        var directory = Path.GetDirectoryName(uniquePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        return new(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, uniquePath);
    }

    #region ファイルI/O テスト

    [Fact]
    public async Task SaveAsync_WithValidSettings_CreatesFileSuccessfully()
    {
        // Arrange - Root cause solution: Use known file path to ensure test consistency
        var testFilePath = Path.Combine(Path.GetDirectoryName(_tempSettingsPath)!, $"test_settings_{Guid.NewGuid():N}.json");
        var service = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, testFilePath);
        var settings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = true, MinimizeToTray = false },
            MainUi = new MainUiSettings { PanelOpacity = 0.8 }
        };

        // Act
        await service.SetSettingsAsync(settings);

        // Assert
        File.Exists(testFilePath).Should().BeTrue();
        
        // ファイル内容の確認
        var fileContent = await File.ReadAllTextAsync(testFilePath);
        fileContent.Should().Contain("true");
        fileContent.Should().Contain("0.8");
    }

    [Fact]
    public async Task ReloadAsync_WithExistingFile_LoadsSettingsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var originalSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = false, MinimizeToTray = true },
            Theme = new ThemeSettings { AppTheme = UiTheme.Dark }
        };

        await service.SetSettingsAsync(originalSettings);

        // Act
        await service.ReloadAsync();
        var reloadedSettings = service.GetSettings();

        // Assert
        reloadedSettings.General.AutoStartWithWindows.Should().BeFalse();
        reloadedSettings.General.MinimizeToTray.Should().BeTrue();
        reloadedSettings.Theme.AppTheme.Should().Be(UiTheme.Dark);
    }

    [Fact]
    public async Task ReloadAsync_WithNonExistentFile_UsesDefaultSettings()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid():N}.json");
        var service = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, nonExistentPath);

        // Act
        await service.ReloadAsync();
        var settings = service.GetSettings();

        // Assert
        settings.Should().NotBeNull();
        settings.General.Should().NotBeNull();
        settings.MainUi.Should().NotBeNull();
        settings.Theme.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateBackupAsync_WithValidSettings_CreatesBackupFile()
    {
        // Arrange
        var service = CreateService();
        var backupPath = Path.Combine(Path.GetTempPath(), $"backup_{Guid.NewGuid():N}.json");
        
        var settings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = true }
        };
        await service.SetSettingsAsync(settings);

        // Act
        await service.CreateBackupAsync(backupPath);

        // Assert
        File.Exists(backupPath).Should().BeTrue();
        
        // バックアップファイルの内容確認
        var backupContent = await File.ReadAllTextAsync(backupPath);
        backupContent.Should().Contain("true");

        // Cleanup
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
    }

    [Fact]
    public async Task RestoreFromBackupAsync_WithValidBackup_RestoresSettingsCorrectly()
    {
        // Arrange
        var service = CreateService();
        var backupPath = Path.Combine(Path.GetTempPath(), $"restore_test_{Guid.NewGuid():N}.json");

        // 元の設定を作成・保存
        var originalSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = true, MinimizeToTray = false },
            MainUi = new MainUiSettings { PanelOpacity = 0.9 }
        };
        await service.SetSettingsAsync(originalSettings);
        await service.CreateBackupAsync(backupPath);

        // 設定を変更
        var modifiedSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = false, MinimizeToTray = true },
            MainUi = new MainUiSettings { PanelOpacity = 0.5 }
        };
        await service.SetSettingsAsync(modifiedSettings);

        // Act - バックアップから復元
        await service.RestoreFromBackupAsync(backupPath);

        // Assert
        var restoredSettings = service.GetSettings();
        restoredSettings.General.AutoStartWithWindows.Should().BeTrue();
        restoredSettings.General.MinimizeToTray.Should().BeFalse();
        restoredSettings.MainUi.PanelOpacity.Should().Be(0.9);

        // Cleanup
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
    }

    [Fact]
    public async Task RestoreFromBackupAsync_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        var service = CreateService();
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"NonExistent_{Guid.NewGuid():N}.json");

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => service.RestoreFromBackupAsync(nonExistentPath));
    }

    #endregion

    #region JSON処理テスト

    [Fact]
    public async Task JsonSerialization_ComplexSettings_PreservesAllData()
    {
        // Arrange
        var service = CreateService();
        var complexSettings = new AppSettings
        {
            General = new GeneralSettings
            {
                AutoStartWithWindows = true,
                MinimizeToTray = false,
                ActiveGameProfile = "TestGame"
            },
            MainUi = new MainUiSettings
            {
                PanelOpacity = 0.75,
                PanelSize = UiSize.Large,
                AlwaysOnTop = true,
                AutoHideWhenIdle = true,
                AutoHideDelaySeconds = 30
            },
            Theme = new ThemeSettings
            {
                AppTheme = UiTheme.Dark,
                EnableAnimations = true
            },
            Translation = new TranslationSettings(),
            Capture = new CaptureSettings(),
            Ocr = new OcrSettings(),
            Overlay = new OverlaySettings
            {
                IsEnabled = true,
                Opacity = 0.85
            },
            Advanced = new AdvancedSettings
            {
                EnableAdvancedFeatures = true,
                EnableProfiling = true
            }
        };

        // Gameプロファイルも追加
        complexSettings.GameProfiles["TestGame"] = new GameProfileSettings
        {
            GameName = "Test Game",
            ProcessName = "testgame.exe",
            IsEnabled = true
        };

        // Act
        await service.SetSettingsAsync(complexSettings);
        await service.ReloadAsync();
        var reloadedSettings = service.GetSettings();

        // Assert - すべてのプロパティが正確に保存・復元されること
        reloadedSettings.General.AutoStartWithWindows.Should().BeTrue();
        reloadedSettings.General.MinimizeToTray.Should().BeFalse();
        reloadedSettings.General.ActiveGameProfile.Should().Be("TestGame");
        
        reloadedSettings.MainUi.PanelOpacity.Should().Be(0.75);
        reloadedSettings.MainUi.PanelSize.Should().Be(UiSize.Large);
        reloadedSettings.MainUi.AlwaysOnTop.Should().BeTrue();
        reloadedSettings.MainUi.AutoHideWhenIdle.Should().BeTrue();
        reloadedSettings.MainUi.AutoHideDelaySeconds.Should().Be(30);
        
        reloadedSettings.Theme.AppTheme.Should().Be(UiTheme.Dark);
        reloadedSettings.Theme.EnableAnimations.Should().BeTrue();
        
        reloadedSettings.Translation.Should().NotBeNull();
        
        reloadedSettings.Capture.Should().NotBeNull();
        
        reloadedSettings.Ocr.Should().NotBeNull();
        
        reloadedSettings.Overlay.IsEnabled.Should().BeTrue();
        reloadedSettings.Overlay.Opacity.Should().Be(0.85);
        
        reloadedSettings.Advanced.EnableAdvancedFeatures.Should().BeTrue();
        reloadedSettings.Advanced.EnableProfiling.Should().BeTrue();

        // Gameプロファイルの確認
        reloadedSettings.GameProfiles.Should().ContainKey("TestGame");
        var gameProfile = reloadedSettings.GameProfiles["TestGame"];
        gameProfile.GameName.Should().Be("Test Game");
        gameProfile.ProcessName.Should().Be("testgame.exe");
        gameProfile.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task JsonDeserialization_CorruptedFile_HandlesGracefully()
    {
        // Arrange - Root cause solution: Create service first, then corrupt the file after initialization
        var tempFilePath = Path.Combine(Path.GetDirectoryName(_tempSettingsPath)!, $"corrupted_settings_{Guid.NewGuid():N}.json");
        
        // 最初に有効なJSONファイルを作成してサービスの初期化を成功させる
        await File.WriteAllTextAsync(tempFilePath, "{\"General\":{\"AutoStartWithWindows\":false}}");
        
        var service = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, tempFilePath);
        
        // 初期化が完了するまで少し待機
        await Task.Delay(100);
        
        // 初期化後にファイルを破損させる
        await File.WriteAllTextAsync(tempFilePath, "{ \"invalid\": json content without quotes }");

        // Act & Assert - 破損したファイルを読み込むときにJsonExceptionが発生することを確認
        await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => service.ReloadAsync());
    }

    #endregion

    #region エラーハンドリングテスト

    [Fact]
    public async Task SaveAsync_WithReadOnlyFile_HandlesIOException()
    {
        // Arrange - Root cause solution: Create service with known file path to ensure test consistency
        var readOnlyFilePath = Path.Combine(Path.GetDirectoryName(_tempSettingsPath)!, $"readonly_settings_{Guid.NewGuid():N}.json");
        
        // ファイルを作成して読み取り専用に設定
        await File.WriteAllTextAsync(readOnlyFilePath, "{}");
        File.SetAttributes(readOnlyFilePath, FileAttributes.ReadOnly);
        
        var service = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, readOnlyFilePath);
        var settings = new AppSettings();

        try
        {
            // Root cause solution: 初期化完了を待機
            await Task.Delay(100);
            
            // Act & Assert
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SetSettingsAsync(settings));
        }
        finally
        {
            // Cleanup - 読み取り専用属性を削除
            if (File.Exists(readOnlyFilePath))
            {
                File.SetAttributes(readOnlyFilePath, FileAttributes.Normal);
                File.Delete(readOnlyFilePath);
            }
        }
    }

    [Fact]
    public void SetValue_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        Assert.Throws<ArgumentException>(() => service.SetValue(null!, "value"));
    }

    [Fact]
    public async Task SetSettingsAsync_WithNullSettings_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SetSettingsAsync(null!));
    }

    [Fact]
    public async Task SaveGameProfileAsync_WithNullProfileId_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();
        var profile = new GameProfileSettings();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveGameProfileAsync(null!, profile));
    }

    [Fact]
    public async Task SaveGameProfileAsync_WithNullProfile_ThrowsArgumentNullException()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.SaveGameProfileAsync("test", null!));
    }

    #endregion

    #region 設定変更ロジックテスト

    [Fact]
    public async Task CategorySettings_GetAndSet_WorksCorrectly()
    {
        // Arrange
        var service = CreateService();
        var originalGeneralSettings = new GeneralSettings
        {
            AutoStartWithWindows = true,
            MinimizeToTray = false
        };

        // Act
        await service.SetCategorySettingsAsync(originalGeneralSettings);
        var retrievedSettings = service.GetCategorySettings<GeneralSettings>();

        // Assert
        retrievedSettings.AutoStartWithWindows.Should().BeTrue();
        retrievedSettings.MinimizeToTray.Should().BeFalse();
    }

    [Fact]
    public async Task GetAsync_WithValidType_ReturnsCorrectSettings()
    {
        // Arrange
        var service = CreateService();
        var themeSettings = new ThemeSettings
        {
            AppTheme = UiTheme.Light,
            EnableAnimations = false
        };

        await service.SetCategorySettingsAsync(themeSettings);

        // Act
        var result = await service.GetAsync<ThemeSettings>();

        // Assert
        result.Should().NotBeNull();
        result!.AppTheme.Should().Be(UiTheme.Light);
        result.EnableAnimations.Should().BeFalse();
    }

    [Fact]
    public async Task SaveAsync_GenericMethod_PersistsSettingsCorrectly()
    {
        // Arrange - 同じファイルパスを使用して永続化をテスト
        var sharedPath = Path.Combine(Path.GetDirectoryName(_tempSettingsPath)!, $"shared_settings_{Guid.NewGuid():N}.json");
        var service = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, sharedPath);
        var mainUiSettings = new MainUiSettings
        {
            PanelOpacity = 0.65,
            PanelSize = UiSize.Small,
            AlwaysOnTop = false
        };

        // Act
        await service.SaveAsync(mainUiSettings);

        // Root cause solution: 初期化が完了するまで待機
        await Task.Delay(200);

        // 別のサービスインスタンスで確認（同じファイルパスを使用）
        var verificationService = new EnhancedSettingsService(_mockLogger.Object, _mockMetadataService.Object, _mockMigrationManager.Object, sharedPath);
        
        // Root cause solution: 新しいサービスの初期化完了を待機
        await Task.Delay(200);
        
        // 明示的にファイルから再読み込みを実行
        await verificationService.ReloadAsync();
        var retrievedSettings = verificationService.GetCategorySettings<MainUiSettings>();

        // Assert
        retrievedSettings.PanelOpacity.Should().Be(0.65);
        retrievedSettings.PanelSize.Should().Be(UiSize.Small);
        retrievedSettings.AlwaysOnTop.Should().BeFalse();
    }

    [Fact]
    public async Task ResetToDefaultsAsync_ResetsAllSettings()
    {
        // Arrange
        var service = CreateService();
        
        // カスタム設定を適用
        var customSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = true, EnableDebugMode = true },
            MainUi = new MainUiSettings { PanelOpacity = 0.3 }
        };
        await service.SetSettingsAsync(customSettings);

        // Act
        await service.ResetToDefaultsAsync();

        // Assert
        var resetSettings = service.GetSettings();
        resetSettings.General.AutoStartWithWindows.Should().Be(new GeneralSettings().AutoStartWithWindows); // デフォルト値
        resetSettings.General.EnableDebugMode.Should().Be(new GeneralSettings().EnableDebugMode);
        resetSettings.MainUi.PanelOpacity.Should().Be(new MainUiSettings().PanelOpacity);
    }

    #endregion

    #region ゲームプロファイル管理テスト

    [Fact]
    public async Task GameProfile_FullLifecycle_WorksCorrectly()
    {
        // Arrange
        var service = CreateService();
        const string profileId = "LifecycleTestGame";
        
        var gameProfile = new GameProfileSettings
        {
            GameName = "Lifecycle Test Game",
            ProcessName = "lifecycle.exe",
            IsEnabled = true
        };

        // Act 1: プロファイル作成
        await service.SaveGameProfileAsync(profileId, gameProfile);

        // Assert 1: プロファイルが保存されること
        var savedProfile = service.GetGameProfile(profileId);
        savedProfile.Should().NotBeNull();
        savedProfile!.GameName.Should().Be("Lifecycle Test Game");
        savedProfile.ProcessName.Should().Be("lifecycle.exe");
        savedProfile.IsEnabled.Should().BeTrue();

        // Act 2: プロファイルをアクティブに設定
        await service.SetActiveGameProfileAsync(profileId);

        // Assert 2: アクティブプロファイルが設定されること
        var activeProfile = service.GetActiveGameProfile();
        activeProfile.Should().NotBeNull();
        activeProfile!.GameName.Should().Be("Lifecycle Test Game");

        // Act 3: 全プロファイル取得
        var allProfiles = service.GetAllGameProfiles();

        // Assert 3: プロファイルがリストに含まれること
        allProfiles.Should().ContainKey(profileId);
        allProfiles[profileId].GameName.Should().Be("Lifecycle Test Game");

        // Act 4: プロファイル削除
        await service.DeleteGameProfileAsync(profileId);

        // Assert 4: プロファイルが削除されること
        var deletedProfile = service.GetGameProfile(profileId);
        deletedProfile.Should().BeNull();
    }

    [Fact]
    public async Task SetActiveGameProfile_WithNull_ClearsActiveProfile()
    {
        // Arrange
        var service = CreateService();
        
        // 最初にプロファイルを設定
        var profileId = "TempProfile";
        var profile = new GameProfileSettings { GameName = "Temp Game" };
        await service.SaveGameProfileAsync(profileId, profile);
        await service.SetActiveGameProfileAsync(profileId);

        // Act
        await service.SetActiveGameProfileAsync(null);

        // Assert
        var activeProfile = service.GetActiveGameProfile();
        activeProfile.Should().BeNull();
    }

    #endregion

    #region パフォーマンステスト

    [Fact]
    public async Task Performance_MultipleSaveOperations_CompletesInReasonableTime()
    {
        // Arrange
        var service = CreateService();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 50回設定保存を実行
        for (int i = 0; i < 50; i++)
        {
            var settings = new AppSettings
            {
                General = new GeneralSettings { EnableDebugMode = i % 2 == 0 },
                MainUi = new MainUiSettings { PanelOpacity = 0.1 + (i * 0.01) }
            };
            
            await service.SetSettingsAsync(settings);
        }
        
        stopwatch.Stop();

        // Assert - 5秒以内で完了すること
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000);
    }

    [Fact]
    public void Performance_MultipleGetOperations_CompletesQuickly()
    {
        // Arrange
        var service = CreateService();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 1000回設定取得を実行
        for (int i = 0; i < 1000; i++)
        {
            _ = service.GetSettings();
            _ = service.GetCategorySettings<GeneralSettings>();
            _ = service.GetCategorySettings<MainUiSettings>();
        }
        
        stopwatch.Stop();

        // Assert - 1秒以内で完了すること
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
    }

    #endregion

    #region 境界値・例外テスト

    [Fact]
    public void GetValue_WithEmptyKey_HandlesGracefully()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.GetValue(string.Empty, "default");

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void HasValue_WithNonExistentKey_ReturnsFalse()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = service.HasValue("non_existent_key_12345");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void RemoveValue_WithNonExistentKey_DoesNotThrow()
    {
        // Arrange
        var service = CreateService();

        // Act & Assert
        var act = () => service.RemoveValue("non_existent_key_12345");
        act.Should().NotThrow();
    }

    #endregion

    public void Dispose()
    {
        // テスト用ファイルのクリーンアップ
        try
        {
            var directory = Path.GetDirectoryName(_tempSettingsPath);
            if (directory != null && Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
        catch (IOException)
        {
            // ファイルシステムエラーは無視
        }
        catch (UnauthorizedAccessException)
        {
            // アクセス権限エラーは無視
        }
    }
}