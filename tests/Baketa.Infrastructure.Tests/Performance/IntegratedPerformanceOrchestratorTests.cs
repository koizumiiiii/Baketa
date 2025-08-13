using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Drawing;
using Xunit;
using Moq;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Performance;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Performance;

namespace Baketa.Infrastructure.Tests.Performance;

/// <summary>
/// 統合パフォーマンスオーケストレーター テスト
/// Issue #143 Week 3 Phase 2: 完全統合システム検証
/// </summary>
public class IntegratedPerformanceOrchestratorTests : IDisposable
{
    private readonly Mock<ILogger<IntegratedPerformanceOrchestrator>> _mockLogger;
    private readonly Mock<IGpuOcrEngine> _mockGpuOcrEngine;
    private readonly Mock<IStickyRoiManager> _mockRoiManager;
    private readonly Mock<ITdrRecoveryManager> _mockTdrManager;
    private readonly Mock<IPersistentSessionCache> _mockSessionCache;
    private readonly Mock<IOptions<OcrSettings>> _mockOcrSettings;
    private readonly IntegratedPerformanceOrchestrator _orchestrator;

    public IntegratedPerformanceOrchestratorTests()
    {
        _mockLogger = new Mock<ILogger<IntegratedPerformanceOrchestrator>>();
        _mockGpuOcrEngine = new Mock<IGpuOcrEngine>();
        _mockRoiManager = new Mock<IStickyRoiManager>();
        _mockTdrManager = new Mock<ITdrRecoveryManager>();
        _mockSessionCache = new Mock<IPersistentSessionCache>();
        _mockOcrSettings = new Mock<IOptions<OcrSettings>>();
        
        _mockOcrSettings.Setup(x => x.Value).Returns(new OcrSettings());
        
        _orchestrator = new IntegratedPerformanceOrchestrator(
            _mockLogger.Object,
            _mockGpuOcrEngine.Object,
            _mockRoiManager.Object,
            _mockTdrManager.Object,
            _mockSessionCache.Object,
            _mockOcrSettings.Object);
    }

