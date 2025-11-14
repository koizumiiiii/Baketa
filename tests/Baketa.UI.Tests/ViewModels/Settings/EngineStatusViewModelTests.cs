using System;
using System.Reactive.Linq;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Configuration;
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
/// EngineStatusViewModelのテスト
/// </summary>
public class EngineStatusViewModelTests
{
    private readonly Mock<ITranslationEngineStatusService> _mockStatusService;
    private readonly Mock<IOptions<TranslationUIOptions>> _mockOptions;
    private readonly Mock<ILogger<EngineStatusViewModel>> _mockLogger;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly TranslationUIOptions _testOptions;

    public EngineStatusViewModelTests()
    {
        _mockStatusService = new Mock<ITranslationEngineStatusService>();
        _mockOptions = new Mock<IOptions<TranslationUIOptions>>();
        _mockLogger = new Mock<ILogger<EngineStatusViewModel>>();
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
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();

        // ViewModelのActivate()を手動で呼び出してUpdateStatusDisplay()を実行
        viewModel.Activator.Activate();

        // Assert
        viewModel.LocalEngineStatusText.Should().NotBeNull();
        viewModel.CloudEngineStatusText.Should().NotBeNull();
        // モックデータではローカルエンジンはOnline=true, Healthy=trueに設定済み
        viewModel.IsLocalEngineHealthy.Should().BeTrue();
        // モックデータではクラウドエンジンはOnline=false, Healthy=falseに設定済み
        viewModel.IsCloudEngineHealthy.Should().BeFalse();
        viewModel.LastUpdateTime.Should().NotBeNullOrEmpty();
        viewModel.RefreshStatusCommand.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullStatusService_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new EngineStatusViewModel(
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
            new EngineStatusViewModel(
                _mockStatusService.Object,
                null!,
                _mockLogger.Object,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new EngineStatusViewModel(
                _mockStatusService.Object,
                _mockOptions.Object,
                null!,
                _mockEventAggregator.Object));
    }

    [Fact]
    public void IsLocalEngineHealthy_WhenStatusIsHealthy_ReturnsTrue()
    {
        // Arrange
        var healthyStatus = new EngineStatus { IsOnline = true, IsHealthy = true };
        _mockStatusService.Setup(x => x.LocalEngineStatus).Returns(healthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // モックで設定した通りの結果が返されることを確認
        viewModel.IsLocalEngineHealthy.Should().BeTrue();
    }

    [Fact]
    public void IsLocalEngineHealthy_WhenStatusIsUnhealthy_ReturnsFalse()
    {
        // Arrange
        var unhealthyStatus = new EngineStatus { IsOnline = false, IsHealthy = false };
        _mockStatusService.Setup(x => x.LocalEngineStatus).Returns(unhealthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // モックで設定した通りの結果が返されることを確認
        viewModel.IsLocalEngineHealthy.Should().BeFalse();
    }

    [Fact]
    public void IsCloudEngineHealthy_WhenStatusIsHealthy_ReturnsTrue()
    {
        // Arrange
        var healthyStatus = new EngineStatus { IsOnline = true, IsHealthy = true };
        _mockStatusService.Setup(x => x.CloudEngineStatus).Returns(healthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // モックで設定した通りの結果が返されることを確認
        viewModel.IsCloudEngineHealthy.Should().BeTrue();
    }

    [Fact]
    public void IsCloudEngineHealthy_WhenStatusIsUnhealthy_ReturnsFalse()
    {
        // Arrange
        var unhealthyStatus = new EngineStatus { IsOnline = false, IsHealthy = false };
        _mockStatusService.Setup(x => x.CloudEngineStatus).Returns(unhealthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // モックで設定した通りの結果が返されることを確認
        viewModel.IsCloudEngineHealthy.Should().BeFalse();
    }


    [Fact]
    public void LastUpdateTime_IsNotEmpty()
    {
        // Arrange & Act
        var viewModel = CreateViewModel();
        viewModel.Activator.Activate();

        // Assert
        // LastUpdateTimeはActivate時のUpdateStatusDisplay()で初期化されるため、空でないことを確認
        viewModel.LastUpdateTime.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LocalEngineStatusText_WithHealthyStatus_ReturnsAppropriateText()
    {
        // Arrange
        var healthyStatus = new EngineStatus { IsOnline = true, IsHealthy = true };
        _mockStatusService.Setup(x => x.LocalEngineStatus).Returns(healthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // ステータステキストはActivate時のUpdateStatusDisplay()で初期化されるため、空でないことを確認
        viewModel.LocalEngineStatusText.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void CloudEngineStatusText_WithUnhealthyStatus_ReturnsAppropriateText()
    {
        // Arrange
        var unhealthyStatus = new EngineStatus { IsOnline = false, IsHealthy = false };
        _mockStatusService.Setup(x => x.CloudEngineStatus).Returns(unhealthyStatus);
        var viewModel = CreateViewModel();

        // Act
        viewModel.Activator.Activate();

        // Assert
        // ステータステキストはActivate時のUpdateStatusDisplay()で初期化されるため、空でないことを確認
        viewModel.CloudEngineStatusText.Should().NotBeNullOrEmpty();
    }

    private EngineStatusViewModel CreateViewModel()
    {
        return new EngineStatusViewModel(
            _mockStatusService.Object,
            _mockOptions.Object,
            _mockLogger.Object,
            _mockEventAggregator.Object);
    }
}
