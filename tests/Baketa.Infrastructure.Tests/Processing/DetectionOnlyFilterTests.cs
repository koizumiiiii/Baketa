using System.Drawing;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Models.ImageProcessing;
using Baketa.Infrastructure.Processing.Strategies;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Processing;

/// <summary>
/// [Issue #500] Detection-Onlyフィルタのロジックテスト
/// OcrExecutionStageStrategy.AreDetectionBoundsMatching の双方向IoUマッチングを検証
/// + AreRegionHashesMatching のpHash比較を検証
/// </summary>
public class DetectionOnlyFilterTests
{
    private const float DefaultIoUThreshold = 0.75f;
    private const float DefaultHashThreshold = 0.90f;

    [Fact]
    public void AreDetectionBoundsMatching_IdenticalBoxes_ReturnsTrue()
    {
        var boxes = new[]
        {
            new Rectangle(10, 20, 200, 50),
            new Rectangle(10, 100, 300, 60)
        };

        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            boxes, boxes, DefaultIoUThreshold);

        Assert.True(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_SlightlyShifted_AboveThreshold_ReturnsTrue()
    {
        // 少しずれたバウンディングボックス（IoU > 0.75）
        var current = new[]
        {
            new Rectangle(10, 20, 200, 50),   // ほぼ同じ位置
            new Rectangle(10, 100, 300, 60)
        };
        var previous = new[]
        {
            new Rectangle(12, 22, 200, 50),   // 2px右下にずれ → IoU ≈ 0.96
            new Rectangle(10, 100, 300, 60)   // 完全一致
        };

        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, DefaultIoUThreshold);

        Assert.True(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_DifferentBoxes_ReturnsFalse()
    {
        var current = new[]
        {
            new Rectangle(10, 20, 200, 50)
        };
        var previous = new[]
        {
            new Rectangle(500, 400, 200, 50) // 完全に異なる位置
        };

        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, DefaultIoUThreshold);

        Assert.False(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_MissingBox_ReturnsFalse()
    {
        var current = new[]
        {
            new Rectangle(10, 20, 200, 50),
            new Rectangle(10, 100, 300, 60)
        };
        var previous = new[]
        {
            new Rectangle(10, 20, 200, 50)
            // 2番目のボックスが欠落 → previous→currentマッチングは通るが数が異なる
        };

        // previous側のボックスは全てcurrentにマッチするが、
        // current側の2番目のボックスがpreviousにマッチしないためFalse
        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, DefaultIoUThreshold);

        Assert.False(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_EmptyArrays_ReturnsTrue()
    {
        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            [], [], DefaultIoUThreshold);

        Assert.True(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_OneEmptyOneNot_ReturnsFalse()
    {
        var boxes = new[] { new Rectangle(10, 20, 200, 50) };

        Assert.False(OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            [], boxes, DefaultIoUThreshold));

        Assert.False(OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            boxes, [], DefaultIoUThreshold));
    }

    [Fact]
    public void AreDetectionBoundsMatching_LargeShift_BelowThreshold_ReturnsFalse()
    {
        // 大きくずれたバウンディングボックス（IoU < 0.75）
        var current = new[]
        {
            new Rectangle(10, 20, 100, 50)    // 元の位置
        };
        var previous = new[]
        {
            new Rectangle(60, 20, 100, 50)    // 50pxずれ → IoU ≈ 0.50
        };

        var result = OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, DefaultIoUThreshold);

        Assert.False(result);
    }

    [Fact]
    public void AreDetectionBoundsMatching_HighThreshold_StricterMatching()
    {
        // 高い閾値ではわずかなずれも不一致になる
        var current = new[] { new Rectangle(10, 20, 100, 50) };
        var previous = new[] { new Rectangle(15, 25, 100, 50) }; // 5pxずれ

        // 0.75閾値ではパスするが...
        Assert.True(OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, 0.50f));

        // 0.95閾値では失敗する
        Assert.False(OcrExecutionStageStrategy.AreDetectionBoundsMatching(
            current, previous, 0.95f));
    }

    // ========================================
    // [Issue #500] pHash比較テスト
    // ========================================

    /// <summary>
    /// pHash比較テスト用のOcrExecutionStageStrategyを作成するヘルパー
    /// AreRegionHashesMatchingはinternalメソッドのため、インスタンスが必要
    /// </summary>
    private static OcrExecutionStageStrategy CreateStrategyWithMockHashService(
        Mock<IPerceptualHashService> mockHashService)
    {
        var mockLogger = new Mock<Microsoft.Extensions.Logging.ILogger<OcrExecutionStageStrategy>>();
        var mockOcrEngine = new Mock<Baketa.Core.Abstractions.OCR.IOcrEngine>();
        var mockImageLifecycleManager = new Mock<Baketa.Core.Abstractions.Memory.IImageLifecycleManager>();
        var mockImageFactory = new Mock<Baketa.Core.Abstractions.Factories.IImageFactory>();
        var mockCoordService = new Mock<Baketa.Core.Abstractions.Services.ICoordinateTransformationService>();

        return new OcrExecutionStageStrategy(
            mockLogger.Object,
            mockOcrEngine.Object,
            mockImageLifecycleManager.Object,
            mockImageFactory.Object,
            mockCoordService.Object,
            perceptualHashService: mockHashService.Object);
    }

    [Fact]
    public void AreRegionHashesMatching_SameContent_ReturnsTrue()
    {
        var mockHash = new Mock<IPerceptualHashService>();
        mockHash.Setup(h => h.CompareHashes(It.IsAny<string>(), It.IsAny<string>(), HashAlgorithmType.DifferenceHash))
            .Returns(0.95f); // 閾値0.90を超える類似度

        var strategy = CreateStrategyWithMockHashService(mockHash);

        var bounds = new[]
        {
            new Rectangle(10, 20, 200, 50),
            new Rectangle(10, 100, 300, 60)
        };
        var hashes = new[] { "hash1", "hash2" };

        var result = strategy.AreRegionHashesMatching(
            bounds, hashes,
            bounds, hashes,
            DefaultIoUThreshold, DefaultHashThreshold);

        Assert.True(result);
    }

    [Fact]
    public void AreRegionHashesMatching_ContentChanged_ReturnsFalse()
    {
        var mockHash = new Mock<IPerceptualHashService>();
        mockHash.Setup(h => h.CompareHashes(It.IsAny<string>(), It.IsAny<string>(), HashAlgorithmType.DifferenceHash))
            .Returns(0.50f); // 閾値0.90を下回る → コンテンツ変化

        var strategy = CreateStrategyWithMockHashService(mockHash);

        var bounds = new[] { new Rectangle(10, 20, 200, 50) };
        var currentHashes = new[] { "hashA" };
        var previousHashes = new[] { "hashB" };

        var result = strategy.AreRegionHashesMatching(
            bounds, currentHashes,
            bounds, previousHashes,
            DefaultIoUThreshold, DefaultHashThreshold);

        Assert.False(result);
    }

    [Fact]
    public void AreRegionHashesMatching_PositionSameContentDifferent_ReturnsFalse()
    {
        // ノベルゲーム対策の核心テスト:
        // 位置は完全一致だが、テキスト内容が変わった場合にスキップしないこと

        var mockHash = new Mock<IPerceptualHashService>();

        // 1番目の矩形: 同一コンテンツ（0.95）
        // 2番目の矩形: コンテンツ変化（0.60 < 0.90閾値）
        var callCount = 0;
        mockHash.Setup(h => h.CompareHashes(It.IsAny<string>(), It.IsAny<string>(), HashAlgorithmType.DifferenceHash))
            .Returns(() =>
            {
                callCount++;
                return callCount == 1 ? 0.95f : 0.60f;
            });

        var strategy = CreateStrategyWithMockHashService(mockHash);

        var bounds = new[]
        {
            new Rectangle(100, 400, 600, 40),  // タイトルバー（変化なし）
            new Rectangle(100, 450, 600, 80)   // テキストエリア（テキスト変化）
        };

        var currentHashes = new[] { "title_same", "text_new" };
        var previousHashes = new[] { "title_same", "text_old" };

        var result = strategy.AreRegionHashesMatching(
            bounds, currentHashes,
            bounds, previousHashes,
            DefaultIoUThreshold, DefaultHashThreshold);

        Assert.False(result);
    }

    [Fact]
    public void AreRegionHashesMatching_EmptyHashes_ReturnsTrue()
    {
        // pHashが空の場合はIoUのみで判定（後方互換）
        var mockHash = new Mock<IPerceptualHashService>();
        var strategy = CreateStrategyWithMockHashService(mockHash);

        var bounds = new[] { new Rectangle(10, 20, 200, 50) };

        var result = strategy.AreRegionHashesMatching(
            bounds, [],
            bounds, [],
            DefaultIoUThreshold, DefaultHashThreshold);

        Assert.True(result);
    }

    [Fact]
    public void AreRegionHashesMatching_MultipleBoxes_AllMatch_ReturnsTrue()
    {
        var mockHash = new Mock<IPerceptualHashService>();
        mockHash.Setup(h => h.CompareHashes(It.IsAny<string>(), It.IsAny<string>(), HashAlgorithmType.DifferenceHash))
            .Returns(0.98f); // 全て高類似度

        var strategy = CreateStrategyWithMockHashService(mockHash);

        var bounds = new[]
        {
            new Rectangle(10, 20, 200, 50),
            new Rectangle(10, 100, 300, 60),
            new Rectangle(10, 200, 250, 45)
        };
        var hashes = new[] { "h1", "h2", "h3" };

        var result = strategy.AreRegionHashesMatching(
            bounds, hashes,
            bounds, hashes,
            DefaultIoUThreshold, DefaultHashThreshold);

        Assert.True(result);
    }
}
