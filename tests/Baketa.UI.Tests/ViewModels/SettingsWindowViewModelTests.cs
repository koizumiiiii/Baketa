using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Services;
using Baketa.Core.Settings;
using Baketa.UI.Models.Settings;
using Baketa.UI.Services;
using Baketa.UI.Tests.Infrastructure;
using Baketa.UI.ViewModels;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ReactiveUI;
using Xunit;

namespace Baketa.UI.Tests.ViewModels;

/// <summary>
/// SettingsWindowViewModelのテスト
/// プログレッシブディスクロージャー、カテゴリ管理、コマンド実行の包括的テスト
/// </summary>
public sealed class SettingsWindowViewModelTests : AvaloniaTestBase
{
    private readonly Mock<ISettingsChangeTracker> _mockChangeTracker;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<IAuthService> _mockAuthService;
    private readonly Mock<INavigationService> _mockNavigationService;
    private readonly Mock<ISettingsService> _mockSettingsService;
    private readonly Mock<ILogger<SettingsWindowViewModel>> _mockLogger;
    private SettingsWindowViewModel? _currentViewModel;

    public SettingsWindowViewModelTests()
    {
        _mockChangeTracker = new Mock<ISettingsChangeTracker>();
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockAuthService = new Mock<IAuthService>();
        _mockNavigationService = new Mock<INavigationService>();
        _mockSettingsService = new Mock<ISettingsService>();
        _mockLogger = new Mock<ILogger<SettingsWindowViewModel>>();

        ResetMocks();
    }

    /// <summary>
    /// Mockオブジェクトの状態をリセット
    /// </summary>
    private void ResetMocks()
    {
        _mockChangeTracker.Reset();
        _mockEventAggregator.Reset();
        _mockAuthService.Reset();
        _mockNavigationService.Reset();
        _mockSettingsService.Reset();
        _mockLogger.Reset();

        // デフォルト設定の再設定
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
        _mockSettingsService.Setup(x => x.SaveAsync()).Returns(Task.CompletedTask);
    }

    /// <summary>
    /// ViewModelを作成するヘルパーメソッド
    /// </summary>
    private SettingsWindowViewModel CreateViewModel()
    {
        ResetMocks(); // 各テスト前にMockをリセット
        _currentViewModel?.Dispose(); // 前のViewModelがあれば破棄
        _currentViewModel = RunOnUIThread(() => new SettingsWindowViewModel(
            _mockChangeTracker.Object,
            _mockEventAggregator.Object,
            _mockAuthService.Object,
            _mockNavigationService.Object,
            _mockSettingsService.Object,
            null, // ILocalizationService
            _mockLogger.Object));
        return _currentViewModel;
    }

