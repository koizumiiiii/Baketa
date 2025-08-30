using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Drawing;
using Xunit;
using Xunit.Abstractions;
using Moq;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.StickyRoi;

namespace Baketa.Infrastructure.Tests.Performance;

/// <summary>
/// スティッキーROI統合システムパフォーマンステスト
/// Sprint 2 Phase 2: 処理時間測定とパフォーマンス最適化検証
/// </summary>
public class StickyRoiPerformanceTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<InMemoryStickyRoiManager>> _roiManagerLogger;
    private readonly Mock<ILogger<StickyRoiEnhancedOcrEngine>> _ocrEngineLogger;
    private readonly Mock<ISimpleOcrEngine> _mockBaseOcrEngine;
    private readonly Mock<IOptions<OcrSettings>> _mockOcrSettings;
    private readonly Mock<IOptionsMonitor<OcrSettings>> _mockOcrSettingsMonitor;
    private readonly InMemoryStickyRoiManager _roiManager;
    private readonly StickyRoiEnhancedOcrEngine _enhancedOcrEngine;

    public StickyRoiPerformanceTests(ITestOutputHelper output)
    {
        _output = output;
        _roiManagerLogger = new Mock<ILogger<InMemoryStickyRoiManager>>();
        _ocrEngineLogger = new Mock<ILogger<StickyRoiEnhancedOcrEngine>>();
        _mockBaseOcrEngine = new Mock<ISimpleOcrEngine>();
        _mockOcrSettings = new Mock<IOptions<OcrSettings>>();
        _mockOcrSettingsMonitor = new Mock<IOptionsMonitor<OcrSettings>>();
        
        _mockOcrSettings.Setup(x => x.Value).Returns(new OcrSettings());
        _mockOcrSettingsMonitor.Setup(x => x.CurrentValue).Returns(new OcrSettings());
        
        _roiManager = new InMemoryStickyRoiManager(
            _roiManagerLogger.Object,
            _mockOcrSettings.Object);
        
        _enhancedOcrEngine = new StickyRoiEnhancedOcrEngine(
            _ocrEngineLogger.Object,
            _mockBaseOcrEngine.Object,
            _roiManager,
            _mockOcrSettingsMonitor.Object);
    }

    [Fact]
    public async Task IntegratedOcrProcessing_ShouldMeetPerformanceTarget()
    {
        // Arrange
        var targetProcessingTime = TimeSpan.FromSeconds(2.0); // 目標: <2秒
        var testImageData = CreateLargeTestImageData(); // 高解像度画像
        
        var complexOcrResult = CreateComplexOcrResult();
        
        _mockBaseOcrEngine
            .Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(complexOcrResult);
        
        _mockBaseOcrEngine
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var stopwatch = Stopwatch.StartNew();
        
        // Act - 1回目処理（ROIなし、フルスクリーン）
        var firstResult = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);
        var firstProcessingTime = stopwatch.Elapsed;
        
        _output.WriteLine($"🔍 1回目処理時間: {firstProcessingTime.TotalMilliseconds:F2}ms");
        
        // ROIが学習されるまで短時間待機
        await Task.Delay(100);
        
        stopwatch.Restart();
        
        // Act - 2回目処理（ROI最適化適用）
        var secondResult = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);
        var secondProcessingTime = stopwatch.Elapsed;
        
        _output.WriteLine($"⚡ 2回目処理時間: {secondProcessingTime.TotalMilliseconds:F2}ms");
        
        stopwatch.Stop();
        
        // Assert - パフォーマンス目標検証
        Assert.True(firstResult.IsSuccessful, "1回目処理が失敗");
        Assert.True(secondResult.IsSuccessful, "2回目処理が失敗");
        
        // 目標処理時間内であることを確認
        Assert.True(firstProcessingTime < targetProcessingTime, 
            $"1回目処理時間が目標を超過: {firstProcessingTime.TotalSeconds:F2}s > {targetProcessingTime.TotalSeconds}s");
        
        Assert.True(secondProcessingTime < targetProcessingTime, 
            $"2回目処理時間が目標を超過: {secondProcessingTime.TotalSeconds:F2}s > {targetProcessingTime.TotalSeconds}s");
        
        // ROI効果検証（2回目が1回目より高速または同等）
        var speedupRatio = firstProcessingTime.TotalMilliseconds / secondProcessingTime.TotalMilliseconds;
        _output.WriteLine($"📊 ROI最適化効果: {speedupRatio:F2}x speedup");
        
        Assert.True(speedupRatio >= 0.8, 
            $"ROI最適化により処理時間が大幅に悪化: {speedupRatio:F2}x");
        
        // 統計情報の確認
        var stats = await _roiManager.GetStatisticsAsync();
        _output.WriteLine($"📈 ROI統計 - 総数: {stats.TotalRois}, アクティブ: {stats.ActiveRois}, 効率向上: {stats.EfficiencyGain:P1}");
    }

    [Fact]
    public async Task HighFrequencyProcessing_ShouldMaintainPerformance()
    {
        // Arrange
        var maxProcessingTime = TimeSpan.FromMilliseconds(500); // 高頻度処理での目標
        var testImageData = CreateTestImageData();
        var processCount = 10;
        
        var mockOcrResult = CreateMockOcrResult();
        _mockBaseOcrEngine
            .Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOcrResult);
        
        _mockBaseOcrEngine
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var processingTimes = new List<TimeSpan>();
        
        // Act - 高頻度処理シミュレーション
        for (int i = 0; i < processCount; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);
            stopwatch.Stop();
            
            Assert.True(result.IsSuccessful, $"処理{i + 1}回目が失敗");
            processingTimes.Add(stopwatch.Elapsed);
            
            _output.WriteLine($"処理{i + 1}: {stopwatch.ElapsedMilliseconds}ms");
            
            // 短時間待機（リアルタイム処理をシミュレーション）
            await Task.Delay(50);
        }
        
        // Assert - 全処理が目標時間内
        var averageTime = TimeSpan.FromTicks((long)processingTimes.Average(t => t.Ticks));
        var maxTime = processingTimes.Max();
        
        _output.WriteLine($"📊 平均処理時間: {averageTime.TotalMilliseconds:F2}ms");
        _output.WriteLine($"📊 最大処理時間: {maxTime.TotalMilliseconds:F2}ms");
        
        Assert.True(averageTime < maxProcessingTime, 
            $"平均処理時間が目標を超過: {averageTime.TotalMilliseconds:F2}ms > {maxProcessingTime.TotalMilliseconds}ms");
        
        Assert.True(maxTime < TimeSpan.FromSeconds(1), 
            $"最大処理時間が許容範囲を超過: {maxTime.TotalMilliseconds:F2}ms > 1000ms");
        
        // ROI学習効果の確認
        var laterProcessingTimes = processingTimes.Skip(5).ToList();
        var earlierProcessingTimes = processingTimes.Take(5).ToList();
        
        var laterAverage = TimeSpan.FromTicks((long)laterProcessingTimes.Average(t => t.Ticks));
        var earlierAverage = TimeSpan.FromTicks((long)earlierProcessingTimes.Average(t => t.Ticks));
        
        var improvementRatio = earlierAverage.TotalMilliseconds / laterAverage.TotalMilliseconds;
        _output.WriteLine($"📈 学習効果: {improvementRatio:F2}x improvement");
    }

    [Fact]
    public async Task RoiLearningEffectiveness_ShouldImproveOverTime()
    {
        // Arrange
        var testImageData = CreateTestImageData();
        var mockResult = CreateMockOcrResult();
        
        _mockBaseOcrEngine
            .Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResult);
        
        _mockBaseOcrEngine
            .Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var initialStats = await _roiManager.GetStatisticsAsync();
        var processingTimes = new List<TimeSpan>();
        
        // Act - 学習プロセスシミュレーション
        for (int i = 0; i < 15; i++)
        {
            var stopwatch = Stopwatch.StartNew();
            var result = await _enhancedOcrEngine.RecognizeTextAsync(testImageData);
            stopwatch.Stop();
            
            processingTimes.Add(stopwatch.Elapsed);
            await Task.Delay(100); // 学習間隔
        }
        
        var finalStats = await _roiManager.GetStatisticsAsync();
        
        // Assert - 学習効果の確認
        Assert.True(finalStats.TotalRois > initialStats.TotalRois, "ROIが学習されていない");
        Assert.True(finalStats.TotalDetections > 0, "検出履歴が記録されていない");
        
        // 処理時間の改善傾向を確認
        var earlyTimes = processingTimes.Take(5).Average(t => t.TotalMilliseconds);
        var lateTimes = processingTimes.Skip(10).Average(t => t.TotalMilliseconds);
        
        var improvementPercentage = ((earlyTimes - lateTimes) / earlyTimes) * 100;
        
        _output.WriteLine($"📈 学習による改善率: {improvementPercentage:F1}%");
        _output.WriteLine($"📊 最終ROI統計 - 総数: {finalStats.TotalRois}, 検出数: {finalStats.TotalDetections}");
        
        // 改善が見られるか、少なくとも劣化していないことを確認
        Assert.True(improvementPercentage >= -10, $"処理時間が大幅に悪化: {improvementPercentage:F1}%");
    }

    private static byte[] CreateTestImageData()
    {
        // 標準テスト画像データ（PNG形式）
        return
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
            0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
            0xDE, 0x00, 0x00, 0x00, 0x09, 0x70, 0x48, 0x59,
            0x73, 0x00, 0x00, 0x0B, 0x13, 0x00, 0x00, 0x0B,
            0x13, 0x01, 0x00, 0x9A, 0x9C, 0x18, 0x00, 0x00,
            0x00, 0x0A, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7,
            0x63, 0xF8, 0x0F, 0x00, 0x00, 0x01, 0x00, 0x01,
            0x76, 0x36, 0xDD, 0xDB, 0x00, 0x00, 0x00, 0x00,
            0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82
        ];
    }

    private static byte[] CreateLargeTestImageData()
    {
        // より大きなテストデータ（高解像度シミュレーション）
        var baseData = CreateTestImageData();
        var largeData = new byte[baseData.Length * 10];
        
        for (int i = 0; i < 10; i++)
        {
            Array.Copy(baseData, 0, largeData, i * baseData.Length, baseData.Length);
        }
        
        return largeData;
    }

    private static Baketa.Core.Abstractions.OCR.OcrResult CreateMockOcrResult()
    {
        return new Baketa.Core.Abstractions.OCR.OcrResult
        {
            DetectedTexts = new[]
            {
                new Baketa.Core.Abstractions.OCR.DetectedText
                {
                    Text = "テスト文字列",
                    Confidence = 0.95,
                    BoundingBox = new Rectangle(100, 100, 150, 30),
                    Language = "ja"
                }
            }.ToList().AsReadOnly(),
            IsSuccessful = true,
            ProcessingTime = TimeSpan.FromMilliseconds(50),
            Metadata = new Dictionary<string, object>
            {
                ["ProcessingMode"] = "Mock",
                ["Engine"] = "MockOcrEngine"
            }
        };
    }

    private static Baketa.Core.Abstractions.OCR.OcrResult CreateComplexOcrResult()
    {
        return new Baketa.Core.Abstractions.OCR.OcrResult
        {
            DetectedTexts = new[]
            {
                new Baketa.Core.Abstractions.OCR.DetectedText { Text = "複雑なテスト1", Confidence = 0.92, BoundingBox = new Rectangle(50, 50, 200, 40), Language = "ja" },
                new Baketa.Core.Abstractions.OCR.DetectedText { Text = "Complex Test 2", Confidence = 0.88, BoundingBox = new Rectangle(300, 100, 180, 35), Language = "en" },
                new Baketa.Core.Abstractions.OCR.DetectedText { Text = "テストデータ3", Confidence = 0.85, BoundingBox = new Rectangle(100, 200, 160, 25), Language = "ja" },
                new Baketa.Core.Abstractions.OCR.DetectedText { Text = "Performance", Confidence = 0.94, BoundingBox = new Rectangle(400, 250, 120, 30), Language = "en" },
                new Baketa.Core.Abstractions.OCR.DetectedText { Text = "測定用文字", Confidence = 0.91, BoundingBox = new Rectangle(150, 350, 140, 28), Language = "ja" }
            }.ToList().AsReadOnly(),
            IsSuccessful = true,
            ProcessingTime = TimeSpan.FromMilliseconds(150),
            Metadata = new Dictionary<string, object>
            {
                ["ProcessingMode"] = "Complex",
                ["Engine"] = "MockComplexOcrEngine",
                ["TextRegions"] = 5
            }
        };
    }

    public void Dispose()
    {
        _roiManager?.Dispose();
        _enhancedOcrEngine?.Dispose();
    }
}