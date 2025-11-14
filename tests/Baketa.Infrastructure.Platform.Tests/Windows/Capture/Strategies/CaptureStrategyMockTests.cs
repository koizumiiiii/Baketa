using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Models.Capture;
using Baketa.Infrastructure.Platform.Tests.Windows.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
// 🔥 [PHASE_K-29-G] CaptureOptions統合: Baketa.Core.Abstractions.Servicesから取得
using CaptureOptions = Baketa.Core.Abstractions.Services.CaptureOptions;

namespace Baketa.Infrastructure.Platform.Tests.Windows.Capture.Strategies;

/// <summary>
/// キャプチャ戦略のモックベース統合テスト
/// </summary>
public class CaptureStrategyMockTests
{
    private readonly Mock<IWindowsCapturer> _mockCapturer;
    private readonly Mock<ILogger<DirectFullScreenCaptureStrategy>> _mockLogger;
    private readonly Mock<IEventAggregator> _mockEventAggregator;
    private readonly CaptureOptions _defaultOptions;

    public CaptureStrategyMockTests()
    {
        _mockCapturer = new Mock<IWindowsCapturer>();
        _mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        _mockEventAggregator = new Mock<IEventAggregator>();
        _defaultOptions = new CaptureOptions
        {
            AllowDirectFullScreen = true,
            AllowROIProcessing = true,
            AllowSoftwareFallback = true,
            ROIScaleFactor = 0.25f,
            MaxRetryAttempts = 3,
            EnableHDRProcessing = true,
            TDRTimeoutMs = 2000
        };
    }

    [Theory]
    [MemberData(nameof(GetGPUEnvironmentTestCases))]
    public void StrategySelection_BasedOnGPUEnvironment(GpuEnvironmentInfo gpuEnv, string expectedStrategy, string testDescription)
    {
        // Arrange
        var directFullScreenStrategy = new DirectFullScreenCaptureStrategy(_mockLogger.Object, _mockCapturer.Object, _mockEventAggregator.Object);
        var windowHandle = new IntPtr(0x12345);

        // Act
        var canApplyDirectFullScreen = directFullScreenStrategy.CanApply(gpuEnv, windowHandle);

        // Assert
        switch (expectedStrategy)
        {
            case "DirectFullScreen":
                Assert.True(canApplyDirectFullScreen, $"DirectFullScreen戦略が適用可能であるべき: {testDescription}");
                break;
            case "ROIBased":
                Assert.False(canApplyDirectFullScreen, $"DirectFullScreen戦略は適用不可で、ROI戦略が選択されるべき: {testDescription}");
                break;
            case "Fallback":
                Assert.False(canApplyDirectFullScreen, $"DirectFullScreen戦略は適用不可で、フォールバック戦略が選択されるべき: {testDescription}");
                break;
        }
    }

    [Fact]
    public async Task DirectFullScreenStrategy_ExecuteCapture_SuccessfulCapture()
    {
        // Arrange
        var strategy = new DirectFullScreenCaptureStrategy(_mockLogger.Object, _mockCapturer.Object, _mockEventAggregator.Object);
        var mockImage = new Mock<IWindowsImage>();
        mockImage.Setup(x => x.Width).Returns(1920);
        mockImage.Setup(x => x.Height).Returns(1080);

        _mockCapturer.Setup(x => x.CaptureWindowAsync(It.IsAny<IntPtr>()))
            .ReturnsAsync(mockImage.Object);

        var windowHandle = new IntPtr(0x12345);

        // Act
        var result = await strategy.ExecuteCaptureAsync(windowHandle, _defaultOptions);

        // Assert
        Assert.True(result.Success, $"キャプチャは成功するべき。エラー: {result.ErrorMessage}");
        Assert.NotNull(result.Images);
        Assert.Single(result.Images);
        Assert.NotNull(result.Images[0]);
        Assert.Equal(1920, result.Images[0].Width);
        Assert.Equal(1080, result.Images[0].Height);
        Assert.Equal("DirectFullScreen", result.StrategyName);
        Assert.Equal("HighPerformance", result.Metrics.PerformanceCategory);
        Assert.True(result.Metrics.TotalProcessingTime > TimeSpan.Zero);
    }

