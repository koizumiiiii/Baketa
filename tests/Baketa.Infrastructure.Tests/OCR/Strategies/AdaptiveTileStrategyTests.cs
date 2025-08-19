using System.Drawing;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using FluentAssertions;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.Strategies;
using Baketa.Infrastructure.Tests.TestUtilities;

namespace Baketa.Infrastructure.Tests.OCR.Strategies;

/// <summary>
/// AdaptiveTileStrategy単体テストクラス
/// テキスト境界分割回避機能の検証
/// </summary>
public class AdaptiveTileStrategyTests
{
    private readonly Mock<IOcrEngine> _mockOcrEngine;
    private readonly Mock<ILogger<AdaptiveTileStrategy>> _mockLogger;
    private readonly Mock<IAdvancedImage> _mockImage;
    private readonly AdaptiveTileStrategy _strategy;

    public AdaptiveTileStrategyTests()
    {
        _mockOcrEngine = new Mock<IOcrEngine>();
        _mockLogger = new Mock<ILogger<AdaptiveTileStrategy>>();
        _mockImage = new Mock<IAdvancedImage>();
        _strategy = new AdaptiveTileStrategy(_mockOcrEngine.Object, _mockLogger.Object);

        // デフォルト画像設定
        _mockImage.Setup(x => x.Width).Returns(2560);
        _mockImage.Setup(x => x.Height).Returns(1080);
        _mockImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(new byte[100]);
    }

