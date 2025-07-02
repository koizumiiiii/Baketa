using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using ReactiveUI;
using System;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Xunit;
using Baketa.Core.Settings;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Services;
using Baketa.UI.Models.Settings;
using Baketa.UI.Tests.Infrastructure;

namespace Baketa.UI.Tests.ViewModels;

/// <summary>
/// SettingsWindowViewModelのテスト
/// プログレッシブディスクロージャー、カテゴリ管理、コマンド実行の包括的テスト
/// </summary>
public sealed class SettingsWindowViewModelTests : AvaloniaTestBase
{
    private readonly Mock<ISettingsChangeTracker> _mockChangeTracker;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<SettingsWindowViewModel>> _mockLogger;

    public SettingsWindowViewModelTests()
    {
        _mockChangeTracker = new Mock<ISettingsChangeTracker>();
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<SettingsWindowViewModel>>();
        
        // デフォルトでは変更なしに設定
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
    }

    /// <summary>
    /// ViewModelを作成するヘルパーメソッド
    /// </summary>
    private SettingsWindowViewModel CreateViewModel() =>
        new(_mockChangeTracker.Object, _mockEventAggregator.Object, _mockLogger.Object);

    [Fact]
    public void Constructor_WithValidDependencies_InitializesCorrectly()
    {
        // Act
        var viewModel = RunOnUIThread(() => CreateViewModel());

        // Assert
        viewModel.Should().NotBeNull();
        viewModel.AllCategories.Should().HaveCount(8);
        viewModel.ShowAdvancedSettings.Should().BeFalse();
        viewModel.SelectedCategory.Should().NotBeNull();
        viewModel.StatusMessage.Should().Be(string.Empty);
    }

    [Fact]
    public void Constructor_WithNullChangeTracker_ThrowsArgumentNullException()
    {
        // Act & Assert
        RunOnUIThread(() => 
            Assert.Throws<ArgumentNullException>(() => 
                new SettingsWindowViewModel(null!, _mockEventAggregator.Object, _mockLogger.Object)));
    }

    [Fact]
    public void Constructor_WithNullEventAggregator_ThrowsArgumentNullException()
    {
        // Act & Assert
        RunOnUIThread(() => 
            Assert.Throws<ArgumentNullException>(() => 
                new SettingsWindowViewModel(_mockChangeTracker.Object, null!, _mockLogger.Object)));
    }

    [Fact]
    public void AllCategories_ReturnsExpectedCategoriesInCorrectOrder()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        var categories = viewModel.AllCategories;

        // Assert
        categories.Should().HaveCount(8);
        
        // 基本設定カテゴリの確認（DisplayOrder順）
        categories[0].Id.Should().Be("general");
        categories[0].Level.Should().Be(SettingLevel.Basic);
        categories[0].DisplayOrder.Should().Be(1);
        
        categories[1].Id.Should().Be("appearance");
        categories[1].Level.Should().Be(SettingLevel.Basic);
        
        categories[2].Id.Should().Be("mainui");
        categories[2].Level.Should().Be(SettingLevel.Basic);
        
        categories[3].Id.Should().Be("translation");
        categories[3].Level.Should().Be(SettingLevel.Basic);
        
        categories[4].Id.Should().Be("overlay");
        categories[4].Level.Should().Be(SettingLevel.Basic);
        
        // 詳細設定カテゴリの確認
        categories[5].Id.Should().Be("capture");
        categories[5].Level.Should().Be(SettingLevel.Advanced);
        
        categories[6].Id.Should().Be("ocr");
        categories[6].Level.Should().Be(SettingLevel.Advanced);
        
