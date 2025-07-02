using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
using Baketa.Application.Services.Capture;

namespace Baketa.Application.Tests.Services.Capture;

/// <summary>
/// フルスクリーン管理サービスのテスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class FullscreenManagerServiceTests : IDisposable
{
    private readonly Mock<IFullscreenDetectionService> _mockDetectionService;
    private readonly Mock<IFullscreenOptimizationService> _mockOptimizationService;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly Mock<ILogger<FullscreenManagerService>> _mockLogger;
    private readonly FullscreenManagerService _managerService;
    
    public FullscreenManagerServiceTests()
    {
        _mockDetectionService = new Mock<IFullscreenDetectionService>();
        _mockOptimizationService = new Mock<IFullscreenOptimizationService>();
        _mockEventAggregator = new Mock<IEventAggregator>();
        _mockLogger = new Mock<ILogger<FullscreenManagerService>>();
        
        // デフォルトの設定をセットアップ
        _mockDetectionService.Setup(x => x.Settings).Returns(new FullscreenDetectionSettings());
        
        _managerService = new FullscreenManagerService(
            _mockDetectionService.Object,
            _mockOptimizationService.Object,
            _mockEventAggregator.Object,
            _mockLogger.Object);
    }
    
    [Fact]
    public void IsRunning_ShouldReturnFalseInitially()
    {
        // Assert
        Assert.False(_managerService.IsRunning);
    }
    
    [Fact]
    public void DetectionService_ShouldReturnInjectedService()
    {
        // Assert
        Assert.Same(_mockDetectionService.Object, _managerService.DetectionService);
    }
    
    [Fact]
    public void OptimizationService_ShouldReturnInjectedService()
    {
        // Assert
        Assert.Same(_mockOptimizationService.Object, _managerService.OptimizationService);
    }
    
    [Fact]
    public async Task StartAsync_ShouldStartBothServices()
    {
        // Arrange
        var cancellationToken = new CancellationToken();
        
        // Act
        await _managerService.StartAsync(cancellationToken).ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.StartOptimizationAsync(cancellationToken), Times.Once);
        _mockDetectionService.Verify(x => x.StartMonitoringAsync(cancellationToken), Times.Once);
        _mockEventAggregator.Verify(x => x.PublishAsync(It.IsAny<FullscreenDetectionStartedEvent>()), Times.Once);
    }
    
    [Fact]
    public async Task StopAsync_ShouldStopBothServices()
    {
        // Arrange
        await _managerService.StartAsync().ConfigureAwait(true);
        
        // Act
        await _managerService.StopAsync().ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.StopOptimizationAsync(), Times.Once);
        _mockDetectionService.Verify(x => x.StopMonitoringAsync(), Times.Once);
        _mockEventAggregator.Verify(x => x.PublishAsync(It.IsAny<FullscreenDetectionStoppedEvent>()), Times.Once);
    }
    
    [Fact]
    public async Task GetCurrentFullscreenStateAsync_ShouldDelegateToDetectionService()
    {
        // Arrange
        var expectedInfo = new FullscreenInfo { IsFullscreen = true, ProcessName = "TestGame" };
        _mockDetectionService
            .Setup(x => x.DetectCurrentFullscreenAsync())
            .ReturnsAsync(expectedInfo);
        
        // Act
        var result = await _managerService.GetCurrentFullscreenStateAsync().ConfigureAwait(true);
        
        // Assert
        Assert.Same(expectedInfo, result);
        _mockDetectionService.Verify(x => x.DetectCurrentFullscreenAsync(), Times.Once);
    }
    
    [Fact]
    public async Task GetWindowFullscreenStateAsync_ShouldDelegateToDetectionService()
    {
        // Arrange
        var windowHandle = new IntPtr(12345);
        var expectedInfo = new FullscreenInfo { IsFullscreen = false, WindowHandle = windowHandle };
        _mockDetectionService
            .Setup(x => x.DetectFullscreenAsync(windowHandle))
            .ReturnsAsync(expectedInfo);
        
        // Act
        var result = await _managerService.GetWindowFullscreenStateAsync(windowHandle).ConfigureAwait(true);
        
        // Assert
        Assert.Same(expectedInfo, result);
        _mockDetectionService.Verify(x => x.DetectFullscreenAsync(windowHandle), Times.Once);
    }
    
    [Fact]
    public async Task ApplyOptimizationAsync_ShouldDelegateToOptimizationService()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo { IsFullscreen = true, ProcessName = "TestGame" };
        
        // Act
        await _managerService.ApplyOptimizationAsync(fullscreenInfo).ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.ApplyOptimizationAsync(fullscreenInfo), Times.Once);
    }
    
    [Fact]
    public async Task RemoveOptimizationAsync_ShouldDelegateToOptimizationService()
    {
        // Act
        await _managerService.RemoveOptimizationAsync().ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.RemoveOptimizationAsync(), Times.Once);
    }
    
    [Fact]
    public void UpdateDetectionSettings_ShouldDelegateToDetectionService()
    {
        // Arrange
        var settings = new FullscreenDetectionSettings { DetectionIntervalMs = 2000 };
        
        // Act
        _managerService.UpdateDetectionSettings(settings);
        
        // Assert
        _mockDetectionService.Verify(x => x.UpdateDetectionSettings(settings), Times.Once);
    }
    
    [Fact]
    public void ResetOptimizationStatistics_ShouldDelegateToOptimizationService()
    {
        // Act
        _managerService.ResetOptimizationStatistics();
        
        // Assert
        _mockOptimizationService.Verify(x => x.ResetStatistics(), Times.Once);
    }
    
    [Fact]
    public async Task ForceResetOptimizationAsync_ShouldDelegateToOptimizationService()
    {
        // Act
        await _managerService.ForceResetOptimizationAsync().ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.ForceResetAsync(), Times.Once);
    }
    
    [Fact]
    public void SetOptimizationEnabled_ShouldUpdateOptimizationServiceProperty()
    {
        // Arrange
        _mockOptimizationService.SetupProperty(x => x.IsEnabled);
        
        // Act
        _managerService.SetOptimizationEnabled(false);
        
        // Assert
        _mockOptimizationService.VerifySet(x => x.IsEnabled = false, Times.Once);
    }
    
    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_ShouldNotStartAgain()
    {
        // Arrange
        await _managerService.StartAsync().ConfigureAwait(true);
        _mockOptimizationService.Reset();
        _mockDetectionService.Reset();
        
        // Act
        await _managerService.StartAsync().ConfigureAwait(true);
        
        // Assert - サービスが再度開始されないことを確認
        _mockOptimizationService.Verify(x => x.StartOptimizationAsync(It.IsAny<CancellationToken>()), Times.Never);
        _mockDetectionService.Verify(x => x.StartMonitoringAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    
    [Fact]
    public async Task StopAsync_WhenNotRunning_ShouldNotCallStopMethods()
    {
        // Act
        await _managerService.StopAsync().ConfigureAwait(true);
        
        // Assert
        _mockOptimizationService.Verify(x => x.StopOptimizationAsync(), Times.Never);
        _mockDetectionService.Verify(x => x.StopMonitoringAsync(), Times.Never);
    }
    
    [Fact]
    public async Task StartAsync_WithException_ShouldPublishErrorEvent()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        _mockOptimizationService
            .Setup(x => x.StartOptimizationAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(exception);
        
        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(() => _managerService.StartAsync()).ConfigureAwait(true);
        
        _mockEventAggregator.Verify(x => 
            x.PublishAsync(It.Is<FullscreenOptimizationErrorEvent>(e => e.Exception == exception)), 
            Times.Once);
    }
    
    [Fact]
    public async Task ApplyOptimizationAsync_WithException_ShouldPublishErrorEvent()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo();
        var exception = new InvalidOperationException("Test exception");
        _mockOptimizationService
            .Setup(x => x.ApplyOptimizationAsync(fullscreenInfo))
            .ThrowsAsync(exception);
        
        // Act & Assert
        var thrownException = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            _managerService.ApplyOptimizationAsync(fullscreenInfo)).ConfigureAwait(true);
        
        _mockEventAggregator.Verify(x => 
            x.PublishAsync(It.Is<FullscreenOptimizationErrorEvent>(e => e.Exception == exception)), 
            Times.Once);
    }
    
    [Fact]
    public void Dispose_ShouldStopServiceAndNotThrow()
    {
        // Act & Assert
        _managerService.Dispose();
        
        // 二重Disposeのテスト
        _managerService.Dispose();
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _managerService?.Dispose();
        }
    }
    
    [Fact]
    public async Task Dispose_WhenRunning_ShouldStopServicesFirst()
    {
        // Arrange
        await _managerService.StartAsync().ConfigureAwait(true);
        
        // Act
        _managerService.Dispose();
        
        // Assert
        _mockOptimizationService.Verify(x => x.StopOptimizationAsync(), Times.Once);
        _mockDetectionService.Verify(x => x.StopMonitoringAsync(), Times.Once);
    }
}

