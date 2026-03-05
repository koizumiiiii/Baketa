using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Processing;
using Baketa.Core.Models.Processing;
using Baketa.Infrastructure.Processing.Strategies;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Processing.Strategies;

/// <summary>
/// [Issue #501] TextChangeDetectionStageStrategy の領域単位タイプライター判定テスト
/// </summary>
public class TextChangeDetectionStageStrategyTypewriterTests
{
    private readonly Mock<ITextChangeDetectionService> _mockTextChangeService;
    private readonly Mock<IOptionsMonitor<ProcessingPipelineSettings>> _mockSettings;
    private readonly Mock<ILogger<TextChangeDetectionStageStrategy>> _mockLogger;
    private readonly TextChangeDetectionStageStrategy _strategy;

    public TextChangeDetectionStageStrategyTypewriterTests()
    {
        _mockTextChangeService = new Mock<ITextChangeDetectionService>();
        _mockSettings = new Mock<IOptionsMonitor<ProcessingPipelineSettings>>();
        _mockLogger = new Mock<ILogger<TextChangeDetectionStageStrategy>>();

        var settings = new ProcessingPipelineSettings { TextChangeThreshold = 0.1f };
        _mockSettings.Setup(x => x.CurrentValue).Returns(settings);

        _strategy = new TextChangeDetectionStageStrategy(
            _mockTextChangeService.Object,
            _mockSettings.Object,
            _mockLogger.Object);
    }

    #region CalculateIoU Tests

    [Fact]
    public void CalculateIoU_FullOverlap_Returns1()
    {
        // Arrange
        var rect = new Rectangle(100, 100, 200, 50);

        // Act
        var iou = TextChangeDetectionStageStrategy.CalculateIoU(rect, rect);

        // Assert
        Assert.Equal(1.0f, iou, precision: 3);
    }

    [Fact]
    public void CalculateIoU_NoOverlap_Returns0()
    {
        // Arrange
        var a = new Rectangle(0, 0, 100, 50);
        var b = new Rectangle(200, 200, 100, 50);

        // Act
        var iou = TextChangeDetectionStageStrategy.CalculateIoU(a, b);

        // Assert
        Assert.Equal(0.0f, iou);
    }

    [Fact]
    public void CalculateIoU_PartialOverlap_ReturnsCorrectValue()
    {
        // Arrange: 50% horizontal overlap
        // a: (0,0)-(200,100) area=20000
        // b: (100,0)-(300,100) area=20000
        // intersection: (100,0)-(200,100) area=10000
        // union: 20000+20000-10000=30000
        // IoU: 10000/30000 = 0.333...
        var a = new Rectangle(0, 0, 200, 100);
        var b = new Rectangle(100, 0, 200, 100);

        // Act
        var iou = TextChangeDetectionStageStrategy.CalculateIoU(a, b);

        // Assert
        Assert.Equal(1f / 3f, iou, precision: 3);
    }

    #endregion

    #region Typewriter Detection Integration Tests

    [Fact]
    public async Task TypewriterDetection_SameRegionTextGrowth_DetectsTypewriter()
    {
        // Arrange: 同一位置（IoU高）の領域で "Hello" → "Hello World"
        var contextId = "window_0x1234";

        // 1回目: "Hello" を含む1領域
        var firstOcrResult = new OcrExecutionResult
        {
            DetectedText = "Hello",
            TextChunks = [new OcrTextRegion("Hello", new Rectangle(100, 100, 200, 50), 0.9)],
            Success = true
        };

        var firstContext = CreateContext(contextId, null, firstOcrResult);

        // 1回目実行（初回 → CreateFirstTime）
        var firstResult = await _strategy.ExecuteAsync(firstContext, CancellationToken.None);
        Assert.True(firstResult.Success);

        // 2回目: 同じ位置で "Hello World" に成長
        var secondOcrResult = new OcrExecutionResult
        {
            DetectedText = "Hello World",
            TextChunks = [new OcrTextRegion("Hello World", new Rectangle(100, 100, 300, 50), 0.9)],
            Success = true
        };

        var secondContext = CreateContext(contextId, "Hello", secondOcrResult);

        // Act: 2回目実行
        var secondResult = await _strategy.ExecuteAsync(secondContext, CancellationToken.None);

        // Assert: タイプライターとして検出（変化なし = 翻訳遅延）
        Assert.True(secondResult.Success);
        var detectionResult = secondResult.Data as TextChangeDetectionResult;
        Assert.NotNull(detectionResult);
        Assert.False(detectionResult.HasTextChanged, "タイプライター検出時は変化なし（翻訳遅延）");
    }

