using System.Drawing;
using System.Reflection;
using Baketa.Application.Services.OCR;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Application.Tests.Services.OCR;

/// <summary>
/// ParallelOcrExecutorの単体テスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class ParallelOcrExecutorTests : IDisposable
{
    private readonly Mock<IOcrEngine> _mockOcrEngine;
    private readonly Mock<IImageProcessingService> _mockImageProcessingService;
    private readonly Mock<ILogger<ParallelOcrExecutor>> _mockLogger;
    private readonly ParallelOcrExecutor _executor;

    public ParallelOcrExecutorTests()
    {
        _mockOcrEngine = new Mock<IOcrEngine>();
        _mockImageProcessingService = new Mock<IImageProcessingService>();
        _mockLogger = new Mock<ILogger<ParallelOcrExecutor>>();

        _executor = new ParallelOcrExecutor(
            _mockOcrEngine.Object,
            _mockImageProcessingService.Object,
            _mockLogger.Object);
    }

    public void Dispose()
    {
        _executor.Dispose();
        GC.SuppressFinalize(this);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullOcrEngine_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelOcrExecutor(
            null!,
            _mockImageProcessingService.Object,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullImageProcessingService_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelOcrExecutor(
            _mockOcrEngine.Object,
            null!,
            _mockLogger.Object));
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ParallelOcrExecutor(
            _mockOcrEngine.Object,
            _mockImageProcessingService.Object,
            null!));
    }

    [Fact]
    public void Constructor_WithValidParameters_InitializesWithDefaultSettings()
    {
        // Arrange & Act
        using var executor = new ParallelOcrExecutor(
            _mockOcrEngine.Object,
            _mockImageProcessingService.Object,
            _mockLogger.Object);

        // Assert
        var settings = executor.GetSettings();
        settings.Should().NotBeNull();
        settings.MaxParallelism.Should().Be(4);
        settings.TileColumnsCount.Should().Be(2);
        settings.TileRowsCount.Should().Be(2);
        settings.TileOverlapPixels.Should().Be(20);
        settings.EnableParallelOcr.Should().BeTrue();
        settings.MinImageSizeForParallel.Should().Be(640 * 480);
    }

    #endregion

    #region GetSettings / UpdateSettings Tests

    [Fact]
    public void GetSettings_ReturnsCurrentSettings()
    {
        // Act
        var settings = _executor.GetSettings();

        // Assert
        settings.Should().NotBeNull();
    }

    [Fact]
    public void UpdateSettings_WithValidSettings_UpdatesSettings()
    {
        // Arrange
        var newSettings = new ParallelOcrSettings
        {
            MaxParallelism = 8,
            TileColumnsCount = 3,
            TileRowsCount = 3,
            TileOverlapPixels = 30,
            EnableParallelOcr = false
        };

        // Act
        _executor.UpdateSettings(newSettings);

        // Assert
        var currentSettings = _executor.GetSettings();
        currentSettings.MaxParallelism.Should().Be(8);
        currentSettings.TileColumnsCount.Should().Be(3);
        currentSettings.TileRowsCount.Should().Be(3);
        currentSettings.TileOverlapPixels.Should().Be(30);
        currentSettings.EnableParallelOcr.Should().BeFalse();
    }

    [Fact]
    public void UpdateSettings_WithNullSettings_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _executor.UpdateSettings(null!));
    }

    [Fact]
    public void UpdateSettings_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        _executor.Dispose();

        // Act & Assert
        Assert.Throws<ObjectDisposedException>(() => _executor.UpdateSettings(new ParallelOcrSettings()));
    }

    #endregion

    #region ExecuteParallelOcrAsync Tests

    [Fact]
    public async Task ExecuteParallelOcrAsync_WithNullImage_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _executor.ExecuteParallelOcrAsync(null!));
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);
        mockImage.Setup(x => x.Height).Returns(100);
        _executor.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            _executor.ExecuteParallelOcrAsync(mockImage.Object));
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_WithSmallImage_UsesSingleOcr()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);  // 100x100 = 10,000 < MinImageSizeForParallel
        mockImage.Setup(x => x.Height).Returns(100);

        var expectedResult = new OcrResults(
            new List<OcrTextRegion>(),
            mockImage.Object,
            TimeSpan.FromMilliseconds(100),
            "en");

        _mockOcrEngine
            .Setup(x => x.RecognizeAsync(mockImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _executor.ExecuteParallelOcrAsync(mockImage.Object);

        // Assert
        result.Should().BeSameAs(expectedResult);
        _mockOcrEngine.Verify(x => x.RecognizeAsync(mockImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
        _mockImageProcessingService.Verify(x => x.CropImageAsync(It.IsAny<IImage>(), It.IsAny<Rectangle>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_WithDisabledParallelOcr_UsesSingleOcr()
    {
        // Arrange
        _executor.UpdateSettings(new ParallelOcrSettings { EnableParallelOcr = false });

        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(1920);
        mockImage.Setup(x => x.Height).Returns(1080);

        var expectedResult = new OcrResults(
            new List<OcrTextRegion>(),
            mockImage.Object,
            TimeSpan.FromMilliseconds(100),
            "en");

        _mockOcrEngine
            .Setup(x => x.RecognizeAsync(mockImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        // Act
        var result = await _executor.ExecuteParallelOcrAsync(mockImage.Object);

        // Assert
        result.Should().BeSameAs(expectedResult);
        _mockOcrEngine.Verify(x => x.RecognizeAsync(mockImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_WithLargeImage_UsesParallelOcr()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(1920);  // 1920x1080 > MinImageSizeForParallel
        mockImage.Setup(x => x.Height).Returns(1080);

        var mockTileImage = new Mock<IImage>();
        var tileResult = new OcrResults(
            new List<OcrTextRegion>(),
            mockTileImage.Object,
            TimeSpan.FromMilliseconds(50),
            "en");

        _mockImageProcessingService
            .Setup(x => x.CropImageAsync(mockImage.Object, It.IsAny<Rectangle>()))
            .ReturnsAsync(mockTileImage.Object);

        _mockOcrEngine
            .Setup(x => x.RecognizeAsync(mockTileImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tileResult);

        // Act
        var result = await _executor.ExecuteParallelOcrAsync(mockImage.Object);

        // Assert
        result.Should().NotBeNull();
        // 2x2 tiles = 4 calls
        _mockImageProcessingService.Verify(x => x.CropImageAsync(mockImage.Object, It.IsAny<Rectangle>()), Times.Exactly(4));
        _mockOcrEngine.Verify(x => x.RecognizeAsync(mockTileImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_WithCancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(1920);
        mockImage.Setup(x => x.Height).Returns(1080);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var mockTileImage = new Mock<IImage>();
        _mockImageProcessingService
            .Setup(x => x.CropImageAsync(mockImage.Object, It.IsAny<Rectangle>()))
            .ReturnsAsync(mockTileImage.Object);

        // Act & Assert
        // TaskCanceledException is derived from OperationCanceledException
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _executor.ExecuteParallelOcrAsync(mockImage.Object, cancellationToken: cts.Token));
        exception.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteParallelOcrAsync_ReportsProgress()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(1920);
        mockImage.Setup(x => x.Height).Returns(1080);

        var mockTileImage = new Mock<IImage>();
        var tileResult = new OcrResults(
            new List<OcrTextRegion>(),
            mockTileImage.Object,
            TimeSpan.FromMilliseconds(50),
            "en");

        _mockImageProcessingService
            .Setup(x => x.CropImageAsync(mockImage.Object, It.IsAny<Rectangle>()))
            .ReturnsAsync(mockTileImage.Object);

        _mockOcrEngine
            .Setup(x => x.RecognizeAsync(mockTileImage.Object, It.IsAny<IProgress<OcrProgress>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(tileResult);

        var progressReports = new List<OcrProgress>();
        var progress = new Progress<OcrProgress>(p => progressReports.Add(p));

        // Act
        await _executor.ExecuteParallelOcrAsync(mockImage.Object, progress);

        // Assert - Wait for progress reports to be captured
        await Task.Delay(100);
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Phase == OcrPhase.Initializing);
    }

    #endregion

    #region CalculateTileRegions Tests (via Reflection)

    [Fact]
    public void CalculateTileRegions_With2x2Tiles_Returns4Regions()
    {
        // Arrange
        var settings = new ParallelOcrSettings
        {
            TileColumnsCount = 2,
            TileRowsCount = 2,
            TileOverlapPixels = 0
        };

        // Act
        var regions = InvokeCalculateTileRegions(1920, 1080, settings);

        // Assert
        regions.Should().HaveCount(4);
    }

    [Fact]
    public void CalculateTileRegions_WithOverlap_RegionsOverlap()
    {
        // Arrange
        var settings = new ParallelOcrSettings
        {
            TileColumnsCount = 2,
            TileRowsCount = 2,
            TileOverlapPixels = 20
        };

        // Act
        var regions = InvokeCalculateTileRegions(1000, 1000, settings);

        // Assert
        regions.Should().HaveCount(4);

        // 基本タイルサイズは500x500
        // オーバーラップ20pxにより、隣接タイルは重なるはず
        var topLeft = regions[0];
        var topRight = regions[1];

        // topRightの開始X座標はオーバーラップを考慮して左にずれる
        topRight.X.Should().BeLessThan(500); // 500 - 20 = 480
    }

    [Fact]
    public void CalculateTileRegions_With3x3Tiles_Returns9Regions()
    {
        // Arrange
        var settings = new ParallelOcrSettings
        {
            TileColumnsCount = 3,
            TileRowsCount = 3,
            TileOverlapPixels = 10
        };

        // Act
        var regions = InvokeCalculateTileRegions(900, 900, settings);

        // Assert
        regions.Should().HaveCount(9);
    }

    [Fact]
    public void CalculateTileRegions_RegionsCoverEntireImage()
    {
        // Arrange
        var settings = new ParallelOcrSettings
        {
            TileColumnsCount = 2,
            TileRowsCount = 2,
            TileOverlapPixels = 20
        };
        const int imageWidth = 1000;
        const int imageHeight = 800;

        // Act
        var regions = InvokeCalculateTileRegions(imageWidth, imageHeight, settings);

        // Assert
        // すべてのピクセルが少なくとも1つの領域でカバーされていることを確認
        foreach (var region in regions)
        {
            region.X.Should().BeGreaterOrEqualTo(0);
            region.Y.Should().BeGreaterOrEqualTo(0);
            (region.X + region.Width).Should().BeLessOrEqualTo(imageWidth);
            (region.Y + region.Height).Should().BeLessOrEqualTo(imageHeight);
        }
    }

    private static List<Rectangle> InvokeCalculateTileRegions(int imageWidth, int imageHeight, ParallelOcrSettings settings)
    {
        var method = typeof(ParallelOcrExecutor).GetMethod(
            "CalculateTileRegions",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (List<Rectangle>)method!.Invoke(null, [imageWidth, imageHeight, settings])!;
    }

    #endregion

    #region IsSimilarText Tests (via Reflection)

    [Theory]
    [InlineData("Hello", "Hello", true)]  // 完全一致
    [InlineData("Hello World", "Hello", true)]  // 含む
    [InlineData("Hello", "Hello World", true)]  // 含まれる
    [InlineData("Hello", "Hallo", true)]  // 編集距離1 (20%以下)
    [InlineData("Hello", "Goodbye", false)]  // 全く異なる
    [InlineData("", "Hello", false)]  // 空文字列
    [InlineData("Hello", "", false)]  // 空文字列
    [InlineData(null, "Hello", false)]  // null
    [InlineData("Hello", null, false)]  // null
    public void IsSimilarText_ReturnsExpectedResult(string? text1, string? text2, bool expected)
    {
        // Act
        var result = InvokeIsSimilarText(text1!, text2!);

        // Assert
        result.Should().Be(expected);
    }

    private static bool InvokeIsSimilarText(string text1, string text2)
    {
        var method = typeof(ParallelOcrExecutor).GetMethod(
            "IsSimilarText",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (bool)method!.Invoke(null, [text1, text2])!;
    }

    #endregion

    #region IsOverlapping Tests (via Reflection)

    [Fact]
    public void IsOverlapping_WithCompleteOverlap_ReturnsTrue()
    {
        // Arrange
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(0, 0, 100, 100);  // 同一領域

        // Act
        var result = InvokeIsOverlapping(r1, r2);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void IsOverlapping_WithNoOverlap_ReturnsFalse()
    {
        // Arrange
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(200, 200, 100, 100);  // 完全に離れている

        // Act
        var result = InvokeIsOverlapping(r1, r2);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void IsOverlapping_WithPartialOverlapBelow50Percent_ReturnsFalse()
    {
        // Arrange
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(80, 0, 100, 100);  // 20%重複 (20x100 / (100x100 + 100x100 - 2000))

        // Act
        var result = InvokeIsOverlapping(r1, r2);

        // Assert
        result.Should().BeFalse();  // IoU < 0.5
    }

    [Fact]
    public void IsOverlapping_WithPartialOverlapAbove50Percent_ReturnsTrue()
    {
        // Arrange
        var r1 = new Rectangle(0, 0, 100, 100);
        var r2 = new Rectangle(20, 0, 100, 100);  // 80%重複

        // Act
        var result = InvokeIsOverlapping(r1, r2);

        // Assert
        result.Should().BeTrue();  // IoU > 0.5
    }

    private static bool InvokeIsOverlapping(Rectangle r1, Rectangle r2)
    {
        var method = typeof(ParallelOcrExecutor).GetMethod(
            "IsOverlapping",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (bool)method!.Invoke(null, [r1, r2])!;
    }

    #endregion

    #region LevenshteinDistance Tests (via Reflection)

    [Theory]
    [InlineData("", "", 0)]
    [InlineData("a", "", 1)]
    [InlineData("", "a", 1)]
    [InlineData("abc", "abc", 0)]
    [InlineData("abc", "abd", 1)]  // 1文字置換
    [InlineData("abc", "ab", 1)]   // 1文字削除
    [InlineData("abc", "abcd", 1)] // 1文字挿入
    [InlineData("kitten", "sitting", 3)]  // 有名な例
    public void LevenshteinDistance_ReturnsExpectedResult(string s1, string s2, int expected)
    {
        // Act
        var result = InvokeLevenshteinDistance(s1, s2);

        // Assert
        result.Should().Be(expected);
    }

    private static int InvokeLevenshteinDistance(string s1, string s2)
    {
        var method = typeof(ParallelOcrExecutor).GetMethod(
            "LevenshteinDistance",
            BindingFlags.NonPublic | BindingFlags.Static);

        return (int)method!.Invoke(null, [s1, s2])!;
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        // Arrange
        using var executor = new ParallelOcrExecutor(
            _mockOcrEngine.Object,
            _mockImageProcessingService.Object,
            _mockLogger.Object);

        // Act & Assert - Should not throw
        executor.Dispose();
        executor.Dispose();
        executor.Dispose();
    }

    #endregion

    #region Thread Safety Tests

    [Fact]
    public async Task UpdateSettings_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();

        // Act - 複数スレッドから同時に設定更新
        for (int i = 0; i < 100; i++)
        {
            var parallelism = i % 10 + 1;
            tasks.Add(Task.Run(() => _executor.UpdateSettings(new ParallelOcrSettings
            {
                MaxParallelism = parallelism
            })));
        }

        // Assert - 例外なく完了すること
        await Task.WhenAll(tasks);
        var settings = _executor.GetSettings();
        settings.MaxParallelism.Should().BeInRange(1, 10);
    }

    [Fact]
    public async Task GetSettings_ConcurrentCalls_ThreadSafe()
    {
        // Arrange
        var tasks = new List<Task<ParallelOcrSettings>>();

        // Act - 複数スレッドから同時に設定取得
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => _executor.GetSettings()));
        }

        // Assert - すべて有効な設定を返すこと
        var results = await Task.WhenAll(tasks);
        foreach (var settings in results)
        {
            settings.Should().NotBeNull();
            settings.MaxParallelism.Should().BeGreaterThan(0);
        }
    }

    #endregion
}