/// <summary>
/// フルスクリーンイベントハンドラーのテスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class FullscreenEventHandlersTests
{
    [Fact]
    public async Task FullscreenStateChangedEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenStateChangedEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenStateChangedEventHandler(mockLogger.Object);
        var eventData = new FullscreenStateChangedEvent(new FullscreenInfo
        {
            IsFullscreen = true,
            ProcessName = "TestGame",
            WindowTitle = "Test Game Window",
            Confidence = 0.9,
            IsLikelyGame = true
        });
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
    
    [Fact]
    public async Task FullscreenOptimizationAppliedEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenOptimizationAppliedEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenOptimizationAppliedEventHandler(mockLogger.Object);
        var eventData = new FullscreenOptimizationAppliedEvent(
            new FullscreenInfo { ProcessName = "TestGame" },
            new Baketa.Core.Settings.CaptureSettings { CaptureIntervalMs = 300, CaptureQuality = 80, DifferenceDetectionGridSize = 16 },
            new Baketa.Core.Settings.CaptureSettings { CaptureIntervalMs = 200, CaptureQuality = 90 });
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
    
    [Fact]
    public async Task FullscreenOptimizationRemovedEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenOptimizationRemovedEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenOptimizationRemovedEventHandler(mockLogger.Object);
        var eventData = new FullscreenOptimizationRemovedEvent(
            new Baketa.Core.Settings.CaptureSettings { CaptureIntervalMs = 200, CaptureQuality = 90 },
            "Window mode detected");
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
    
    [Fact]
    public async Task FullscreenOptimizationErrorEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenOptimizationErrorEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenOptimizationErrorEventHandler(mockLogger.Object);
        var exception = new InvalidOperationException("Test error");
        var eventData = new FullscreenOptimizationErrorEvent(exception, "Test context", "Custom error message");
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
    
    [Fact]
    public async Task FullscreenDetectionStartedEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenDetectionStartedEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenDetectionStartedEventHandler(mockLogger.Object);
        var eventData = new FullscreenDetectionStartedEvent(new FullscreenDetectionSettings
        {
            DetectionIntervalMs = 1000,
            MinConfidence = 0.8
        });
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
    
    [Fact]
    public async Task FullscreenDetectionStoppedEventHandler_Handle_ShouldNotThrow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<Baketa.Application.EventHandlers.Capture.FullscreenDetectionStoppedEventHandler>>();
        var handler = new Baketa.Application.EventHandlers.Capture.FullscreenDetectionStoppedEventHandler(mockLogger.Object);
        var eventData = new FullscreenDetectionStoppedEvent("Manual stop", TimeSpan.FromMinutes(5));
        
        // Act & Assert
        await handler.HandleAsync(eventData).ConfigureAwait(true);
    }
}