    [Fact]
    public async Task TypewriterDetection_NewRegionAdded_DoesNotDetectTypewriter()
    {
        // Arrange: Issue #501 の核心テスト
        // 前回3領域、今回4領域（新領域追加、既存領域は同一テキスト）
        var contextId = "window_0x5678";

        var prevRegions = new List<OcrTextRegion>
        {
            new("Region A", new Rectangle(100, 100, 200, 50), 0.9),
            new("Region B", new Rectangle(100, 200, 200, 50), 0.9),
            new("Region C", new Rectangle(100, 300, 200, 50), 0.9)
        };

        // 1回目: 3領域
        var firstOcrResult = new OcrExecutionResult
        {
            DetectedText = "Region A Region B Region C",
            TextChunks = prevRegions.Cast<object>().ToList(),
            Success = true
        };

        var firstContext = CreateContext(contextId, null, firstOcrResult);
        await _strategy.ExecuteAsync(firstContext, CancellationToken.None);

        // 2回目: 4領域（新規領域D追加、既存領域は同一テキスト）
        var currRegions = new List<OcrTextRegion>
        {
            new("Region A", new Rectangle(100, 100, 200, 50), 0.9),
            new("Region B", new Rectangle(100, 200, 200, 50), 0.9),
            new("Region C", new Rectangle(100, 300, 200, 50), 0.9),
            new("Region D", new Rectangle(100, 400, 200, 50), 0.9)  // 新規領域
        };

        var secondOcrResult = new OcrExecutionResult
        {
            DetectedText = "Region A Region B Region C Region D",
            TextChunks = currRegions.Cast<object>().ToList(),
            Success = true
        };

        // 結合テキストは StartsWith で一致するが、領域単位ではタイプライターではない
        var secondContext = CreateContext(contextId, "Region A Region B Region C", secondOcrResult);

        // DetectTextChangeAsync のモック（通常の変化検知に委任されるはず）
        _mockTextChangeService.Setup(x => x.DetectTextChangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(TextChangeResult.CreateSignificantChange(
                "Region A Region B Region C",
                "Region A Region B Region C Region D",
                0.3f));

        // Act
        var result = await _strategy.ExecuteAsync(secondContext, CancellationToken.None);

        // Assert: タイプライターではなく通常の変化検知に委任される
        Assert.True(result.Success);
        var detectionResult = result.Data as TextChangeDetectionResult;
        Assert.NotNull(detectionResult);
        // 通常の変化検知が呼ばれたことを確認
        _mockTextChangeService.Verify(x => x.DetectTextChangeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task TypewriterDetection_DifferentRegionPosition_DoesNotDetectTypewriter()
    {
        // Arrange: 同テキストだが位置が異なる領域（IoU低）
        var contextId = "window_0xABCD";

        // 1回目: 左側に1領域
        var firstOcrResult = new OcrExecutionResult
        {
            DetectedText = "Hello",
            TextChunks = [new OcrTextRegion("Hello", new Rectangle(0, 0, 200, 50), 0.9)],
            Success = true
        };

        var firstContext = CreateContext(contextId, null, firstOcrResult);
        await _strategy.ExecuteAsync(firstContext, CancellationToken.None);

        // 2回目: 完全に異なる位置（IoU=0）に成長テキスト
        var secondOcrResult = new OcrExecutionResult
        {
            DetectedText = "Hello World",
            TextChunks = [new OcrTextRegion("Hello World", new Rectangle(800, 500, 300, 50), 0.9)],
            Success = true
        };

        var secondContext = CreateContext(contextId, "Hello", secondOcrResult);

        _mockTextChangeService.Setup(x => x.DetectTextChangeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(TextChangeResult.CreateSignificantChange("Hello", "Hello World", 0.5f));

        // Act
        var result = await _strategy.ExecuteAsync(secondContext, CancellationToken.None);

        // Assert: 位置が異なるためタイプライターではなく通常の変化検知に委任
        Assert.True(result.Success);
        _mockTextChangeService.Verify(x => x.DetectTextChangeAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    #endregion

    #region Helper Methods

    private static ProcessingContext CreateContext(
        string contextId,
        string? previousText,
        OcrExecutionResult ocrResult)
    {
        // contextId は "Window_{handle}" 形式。ContextId = $"Window_{SourceWindowHandle.ToInt64()}"
        // handle値を逆算: contextId = "window_0x1234" → SourceWindowHandle をモックで設定
        var input = new ProcessingPipelineInput
        {
            CapturedImage = Mock.Of<IImage>(),
            CaptureRegion = new Rectangle(0, 0, 1920, 1080),
            SourceWindowHandle = IntPtr.Zero,
            PreviousOcrText = previousText
        };

        var context = new ProcessingContext(input);

        // OcrExecutionResult をステージ結果として追加
        var stageResult = ProcessingStageResult.CreateSuccess(
            ProcessingStageType.OcrExecution, ocrResult);
        context.AddStageResult(ProcessingStageType.OcrExecution, stageResult);

        return context;
    }

    #endregion
}
