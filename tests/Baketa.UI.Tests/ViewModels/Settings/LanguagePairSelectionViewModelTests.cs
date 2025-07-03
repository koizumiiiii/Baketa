using System;
using System.Linq;
using System.Reactive.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ReactiveUI.Testing;
using Xunit;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Models;
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using EngineStatus = Baketa.UI.Services.TranslationEngineStatus;
using StatusUpdate = Baketa.UI.Services.TranslationEngineStatusUpdate;

namespace Baketa.UI.Tests.ViewModels.Settings;

/// <summary>
/// LanguagePairSelectionViewModelのテスト
/// </summary>
public class LanguagePairSelectionViewModelTests
{
    private readonly Mock<ITranslationEngineStatusService> _mockStatusService;
    private readonly Mock<ILocalizationService> _mockLocalizationService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IOptions<TranslationUIOptions>> _mockOptions;
    private readonly Mock<ILogger<LanguagePairSelectionViewModel>> _mockLogger;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly TranslationUIOptions _testOptions;

    public LanguagePairSelectionViewModelTests()
    {
        _mockStatusService = new Mock<ITranslationEngineStatusService>();
        _mockLocalizationService = new Mock<ILocalizationService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockOptions = new Mock<IOptions<TranslationUIOptions>>();
        _mockLogger = new Mock<ILogger<LanguagePairSelectionViewModel>>();
        _mockEventAggregator = new Mock<IEventAggregator>();

        _testOptions = new TranslationUIOptions
        {
            EnableNotifications = true,
            StatusUpdateIntervalSeconds = 30
        };

        _mockOptions.Setup(x => x.Value).Returns(_testOptions);

        // デフォルトのモック設定
        var localStatus = new EngineStatus { IsOnline = true, IsHealthy = true };
        var cloudStatus = new EngineStatus { IsOnline = false, IsHealthy = false };
        
        _mockStatusService.Setup(x => x.LocalEngineStatus).Returns(localStatus);
        _mockStatusService.Setup(x => x.CloudEngineStatus).Returns(cloudStatus);
        _mockStatusService.Setup(x => x.StatusUpdates).Returns(Observable.Never<StatusUpdate>());
        
        // LocalizationServiceのObservableをモック設定
        _mockLocalizationService.Setup(x => x.CurrentLanguageChanged).Returns(Observable.Never<System.Globalization.CultureInfo>());
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // Assert
        // ViewModelのデフォルト値はSimplifiedなので、期待値を修正
        viewModel.SelectedChineseVariant.Should().Be(ChineseVariant.Simplified);
        viewModel.IsLoading.Should().BeFalse();
        viewModel.FilterText.Should().BeEmpty();
    }

    [Fact]
    public void Constructor_WithNullStatusService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LanguagePairSelectionViewModel(
                null!,
                _mockLocalizationService.Object,
                _mockNotificationService.Object,
                _mockOptions.Object,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullLocalizationService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LanguagePairSelectionViewModel(
                _mockStatusService.Object,
                null!,
                _mockNotificationService.Object,
                _mockOptions.Object,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullNotificationService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LanguagePairSelectionViewModel(
                _mockStatusService.Object,
                _mockLocalizationService.Object,
                null!,
                _mockOptions.Object,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LanguagePairSelectionViewModel(
                _mockStatusService.Object,
                _mockLocalizationService.Object,
                _mockNotificationService.Object,
                null!,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new LanguagePairSelectionViewModel(
                _mockStatusService.Object,
                _mockLocalizationService.Object,
                _mockNotificationService.Object,
                _mockOptions.Object,
                null!,
                _mockEventAggregator.Object));
    }


    [Fact]
    public void IsChineseRelatedPair_WhenChinesePairSelected_ReturnsTrue()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var chinesePair = new LanguagePairConfiguration
        {
            SourceLanguage = "zh-cn",
            TargetLanguage = "en"
        };

        // Act
        using (viewModel.Activator.Activate())
        {
            viewModel.SelectedLanguagePair = chinesePair;
            
            // ReactiveUIの処理を完了させるため、少し待機
            System.Threading.Thread.Sleep(50);
        }

        // Assert
        // LanguagePairConfigurationのIsChineseRelatedプロパティがtrueであることを確認
        chinesePair.IsChineseRelated.Should().BeTrue();
        // ViewModelのIsChineseRelatedPairは、OnLanguagePairSelected()メソッドで更新される
        viewModel.IsChineseRelatedPair.Should().BeTrue();
    }

    [Fact]
    public void IsChineseRelatedPair_WhenNonChinesePairSelected_ReturnsFalse()
    {
        // Arrange
        var viewModel = CreateViewModel();
        var nonChinesePair = new LanguagePairConfiguration
        {
            SourceLanguage = "ja",
            TargetLanguage = "en"
        };

        // Act
        using (viewModel.Activator.Activate())
        {
            viewModel.SelectedLanguagePair = nonChinesePair;
            
            // ReactiveUIの処理を完了させるため、少し待機
            System.Threading.Thread.Sleep(50);
        }

        // Assert
        // LanguagePairConfigurationのIsChineseRelatedプロパティがfalseであることを確認
        nonChinesePair.IsChineseRelated.Should().BeFalse();
        // ViewModelのIsChineseRelatedPairも更新される
        viewModel.IsChineseRelatedPair.Should().BeFalse();
    }

    [Theory]
    [InlineData(ChineseVariant.Auto)]
    [InlineData(ChineseVariant.Simplified)]
    [InlineData(ChineseVariant.Traditional)]
    public void SelectedChineseVariant_CanBeSetToAllValidValues(ChineseVariant variant)
    {
        // Arrange
        var viewModel = CreateViewModel();

        // Act
        viewModel.SelectedChineseVariant = variant;

        // Assert
        viewModel.SelectedChineseVariant.Should().Be(variant);
    }

    private LanguagePairSelectionViewModel CreateViewModel()
    {
        return new LanguagePairSelectionViewModel(
            _mockStatusService.Object,
            _mockLocalizationService.Object,
            _mockNotificationService.Object,
            _mockOptions.Object,
            _mockLogger.Object,
            _mockEventAggregator.Object);
    }

}
