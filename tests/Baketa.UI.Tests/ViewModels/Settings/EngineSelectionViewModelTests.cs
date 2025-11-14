using System;
using System.Reactive.Linq;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
using Baketa.UI.Models;
using Baketa.UI.Services;
using Baketa.UI.ViewModels.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ReactiveUI.Testing;
using Xunit;
using EngineStatus = Baketa.UI.Services.TranslationEngineStatus;
using StatusUpdate = Baketa.UI.Services.TranslationEngineStatusUpdate;

namespace Baketa.UI.Tests.ViewModels.Settings;

/// <summary>
/// EngineSelectionViewModelのテスト
/// </summary>
public class EngineSelectionViewModelTests
{
    private readonly Mock<ITranslationEngineStatusService> _mockStatusService;
    private readonly Mock<IUserPlanService> _mockPlanService;
    private readonly Mock<INotificationService> _mockNotificationService;
    private readonly Mock<IOptions<TranslationUIOptions>> _mockOptions;
    private readonly Mock<ILogger<EngineSelectionViewModel>> _mockLogger;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly TranslationUIOptions _testOptions;

    public EngineSelectionViewModelTests()
    {
        _mockStatusService = new Mock<ITranslationEngineStatusService>();
        _mockPlanService = new Mock<IUserPlanService>();
        _mockNotificationService = new Mock<INotificationService>();
        _mockOptions = new Mock<IOptions<TranslationUIOptions>>();
        _mockLogger = new Mock<ILogger<EngineSelectionViewModel>>();
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

        _mockPlanService.Setup(x => x.CanUseCloudOnlyEngine).Returns(false);
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedEngine.Should().Be(TranslationEngine.LocalOnly);
        viewModel.AvailableEngines.Should().NotBeEmpty();
        viewModel.IsLoading.Should().BeFalse();
        viewModel.HasStatusWarning.Should().BeFalse();
        viewModel.IsCloudOnlyEnabled.Should().BeFalse();
        viewModel.SelectEngineCommand.Should().NotBeNull();
        viewModel.RefreshStatusCommand.Should().NotBeNull();
        viewModel.ShowPremiumInfoCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullStatusService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new EngineSelectionViewModel(
                null!,
                _mockPlanService.Object,
                _mockNotificationService.Object,
                _mockOptions.Object,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullPlanService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new EngineSelectionViewModel(
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
            new EngineSelectionViewModel(
                _mockStatusService.Object,
                _mockPlanService.Object,
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
            new EngineSelectionViewModel(
                _mockStatusService.Object,
                _mockPlanService.Object,
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
            new EngineSelectionViewModel(
                _mockStatusService.Object,
                _mockPlanService.Object,
                _mockNotificationService.Object,
                _mockOptions.Object,
                null!,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void LocalEngineStatus_ReturnsStatusServiceValue()
    {
        // Arrange
        var expectedStatus = new EngineStatus { IsOnline = true, IsHealthy = true };
        _mockStatusService.Setup(x => x.LocalEngineStatus).Returns(expectedStatus);
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.LocalEngineStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public void CloudEngineStatus_ReturnsStatusServiceValue()
    {
        // Arrange
        var expectedStatus = new EngineStatus { IsOnline = false, IsHealthy = false };
        _mockStatusService.Setup(x => x.CloudEngineStatus).Returns(expectedStatus);
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.CloudEngineStatus.Should().Be(expectedStatus);
    }

    [Fact]
    public void IsCloudOnlyEnabled_WhenPlanServiceReturnsFalse_ReturnsFalse()
    {
        // Arrange
        _mockPlanService.Setup(x => x.CanUseCloudOnlyEngine).Returns(false);
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.IsCloudOnlyEnabled.Should().BeFalse();
    }

    [Fact]
    public void IsCloudOnlyEnabled_WhenPlanServiceReturnsTrue_ReturnsTrue()
    {
        // Arrange
        _mockPlanService.Setup(x => x.CanUseCloudOnlyEngine).Returns(true);
        var viewModel = CreateViewModel();

        // Act & Assert
        viewModel.IsCloudOnlyEnabled.Should().BeTrue();
    }

    [Fact]
    public void SelectedEngine_DefaultValue_IsLocalOnly()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.SelectedEngine.Should().Be(TranslationEngine.LocalOnly);
    }

    [Fact]
    public void AvailableEngines_ContainsExpectedEngines()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // Assert
        viewModel.AvailableEngines.Should().NotBeEmpty();
        var engineNames = viewModel.AvailableEngines.Select(e => e.Engine);
        engineNames.Should().Contain(TranslationEngine.LocalOnly);
        engineNames.Should().Contain(TranslationEngine.CloudOnly);
    }

    private EngineSelectionViewModel CreateViewModel()
    {
        return new EngineSelectionViewModel(
            _mockStatusService.Object,
            _mockPlanService.Object,
            _mockNotificationService.Object,
            _mockOptions.Object,
            _mockLogger.Object,
            _mockEventAggregator.Object);
    }
}