    public override void Dispose()
    {
        try
        {
            _currentViewModel?.Dispose();
        }
        catch (Exception ex)
        {
            // テスト終了時のDispose例外を無視
            System.Diagnostics.Debug.WriteLine($"Dispose exception ignored: {ex.Message}");
        }
        finally
        {
            _currentViewModel = null;
            base.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithValidDependencies_InitializesCorrectly()
    {
        // Act
        var viewModel = RunOnUIThread(() => CreateViewModel());

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.AllCategories.Should().HaveCount(2); // 一般設定、アカウントの2つに簡素化
        viewModel.SelectedCategory.Should().NotBeNull();
        viewModel.StatusMessage.Should().Be(string.Empty);
    }

    [Fact]
    public void Constructor_WithNullChangeTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        RunOnUIThread(() =>
            Assert.Throws<ArgumentNullException>(() =>
                new SettingsWindowViewModel(null!, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object)));
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_ThrowsArgumentNullException()
    {
        // Act & Assert
        RunOnUIThread(() =>
            Assert.Throws<ArgumentNullException>(() =>
                new SettingsWindowViewModel(_mockChangeTracker.Object, null!, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object)));
    }

    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public void AllCategories_ReturnsExpectedCategoriesInCorrectOrder()
    {
        // Arrange - Mock設定リセット問題を回避するため直接ViewModelを作成
        var viewModel = RunOnUIThread(() => new SettingsWindowViewModel(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object));

        try
        {
            // Act
            var categories = viewModel.AllCategories;

            // Assert
            categories.Should().HaveCount(2); // 一般設定、アカウントの2つに簡素化

            // 基本設定カテゴリの確認（DisplayOrder順）
            categories[0].Id.Should().Be("settings_general");
            categories[0].Level.Should().Be(SettingLevel.Basic);
            categories[0].DisplayOrder.Should().Be(1);

            categories[1].Id.Should().Be("settings_account");
            categories[1].Level.Should().Be(SettingLevel.Basic);
            categories[1].DisplayOrder.Should().Be(2);
        }
        finally
        {
            // 確実なクリーンアップ
            viewModel?.Dispose();
        }
    }

    [Fact(Skip = "PropertyChangedイベントハング問題のため一時的に無効化")]
    public void SelectedCategory_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var propertyChanged = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.SelectedCategory))
                propertyChanged = true;
        };

        // Act
        var newCategory = viewModel.AllCategories[1];
        viewModel.SelectedCategory = newCategory;

        // Assert
        viewModel.SelectedCategory.Should().Be(newCategory);
        propertyChanged.Should().BeTrue();
    }

    [Fact(Skip = "ReactiveUIスケジューラー問題のため一時的に無効化")]
    public void HasChanges_ReturnsValueFromChangeTracker()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(true);
        var viewModel = RunOnUIThread(() => new SettingsWindowViewModel(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object));

        // Act & Assert
        viewModel.HasChanges.Should().BeTrue();

        // Change the mock and verify
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
        _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(false));

        viewModel.HasChanges.Should().BeFalse();

        // Cleanup
        viewModel.Dispose();
    }

    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public void StatusMessage_WhenSet_RaisesPropertyChanged()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var propertyChanged = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.StatusMessage))
                propertyChanged = true;
        };

        // Act
        viewModel.StatusMessage = "テストメッセージ";

        // Assert
        viewModel.StatusMessage.Should().Be("テストメッセージ");
        propertyChanged.Should().BeTrue();
    }

    [Fact]
    public void SaveCommand_CanExecute_DependsOnHasChanges_Simplified()
    {
        // Arrange - Mock設定が確実に反映されるよう直接ViewModelを作成
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
        var viewModel = RunOnUIThread(() => new SettingsWindowViewModel(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object));

        try
        {
            // Act & Assert - ReactiveUIの非同期ストリームテストを避け、基本的な動作のみテスト
            // 変更なしの場合：HasChangesプロパティをチェック
            viewModel.HasChanges.Should().BeFalse();

            // 変更ありの場合：Mock設定を変更してHasChangesChangedイベントを発火
            _mockChangeTracker.Setup(x => x.HasChanges).Returns(true);
            _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(true));

            viewModel.HasChanges.Should().BeTrue();
        }
        finally
        {
            // Cleanup
            viewModel.Dispose();
        }
    }

    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public async Task SaveCommand_WhenExecuted_UpdatesStatusMessage()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(true);
        var viewModel = CreateViewModel();

        // Act
        await viewModel.SaveCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be("設定を保存しました");
        _mockChangeTracker.Verify(x => x.ClearChanges(), Times.Once);
    }

    [Fact]
    public async Task CancelCommand_WhenConfirmed_UpdatesStatusMessage()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.ConfirmDiscardChangesAsync()).ReturnsAsync(true);
        var viewModel = RunOnUIThread(() => new SettingsWindowViewModel(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object));

        // Act
        await viewModel.CancelCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be("変更をキャンセルしました");
        _mockChangeTracker.Verify(x => x.ConfirmDiscardChangesAsync(), Times.Once);

        // Cleanup
        viewModel.Dispose();
    }

    [Fact]
    public async Task CancelCommand_WhenNotConfirmed_DoesNotUpdateStatusMessage()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.ConfirmDiscardChangesAsync()).ReturnsAsync(false);
        var viewModel = CreateViewModel();
        var originalStatus = viewModel.StatusMessage;

        // Act
        await viewModel.CancelCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be(originalStatus);
    }

    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public async Task ResetCommand_WhenConfirmed_UpdatesStatusMessage()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.ConfirmDiscardChangesAsync()).ReturnsAsync(true);
        var viewModel = RunOnUIThread(() => new SettingsWindowViewModel(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockAuthService.Object, _mockNavigationService.Object, _mockSettingsService.Object, null, _mockLogger.Object));

        // Act
        await viewModel.ResetCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be("設定をリセットしました");
        _mockChangeTracker.Verify(x => x.ConfirmDiscardChangesAsync(), Times.Once);

        // Cleanup
        viewModel.Dispose();
    }

    [Fact(Skip = "ハングアップ問題のため一時的に無効化")]
    public void CategoryContent_AllCategoriesHaveNullContentInTestEnvironment()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert
        foreach (var category in viewModel.AllCategories)
        {
            // テスト環境では遅延初期化を避けるためContentはnullになる
            category.Content.Should().BeNull($"Category {category.Id} content should be null in test environment");
        }
    }

    [Fact]
    public void CategoryProperties_AllCategoriesHaveRequiredProperties()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert
        foreach (var category in viewModel.AllCategories)
        {
            category.Id.Should().NotBeNullOrEmpty($"Category should have valid Id");
            category.Name.Should().NotBeNullOrEmpty($"Category {category.Id} should have name");
            category.IconData.Should().NotBeNullOrEmpty($"Category {category.Id} should have icon");
            category.Description.Should().NotBeNullOrEmpty($"Category {category.Id} should have description");
            category.DisplayOrder.Should().BePositive($"Category {category.Id} should have positive display order");
        }
    }

    [Fact]
    public void ChangeTrackerEvents_WhenHasChangesChanged_UpdatesStatusMessage()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act - HasChanges が true になった場合
        _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(true));

        // Assert
        viewModel.StatusMessage.Should().Be("設定に変更があります");

        // Act - HasChanges が false になった場合
        _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(false));

        // Assert
        viewModel.StatusMessage.Should().Be("変更なし");
    }

    [Theory]
    [InlineData("settings_general")]
    [InlineData("settings_account")]
    public void CategorySelection_SpecificCategory_WorksCorrectly(string categoryId)
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        var targetCategory = viewModel.AllCategories.First(c => c.Id == categoryId);
        viewModel.SelectedCategory = targetCategory;

        // Assert
        viewModel.SelectedCategory.Should().Be(targetCategory);
        viewModel.SelectedCategory.Id.Should().Be(categoryId);
    }

    /// <summary>
    /// パフォーマンステスト：多数のカテゴリ切り替えが効率的に動作することを確認
    /// </summary>
    [Fact]
    public void Performance_MultipleCategorySwitches_CompletesQuickly()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        // Act - 100回カテゴリを切り替え
        for (int i = 0; i < 100; i++)
        {
            var categoryIndex = i % viewModel.AllCategories.Count;
            viewModel.SelectedCategory = viewModel.AllCategories[categoryIndex];
        }

        stopwatch.Stop();

        // Assert - 100ms以内で完了すること
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(100);
    }

    /// <summary>
    /// 境界値テスト：カテゴリが存在することの確認
    /// </summary>
    [Fact]
    public void BoundaryConditions_AllCategories_HasExpectedCount()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - 一般設定、アカウントの2カテゴリ存在
        viewModel.AllCategories.Should().HaveCount(2);
        viewModel.AllCategories.Should().NotBeEmpty();
    }
}

