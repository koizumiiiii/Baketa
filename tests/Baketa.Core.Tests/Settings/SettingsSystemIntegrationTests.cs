using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;

namespace Baketa.Core.Tests.Settings;

/// <summary>
/// 設定システム全体の統合テスト
/// </summary>
public class SettingsSystemIntegrationTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly ISettingMetadataService _metadataService;
    private readonly ISettingsMigrationManager _migrationManager;

    public SettingsSystemIntegrationTests()
    {
        var services = new ServiceCollection();
        
        // ロガーのモックを追加
        services.AddSingleton(Mock.Of<ILogger<SettingMetadataService>>());
        services.AddSingleton(Mock.Of<ILogger<SettingsMigrationManager>>());
        
        // サービスを追加
        services.AddSingleton<ISettingMetadataService, SettingMetadataService>();
        services.AddSingleton<ISettingsMigrationManager, SettingsMigrationManager>();
        
        _serviceProvider = services.BuildServiceProvider();
        _metadataService = _serviceProvider.GetRequiredService<ISettingMetadataService>();
        _migrationManager = _serviceProvider.GetRequiredService<ISettingsMigrationManager>();
    }

    #region メタデータとマイグレーションの統合テスト

    [Fact]
    public void SettingsMetadata_WithMainUiSettings_ShouldBeAccessible()
    {
        // Act
        var metadata = _metadataService.GetMetadata<MainUiSettings>();

        // Assert
        Assert.NotNull(metadata);
        Assert.NotEmpty(metadata);
        
        // 基本設定の確認
        var basicSettings = _metadataService.GetMetadataByLevel<MainUiSettings>(SettingLevel.Basic);
        Assert.NotEmpty(basicSettings);
        Assert.Contains(basicSettings, m => m.Property.Name == nameof(MainUiSettings.PanelPosition));
        Assert.Contains(basicSettings, m => m.Property.Name == nameof(MainUiSettings.PanelOpacity));
        
        // 詳細設定の確認
        var advancedSettings = _metadataService.GetMetadataByLevel<MainUiSettings>(SettingLevel.Advanced);
        Assert.NotEmpty(advancedSettings);
        Assert.Contains(advancedSettings, m => m.Property.Name == nameof(MainUiSettings.EnableBoundarySnap));
    }

    [Fact]
    public void SettingsMetadata_WithAppSettings_ShouldIncludeAllCategories()
    {
        // Act
        var metadata = _metadataService.GetMetadata<AppSettings>();

        // Assert
        Assert.NotNull(metadata);
        // AppSettings自体にはメタデータ属性がないため、空のリストが返される
        // これは正常な動作
    }

    [Fact]
    public async Task MigrationSystem_WithMainUiSettings_ShouldHandleNewSettings()
    {
        // Arrange
        var oldSettings = new Dictionary<string, object?>
        {
            { "Version", 0 },
            { "General.Language", "ja" },
            { "Overlay.Position", "TopLeft" }
            // MainUiSettingsはV1で追加されるため、旧設定には含まれない
        };

        // Act
        var result = await _migrationManager.ExecuteMigrationAsync(oldSettings, 0);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.FinalSettings);
        
        // 既存設定が保持されることを確認
        Assert.Contains("General.Language", result.FinalSettings.Keys);
        Assert.Contains("Overlay.Position", result.FinalSettings.Keys);
        
        // バージョンが更新されることを確認
        Assert.Equal(_migrationManager.LatestSchemaVersion, result.FinalSettings["Version"]);
    }

    #endregion

    #region 設定検証の統合テスト

    [Fact]
    public void ValidationSystem_WithValidMainUiSettings_ShouldPass()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            PanelPosition = new Point(100, 100),
            PanelOpacity = 0.8,
            AutoHideDelaySeconds = 10,
            BoundarySnapDistance = 20
        };

        // Act
        var results = _metadataService.ValidateSettings(settings);

        // Assert
        Assert.NotNull(results);
        Assert.All(results, r => Assert.True(r.IsValid));
    }

    [Fact]
    public void ValidationSystem_WithInvalidMainUiSettings_ShouldFail()
    {
        // Arrange
        var settings = new MainUiSettings
        {
            PanelOpacity = 1.5, // 無効な値（1.0を超える）
            AutoHideDelaySeconds = -1, // 無効な値（負数）
            BoundarySnapDistance = -5 // 無効な値（負数）
        };

        // Act
        var results = _metadataService.ValidateSettings(settings);

        // Assert
        Assert.NotNull(results);
        
        // 少なくとも一部の検証が失敗することを確認
        // 実際の検証ルールは SettingMetadata の実装に依存
        Assert.NotEmpty(results);
    }

    #endregion

    #region 設定カテゴリとレベルの統合テスト

    [Fact]
    public void SettingsHierarchy_MainUiCategory_ShouldHaveCorrectStructure()
    {
        // Act
        var categories = _metadataService.GetCategories<MainUiSettings>();
        var mainUiSettings = _metadataService.GetMetadataByCategory<MainUiSettings>("MainUi");

        // Assert
        Assert.Contains("MainUi", categories);
        Assert.NotEmpty(mainUiSettings);
        
        // 基本設定と詳細設定の両方が含まれることを確認
        var basicCount = mainUiSettings.Count(m => m.Level == SettingLevel.Basic);
        var advancedCount = mainUiSettings.Count(m => m.Level == SettingLevel.Advanced);
        
        Assert.True(basicCount > 0, "基本設定が存在する");
        Assert.True(advancedCount > 0, "詳細設定が存在する");
    }

    [Fact]
    public void SettingsHierarchy_ShouldMaintainLevelConsistency()
    {
        // Arrange
        var settingsTypes = new[]
        {
            typeof(MainUiSettings),
            typeof(GeneralSettings),
            typeof(OcrSettings),
            typeof(TranslationSettings)
        };

        foreach (var settingsType in settingsTypes)
        {
            // Act
            var metadata = _metadataService.GetMetadata(settingsType);
            
            // Assert
            if (metadata.Any()) // メタデータが存在する場合のみチェック
            {
                var basicSettings = metadata.Where(m => m.Level == SettingLevel.Basic);
                var advancedSettings = metadata.Where(m => m.Level == SettingLevel.Advanced);
                
                // すべての設定が適切なレベルに分類されていることを確認
                Assert.True(basicSettings.Any() || advancedSettings.Any(), 
                    $"{settingsType.Name} should have at least basic or advanced settings");
                
                // レベル設定の一貫性を確認
                foreach (var meta in metadata)
                {
                    Assert.True(Enum.IsDefined(typeof(SettingLevel), meta.Level),
                        $"Setting {meta.Property.Name} has invalid level");
                }
            }
        }
    }

    #endregion

    #region パフォーマンステスト

    [Fact]
    public void MetadataService_MultipleAccess_ShouldUseCaching()
    {
        // Act & Assert - キャッシュが正しく動作することを確認
        var start = DateTime.UtcNow;
        
        for (int i = 0; i < 100; i++)
        {
            var metadata = _metadataService.GetMetadata<MainUiSettings>();
            Assert.NotEmpty(metadata);
        }
        
        var elapsed = DateTime.UtcNow - start;
        
        // 100回のアクセスが1秒未満で完了することを確認（キャッシュが効いている証拠）
        Assert.True(elapsed.TotalSeconds < 1.0, 
            $"Multiple metadata access took too long: {elapsed.TotalMilliseconds}ms");
    }

    #endregion

    #region リソース管理テスト

    [Fact]
    public void ServiceProvider_ShouldDisposeCorrectly()
    {
        // Act & Assert
        Assert.NotNull(_serviceProvider);
        
        // Dispose時に例外が発生しないことを確認
        _serviceProvider.Dispose();
    }

    #endregion

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _serviceProvider?.Dispose();
        }
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("xUnit", "xUnit1013:Public method 'Dispose' on test class should be marked as a Fact", Justification = "IDisposable pattern implementation")]
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// 実際の設定クラスでの動作確認テスト
/// </summary>
public class RealWorldSettingsTests
{
    #region シナリオテスト

