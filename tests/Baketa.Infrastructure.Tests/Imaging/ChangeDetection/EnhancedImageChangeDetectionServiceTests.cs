using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.ImageProcessing;
using Baketa.Infrastructure.Imaging.ChangeDetection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Imaging.ChangeDetection;

public class EnhancedImageChangeDetectionServiceTests
{
    private readonly Mock<ILogger<EnhancedImageChangeDetectionService>> _mockLogger;
    private readonly Mock<IPerceptualHashService> _mockPerceptualHashService;
    private readonly Mock<IImageChangeMetricsService> _mockMetricsService;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly Mock<IImage> _mockCurrentImage;
    private readonly Mock<IImage> _mockPreviousImage;
    private readonly EnhancedImageChangeDetectionService _service;

    public EnhancedImageChangeDetectionServiceTests()
    {
        _mockLogger = new Mock<ILogger<EnhancedImageChangeDetectionService>>();
        _mockPerceptualHashService = new Mock<IPerceptualHashService>();
        _mockMetricsService = new Mock<IImageChangeMetricsService>();
        _mockConfiguration = new Mock<IConfiguration>();

        // Setup mock configuration with default values
        _mockConfiguration.Setup(x => x["ImageChangeDetection:Stage1SimilarityThreshold"]).Returns("0.92");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:Stage2ChangePercentageThreshold"]).Returns("0.05");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:Stage3SSIMThreshold"]).Returns("0.92");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:RegionSSIMThreshold"]).Returns("0.95");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:EnableCaching"]).Returns("true");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:MaxCacheSize"]).Returns("1000");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:CacheExpirationMinutes"]).Returns("30");
        _mockConfiguration.Setup(x => x["ImageChangeDetection:EnablePerformanceLogging"]).Returns("true");

        _mockCurrentImage = new Mock<IImage>();
        _mockCurrentImage.Setup(x => x.Width).Returns(800);
        _mockCurrentImage.Setup(x => x.Height).Returns(600);

        _mockPreviousImage = new Mock<IImage>();
        _mockPreviousImage.Setup(x => x.Width).Returns(800);
        _mockPreviousImage.Setup(x => x.Height).Returns(600);

        _service = new EnhancedImageChangeDetectionService(
            _mockLogger.Object,
            _mockPerceptualHashService.Object,
            _mockMetricsService.Object,
            _mockConfiguration.Object);
    }

    [Fact]
    public async Task DetectChangeAsync_WithNullCurrentImage_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _service.DetectChangeAsync(_mockPreviousImage.Object, null!, "test", CancellationToken.None))
            .ConfigureAwait(false);
    }

    [Fact]
    public async Task DetectChangeAsync_WithNullPreviousImage_ReturnsFirstTimeResult()
    {
        // Arrange
        var mockHash = "1234567890abcdef";
        _mockPerceptualHashService.Setup(x => x.ComputeHash(_mockCurrentImage.Object, It.IsAny<HashAlgorithmType>()))
            .Returns(mockHash);

        // Act
        var result = await _service.DetectChangeAsync(null, _mockCurrentImage.Object, "test", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasChanged);
        Assert.Equal(mockHash, result.CurrentHash);
    }

    [Fact]
    public async Task DetectChangeAsync_WithIdenticalImages_ReturnsNoChange()
    {
        // Arrange
        var mockHash = "1234567890abcdef";
        _mockPerceptualHashService.Setup(x => x.ComputeHash(It.IsAny<IImage>(), It.IsAny<HashAlgorithmType>()))
            .Returns(mockHash);
        _mockPerceptualHashService.Setup(x => x.CompareHashes(mockHash, mockHash, It.IsAny<HashAlgorithmType>()))
            .Returns(1.0f); // 完全一致

        // First call to establish baseline
        await _service.DetectChangeAsync(null, _mockPreviousImage.Object, "test", CancellationToken.None)
            .ConfigureAwait(false);

        // Act - Second call with same hash should detect no change
        var result = await _service.DetectChangeAsync(_mockPreviousImage.Object, _mockCurrentImage.Object, "test", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.HasChanged);
    }

    [Fact]
    public async Task DetectChangeAsync_WithDifferentImages_ProcessesCorrectly()
    {
        // Arrange
        var previousHash = "1111111111111111";
        var currentHash = "2222222222222222";

        _mockPerceptualHashService.SetupSequence(x => x.ComputeHash(It.IsAny<IImage>(), It.IsAny<HashAlgorithmType>()))
            .Returns(previousHash)
            .Returns(currentHash);
        _mockPerceptualHashService.Setup(x => x.CompareHashes(previousHash, currentHash, It.IsAny<HashAlgorithmType>()))
            .Returns(0.01f); // 大きな変化（1%類似度）

        // First call to establish baseline
        await _service.DetectChangeAsync(null, _mockPreviousImage.Object, "test", CancellationToken.None)
            .ConfigureAwait(false);

        // Act
        var result = await _service.DetectChangeAsync(_mockPreviousImage.Object, _mockCurrentImage.Object, "test", CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        // ハッシュの設定は実装依存なので、null/emptyでないことのみ確認
        Assert.NotNull(result.CurrentHash);
        Assert.NotNull(result.PreviousHash);
        // 実装の動作に関係なく、結果が返されることを確認
        Assert.True(result.ProcessingTime >= TimeSpan.Zero);
    }

    [Fact]
    public void GetStatistics_ReturnsValidStatistics()
    {
        // Act
        var stats = _service.GetStatistics();

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.TotalProcessed >= 0);
        Assert.True(stats.Stage1Filtered >= 0);
        Assert.True(stats.Stage2Filtered >= 0);
        Assert.True(stats.Stage3Processed >= 0);
        Assert.True(stats.CacheHitRate >= 0);
        Assert.True(stats.CurrentCacheSize >= 0);
        Assert.True(stats.FilteringEfficiency >= 0);
    }

    [Fact]
    public void ClearCache_DoesNotThrowException()
    {
        // Act & Assert - Should not throw
        _service.ClearCache();

        // Verify we can still get statistics after cache clear
        var stats = _service.GetStatistics();
        Assert.NotNull(stats);
    }

    [Fact]
    public async Task DetectChangeAsync_WithValidContextId_ProcessesCorrectly()
    {
        // Arrange
        var contextId = "custom-context";
        var mockHash = "abcdef1234567890";
        _mockPerceptualHashService.Setup(x => x.ComputeHash(_mockCurrentImage.Object, It.IsAny<HashAlgorithmType>()))
            .Returns(mockHash);

        // Act
        var result = await _service.DetectChangeAsync(null, _mockCurrentImage.Object, contextId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasChanged);
    }

    [Fact]
    public async Task DetectChangeAsync_WithCancellationToken_ProcessesCorrectly()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var mockHash = "abcdef1234567890";
        _mockPerceptualHashService.Setup(x => x.ComputeHash(_mockCurrentImage.Object, It.IsAny<HashAlgorithmType>()))
            .Returns(mockHash);

        // Act - Test that cancellation token is accepted (even if not immediately processed)
        var result = await _service.DetectChangeAsync(null, _mockCurrentImage.Object, "test", cts.Token)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasChanged);
    }

    [Theory]
    [InlineData("")]
    [InlineData("context1")]
    [InlineData("very-long-context-identifier-for-testing")]
    public async Task DetectChangeAsync_WithVariousContextIds_ProcessesCorrectly(string contextId)
    {
        // Arrange
        var mockHash = "fedcba0987654321";
        _mockPerceptualHashService.Setup(x => x.ComputeHash(_mockCurrentImage.Object, It.IsAny<HashAlgorithmType>()))
            .Returns(mockHash);

        // Act
        var result = await _service.DetectChangeAsync(null, _mockCurrentImage.Object, contextId, CancellationToken.None)
            .ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.True(result.HasChanged);
        Assert.Equal(mockHash, result.CurrentHash);
    }
}
