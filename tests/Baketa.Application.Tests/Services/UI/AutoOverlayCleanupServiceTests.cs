using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.UI;
using Baketa.Core.Events.Capture;
using Baketa.Core.Settings;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.UI;

/// <summary>
/// AutoOverlayCleanupService の単体テスト
/// UltraThink Phase 1: オーバーレイ自動削除システム検証
/// </summary>
public class AutoOverlayCleanupServiceTests : IDisposable
{
    private readonly Mock<IInPlaceTranslationOverlayManager> _overlayManagerMock;
    private readonly Mock<IEventAggregator> _eventAggregatorMock;
    private readonly Mock<ILogger<AutoOverlayCleanupService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AutoOverlayCleanupSettings>> _settingsMock;
    private readonly AutoOverlayCleanupService _service;
    private bool _disposed;

    public AutoOverlayCleanupServiceTests()
    {
        _overlayManagerMock = new Mock<IInPlaceTranslationOverlayManager>();
        _eventAggregatorMock = new Mock<IEventAggregator>();
        _loggerMock = new Mock<ILogger<AutoOverlayCleanupService>>();
        _settingsMock = new Mock<IOptionsMonitor<AutoOverlayCleanupSettings>>();
        
        // Setup default settings values
        var defaultSettings = new AutoOverlayCleanupSettings
        {
            MinConfidenceScore = 0.7f,
            MaxCleanupPerSecond = 10,
            TextDisappearanceChangeThreshold = 0.05f,
            StatisticsLogInterval = 100,
            InitializationTimeoutMs = 10000
        };
        _settingsMock.Setup(s => s.CurrentValue).Returns(defaultSettings);
        
        _service = new AutoOverlayCleanupService(
            _overlayManagerMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object,
            _settingsMock.Object);
    }