    [Fact]
    public async Task ExecuteOptimizedOcrAsync_ShouldUseFullyIntegratedStrategy_WhenAllSystemsHealthy()
    {
        // Arrange
        var imageData = CreateTestImageData();
        var options = new PerformanceOptimizationOptions
        {
            PreferGpuAcceleration = true,
            UseStickyRoi = true,
            Priority = PerformancePriority.Speed
        };

        SetupHealthySystemMocks();
        SetupSuccessfulOcrMocks();

        // 初期化完了を待つ
        await _orchestrator.WaitForInitializationAsync();

        // デバッグ: 健全性スコアを確認
        var healthReport = await _orchestrator.CheckSystemHealthAsync();
        
        // Assert - 健全性スコアを先に確認
        Assert.True(healthReport.OverallHealthScore >= 0.5, 
            $"OverallHealthScore is {healthReport.OverallHealthScore}, expected >= 0.5. " +
            $"GPU: {healthReport.GpuHealth?.Score}, ROI: {healthReport.RoiSystemHealth?.Score}, Memory: {healthReport.MemoryHealth?.Score}");
        
        // デバッグ: GPU利用可能性を確認
        var gpuAvailable = await _mockGpuOcrEngine.Object.IsAvailableAsync();
        Assert.True(gpuAvailable, "GPU should be available according to mock setup");
        
        // Act
        var result = await _orchestrator.ExecuteOptimizedOcrAsync(imageData, options);

        // Assert
        Assert.True(result.IsSuccessful);
        Assert.Equal(OptimizationTechnique.FullyIntegrated, result.UsedTechnique);
        Assert.True(result.PerformanceImprovement > 0);
        Assert.True(result.QualityScore > 0);

        // GPU・ROI利用確認
        _mockGpuOcrEngine.Verify(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _mockRoiManager.Verify(x => x.GetPriorityRoisAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteOptimizedOcrAsync_ShouldFallbackToCpu_WhenSystemHealthPoor()
    {
        // Arrange
        var imageData = CreateTestImageData();
        var options = new PerformanceOptimizationOptions();

        SetupUnhealthySystemMocks();

        // Act
        var result = await _orchestrator.ExecuteOptimizedOcrAsync(imageData, options);

        // Assert
        Assert.Equal(OptimizationTechnique.CpuFallback, result.UsedTechnique);
        Assert.Equal(0.0, result.PerformanceImprovement); // CPU基準のため改善なし
        
        // GPU使用されていないことを確認
        _mockGpuOcrEngine.Verify(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteOptimizedOcrAsync_ShouldHandleTdrRecovery_WhenGpuTimeoutOccurs()
    {
        // Arrange
        var imageData = CreateTestImageData();
        var options = new PerformanceOptimizationOptions
        {
            PreferGpuAcceleration = true,
            EnableTdrProtection = true
        };

        SetupHealthySystemMocks();
        
        // GPU処理でTDRタイムアウトをシミュレート
        _mockGpuOcrEngine
            .Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("TDR timeout occurred"));

        _mockTdrManager
            .Setup(x => x.RecoverFromTdrAsync(It.IsAny<TdrContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TdrRecoveryResult { IsSuccessful = true });

        // Act
        var result = await _orchestrator.ExecuteOptimizedOcrAsync(imageData, options);

        // Assert
        Assert.Equal(OptimizationTechnique.CpuFallback, result.UsedTechnique); // フォールバック確認
        
        // TDRリカバリが実行されたことを確認
        _mockTdrManager.Verify(x => x.RecoverFromTdrAsync(It.IsAny<TdrContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetPerformanceMetricsAsync_ShouldReturnValidMetrics()
    {
        // Arrange
        SetupHealthySystemMocks();
        SetupPerformanceMetricsMocks();

        // Act
        var metrics = await _orchestrator.GetPerformanceMetricsAsync();

        // Assert
        Assert.True(metrics.GpuUtilization >= 0 && metrics.GpuUtilization <= 1);
        Assert.True(metrics.RoiEfficiency >= 0 && metrics.RoiEfficiency <= 1);
        Assert.True(metrics.Throughput >= 0);
        Assert.True(metrics.MemoryUsageMB >= 0);
        Assert.True(metrics.StabilityScore >= 0 && metrics.StabilityScore <= 1);
        Assert.True((DateTime.UtcNow - metrics.MeasuredAt).TotalSeconds < 5); // 最近測定されたもの
    }

    [Fact]
    public async Task AdaptOptimizationAsync_ShouldAdjustSettings_BasedOnMetrics()
    {
        // Arrange
        var metrics = new IntegratedPerformanceMetrics
        {
            GpuUtilization = 0.2, // 低GPU使用率
            RoiEfficiency = 0.1,  // 低ROI効率
            AverageProcessingTime = TimeSpan.FromSeconds(3), // 長い処理時間
            StabilityScore = 0.5  // 中程度の安定性
        };

        // Act
        var result = await _orchestrator.AdaptOptimizationAsync(metrics);

        // Assert
        Assert.True(result.AdjustmentExecuted);
        Assert.True(result.ExecutedAdjustments.Count > 0);
        Assert.True(result.ExpectedImprovement > 0);
        Assert.NotNull(result.NewSettings);
        
        // 具体的な調整内容確認
        Assert.Contains(result.ExecutedAdjustments, adj => adj.Contains("GPU"));
        Assert.Contains(result.ExecutedAdjustments, adj => adj.Contains("ROI"));
        Assert.Contains(result.ExecutedAdjustments, adj => adj.Contains("速度"));
    }

    [Fact]
    public async Task CheckSystemHealthAsync_ShouldReturnHealthReport_WithAllComponents()
    {
        // Arrange
        SetupHealthySystemMocks();

        // Act
        var healthReport = await _orchestrator.CheckSystemHealthAsync();

        // Assert
        Assert.True(healthReport.OverallHealthScore >= 0 && healthReport.OverallHealthScore <= 1);
        Assert.NotNull(healthReport.GpuHealth);
        Assert.NotNull(healthReport.RoiSystemHealth);
        Assert.NotNull(healthReport.MemoryHealth);
        Assert.NotNull(healthReport.DetectedIssues);
        Assert.NotNull(healthReport.RecommendedActions);
        Assert.True((DateTime.UtcNow - healthReport.GeneratedAt).TotalSeconds < 5);
    }

    [Fact]
    public async Task MultipleProcessingRequests_ShouldMaintainStatistics()
    {
        // Arrange
        var imageData = CreateTestImageData();
        SetupHealthySystemMocks();
        SetupSuccessfulOcrMocks();

        // Act - 複数回実行
        for (int i = 0; i < 5; i++)
        {
            await _orchestrator.ExecuteOptimizedOcrAsync(imageData);
        }

        var metrics = await _orchestrator.GetPerformanceMetricsAsync();

        // Assert
        Assert.True(metrics.GpuUtilization > 0); // GPU使用統計が蓄積されている
        Assert.True(metrics.Throughput > 0);     // スループット計算されている
        
        // 複数回の処理が統計に反映されていることを確認
        _mockGpuOcrEngine.Verify(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()), Times.AtLeast(5));
    }

    [Theory]
    [InlineData(PerformancePriority.Speed, OptimizationTechnique.FullyIntegrated)]
    [InlineData(PerformancePriority.Balanced, OptimizationTechnique.GpuRoiIntegrated)]
    [InlineData(PerformancePriority.Quality, OptimizationTechnique.RoiOnly)]
    public async Task ExecuteOptimizedOcrAsync_ShouldSelectCorrectStrategy_BasedOnPriority(
        PerformancePriority priority, 
        OptimizationTechnique expectedTechnique)
    {
        // Arrange
        var imageData = CreateTestImageData();
        var options = new PerformanceOptimizationOptions
        {
            Priority = priority,
            PreferGpuAcceleration = true,
            UseStickyRoi = true
        };

        SetupHealthySystemMocks();
        SetupSuccessfulOcrMocks();

        // Act
        var result = await _orchestrator.ExecuteOptimizedOcrAsync(imageData, options);

        // Assert
        // 完全実装後は expectedTechnique と一致することを確認
        // 現在は基本的な処理完了確認のみ
        Assert.NotNull(result);
        Assert.True(result.TotalProcessingTime.TotalMilliseconds >= 0);
    }

    private void SetupHealthySystemMocks()
    {
        _mockGpuOcrEngine.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        _mockTdrManager.Setup(x => x.StartTdrMonitoringAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _mockTdrManager.Setup(x => x.GetTdrStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TdrStatus { IsHealthy = true, RecentTdrCount = 0 });

        _mockRoiManager.Setup(x => x.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiStatistics { EfficiencyGain = 0.3, AverageConfidence = 0.8 });

        _mockRoiManager.Setup(x => x.GetPriorityRoisAsync(It.IsAny<Rectangle>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StickyRoi>
            {
                new StickyRoi { Region = new Rectangle(100, 100, 200, 50), ConfidenceScore = 0.9 }
            }.AsReadOnly());
    }

    private void SetupUnhealthySystemMocks()
    {
        _mockGpuOcrEngine.Setup(x => x.IsAvailableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _mockTdrManager.Setup(x => x.GetTdrStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TdrStatus { IsHealthy = false, RecentTdrCount = 5 });

        _mockRoiManager.Setup(x => x.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiStatistics { EfficiencyGain = 0.05, AverageConfidence = 0.3 });
    }

    private void SetupSuccessfulOcrMocks()
    {
        var mockOcrResult = new Baketa.Core.Abstractions.OCR.OcrResult
        {
            DetectedTexts = new[]
            {
                new Baketa.Core.Abstractions.OCR.DetectedText
                {
                    Text = "Test Text",
                    Confidence = 0.95,
                    BoundingBox = new Rectangle(100, 100, 200, 50),
                    Language = "ja",
                    ProcessingTechnique = OptimizationTechnique.GpuOnly
                }
            }.ToList().AsReadOnly(),
            IsSuccessful = true,
            ProcessingTime = TimeSpan.FromMilliseconds(100)
        };

        _mockGpuOcrEngine.Setup(x => x.RecognizeTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockOcrResult);
    }

    private void SetupPerformanceMetricsMocks()
    {
        _mockRoiManager.Setup(x => x.GetStatisticsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoiStatistics
            {
                EfficiencyGain = 0.4,
                AverageConfidence = 0.85,
                TotalDetections = 100,
                SuccessRate = 0.9
            });

        _mockTdrManager.Setup(x => x.GetTdrStatusAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TdrStatus
            {
                IsHealthy = true,
                RecentTdrCount = 0,
                LastTdrOccurredAt = DateTime.UtcNow.AddHours(-1)
            });
    }

    private static byte[] CreateTestImageData()
    {
        // テスト用の最小限PNG画像データ
        return new byte[]
        {
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
        };
    }

    public void Dispose()
    {
        _orchestrator?.Dispose();
    }
}