    [Fact]
    public async Task DirectFullScreenStrategy_ExecuteCapture_CaptureFailure()
    {
        // Arrange
        var strategy = new DirectFullScreenCaptureStrategy(_mockLogger.Object, _mockCapturer.Object, _mockEventAggregator.Object);

        _mockCapturer.Setup(x => x.CaptureWindowAsync(It.IsAny<IntPtr>()))
            .ThrowsAsync(new InvalidOperationException("キャプチャに失敗しました"));

        var windowHandle = new IntPtr(0x12345);

        // Act
        var result = await strategy.ExecuteCaptureAsync(windowHandle, _defaultOptions);

        // Assert
        Assert.False(result.Success, "キャプチャは失敗するべき");
        Assert.Empty(result.Images);
        Assert.Equal("DirectFullScreen", result.StrategyName);
        // エラーメッセージが実際の戦略例外でラップされている
        Assert.Contains("直接キャプチ", result.ErrorMessage);
    }

    [Fact]
    public void CaptureOptions_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new CaptureOptions();

        // Assert
        Assert.True(options.AllowDirectFullScreen);
        Assert.True(options.AllowROIProcessing);
        Assert.True(options.AllowSoftwareFallback);
        Assert.Equal(0.25f, options.ROIScaleFactor);
        Assert.Equal(3, options.MaxRetryAttempts);
        Assert.True(options.EnableHDRProcessing);
        Assert.Equal(2000, options.TDRTimeoutMs);
    }

    [Fact]
    public void CaptureStrategy_Priority_IsCorrect()
    {
        // Arrange
        var strategy = new DirectFullScreenCaptureStrategy(_mockLogger.Object, _mockCapturer.Object, _mockEventAggregator.Object);

        // Act
        var actualPriority = strategy.Priority;
        var strategyName = strategy.StrategyName;

        // Assert
        Assert.Equal(100, actualPriority); // DirectFullScreenは最高優先度
        Assert.Equal("DirectFullScreen", strategyName);
    }

    [Fact]
    public void Multiple_GPU_Environments_Strategy_Selection()
    {
        // Arrange
        var integratedGPU = GPUEnvironmentMockTests.CreateMockIntegratedGpu();
        var dedicatedGPU = GPUEnvironmentMockTests.CreateMockDedicatedGPU();
        var lowEndGPU = GPUEnvironmentMockTests.CreateMockLowEndIntegratedGPU();

        var strategy = new DirectFullScreenCaptureStrategy(_mockLogger.Object, _mockCapturer.Object, _mockEventAggregator.Object);
        var windowHandle = new IntPtr(0x12345);

        // Act & Assert
        Assert.True(strategy.CanApply(integratedGPU, windowHandle), "統合GPUでDirectFullScreen適用可能");
        Assert.False(strategy.CanApply(dedicatedGPU, windowHandle), "専用GPUでDirectFullScreen適用不可");
        Assert.False(strategy.CanApply(lowEndGPU, windowHandle), "低性能GPUでDirectFullScreen適用不可");
    }

    [Fact]
    public void CaptureStrategyResult_DefaultValues()
    {
        // Arrange & Act
        var result = new CaptureStrategyResult();

        // Assert
        Assert.False(result.Success);
        Assert.Empty(result.Images);
        Assert.Empty(result.TextRegions);
        Assert.Equal(string.Empty, result.StrategyName);
        Assert.Equal(string.Empty, result.ErrorMessage);
        Assert.NotNull(result.Metrics);
        Assert.True(result.CompletionTime <= DateTime.Now);
    }

    [Fact]
    public void CaptureMetrics_DefaultValues()
    {
        // Arrange & Act
        var metrics = new CaptureMetrics();

        // Assert
        Assert.Equal(TimeSpan.Zero, metrics.TotalProcessingTime);
        Assert.Equal(TimeSpan.Zero, metrics.GPUDetectionTime);
        Assert.Equal(TimeSpan.Zero, metrics.StrategySelectionTime);
        Assert.Equal(TimeSpan.Zero, metrics.ActualCaptureTime);
        Assert.Equal(TimeSpan.Zero, metrics.TextureConversionTime);
        Assert.Equal(0, metrics.MemoryUsedMB);
        Assert.Equal(0, metrics.RetryAttempts);
        Assert.Equal(0, metrics.FrameCount);
        Assert.Equal(string.Empty, metrics.PerformanceCategory);
    }

    public static TheoryData<GpuEnvironmentInfo, string, string> GetGPUEnvironmentTestCases()
    {
        return new TheoryData<GpuEnvironmentInfo, string, string>
        {
            {
                GPUEnvironmentMockTests.CreateMockIntegratedGpu(),
                "DirectFullScreen",
                "統合GPU（十分な性能）"
            },
            {
                GPUEnvironmentMockTests.CreateMockDedicatedGPU(),
                "ROIBased",
                "専用GPU（ROIベース最適）"
            },
            {
                GPUEnvironmentMockTests.CreateMockLowEndIntegratedGPU(),
                "Fallback",
                "低性能統合GPU（フォールバック必要）"
            }
        };
    }
}