    [Fact]
    public async Task InitializeAsync_ShouldSubscribeToTextDisappearanceEvent()
    {
        // Act
        await _service.InitializeAsync();

        // Assert
        _eventAggregatorMock.Verify(ea => ea.Subscribe<TextDisappearanceEvent>(_service), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_WithNullEvent_ShouldReturnEarly()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        await _service.HandleAsync(null!);

        // Assert
        _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithLowConfidence_ShouldRejectRequest()
    {
        // Arrange
        await _service.InitializeAsync();
        var lowConfidenceEvent = new TextDisappearanceEvent(
            regions: [new Rectangle(0, 0, 100, 100)],
            sourceWindow: IntPtr.Zero,
            regionId: "test-region",
            confidenceScore: 0.5f // Below default threshold of 0.7
        );

        // Act
        await _service.HandleAsync(lowConfidenceEvent);

        // Assert
        var statistics = _service.GetStatistics();
        statistics.RejectedByConfidence.Should().Be(1);
        statistics.OverlaysCleanedUp.Should().Be(0);
        
        _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_WithSufficientConfidence_ShouldCleanupOverlays()
    {
        // Arrange
        await _service.InitializeAsync();
        var regions = new List<Rectangle> { new(10, 10, 50, 50), new(100, 100, 80, 80) };
        var highConfidenceEvent = new TextDisappearanceEvent(
            regions: regions,
            sourceWindow: new IntPtr(12345),
            regionId: "test-region-high",
            confidenceScore: 0.85f // Above threshold
        );

        // Act
        await _service.HandleAsync(highConfidenceEvent);

        // Assert
        var statistics = _service.GetStatistics();
        statistics.OverlaysCleanedUp.Should().Be(2); // Number of regions
        statistics.RejectedByConfidence.Should().Be(0);

        // Verify overlay manager was called for each region
        foreach (var region in regions)
        {
            _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(region, -1, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task HandleAsync_RateLimit_ShouldRejectExcessiveRequests()
    {
        // Arrange
        await _service.InitializeAsync();
        var event1 = CreateTestEvent(0.8f);

        // Act - Send 15 requests rapidly (above rate limit of 10/second)
        var tasks = new List<Task>();
        for (int i = 0; i < 15; i++)
        {
            tasks.Add(_service.HandleAsync(event1));
        }
        await Task.WhenAll(tasks);

        // Assert
        var statistics = _service.GetStatistics();
        statistics.RejectedByRateLimit.Should().BeGreaterThan(0);
        statistics.TotalEventsProcessed.Should().Be(15);
    }

    [Fact]
    public async Task UpdateCircuitBreakerSettings_ShouldValidateAndLogWarning()
    {
        // Arrange
        await _service.InitializeAsync();
        
        // Act - Call the deprecated update method
        _service.UpdateCircuitBreakerSettings(0.9f, 5);

        // Assert - Should log warning about deprecated usage but not affect current runtime behavior
        // Note: With configuration externalization, runtime updates should be done via appsettings.json
        // This method now only validates parameters and logs warnings
        
        // Test still uses original settings from mock
        var regions = new List<Rectangle> { new(10, 10, 50, 50) };
        var highConfidenceEvent = new TextDisappearanceEvent(
            regions: regions,
            sourceWindow: new IntPtr(12345),
            regionId: "test-region",
            confidenceScore: 0.8f // Above default threshold of 0.7
        );
        
        await _service.HandleAsync(highConfidenceEvent);
        
        var statistics = _service.GetStatistics();
        statistics.RejectedByConfidence.Should().Be(0); // Should pass with default settings
        statistics.OverlaysCleanedUp.Should().Be(1); // One region cleaned up
    }

    [Fact]
    public void UpdateCircuitBreakerSettings_WithInvalidValues_ShouldThrowException()
    {
        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.UpdateCircuitBreakerSettings(-0.1f, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.UpdateCircuitBreakerSettings(1.1f, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.UpdateCircuitBreakerSettings(0.5f, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => _service.UpdateCircuitBreakerSettings(0.5f, 101));
    }

    [Fact]
    public async Task CleanupOverlaysInRegionAsync_WithEmptyRegions_ShouldReturnZero()
    {
        // Arrange
        await _service.InitializeAsync();
        var emptyRegions = new List<Rectangle>();

        // Act
        var result = await _service.CleanupOverlaysInRegionAsync(IntPtr.Zero, emptyRegions);

        // Assert
        result.Should().Be(0);
        _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CleanupOverlaysInRegionAsync_WithNullRegions_ShouldReturnZero()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        var result = await _service.CleanupOverlaysInRegionAsync(IntPtr.Zero, null!);

        // Assert
        result.Should().Be(0);
    }

    [Fact]
    public async Task CleanupOverlaysInRegionAsync_WithValidRegions_ShouldCallOverlayManager()
    {
        // Arrange
        await _service.InitializeAsync();
        var regions = new List<Rectangle> { new(0, 0, 100, 100), new(200, 200, 150, 150) };
        var windowHandle = new IntPtr(54321);

        // Act
        var result = await _service.CleanupOverlaysInRegionAsync(windowHandle, regions);

        // Assert
        result.Should().Be(regions.Count);
        
        foreach (var region in regions)
        {
            _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(region, -1, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task GetStatistics_ShouldReturnAccurateStatistics()
    {
        // Arrange
        var initialStats = _service.GetStatistics();

        // Act - Process some events
        var highConfidenceEvent = CreateTestEvent(0.8f);
        var lowConfidenceEvent = CreateTestEvent(0.5f);

        await _service.InitializeAsync();
        await _service.HandleAsync(highConfidenceEvent);
        await _service.HandleAsync(lowConfidenceEvent);

        var finalStats = _service.GetStatistics();

        // Assert
        finalStats.TotalEventsProcessed.Should().Be(2);
        finalStats.RejectedByConfidence.Should().Be(1);
        finalStats.OverlaysCleanedUp.Should().BeGreaterThan(0);
        finalStats.LastEventProcessedAt.Should().NotBeNull();
    }

    [Fact]
    public void Priority_ShouldReturn100()
    {
        // Assert
        _service.Priority.Should().Be(100);
    }

    [Fact]
    public void SynchronousExecution_ShouldReturnFalse()
    {
        // Assert
        _service.SynchronousExecution.Should().BeFalse();
    }

    [Fact]
    public async Task Dispose_ShouldUnsubscribeFromEvents()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act
        _service.Dispose();

        // Assert
        _eventAggregatorMock.Verify(ea => ea.Unsubscribe<TextDisappearanceEvent>(_service), Times.Once);
    }

    [Fact]
    public async Task Dispose_MultipleCallsShould_NotThrow()
    {
        // Arrange
        await _service.InitializeAsync();

        // Act & Assert
        _service.Dispose();
        _service.Dispose(); // Second call should not throw
    }

    private static TextDisappearanceEvent CreateTestEvent(float confidenceScore)
    {
        return new TextDisappearanceEvent(
            regions: [new Rectangle(10, 10, 100, 100)],
            sourceWindow: IntPtr.Zero,
            regionId: "test-region",
            confidenceScore: confidenceScore
        );
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _service?.Dispose();
            _disposed = true;
        }
    }
}