    [Fact]
    public void Constructor_ValidParameters_SetsProperties()
    {
        // Act & Assert
        _strategy.StrategyName.Should().Be("AdaptiveTile");
        _strategy.Parameters.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullOcrEngine_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new AdaptiveTileStrategy(null!, _mockLogger.Object));
    }

    [Fact]
    public async Task GenerateRegionsAsync_WithTextDetection_ReturnsAdaptiveRegions()
    {
        // Arrange
        var textRegions = new List<OcrTextRegion>
        {
            new("第一のスープ", new Rectangle(100, 50, 200, 30), 0.95),
            new("メニュー", new Rectangle(120, 100, 150, 25), 0.90)
        };

        var ocrResult = new OcrResults(textRegions, _mockImage.Object, TimeSpan.FromMilliseconds(100), "ja");

        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(ocrResult);

        var options = new TileGenerationOptions
        {
            DefaultTileSize = 1024,
            EnableDebugCapture = false,
            MaxRegionCount = 20
        };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(region => 
        {
            region.RegionType.Should().Be(TileRegionType.TextAdaptive);
            region.RegionId.Should().StartWith("adaptive-");
            region.ConfidenceScore.Should().BeGreaterThan(0.0);
        });

        // テキストが検出された「第一のスープ」問題の解決確認
        var textRegion = result.FirstOrDefault(r => 
            r.Bounds.IntersectsWith(new Rectangle(100, 50, 200, 30)));
        textRegion.Should().NotBeNull("「第一のスープ」テキストを含む領域が生成されるべき");
    }

    [Fact]
    public async Task GenerateRegionsAsync_NoTextDetection_FallsBackToGrid()
    {
        // Arrange - テキスト検出失敗をシミュレート
        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(new OcrResults([], _mockImage.Object, TimeSpan.FromMilliseconds(10), "ja"));

        var options = new TileGenerationOptions
        {
            DefaultTileSize = 1024,
            EnableDebugCapture = false
        };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(region => 
        {
            region.RegionType.Should().Be(TileRegionType.Fallback);
            region.RegionId.Should().StartWith("fallback-");
        });
    }

    [Fact]
    public async Task GenerateRegionsAsync_EmptyTextRegions_FallsBackToGrid()
    {
        // Arrange - 空のテキスト検出結果
        var ocrResult = new OcrResults(new List<OcrTextRegion>(), _mockImage.Object, TimeSpan.FromMilliseconds(100), "ja");

        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(ocrResult);

        var options = new TileGenerationOptions { DefaultTileSize = 1024 };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(region => 
            region.RegionType.Should().Be(TileRegionType.Fallback));
    }

    [Fact]
    public async Task GenerateRegionsAsync_ExceptionDuringDetection_FallsBackGracefully()
    {
        // Arrange - OCRエンジンで例外発生
        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ThrowsAsync(new InvalidOperationException("OCR Engine Error"));

        var options = new TileGenerationOptions { DefaultTileSize = 1024 };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Should().NotBeEmpty();
        result.Should().AllSatisfy(region => 
            region.RegionType.Should().Be(TileRegionType.Fallback));

        // 警告ログ確認（例外は内部でキャッチされWarningになる）
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("テキスト検出エラー")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateRegionsAsync_MaxRegionCountExceeded_LimitsRegions()
    {
        // Arrange - 大量のテキスト領域
        var textRegions = Enumerable.Range(0, 30).Select(i => 
            new OcrTextRegion($"Text{i}", new Rectangle(i * 100, i * 50, 80, 20), 0.8)).ToList();

        var ocrResult = new OcrResults(textRegions, _mockImage.Object, TimeSpan.FromMilliseconds(100), "ja");

        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(ocrResult);

        var options = new TileGenerationOptions 
        { 
            DefaultTileSize = 1024,
            MaxRegionCount = 10 // 制限
        };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Count.Should().BeLessOrEqualTo(10);
        
        // 信頼度順でトリミングされていることを確認
        if (result.Count > 1)
        {
            result.Should().BeInDescendingOrder(r => r.ConfidenceScore);
        }
    }

    [Fact]
    public async Task GenerateRegionsAsync_WithDebugCapture_SavesDebugImages()
    {
        // Arrange
        var textRegions = new List<OcrTextRegion>
        {
            new("デバッグテスト", new Rectangle(100, 100, 200, 30), 0.95)
        };

        var ocrResult = new OcrResults(textRegions, _mockImage.Object, TimeSpan.FromMilliseconds(100), "ja");
        _mockOcrEngine.Setup(x => x.DetectTextRegionsAsync(It.IsAny<IAdvancedImage>(), It.IsAny<CancellationToken>()))
                     .ReturnsAsync(ocrResult);

        var options = new TileGenerationOptions
        {
            DefaultTileSize = 1024,
            EnableDebugCapture = true,
            DebugCapturePath = Path.GetTempPath()
        };

        // Act
        var result = await _strategy.GenerateRegionsAsync(_mockImage.Object, options);

        // Assert
        result.Should().NotBeEmpty();
        
        // デバッグログの確認
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("デバッグキャプチャ完了")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public void Parameters_CustomSettings_AppliedCorrectly()
    {
        // Arrange
        var customParameters = new TileStrategyParameters
        {
            MinBoundingBoxArea = 200,
            MinConfidenceThreshold = 0.7,
            LineGroupingYTolerance = 15,
            HorizontalMergingMaxDistance = 100,
            MaxRegionSizeRatio = 0.9
        };

        // Act
        _strategy.Parameters = customParameters;

        // Assert
        _strategy.Parameters.MinBoundingBoxArea.Should().Be(200);
        _strategy.Parameters.MinConfidenceThreshold.Should().Be(0.7);
        _strategy.Parameters.LineGroupingYTolerance.Should().Be(15);
        _strategy.Parameters.HorizontalMergingMaxDistance.Should().Be(100);
        _strategy.Parameters.MaxRegionSizeRatio.Should().Be(0.9);
    }

    [Fact]
    public async Task GenerateRegionsAsync_SmallImage_ReturnsSingleRegion()
    {
        // Arrange - 小さい画像
        var smallImage = new Mock<IAdvancedImage>();
        smallImage.Setup(x => x.Width).Returns(500);
        smallImage.Setup(x => x.Height).Returns(300);
        smallImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(new byte[50]);

        var options = new TileGenerationOptions { DefaultTileSize = 1024 };

        // テキスト検出失敗をシミュレート（フォールバックテスト）
        var emptyResult = MoqTestHelper.CreateTestOcrResults("", 0.0);
        MoqTestHelper.SetupOcrEngineDetectTextRegionsAsync(_mockOcrEngine, emptyResult);

        // Act
        var result = await _strategy.GenerateRegionsAsync(smallImage.Object, options);

        // Assert
        result.Should().HaveCount(1);
        result[0].RegionId.Should().Be("fallback-single");
        result[0].Bounds.Should().Be(new Rectangle(0, 0, 500, 300));
    }
}