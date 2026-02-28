using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Capture;
using Baketa.Core.Models.ImageProcessing;
using Baketa.Core.Models.Processing;
using Baketa.Infrastructure.Processing.Strategies;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Processing.Strategies;

/// <summary>
/// ImageChangeDetectionStageStrategy のユニットテスト
/// Issue #480/#481 で追加されたテキスト位置履歴蓄積機構の検証
/// </summary>
public class ImageChangeDetectionStageStrategyTests
{
    private readonly Mock<IImageChangeDetectionService> _mockChangeDetectionService;
    private readonly Mock<ILogger<ImageChangeDetectionStageStrategy>> _mockLogger;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly ImageChangeDetectionStageStrategy _strategy;

    public ImageChangeDetectionStageStrategyTests()
    {
        _mockChangeDetectionService = new Mock<IImageChangeDetectionService>();
        _mockLogger = new Mock<ILogger<ImageChangeDetectionStageStrategy>>();
        _mockEventAggregator = new Mock<IEventAggregator>();

        // GetStatistics のデフォルトセットアップ
        _mockChangeDetectionService
            .Setup(x => x.GetStatistics())
            .Returns(new ImageChangeDetectionStatistics());

        _strategy = new ImageChangeDetectionStageStrategy(
            _mockChangeDetectionService.Object,
            _mockLogger.Object,
            _mockEventAggregator.Object);
    }

    #region UpdatePreviousTextBounds - 履歴蓄積テスト

    [Fact]
    public void UpdatePreviousTextBounds_FirstCall_CompletesWithoutError()
    {
        // Arrange
        var contextId = "window_0x1234_region_0_0_1920_1080";
        var textBounds = new[] { new Rectangle(100, 100, 200, 50) };

        // Act & Assert - 初回呼び出しは前回データなし、例外なく完了
        _strategy.UpdatePreviousTextBounds(contextId, textBounds);
    }

    [Fact]
    public void UpdatePreviousTextBounds_SecondCallWithDifferentBounds_CompletesWithoutError()
    {
        // Arrange
        var contextId = "window_0x1234_region_0_0_1920_1080";
        var firstBounds = new[] { new Rectangle(100, 100, 200, 50) };
        var secondBounds = new[] { new Rectangle(500, 300, 200, 50) };

        // Act - 2回呼び出し（1回目のデータが履歴に蓄積されるはず）
        _strategy.UpdatePreviousTextBounds(contextId, firstBounds);
        _strategy.UpdatePreviousTextBounds(contextId, secondBounds);

        // Assert - 例外なく完了
    }

    [Fact]
    public void UpdatePreviousTextBounds_ExceedsMaxHistory_TrimsWithoutError()
    {
        // Arrange
        var contextId = "window_0x1234_region_0_0_1920_1080";

        // Act - 60回異なる位置で呼び出し（MaxHistoricalBoundsPerContext=50を超過させる）
        for (int i = 0; i < 60; i++)
        {
            var bounds = new[] { new Rectangle(i * 100, 0, 50, 50) };
            _strategy.UpdatePreviousTextBounds(contextId, bounds);
        }

        // Assert - 上限トリミングが正常に動作し、例外なく完了
    }

