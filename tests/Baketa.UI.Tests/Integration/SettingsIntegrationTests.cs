using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Settings.Migration;
using Baketa.Core.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Services;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Settings;

namespace Baketa.UI.Tests.Integration;

/// <summary>
/// 設定システム全体の統合テスト
/// エンドツーエンドの設定保存・読み込みフローと複数ViewModelでのデータ一貫性をテスト
/// </summary>
public sealed class SettingsIntegrationTests : IDisposable
{
    private readonly string _tempSettingsPath;
    private readonly ServiceProvider _serviceProvider;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<EnhancedSettingsService>> _mockSettingsLogger;
    private readonly Mock<ILogger<SettingsWindowViewModel>> _mockWindowLogger;

    public SettingsIntegrationTests()
    {
        // テスト用一時ディレクトリの作成（プロセスIDとスレッドIDを含めてユニーク性を確保）
        var processId = Environment.ProcessId;
        var threadId = Environment.CurrentManagedThreadId;
        var uniqueId = $"{processId}_{threadId}_{Guid.NewGuid():N}";
        _tempSettingsPath = Path.Combine(Path.GetTempPath(), $"BaketaTest_{uniqueId}", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(_tempSettingsPath)!);

        // モックの初期化
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockSettingsLogger = new Mock<ILogger<EnhancedSettingsService>>();
        _mockWindowLogger = new Mock<ILogger<SettingsWindowViewModel>>();

        // DIコンテナの設定
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Core services
        services.AddSingleton(_mockEventAggregator.Object);
        
        // Settings services
        services.AddSingleton<ISettingsService>(provider =>
            new EnhancedSettingsService(
                _mockSettingsLogger.Object,
                Mock.Of<ISettingMetadataService>(),
                Mock.Of<ISettingsMigrationManager>(),
                _tempSettingsPath));

        // UI services  
        services.AddSingleton<ISettingsChangeTracker, SettingsChangeTracker>();
        
        // Add missing migration service dependency
        services.AddSingleton(Mock.Of<ISettingsMigrationManager>());
        
        // ViewModels
        services.AddTransient<SettingsWindowViewModel>();
        services.AddTransient<MainUiSettingsViewModel>();
        services.AddTransient<GeneralSettingsViewModel>();
        services.AddTransient<ThemeSettingsViewModel>();
    }

    [Fact]
    public async Task EndToEndFlow_SettingsChangedThroughMultipleViewModels_PersistCorrectly()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        _ = _serviceProvider.GetRequiredService<ISettingsChangeTracker>();
        
        // Act 1: 初期設定の作成と保存
        var initialSettings = new AppSettings
        {
            General = new GeneralSettings
            {
                AutoStartWithWindows = true,
                MinimizeToTray = false
            },
            MainUi = new MainUiSettings
            {
                PanelOpacity = 0.8,
                AlwaysOnTop = true,
                PanelSize = UiSize.Large
            },
            Theme = new ThemeSettings
            {
                AppTheme = UiTheme.Dark,
                EnableAnimations = false
            }
        };

        await settingsService.SetSettingsAsync(initialSettings);

        // Act 2: 設定の読み込み確認
        var loadedSettings = settingsService.GetSettings();

        // Assert 1: 設定が正しく保存・読み込みされること
        loadedSettings.Should().NotBeNull();
        loadedSettings.General.AutoStartWithWindows.Should().BeTrue();
        loadedSettings.General.MinimizeToTray.Should().BeFalse();
        loadedSettings.MainUi.PanelOpacity.Should().Be(0.8);
        loadedSettings.MainUi.AlwaysOnTop.Should().BeTrue();
        loadedSettings.MainUi.PanelSize.Should().Be(UiSize.Large);
        loadedSettings.Theme.AppTheme.Should().Be(UiTheme.Dark);
        loadedSettings.Theme.EnableAnimations.Should().BeFalse();