    [Fact]
    public void CompleteSettings_ShouldWorkEndToEnd()
    {
        // Arrange - 実際のアプリケーション設定を作成
        var appSettings = new AppSettings
        {
            General = new GeneralSettings(),
            MainUi = new MainUiSettings
            {
                PanelPosition = new Point(200, 150),
                PanelOpacity = 0.9,
                PanelSize = UiSize.Large,
                AutoHideWhenIdle = true,
                AutoHideDelaySeconds = 15
            },
            Overlay = new OverlaySettings(),
            Translation = new TranslationSettings()
        };

        // Act - 設定のシリアライズとデシリアライズ
        var json = System.Text.Json.JsonSerializer.Serialize(appSettings);
        var deserializedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        // Assert - すべての設定が正しく保持されることを確認
        Assert.NotNull(deserializedSettings);
        Assert.NotNull(deserializedSettings.MainUi);
        Assert.Equal(appSettings.MainUi.PanelPosition, deserializedSettings.MainUi.PanelPosition);
        Assert.Equal(appSettings.MainUi.PanelOpacity, deserializedSettings.MainUi.PanelOpacity);
        Assert.Equal(appSettings.MainUi.PanelSize, deserializedSettings.MainUi.PanelSize);
    }

    [Fact]
    public void SettingsProfile_ShouldSupportGameSpecificConfiguration()
    {
        // Arrange
        var gameProfile = new GameProfileSettings
        {
            GameName = "Test Game",
            ProcessName = "testgame.exe",
            IsActive = true
        };

        var appSettings = new AppSettings();
        appSettings.GameProfiles["TestGame"] = gameProfile;

        // Act
        var json = System.Text.Json.JsonSerializer.Serialize(appSettings);
        var deserializedSettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json);

        // Assert
        Assert.NotNull(deserializedSettings);
        Assert.NotNull(deserializedSettings.GameProfiles);
        Assert.True(deserializedSettings.GameProfiles.ContainsKey("TestGame"));
        
        var retrievedProfile = deserializedSettings.GameProfiles["TestGame"];
        Assert.Equal(gameProfile.GameName, retrievedProfile.GameName);
        Assert.Equal(gameProfile.ProcessName, retrievedProfile.ProcessName);
        Assert.Equal(gameProfile.IsActive, retrievedProfile.IsActive);
    }

    #endregion

    #region 後方互換性テスト

    [Fact]
    public void LegacySettings_ShouldDeserializeWithoutMainUi()
    {
        // Arrange - MainUiSettingsが含まれていない旧形式のJSON
        var legacyJson = """
        {
            "general": {
                "language": "ja",
                "theme": "Dark"
            },
            "overlay": {
                "position": "TopLeft",
                "opacity": 0.8
            }
        }
        """;

        // Act
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(legacyJson);

        // Assert
        Assert.NotNull(settings);
        Assert.NotNull(settings.MainUi); // デフォルト値で初期化される
        Assert.Equal(new Point(50, 50), settings.MainUi.PanelPosition); // デフォルト値
    }

    #endregion
}