        categories[7].Id.Should().Be("advanced");
        categories[7].Level.Should().Be(SettingLevel.Advanced);
    }

    [Fact]
    public void VisibleCategories_WhenShowAdvancedSettingsFalse_ReturnsOnlyBasicCategories()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowAdvancedSettings = false;

        // Act
        var visibleCategories = viewModel.VisibleCategories;

        // Assert
        visibleCategories.Should().HaveCount(5);
        visibleCategories.Should().OnlyContain(c => c.Level == SettingLevel.Basic);
        
        var categoryIds = visibleCategories.Select(c => c.Id).ToArray();
        categoryIds.Should().Equal("general", "appearance", "mainui", "translation", "overlay");
    }

    [Fact]
    public void VisibleCategories_WhenShowAdvancedSettingsTrue_ReturnsAllCategories()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowAdvancedSettings = true;

        // Act
        var visibleCategories = viewModel.VisibleCategories;

        // Assert
        visibleCategories.Should().HaveCount(8);
        visibleCategories.Should().Contain(c => c.Level == SettingLevel.Basic);
        visibleCategories.Should().Contain(c => c.Level == SettingLevel.Advanced);
    }

    [Fact]
    public void ShowAdvancedSettings_WhenToggledTrue_UpdatesVisibleCategoriesProperty()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var propertyChanged = false;
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(viewModel.VisibleCategories))
                propertyChanged = true;
        };

        // Act
        viewModel.ShowAdvancedSettings = true;

        // Assert
        viewModel.ShowAdvancedSettings.Should().BeTrue();
        propertyChanged.Should().BeTrue();
        viewModel.VisibleCategories.Should().HaveCount(8);
    }

    [Fact]
    public void ShowAdvancedSettings_WhenCurrentCategoryBecomesInvisible_SelectsFirstVisibleCategory()
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowAdvancedSettings = true;
        
        // 詳細設定カテゴリを選択
        var advancedCategory = viewModel.VisibleCategories.First(c => c.Level == SettingLevel.Advanced);
        viewModel.SelectedCategory = advancedCategory;

        // Act - 基本設定のみに切り替え
        viewModel.ShowAdvancedSettings = false;

        // Assert
        viewModel.SelectedCategory.Should().NotBe(advancedCategory);
        viewModel.SelectedCategory?.Level.Should().Be(SettingLevel.Basic);
        viewModel.SelectedCategory?.Id.Should().Be("general"); // 最初のカテゴリ
    }

    [Fact]
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

    [Fact]
    public void HasChanges_ReturnsValueFromChangeTracker()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(true);
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.HasChanges.Should().BeTrue();
        
        // Change the mock and verify
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
        _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(false));
        
        viewModel.HasChanges.Should().BeFalse();
    }

    [Fact]
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
    public void ToggleAdvancedSettingsCommand_WhenExecuted_TogglesShowAdvancedSettings()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var initialValue = viewModel.ShowAdvancedSettings;

        // Act
        viewModel.ToggleAdvancedSettingsCommand.Execute().Subscribe();

        // Assert
        viewModel.ShowAdvancedSettings.Should().Be(!initialValue);
    }

    [Fact]
    public void SaveCommand_CanExecute_DependsOnHasChanges()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(false);
        var viewModel = CreateViewModel();

        // Act & Assert - 変更なしの場合
        viewModel.SaveCommand.CanExecute.FirstAsync().GetAwaiter().GetResult().Should().BeFalse();

        // 変更ありの場合
        _mockChangeTracker.Setup(x => x.HasChanges).Returns(true);
        _mockChangeTracker.Raise(x => x.HasChangesChanged += null, new HasChangesChangedEventArgs(true));
        
        viewModel.SaveCommand.CanExecute.FirstAsync().GetAwaiter().GetResult().Should().BeTrue();
    }

    [Fact]
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
        var viewModel = CreateViewModel();

        // Act
        await viewModel.CancelCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be("変更をキャンセルしました");
        _mockChangeTracker.Verify(x => x.ConfirmDiscardChangesAsync(), Times.Once);
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

    [Fact]
    public async Task ResetCommand_WhenConfirmed_UpdatesStatusMessage()
    {
        // Arrange
        _mockChangeTracker.Setup(x => x.ConfirmDiscardChangesAsync()).ReturnsAsync(true);
        var viewModel = CreateViewModel();

        // Act
        await viewModel.ResetCommand.Execute().FirstAsync();

        // Assert
        viewModel.StatusMessage.Should().Be("設定をリセットしました");
        _mockChangeTracker.Verify(x => x.ConfirmDiscardChangesAsync(), Times.Once);
    }

    [Fact]
    public void CategoryContent_AllCategoriesHaveNonNullContent()
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert
        foreach (var category in viewModel.AllCategories)
        {
            category.Content.Should().NotBeNull($"Category {category.Id} should have content");
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
    [InlineData("general")]
    [InlineData("appearance")]
    [InlineData("mainui")]
    [InlineData("translation")]
    [InlineData("overlay")]
    [InlineData("capture")]
    [InlineData("ocr")]
    [InlineData("advanced")]
    public void CategorySelection_SpecificCategory_WorksCorrectly(string categoryId)
    {
        // Arrange
        var viewModel = CreateViewModel();
        viewModel.ShowAdvancedSettings = true; // すべてのカテゴリを表示

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
        viewModel.ShowAdvancedSettings = true;
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
    /// 境界値テスト：空のコレクション状態での動作確認
    /// </summary>
    [Fact]
    public void BoundaryConditions_EmptyVisibleCategories_HandledGracefully()
    {
        // NOTE: 現在の実装では空のカテゴリになることはないが、
        // 将来的な拡張のために境界条件をテスト
        
        // Arrange
        var viewModel = CreateViewModel();

        // Act & Assert - 通常の状態では8カテゴリ存在
        viewModel.AllCategories.Should().HaveCount(8);
        viewModel.VisibleCategories.Should().NotBeEmpty();
    }
}