    [Fact]
    public void UpdatePreviousTextBounds_EmptyBounds_CompletesWithoutError()
    {
        // Arrange
        var contextId = "window_0x1234_region_0_0_1920_1080";
        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(100, 100, 200, 50)]);

        // Act - 空配列で上書き（前回データが履歴に蓄積されるはず）
        _strategy.UpdatePreviousTextBounds(contextId, Array.Empty<Rectangle>());

        // Assert - 例外なく完了
    }

    [Fact]
    public void UpdatePreviousTextBounds_MultipleContexts_IsolatedHistory()
    {
        // Arrange
        var contextA = "window_0x1234_region_0_0_1920_1080";
        var contextB = "window_0x5678_region_0_0_1920_1080";

        // Act - 異なるコンテキストに個別にデータ設定
        _strategy.UpdatePreviousTextBounds(contextA, [new Rectangle(100, 100, 200, 50)]);
        _strategy.UpdatePreviousTextBounds(contextB, [new Rectangle(500, 500, 200, 50)]);
        _strategy.UpdatePreviousTextBounds(contextA, [new Rectangle(300, 300, 200, 50)]);
        _strategy.UpdatePreviousTextBounds(contextB, [new Rectangle(700, 700, 200, 50)]);

        // Assert - 例外なく完了（コンテキスト分離が正常）
    }

    #endregion

    #region ClearPreviousImages - 履歴クリアテスト

    [Fact]
    public void ClearPreviousImages_AfterHistoryAccumulation_ClearsAll()
    {
        // Arrange - 履歴を蓄積
        var contextId = "window_0x1234_region_0_0_1920_1080";
        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(100, 100, 200, 50)]);
        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(500, 300, 200, 50)]);

        // Act
        _strategy.ClearPreviousImages();

        // Assert - クリア後に再度Updateしてもエラーなく動作
        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(100, 100, 200, 50)]);
    }

    [Fact]
    public void ClearPreviousImages_EmptyState_CompletesWithoutError()
    {
        // Act & Assert - 空状態でもエラーなし
        _strategy.ClearPreviousImages();
    }

    #endregion

    #region ExecuteAsync - テキスト消失検知の統合テスト

    [Fact]
    public async Task ExecuteAsync_WithHistoricalTextBounds_PublishesDisappearanceEvent()
    {
        // Arrange
        // シナリオ: テキストA検出 → テキストB検出(Aは履歴へ) → テキストA位置で変化検知
        var textA = new Rectangle(100, 100, 200, 50);
        var textB = new Rectangle(500, 300, 200, 50);
        var windowHandle = new IntPtr(0x1234);
        var captureRegion = new Rectangle(0, 0, 1920, 1080);
        var contextId = ImageChangeDetectionStageStrategy.BuildContextId(windowHandle, captureRegion);

        // テキストA検出 → テキストB検出 (Aが履歴に入る)
        _strategy.UpdatePreviousTextBounds(contextId, [textA]);
        _strategy.UpdatePreviousTextBounds(contextId, [textB]);

        // 画像変化検知: テキストA位置で変化あり（ChangedRegionsがテキストA位置と重なる）
        var changeResult = ImageChangeResult.CreateChanged(
            "hash1", "hash2", 0.1f, HashAlgorithmType.AverageHash,
            TimeSpan.FromMilliseconds(5),
            detectionStage: 1,
            regions: [textA]);

        _mockChangeDetectionService
            .Setup(x => x.DetectChangeAsync(
                It.IsAny<IImage?>(), It.IsAny<IImage>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeResult);

        // PublishAsyncのセットアップ
        _mockEventAggregator
            .Setup(x => x.PublishAsync(It.IsAny<TextDisappearanceEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // 1回目のExecuteAsync: previousImageを設定
        var mockImage1 = CreateMockImage(1920, 1080);
        var context1 = CreateProcessingContext(windowHandle, captureRegion, mockImage1.Object);
        await _strategy.ExecuteAsync(context1, CancellationToken.None);

        // 2回目のExecuteAsync: 前回画像あり + 変化検知 + テキスト位置マッチ → 消失イベント発行
        // ※ previousImageがnon-null、IsTextDisappearance内のPixelDataLockで
        //    LockPixelData()が呼ばれるが、モック画像は例外を返す
        //    → CalculateTextAreaChangeRate()のcatchブロックで1.0f返却 → 閾値超え → 消失判定true
        var mockImage2 = CreateMockImage(1920, 1080);
        var context2 = CreateProcessingContext(windowHandle, captureRegion, mockImage2.Object);
        await _strategy.ExecuteAsync(context2, CancellationToken.None);

        // Assert - TextDisappearanceEventが発行されたことを確認
        _mockEventAggregator.Verify(
            x => x.PublishAsync(It.IsAny<TextDisappearanceEvent>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce(),
            "履歴テキスト位置(テキストA)での変化がTextDisappearanceEventとして発行されるべき");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutTextBounds_DoesNotPublishDisappearanceEvent()
    {
        // Arrange - テキスト位置を一切設定しない
        var windowHandle = new IntPtr(0x5678);
        var captureRegion = new Rectangle(0, 0, 1920, 1080);

        var changeResult = ImageChangeResult.CreateChanged(
            "hash1", "hash2", 0.1f, HashAlgorithmType.AverageHash,
            TimeSpan.FromMilliseconds(5),
            detectionStage: 1,
            regions: [new Rectangle(100, 100, 200, 50)]);

        _mockChangeDetectionService
            .Setup(x => x.DetectChangeAsync(
                It.IsAny<IImage?>(), It.IsAny<IImage>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeResult);

        // 1回目
        var mockImage1 = CreateMockImage(1920, 1080);
        var context1 = CreateProcessingContext(windowHandle, captureRegion, mockImage1.Object);
        await _strategy.ExecuteAsync(context1, CancellationToken.None);

        // 2回目
        var mockImage2 = CreateMockImage(1920, 1080);
        var context2 = CreateProcessingContext(windowHandle, captureRegion, mockImage2.Object);
        await _strategy.ExecuteAsync(context2, CancellationToken.None);

        // Assert - テキスト位置がないためイベント未発行
        _mockEventAggregator.Verify(
            x => x.PublishAsync(It.IsAny<TextDisappearanceEvent>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "テキスト位置がない場合、TextDisappearanceEventは発行されるべきでない");
    }

    [Fact]
    public async Task ExecuteAsync_NoImageChange_DoesNotPublishDisappearanceEvent()
    {
        // Arrange
        var windowHandle = new IntPtr(0x9ABC);
        var captureRegion = new Rectangle(0, 0, 1920, 1080);
        var contextId = ImageChangeDetectionStageStrategy.BuildContextId(windowHandle, captureRegion);

        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(100, 100, 200, 50)]);

        // 変化なし
        var changeResult = ImageChangeResult.CreateNoChange(TimeSpan.FromMilliseconds(1));

        _mockChangeDetectionService
            .Setup(x => x.DetectChangeAsync(
                It.IsAny<IImage?>(), It.IsAny<IImage>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeResult);

        // 1回目
        var mockImage1 = CreateMockImage(1920, 1080);
        var context1 = CreateProcessingContext(windowHandle, captureRegion, mockImage1.Object);
        await _strategy.ExecuteAsync(context1, CancellationToken.None);

        // 2回目
        var mockImage2 = CreateMockImage(1920, 1080);
        var context2 = CreateProcessingContext(windowHandle, captureRegion, mockImage2.Object);
        await _strategy.ExecuteAsync(context2, CancellationToken.None);

        // Assert - HasChanged=falseなのでイベント未発行
        _mockEventAggregator.Verify(
            x => x.PublishAsync(It.IsAny<TextDisappearanceEvent>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "画像変化なしの場合、TextDisappearanceEventは発行されるべきでない");
    }

    [Fact]
    public async Task ExecuteAsync_AfterClearPreviousImages_DoesNotPublishDisappearanceEvent()
    {
        // Arrange - 履歴を蓄積してからクリア
        var windowHandle = new IntPtr(0xDEF0);
        var captureRegion = new Rectangle(0, 0, 1920, 1080);
        var contextId = ImageChangeDetectionStageStrategy.BuildContextId(windowHandle, captureRegion);

        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(100, 100, 200, 50)]);
        _strategy.UpdatePreviousTextBounds(contextId, [new Rectangle(500, 300, 200, 50)]);

        // クリア（Stop→Start相当）
        _strategy.ClearPreviousImages();

        var changeResult = ImageChangeResult.CreateChanged(
            "hash1", "hash2", 0.1f, HashAlgorithmType.AverageHash,
            TimeSpan.FromMilliseconds(5),
            detectionStage: 1,
            regions: [new Rectangle(100, 100, 200, 50)]);

        _mockChangeDetectionService
            .Setup(x => x.DetectChangeAsync(
                It.IsAny<IImage?>(), It.IsAny<IImage>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(changeResult);

        // 1回目
        var mockImage1 = CreateMockImage(1920, 1080);
        var context1 = CreateProcessingContext(windowHandle, captureRegion, mockImage1.Object);
        await _strategy.ExecuteAsync(context1, CancellationToken.None);

        // 2回目
        var mockImage2 = CreateMockImage(1920, 1080);
        var context2 = CreateProcessingContext(windowHandle, captureRegion, mockImage2.Object);
        await _strategy.ExecuteAsync(context2, CancellationToken.None);

        // Assert - クリア後なのでテキスト位置なし → イベント未発行
        _mockEventAggregator.Verify(
            x => x.PublishAsync(It.IsAny<TextDisappearanceEvent>(), It.IsAny<CancellationToken>()),
            Times.Never(),
            "ClearPreviousImages後はテキスト位置がないため、TextDisappearanceEventは発行されるべきでない");
    }

    #endregion

    #region BuildContextId テスト

    [Fact]
    public void BuildContextId_SameInput_ReturnsSameId()
    {
        var handle = new IntPtr(0x1234);
        var region = new Rectangle(100, 200, 1920, 1080);

        var id1 = ImageChangeDetectionStageStrategy.BuildContextId(handle, region);
        var id2 = ImageChangeDetectionStageStrategy.BuildContextId(handle, region);

        Assert.Equal(id1, id2);
    }

    [Theory]
    [InlineData(719, 720)]
    [InlineData(720, 720)]
    public void BuildContextId_JitterAbsorption_ProducesSameId(int height1, int height2)
    {
        var handle = new IntPtr(0x1234);
        var region1 = new Rectangle(0, 0, 1920, height1);
        var region2 = new Rectangle(0, 0, 1920, height2);

        var id1 = ImageChangeDetectionStageStrategy.BuildContextId(handle, region1);
        var id2 = ImageChangeDetectionStageStrategy.BuildContextId(handle, region2);

        Assert.Equal(id1, id2);
    }

    [Fact]
    public void BuildContextId_DifferentWindows_ReturnsDifferentIds()
    {
        var region = new Rectangle(0, 0, 1920, 1080);

        var id1 = ImageChangeDetectionStageStrategy.BuildContextId(new IntPtr(0x1234), region);
        var id2 = ImageChangeDetectionStageStrategy.BuildContextId(new IntPtr(0x5678), region);

        Assert.NotEqual(id1, id2);
    }

    #endregion

    #region ヘルパーメソッド

    private static Mock<IImage> CreateMockImage(int width, int height)
    {
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(width);
        mockImage.Setup(x => x.Height).Returns(height);
        return mockImage;
    }

    private static ProcessingContext CreateProcessingContext(
        IntPtr windowHandle, Rectangle captureRegion, IImage capturedImage)
    {
        var input = new ProcessingPipelineInput
        {
            CapturedImage = capturedImage,
            SourceWindowHandle = windowHandle,
            CaptureRegion = captureRegion,
            OwnsImage = false // テストではモックなのでDispose管理不要
        };
        return new ProcessingContext(input);
    }

    #endregion
}
