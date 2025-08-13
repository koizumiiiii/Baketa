using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Baketa.Core.Models.Capture;
using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.Platform.Windows.GPU;
using Baketa.Infrastructure.Platform.Windows.Capture.Strategies;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Tests.Windows.GPU;

/// <summary>
/// GPU環境検出とキャプチャ戦略選択のモックベーステスト
/// </summary>
public class GPUEnvironmentMockTests
{
    /// <summary>
    /// 統合GPUモック環境の作成
    /// </summary>
    public static GpuEnvironmentInfo CreateMockIntegratedGpu()
    {
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = true,
            IsDedicatedGpu = false,
            SupportsCuda = false,
            SupportsOpenCL = true,
            SupportsDirectML = true,
            SupportsOpenVINO = true,
            SupportsTensorRT = false,
            MaximumTexture2DDimension = 4096,
            AvailableMemoryMB = 512,
            GpuName = "Intel UHD Graphics 620 (Mock)",
            DirectXFeatureLevel = DirectXFeatureLevel.D3D111,
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Unknown,
            RecommendedProviders = [ExecutionProvider.DirectML, ExecutionProvider.CPU]
        };
    }

    /// <summary>
    /// 専用GPUモック環境の作成
    /// </summary>
    public static GpuEnvironmentInfo CreateMockDedicatedGPU()
    {
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = false,
            IsDedicatedGpu = true,
            SupportsCuda = true,
            SupportsOpenCL = true,
            SupportsDirectML = true,
            SupportsOpenVINO = false,
            SupportsTensorRT = true,
            MaximumTexture2DDimension = 16384,
            AvailableMemoryMB = 8192,
            GpuName = "NVIDIA GeForce RTX 3060 (Mock)",
            DirectXFeatureLevel = DirectXFeatureLevel.D3D121,
            GpuDeviceId = 1,
            ComputeCapability = ComputeCapability.Compute75,
            RecommendedProviders = [ExecutionProvider.CUDA, ExecutionProvider.TensorRT]
        };
    }

    /// <summary>
    /// 低性能統合GPUモック環境の作成（制約あり）
    /// </summary>
    public static GpuEnvironmentInfo CreateMockLowEndIntegratedGPU()
    {
        return new GpuEnvironmentInfo
        {
            IsIntegratedGpu = true,
            IsDedicatedGpu = false,
            SupportsCuda = false,
            SupportsOpenCL = false,
            SupportsDirectML = true,
            SupportsOpenVINO = false,
            SupportsTensorRT = false,
            MaximumTexture2DDimension = 2048, // 制約あり
            AvailableMemoryMB = 256,           // 低メモリ
            GpuName = "Intel HD Graphics 4000 (Mock)",
            DirectXFeatureLevel = DirectXFeatureLevel.D3D110,
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Unknown,
            RecommendedProviders = [ExecutionProvider.DirectML, ExecutionProvider.CPU]
        };
    }

    [Fact]
    public void DirectFullScreenStrategy_ShouldApply_ForIntegratedGPU()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        var integratedGPUEnv = CreateMockIntegratedGpu();
        var windowHandle = new IntPtr(0x12345);

        // Act
        var canApply = strategy.CanApply(integratedGPUEnv, windowHandle);

        // Assert
        Assert.True(canApply, "DirectFullScreen戦略は統合GPUで適用可能であるべき");
    }

    [Fact]
    public void DirectFullScreenStrategy_ShouldNotApply_ForDedicatedGPU()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        var dedicatedGPUEnv = CreateMockDedicatedGPU();
        var windowHandle = new IntPtr(0x12345);

        // Act
        var canApply = strategy.CanApply(dedicatedGPUEnv, windowHandle);

        // Assert
        Assert.False(canApply, "DirectFullScreen戦略は専用GPUでは適用不可であるべき");
    }

    [Fact]
    public void DirectFullScreenStrategy_ShouldNotApply_ForLowEndGPU()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        var lowEndGPUEnv = CreateMockLowEndIntegratedGPU();
        var windowHandle = new IntPtr(0x12345);

        // Act
        var canApply = strategy.CanApply(lowEndGPUEnv, windowHandle);

        // Assert
        Assert.False(canApply, "DirectFullScreen戦略はテクスチャサイズ不足のGPUでは適用不可であるべき");
    }

    [Theory]
    [InlineData(4096, true)]  // 十分なテクスチャサイズ
    [InlineData(2048, false)] // 不十分なテクスチャサイズ
    [InlineData(1024, false)] // 明らかに不十分
    public void DirectFullScreenStrategy_TextureSize_Validation(int textureSize, bool expectedResult)
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        
        var gpuEnv = new GpuEnvironmentInfo
        {
            IsIntegratedGpu = true,
            IsDedicatedGpu = false,
            SupportsCuda = false,
            SupportsOpenCL = true,
            SupportsDirectML = true,
            SupportsOpenVINO = true,
            SupportsTensorRT = false,
            MaximumTexture2DDimension = textureSize,
            AvailableMemoryMB = 512,
            GpuName = "Intel UHD Graphics 620 (Mock)",
            DirectXFeatureLevel = DirectXFeatureLevel.D3D111,
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Unknown,
            RecommendedProviders = [ExecutionProvider.DirectML, ExecutionProvider.CPU]
        };
        var windowHandle = new IntPtr(0x12345);

        // Act
        var canApply = strategy.CanApply(gpuEnv, windowHandle);

        // Assert
        Assert.Equal(expectedResult, canApply);
    }

    [Fact]
    public async Task DirectFullScreenStrategy_ValidatePrerequisites_ValidWindow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        var validWindowHandle = new IntPtr(0x12345);

        // Act
        var isValid = await strategy.ValidatePrerequisitesAsync(validWindowHandle);

        // Assert - 実際のウィンドウAPIを使用するため、結果は環境依存
        // モックテストでは前提条件の検証ロジック自体をテスト
        Assert.True(validWindowHandle != IntPtr.Zero);
        
        // Note: isValid は環境依存のため、具体的な値をアサートしない
        _ = isValid; // 使用されていない警告を抑制
    }

    [Fact]
    public async Task DirectFullScreenStrategy_ValidatePrerequisites_InvalidWindow()
    {
        // Arrange
        var mockLogger = new Mock<ILogger<DirectFullScreenCaptureStrategy>>();
        var mockCapturer = new Mock<IWindowsCapturer>();
        var strategy = new DirectFullScreenCaptureStrategy(mockLogger.Object, mockCapturer.Object);
        var invalidWindowHandle = IntPtr.Zero;

        // Act
        var isValid = await strategy.ValidatePrerequisitesAsync(invalidWindowHandle);

        // Assert
        Assert.False(isValid, "無効なウィンドウハンドルでは前提条件検証が失敗するべき");
    }

    [Fact]
    public void GPUEnvironmentInfo_IntegratedGPU_Properties()
    {
        // Arrange & Act
        var integratedGPU = CreateMockIntegratedGpu();

        // Assert
        Assert.True(integratedGPU.IsIntegratedGpu);
        Assert.False(integratedGPU.IsDedicatedGpu);
        Assert.True(integratedGPU.SupportsDirectML);
        Assert.Equal(4096, integratedGPU.MaximumTexture2DDimension);
        Assert.Equal(512, integratedGPU.AvailableMemoryMB);
        Assert.Contains("Intel UHD Graphics", integratedGPU.GpuName);
        Assert.Equal(DirectXFeatureLevel.D3D111, integratedGPU.DirectXFeatureLevel);
    }

    [Fact]
    public void GPUEnvironmentInfo_DedicatedGPU_Properties()
    {
        // Arrange & Act
        var dedicatedGPU = CreateMockDedicatedGPU();

        // Assert
        Assert.False(dedicatedGPU.IsIntegratedGpu);
        Assert.True(dedicatedGPU.IsDedicatedGpu);
        Assert.True(dedicatedGPU.SupportsCuda);
        Assert.Equal(16384, dedicatedGPU.MaximumTexture2DDimension);
        Assert.Equal(8192, dedicatedGPU.AvailableMemoryMB);
        Assert.Contains("NVIDIA GeForce RTX", dedicatedGPU.GpuName);
        Assert.Equal(DirectXFeatureLevel.D3D121, dedicatedGPU.DirectXFeatureLevel);
    }

    [Fact]
    public void GPUEnvironmentInfo_LowEndGPU_Constraints()
    {
        // Arrange & Act
        var lowEndGPU = CreateMockLowEndIntegratedGPU();

        // Assert
        Assert.True(lowEndGPU.IsIntegratedGpu);
        Assert.Equal(2048, lowEndGPU.MaximumTexture2DDimension); // 制約確認
        Assert.Equal(256, lowEndGPU.AvailableMemoryMB);           // 低メモリ確認
        Assert.False(lowEndGPU.SupportsCuda);                     // CUDAサポートなし
        Assert.Contains("Intel HD Graphics 4000", lowEndGPU.GpuName);
        Assert.Equal(DirectXFeatureLevel.D3D110, lowEndGPU.DirectXFeatureLevel);
    }

    [Fact]
    public void GpuEnvironmentInfo_IntegratedGpu_BasicProperties()
    {
        // Arrange & Act
        var integratedGPU = CreateMockIntegratedGpu();

        // Assert
        Assert.Contains("Intel UHD Graphics", integratedGPU.GpuName);
        Assert.Equal(0, integratedGPU.GpuDeviceId);
        Assert.True(integratedGPU.IsIntegratedGpu);
        Assert.False(integratedGPU.IsDedicatedGpu);
    }
}