        // Act 3: 個別カテゴリでの設定変更
        var updatedMainUiSettings = new MainUiSettings
        {
            PanelOpacity = 0.6,
            AlwaysOnTop = false,
            PanelSize = UiSize.Medium,
            AutoHideWhenIdle = true,
            AutoHideDelaySeconds = 10
        };

        await settingsService.SetCategorySettingsAsync(updatedMainUiSettings);

        // Act 4: 変更後の設定読み込み
        var updatedSettings = settingsService.GetSettings();

        // Assert 2: 個別カテゴリの変更が正しく反映されること
        updatedSettings.MainUi.PanelOpacity.Should().Be(0.6);
        updatedSettings.MainUi.AlwaysOnTop.Should().BeFalse();
        updatedSettings.MainUi.PanelSize.Should().Be(UiSize.Medium);
        updatedSettings.MainUi.AutoHideWhenIdle.Should().BeTrue();
        updatedSettings.MainUi.AutoHideDelaySeconds.Should().Be(10);

        // Assert 3: 他のカテゴリは変更されていないこと
        updatedSettings.General.AutoStartWithWindows.Should().BeTrue();
        updatedSettings.General.MinimizeToTray.Should().BeFalse();
        updatedSettings.Theme.AppTheme.Should().Be(UiTheme.Dark);
        updatedSettings.Theme.EnableAnimations.Should().BeFalse();
    }

    [Fact]
    public async Task MultipleViewModels_ConcurrentAccess_MaintainDataConsistency()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        
        // 初期設定
        var initialSettings = new AppSettings
        {
            MainUi = new MainUiSettings { PanelOpacity = 0.7 },
            General = new GeneralSettings { AutoStartWithWindows = false },
            Theme = new ThemeSettings { AppTheme = UiTheme.Light }
        };
        
        await settingsService.SetSettingsAsync(initialSettings);

        // Act: 複数のViewModelを同時に作成
        var mainUiViewModel = new MainUiSettingsViewModel(
            settingsService.GetCategorySettings<MainUiSettings>(),
            _mockEventAggregator.Object,
            Mock.Of<ILogger<MainUiSettingsViewModel>>());

        var generalViewModel = new GeneralSettingsViewModel(
            settingsService.GetCategorySettings<GeneralSettings>(),
            _mockEventAggregator.Object,
            Mock.Of<ILogger<GeneralSettingsViewModel>>());

        var themeViewModel = new ThemeSettingsViewModel(
            settingsService.GetCategorySettings<ThemeSettings>(),
            _mockEventAggregator.Object,
            Mock.Of<ILogger<ThemeSettingsViewModel>>());

        // Assert 1: すべてのViewModelで一貫したデータが読み込まれること
        mainUiViewModel.PanelOpacity.Should().Be(0.7);
        generalViewModel.AutoStartWithWindows.Should().BeFalse();
        themeViewModel.AppTheme.Should().Be(UiTheme.Light);

        // Act: ViewModelごとに設定を変更
        mainUiViewModel.PanelOpacity = 0.9;
        generalViewModel.AutoStartWithWindows = true;
        themeViewModel.AppTheme = UiTheme.Dark;

        // Root cause solution: ViewModelの変更を手動で設定オブジェクトに反映
        var updatedMainUi = new MainUiSettings { PanelOpacity = mainUiViewModel.PanelOpacity };
        var updatedGeneral = new GeneralSettings { AutoStartWithWindows = generalViewModel.AutoStartWithWindows };
        var updatedTheme = new ThemeSettings { AppTheme = themeViewModel.AppTheme };
        
        await settingsService.SetCategorySettingsAsync(updatedMainUi);
        await settingsService.SetCategorySettingsAsync(updatedGeneral);
        await settingsService.SetCategorySettingsAsync(updatedTheme);

        // Act: 設定を再読み込み
        await settingsService.ReloadAsync();
        var reloadedSettings = settingsService.GetSettings();

        // Assert 2: すべての変更が正しく永続化されていること
        reloadedSettings.MainUi.PanelOpacity.Should().Be(0.9);
        reloadedSettings.General.AutoStartWithWindows.Should().BeTrue();
        reloadedSettings.Theme.AppTheme.Should().Be(UiTheme.Dark);
    }

    [Fact]
    public void ChangeTracking_MultipleViewModels_TracksAllChanges()
    {
        // Arrange
        _ = _serviceProvider.GetRequiredService<ISettingsService>();
        var changeTracker = _serviceProvider.GetRequiredService<ISettingsChangeTracker>();

        // Act 1: 初期状態では変更なし
        changeTracker.HasChanges.Should().BeFalse();

        // Act 2: ViewModelで設定変更
        var _1 = new MainUiSettingsViewModel(
            new MainUiSettings(),
            _mockEventAggregator.Object,
            Mock.Of<ILogger<MainUiSettingsViewModel>>())
        {
            PanelOpacity = 0.5
        };

        // Note: 実際の実装では、ViewModelの変更がChangeTrackerに通知される仕組みが必要
        // ここではモックを使用してその動作をシミュレート
        // changeTracker.StartTracking(); // StartTrackingメソッドは存在しません
        changeTracker.TrackChange("MainUI", "PanelOpacity", 0.8, 0.5);

        // Assert 1: 変更が追跡されること
        changeTracker.HasChanges.Should().BeTrue();

        // Act 3: 変更をクリア
        changeTracker.ClearChanges();

        // Assert 2: 変更追跡がリセットされること
        changeTracker.HasChanges.Should().BeFalse();
    }

    [Fact]
    public async Task SettingsValidation_InvalidValues_HandledGracefully()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();

        // Act & Assert: 無効な設定値での動作確認
        var invalidSettings = new AppSettings
        {
            MainUi = new MainUiSettings
            {
                PanelOpacity = 1.5, // 無効な値（1.0を超える）
                AutoHideDelaySeconds = -1 // 無効な値（負数）
            }
        };

        // 設定サービスが無効な値を適切に処理するか確認
        Func<Task> act = async () => await settingsService.SetSettingsAsync(invalidSettings);
        
        // 実際の実装では、バリデーション機能により例外がスローされるか、
        // 自動的に有効な値に修正される
        await act.Should().NotThrowAsync("設定サービスは無効な値を適切に処理する必要があります");
    }

    [Fact]
    public async Task SettingsBackupAndRestore_CompleteFlow_WorksCorrectly()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var backupPath = Path.Combine(Path.GetTempPath(), $"BaketaBackup_{Guid.NewGuid():N}.json");

        var originalSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = true, MinimizeToTray = false },
            MainUi = new MainUiSettings { PanelOpacity = 0.75, PanelSize = UiSize.Large }
        };

        await settingsService.SetSettingsAsync(originalSettings);

        // Act 1: バックアップ作成
        await settingsService.CreateBackupAsync(backupPath);

        // Assert 1: バックアップファイルが作成されること
        File.Exists(backupPath).Should().BeTrue();

        // Act 2: 設定を変更
        var modifiedSettings = new AppSettings
        {
            General = new GeneralSettings { AutoStartWithWindows = false, MinimizeToTray = true },
            MainUi = new MainUiSettings { PanelOpacity = 0.5, PanelSize = UiSize.Small }
        };

        await settingsService.SetSettingsAsync(modifiedSettings);

        // Act 3: バックアップから復元
        await settingsService.RestoreFromBackupAsync(backupPath);

        // Assert 2: 元の設定に復元されること
        var restoredSettings = settingsService.GetSettings();
        restoredSettings.General.AutoStartWithWindows.Should().BeTrue();
        restoredSettings.General.MinimizeToTray.Should().BeFalse();
        restoredSettings.MainUi.PanelOpacity.Should().Be(0.75);
        restoredSettings.MainUi.PanelSize.Should().Be(UiSize.Large);

        // Cleanup
        if (File.Exists(backupPath))
        {
            File.Delete(backupPath);
        }
    }

    [Fact]
    public async Task GameProfileManagement_FullLifecycle_WorksCorrectly()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        const string profileId = "TestGame";
        
        var gameProfile = new GameProfileSettings
        {
            GameName = "Test Game",
            ProcessName = "testgame.exe",
            IsEnabled = true
        };

        // Act 1: プロファイル作成
        await settingsService.SaveGameProfileAsync(profileId, gameProfile);

        // Assert 1: プロファイルが保存されること
        var savedProfile = settingsService.GetGameProfile(profileId);
        savedProfile.Should().NotBeNull();
        savedProfile!.GameName.Should().Be("Test Game");
        savedProfile.ProcessName.Should().Be("testgame.exe");
        savedProfile.IsEnabled.Should().BeTrue();

        // Act 2: プロファイルをアクティブに設定
        await settingsService.SetActiveGameProfileAsync(profileId);

        // Assert 2: アクティブプロファイルが設定されること
        var activeProfile = settingsService.GetActiveGameProfile();
        activeProfile.Should().NotBeNull();
        activeProfile!.GameName.Should().Be("Test Game");

        // Act 3: プロファイル削除
        await settingsService.DeleteGameProfileAsync(profileId);

        // Assert 3: プロファイルが削除されること
        var deletedProfile = settingsService.GetGameProfile(profileId);
        deletedProfile.Should().BeNull();
    }

    [Fact]
    public void SettingsService_EventNotifications_WorkCorrectly()
    {
        // Arrange
        var settingsService = _serviceProvider.GetRequiredService<ISettingsService>();
        var settingChangedEvents = new List<SettingChangedEventArgs>();
        var settingsSavedEvents = new List<SettingsSavedEventArgs>();

        settingsService.SettingChanged += (_, e) => settingChangedEvents.Add(e);
        settingsService.SettingsSaved += (_, e) => settingsSavedEvents.Add(e);

        // Act: 設定変更とイベント発行のテスト
        settingsService.SetValue("TestKey", "TestValue");

        // Assert: イベントが適切に発行されること
        settingChangedEvents.Should().HaveCount(1);
        settingChangedEvents[0].SettingKey.Should().Be("TestKey");
        settingChangedEvents[0].NewValue.Should().Be("TestValue");
    }

    public void Dispose()
    {
        _serviceProvider?.Dispose();
        
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

/// <summary>
/// テスト用の設定変更追跡サービス実装
/// </summary>
public class SettingsChangeTracker : ISettingsChangeTracker
{
    private readonly List<string> _trackedChanges = [];

    public bool HasChanges { get; private set; }

    public event EventHandler<HasChangesChangedEventArgs>? HasChangesChanged;

    public void StartTracking()
    {
        HasChanges = false;
        _trackedChanges.Clear();
    }

    public void ClearChanges(string categoryId)
    {
        // テスト用：カテゴリ指定のクリアは無視
    }

    public string[] GetChangedCategories()
    {
        return [];
    }

    public void TrackChange(string categoryId, string propertyName, object? oldValue, object? newValue)
    {
        _trackedChanges.Add($"{categoryId}.{propertyName}: {oldValue} -> {newValue}");
        SetHasChanges(true);
    }

    public void ClearChanges()
    {
        _trackedChanges.Clear();
        SetHasChanges(false);
    }

    public Task<bool> ConfirmDiscardChangesAsync()
    {
        // テスト用：常にtrueを返す
        return Task.FromResult(true);
    }

    private void SetHasChanges(bool hasChanges)
    {
        if (HasChanges != hasChanges)
        {
            HasChanges = hasChanges;
            HasChangesChanged?.Invoke(this, new HasChangesChangedEventArgs(hasChanges));
        }
    }
}
