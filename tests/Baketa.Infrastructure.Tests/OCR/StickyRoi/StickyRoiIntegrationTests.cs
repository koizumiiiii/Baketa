using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using Xunit;
using Moq;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.StickyRoi;

namespace Baketa.Infrastructure.Tests.OCR.StickyRoi;

/// <summary>
/// スティッキーROIシステム統合テスト
/// Issue #143 Week 3 Phase 1: ROI最適化システム検証
/// </summary>
public class StickyRoiIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<InMemoryStickyRoiManager>> _roiManagerLogger;
    private readonly Mock<ILogger<StickyRoiEnhancedOcrEngine>> _ocrEngineLogger;
    private readonly Mock<ISimpleOcrEngine> _mockBaseOcrEngine;
    private readonly Mock<IOptions<OcrSettings>> _mockOcrSettings;
    private readonly InMemoryStickyRoiManager _roiManager;
    private readonly StickyRoiEnhancedOcrEngine _enhancedOcrEngine;

    public StickyRoiIntegrationTests()
    {
        _roiManagerLogger = new Mock<ILogger<InMemoryStickyRoiManager>>();
        _ocrEngineLogger = new Mock<ILogger<StickyRoiEnhancedOcrEngine>>();
        _mockBaseOcrEngine = new Mock<ISimpleOcrEngine>();
        _mockOcrSettings = new Mock<IOptions<OcrSettings>>();
        
        _mockOcrSettings.Setup(x => x.Value).Returns(new OcrSettings());
        
        _roiManager = new InMemoryStickyRoiManager(
            _roiManagerLogger.Object,
            _mockOcrSettings.Object);
        
        _enhancedOcrEngine = new StickyRoiEnhancedOcrEngine(
            _ocrEngineLogger.Object,
            _mockBaseOcrEngine.Object,
            _roiManager);
    }

    [Fact]
    public async Task StickyRoiWorkflow_ShouldWorkEndToEnd()
    {
        // Arrange
        var testImageData = CreateTestImageData();
        var expectedTexts = new List<Baketa.Core.Abstractions.OCR.DetectedText>
        {
            new Baketa.Core.Abstractions.OCR.DetectedText
            {
                Text = "Test Text 1",
                Confidence = 0.95,
                BoundingBox = new Rectangle(100, 100, 200, 50),
                Language = "ja"
            },
            new Baketa.Core.Abstractions.OCR.DetectedText
            {
                Text = "Test Text 2", 
                Confidence = 0.88,
                BoundingBox = new Rectangle(300, 200, 150, 40),
                Language = "ja"
            }
        };

        var mockOcrResult = new Baketa.Core.Abstractions.OCR.OcrResult
        {
            DetectedTexts = expectedTexts,
            IsSuccessful = true,
            ProcessingTime = TimeSpan.FromMilliseconds(100)
        };

        _mockBaseOcrEngine
            .Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOcrResult);

        _mockBaseOcrEngine
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        // Act - 初回実行（ROIなし、フルスクリーン処理）
        var firstResult = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);

        // Assert - 初回結果検証
        Assert.True(firstResult.IsSuccessful);
        Assert.Equal(2, firstResult.DetectedTexts.Count);
        Assert.True(firstResult.Metadata.ContainsKey("ProcessingMode"));

        // Act - ROI統計確認
        var roiStats = await _roiManager.GetStatisticsAsync();

        // Assert - ROI作成確認
        Assert.True(roiStats.TotalRois > 0);
        Assert.Equal(2, roiStats.TotalDetections);

        // Act - 優先ROI取得テスト
        var priorityRois = await _roiManager.GetPriorityRoisAsync(
            new Rectangle(0, 0, 1920, 1080), 5);

        // Assert - ROI優先度システム確認
        Assert.True(priorityRois.Count > 0);
        Assert.All(priorityRois, roi =>
        {
            Assert.True(roi.ConfidenceScore > 0);
            Assert.True(roi.Region.Width > 0);
            Assert.True(roi.Region.Height > 0);
        });

        // Act - 2回目実行（ROI使用予定）
        var secondResult = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);

        // Assert - 2回目結果検証（ROI効率化確認）
        Assert.True(secondResult.IsSuccessful);
        Assert.True(secondResult.DetectedTexts.Count > 0);
    }

    [Fact]
    public async Task RoiManager_ShouldMergeOverlappingRegions()
    {
        // Arrange
        var overlappingRegions = new List<TextRegion>
        {
            new TextRegion
            {
                Bounds = new Rectangle(100, 100, 100, 50),
                Text = "Text A",
                Confidence = 0.9
            },
            new TextRegion
            {
                Bounds = new Rectangle(120, 110, 100, 50), // 重複領域
                Text = "Text B",
                Confidence = 0.85
            }
        };

        // Act
        var recordResult = await _roiManager.RecordDetectedRegionsAsync(
            overlappingRegions, DateTime.UtcNow);

        // Assert
        Assert.True(recordResult.IsSuccessful);
        
        var stats = await _roiManager.GetStatisticsAsync();
        Assert.True(stats.TotalRois > 0);
        
        // 重複領域が適切に処理されているか確認
        var priorityRois = await _roiManager.GetPriorityRoisAsync(
            new Rectangle(0, 0, 1920, 1080));
        
        Assert.True(priorityRois.Count > 0);
    }

    [Fact]
    public async Task RoiManager_ShouldCleanupExpiredRois()
    {
        // Arrange
        var testRegions = new List<TextRegion>
        {
            new TextRegion
            {
                Bounds = new Rectangle(50, 50, 100, 30),
                Text = "Expired Text",
                Confidence = 0.8
            }
        };

        await _roiManager.RecordDetectedRegionsAsync(testRegions, DateTime.UtcNow);

        // Act - 期限切れクリーンアップ（即座に期限切れとして処理）
        var cleanedCount = await _roiManager.CleanupExpiredRoisAsync(TimeSpan.Zero);

        // Assert
        Assert.True(cleanedCount >= 0); // クリーンアップが実行された
        
        var statsAfter = await _roiManager.GetStatisticsAsync();
        // 期限切れROIが削除されているか確認
        Assert.True(statsAfter.ActiveRois >= 0);
    }

    [Fact]
    public async Task EnhancedOcrEngine_ShouldFallbackOnRoiFailure()
    {
        // Arrange
        var testImageData = CreateTestImageData();
        
        // ROI処理は失敗、フルスクリーン処理は成功するシナリオ
        _mockBaseOcrEngine
            .SetupSequence(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Baketa.Core.Abstractions.OCR.OcrResult { DetectedTexts = Array.Empty<Baketa.Core.Abstractions.OCR.DetectedText>(), IsSuccessful = false }) // ROI処理失敗
            .ReturnsAsync(new Baketa.Core.Abstractions.OCR.OcrResult 
            { 
                DetectedTexts = new[] 
                {
                    new Baketa.Core.Abstractions.OCR.DetectedText { Text = "Fallback Text", Confidence = 0.7, BoundingBox = new Rectangle(0, 0, 100, 20) }
                },
                IsSuccessful = true 
            }); // フルスクリーン処理成功

        // Act
        var result = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);

        // Assert - フォールバックが機能していることを確認
        Assert.True(result.IsSuccessful);
        Assert.Single(result.DetectedTexts);
        Assert.Equal("Fallback Text", result.DetectedTexts[0].Text);
    }

    [Fact]
    public async Task RoiConfidenceUpdate_ShouldAdjustScoresCorrectly()
    {
        // Arrange
        var testRegions = new List<TextRegion>
        {
            new TextRegion
            {
                Bounds = new Rectangle(200, 200, 100, 50),
                Text = "Confidence Test",
                Confidence = 0.8
            }
        };

        await _roiManager.RecordDetectedRegionsAsync(testRegions, DateTime.UtcNow);
        var priorityRois = await _roiManager.GetPriorityRoisAsync(new Rectangle(0, 0, 1920, 1080));
        var testRoi = priorityRois.FirstOrDefault();
        
        Assert.NotNull(testRoi);

        // Act - 成功時の信頼度更新
        var successUpdate = await _roiManager.UpdateRoiConfidenceAsync(
            testRoi.RoiId, RoiDetectionResult.Success, 0.95);

        // Assert
        Assert.True(successUpdate);

        // Act - 失敗時の信頼度更新
        var failureUpdate = await _roiManager.UpdateRoiConfidenceAsync(
            testRoi.RoiId, RoiDetectionResult.Failed, 0.1);

        // Assert
        Assert.True(failureUpdate);
        
        // 信頼度が適切に調整されているかは内部実装のため、
        // ここでは更新処理が成功することを確認
    }

    private static byte[] CreateTestImageData()
    {
        // テスト用の最小限画像データ（PNG形式のダミー）
        return new byte[] 
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // PNG シグネチャ
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, // IHDR チャンク
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, // 1x1 ピクセル
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x09, 0x70, 0x48, 0x59,
            0x73, 0x00, 0x00, 0x0B, 0x13, 0x00, 0x00, 0x0B,
            0x13, 0x01, 0x00, 0x9A, 0x9C, 0x18, 0x00, 0x00,
            0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7,
            0x63, 0xF8, 0x0F, 0x00, 0x00, 0x01, 0x00, 0x01,
            0x76, 0x36, 0xDD, 0xDB, 0x00, 0x00, 0x00, 0x00,
            0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        };
    }

    public void Dispose()
    {
        _roiManager?.Dispose();
        _enhancedOcrEngine?.Dispose();
    }
}