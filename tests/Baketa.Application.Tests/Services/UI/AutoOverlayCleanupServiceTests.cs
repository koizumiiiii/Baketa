using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Application.Services.UI;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Processing; // [Issue #486] ITextChangeDetectionService用
using Baketa.Core.Abstractions.Services; // [Issue #525] ITranslationModeService用
using Baketa.Core.Abstractions.UI.Overlays; // 🔧 [OVERLAY_UNIFICATION] IOverlayManager 用
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
    // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
    private readonly Mock<IOverlayManager> _overlayManagerMock;
    private readonly Mock<IEventAggregator> _eventAggregatorMock;
    private readonly Mock<ILogger<AutoOverlayCleanupService>> _loggerMock;
    private readonly Mock<IOptionsMonitor<AutoOverlayCleanupSettings>> _settingsMock;
    private readonly AutoOverlayCleanupService _service;
    private bool _disposed;

    public AutoOverlayCleanupServiceTests()
    {
        // 🔧 [OVERLAY_UNIFICATION] IInPlaceTranslationOverlayManager → IOverlayManager に統一
        _overlayManagerMock = new Mock<IOverlayManager>();
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
        // 🔧 [OVERLAY_UNIFICATION] HideOverlaysInAreaAsync → HideAllAsync に変更
        _overlayManagerMock.Verify(om => om.HideAllAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        // 🔧 [OVERLAY_UNIFICATION] HideOverlaysInAreaAsync → HideAllAsync に変更
        _overlayManagerMock.Verify(om => om.HideAllAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        // [Issue #408] 領域指定削除: HideOverlaysInAreaAsync が各領域に対して呼ばれることを確認
        _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(
            It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()), Times.Exactly(regions.Count));
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
        // 🔧 [OVERLAY_UNIFICATION] HideOverlaysInAreaAsync → HideAllAsync に変更
        _overlayManagerMock.Verify(om => om.HideAllAsync(It.IsAny<CancellationToken>()), Times.Never);
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

        // [Issue #408] 領域指定削除: HideOverlaysInAreaAsync が各領域に対して呼ばれることを確認
        _overlayManagerMock.Verify(om => om.HideOverlaysInAreaAsync(
            It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()), Times.Exactly(regions.Count));
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

    #region [Issue #486] ScaleToOriginalWindowSize テスト

    /// <summary>
    /// captureImageSizeが正しく設定されている場合、
    /// DisappearedRegionsが正確にスケーリングされてHideOverlaysInAreaAsyncに渡される
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithCaptureImageSize_ShouldScaleRegionsCorrectly()
    {
        // Arrange
        await _service.InitializeAsync();

        // キャプチャ座標（1280x720空間）でのテキスト矩形
        var captureRegion = new Rectangle(480, 571, 60, 24);
        var eventData = new TextDisappearanceEvent(
            regions: [captureRegion],
            sourceWindow: new IntPtr(12345),
            regionId: "test-scaling",
            confidenceScore: 0.85f,
            originalWindowSize: new Size(3840, 2160),
            captureImageSize: new Size(1280, 720)
        );

        Rectangle capturedArea = default;
        _overlayManagerMock
            .Setup(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()))
            .Callback<Rectangle, int, CancellationToken>((area, _, _) => capturedArea = area)
            .Returns(Task.CompletedTask);

        // Act
        await _service.HandleAsync(eventData);

        // Assert: scaleX = 3840/1280 = 3.0, scaleY = 2160/720 = 3.0
        // (480*3, 571*3, 60*3, 24*3) = (1440, 1713, 180, 72)
        capturedArea.X.Should().Be(1440);
        capturedArea.Y.Should().Be(1713);
        capturedArea.Width.Should().Be(180);
        capturedArea.Height.Should().Be(72);
    }

    /// <summary>
    /// OriginalWindowSizeがEmptyの場合、スケーリングせずそのままの座標で削除
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithEmptyOriginalWindowSize_ShouldNotScale()
    {
        // Arrange
        await _service.InitializeAsync();

        var region = new Rectangle(100, 200, 50, 30);
        var eventData = new TextDisappearanceEvent(
            regions: [region],
            sourceWindow: new IntPtr(12345),
            regionId: "test-no-scale",
            confidenceScore: 0.85f,
            originalWindowSize: Size.Empty,
            captureImageSize: new Size(1280, 720)
        );

        Rectangle capturedArea = default;
        _overlayManagerMock
            .Setup(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()))
            .Callback<Rectangle, int, CancellationToken>((area, _, _) => capturedArea = area)
            .Returns(Task.CompletedTask);

        // Act
        await _service.HandleAsync(eventData);

        // Assert: スケーリングなし、元の座標のまま
        capturedArea.X.Should().Be(100);
        capturedArea.Y.Should().Be(200);
        capturedArea.Width.Should().Be(50);
        capturedArea.Height.Should().Be(30);
    }

    /// <summary>
    /// CaptureImageSizeとOriginalWindowSizeが同じ場合、スケーリング不要
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithSameCaptureAndOriginalSize_ShouldNotScale()
    {
        // Arrange
        await _service.InitializeAsync();

        var region = new Rectangle(500, 600, 200, 100);
        var eventData = new TextDisappearanceEvent(
            regions: [region],
            sourceWindow: new IntPtr(12345),
            regionId: "test-same-size",
            confidenceScore: 0.85f,
            originalWindowSize: new Size(1920, 1080),
            captureImageSize: new Size(1920, 1080)
        );

        Rectangle capturedArea = default;
        _overlayManagerMock
            .Setup(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()))
            .Callback<Rectangle, int, CancellationToken>((area, _, _) => capturedArea = area)
            .Returns(Task.CompletedTask);

        // Act
        await _service.HandleAsync(eventData);

        // Assert: サイズ同一のためスケーリングなし
        capturedArea.X.Should().Be(500);
        capturedArea.Y.Should().Be(600);
        capturedArea.Width.Should().Be(200);
        capturedArea.Height.Should().Be(100);
    }

    /// <summary>
    /// CaptureImageSizeが未設定でregions[0]が小さいテキスト矩形の場合、
    /// 異常なスケール倍率を防ぐためスケーリングをスキップする
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithEmptyCaptureImageSizeAndSmallRegion_ShouldSkipScaling()
    {
        // Arrange
        await _service.InitializeAsync();

        // CaptureImageSize未設定、regions[0]は小さいテキスト矩形
        var smallTextRegion = new Rectangle(480, 571, 60, 24);
        var eventData = new TextDisappearanceEvent(
            regions: [smallTextRegion],
            sourceWindow: new IntPtr(12345),
            regionId: "test-fallback-guard",
            confidenceScore: 0.85f,
            originalWindowSize: new Size(3840, 2160),
            captureImageSize: Size.Empty // 未設定
        );

        Rectangle capturedArea = default;
        _overlayManagerMock
            .Setup(om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()))
            .Callback<Rectangle, int, CancellationToken>((area, _, _) => capturedArea = area)
            .Returns(Task.CompletedTask);

        // Act
        await _service.HandleAsync(eventData);

        // Assert: 60 < 3840/4=960, 24 < 2160/4=540 → スケーリングスキップ
        // 元の座標がそのまま渡される（異常な64倍スケーリングは発生しない）
        capturedArea.X.Should().Be(480);
        capturedArea.Y.Should().Be(571);
        capturedArea.Width.Should().Be(60);
        capturedArea.Height.Should().Be(24);
    }

    /// <summary>
    /// テキスト安定性チェック: OCRが最近テキストを確認したゾーンはTextDisappearance削除を抑制
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithStableZone_ShouldSuppressCleanup()
    {
        // Arrange
        var textChangeDetectionMock = new Mock<ITextChangeDetectionService>();

        // zone_4_3が最近確認済み（1秒前）
        textChangeDetectionMock
            .Setup(s => s.GetLastTextConfirmation(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddSeconds(-1));

        var serviceWithTextDetection = new AutoOverlayCleanupService(
            _overlayManagerMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object,
            _settingsMock.Object,
            textChangeDetectionMock.Object);

        await serviceWithTextDetection.InitializeAsync();

        var eventData = new TextDisappearanceEvent(
            regions: [new Rectangle(480, 571, 60, 24)],
            sourceWindow: new IntPtr(12345),
            regionId: "test-stability",
            confidenceScore: 0.85f,
            originalWindowSize: new Size(3840, 2160),
            captureImageSize: new Size(1280, 720)
        );

        // Act
        await serviceWithTextDetection.HandleAsync(eventData);

        // Assert: 安定性チェックにより削除が抑制される
        _overlayManagerMock.Verify(
            om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        serviceWithTextDetection.Dispose();
    }

    /// <summary>
    /// テキスト安定性チェック: 安定性ウィンドウ外のゾーンは削除を許可
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithExpiredStability_ShouldAllowCleanup()
    {
        // Arrange
        var textChangeDetectionMock = new Mock<ITextChangeDetectionService>();

        // zone確認が6秒前（5秒のウィンドウ外）
        textChangeDetectionMock
            .Setup(s => s.GetLastTextConfirmation(It.IsAny<string>()))
            .Returns(DateTime.UtcNow.AddSeconds(-6));

        var serviceWithTextDetection = new AutoOverlayCleanupService(
            _overlayManagerMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object,
            _settingsMock.Object,
            textChangeDetectionMock.Object);

        await serviceWithTextDetection.InitializeAsync();

        var eventData = new TextDisappearanceEvent(
            regions: [new Rectangle(480, 571, 60, 24)],
            sourceWindow: new IntPtr(12345),
            regionId: "test-expired-stability",
            confidenceScore: 0.85f,
            originalWindowSize: new Size(3840, 2160),
            captureImageSize: new Size(1280, 720)
        );

        // Act
        await serviceWithTextDetection.HandleAsync(eventData);

        // Assert: ウィンドウ外なので削除が実行される
        _overlayManagerMock.Verify(
            om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()),
            Times.Once);

        serviceWithTextDetection.Dispose();
    }

    #endregion

    #region [Issue #525] Singleshotモード抑制テスト

    /// <summary>
    /// Singleshotモード中はTextDisappearanceEventを無視してオーバーレイを削除しない
    /// </summary>
    [Fact]
    public async Task HandleAsync_InSingleshotMode_ShouldIgnoreEvent()
    {
        // Arrange
        var translationModeMock = new Mock<ITranslationModeService>();
        translationModeMock.Setup(s => s.CurrentMode).Returns(TranslationMode.Singleshot);

        var service = new AutoOverlayCleanupService(
            _overlayManagerMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object,
            _settingsMock.Object,
            translationModeService: translationModeMock.Object);

        await service.InitializeAsync();

        var eventData = new TextDisappearanceEvent(
            regions: [new Rectangle(10, 10, 100, 100)],
            sourceWindow: new IntPtr(12345),
            regionId: "test-singleshot",
            confidenceScore: 0.9f
        );

        // Act
        await service.HandleAsync(eventData);

        // Assert: Singleshotモードのためオーバーレイ削除は呼ばれない
        _overlayManagerMock.Verify(
            om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // 統計にもカウントされない（モードガードはカウント前に返る）
        var stats = service.GetStatistics();
        stats.TotalEventsProcessed.Should().Be(0);

        service.Dispose();
    }

    /// <summary>
    /// Liveモード中はTextDisappearanceEventを通常通り処理する（回帰テスト）
    /// </summary>
    [Fact]
    public async Task HandleAsync_InLiveMode_ShouldProcessEvent()
    {
        // Arrange
        var translationModeMock = new Mock<ITranslationModeService>();
        translationModeMock.Setup(s => s.CurrentMode).Returns(TranslationMode.Live);

        var service = new AutoOverlayCleanupService(
            _overlayManagerMock.Object,
            _eventAggregatorMock.Object,
            _loggerMock.Object,
            _settingsMock.Object,
            translationModeService: translationModeMock.Object);

        await service.InitializeAsync();

        var eventData = new TextDisappearanceEvent(
            regions: [new Rectangle(10, 10, 100, 100)],
            sourceWindow: new IntPtr(12345),
            regionId: "test-live",
            confidenceScore: 0.9f
        );

        // Act
        await service.HandleAsync(eventData);

        // Assert: Liveモードのため通常通りオーバーレイ削除が実行される
        _overlayManagerMock.Verify(
            om => om.HideOverlaysInAreaAsync(It.IsAny<Rectangle>(), -1, It.IsAny<CancellationToken>()),
            Times.Once);

        service.Dispose();
    }

    #endregion

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
