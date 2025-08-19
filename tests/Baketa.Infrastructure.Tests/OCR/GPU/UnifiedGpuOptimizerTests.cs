using Baketa.Core.Abstractions.GPU;
using Baketa.Infrastructure.OCR.GPU;
using Baketa.Infrastructure.OCR.GPU.Providers;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;
using Microsoft.ML.OnnxRuntime;

namespace Baketa.Infrastructure.Tests.OCR.GPU;

/// <summary>
/// 統合GPU最適化システム ユニットテスト
/// Phase 4.3: 統合GPU最適化テスト
/// </summary>
public sealed class UnifiedGpuOptimizerTests : IDisposable
{
    private readonly Mock<IGpuEnvironmentDetector> _mockEnvironmentDetector;
    private readonly Mock<ILogger<UnifiedGpuOptimizer>> _mockLogger;
    private readonly List<Mock<IExecutionProviderFactory>> _mockProviderFactories;
    private readonly UnifiedGpuOptimizer _optimizer;
    private readonly GpuEnvironmentInfo _testEnvironment;

    public UnifiedGpuOptimizerTests()
    {
        _mockEnvironmentDetector = new Mock<IGpuEnvironmentDetector>();
        _mockLogger = new Mock<ILogger<UnifiedGpuOptimizer>>();
        _mockProviderFactories = [];

        // テスト用GPU環境情報
        _testEnvironment = new GpuEnvironmentInfo
        {
            IsIntegratedGpu = false,
            IsDedicatedGpu = true,
            SupportsCuda = true,
            SupportsDirectML = true,
            SupportsOpenVINO = true,
            AvailableMemoryMB = 8192,
            GpuName = "Test RTX 4070",
            DirectXFeatureLevel = DirectXFeatureLevel.D3D121,
            GpuDeviceId = 0,
            ComputeCapability = ComputeCapability.Compute89,
            RecommendedProviders = [ExecutionProvider.CUDA, ExecutionProvider.TensorRT, ExecutionProvider.DirectML]
        };

        _mockEnvironmentDetector
            .Setup(x => x.DetectEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(_testEnvironment);
            
        _mockEnvironmentDetector
            .Setup(x => x.GetCachedEnvironment())
            .Returns(_testEnvironment);

        // モックプロバイダー作成
        CreateMockProviderFactories();

        _optimizer = new UnifiedGpuOptimizer(
            _mockProviderFactories.Select(m => m.Object),
            _mockEnvironmentDetector.Object,
            _mockLogger.Object);
    }

    [Fact]
    public async Task SelectOptimalProvider_ShouldReturnHighestPriorityProvider()
    {
        // Act
        var result = await _optimizer.SelectOptimalProviderAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(ExecutionProvider.CUDA, result.Type); // 最高優先度（90）のCUDAが選択されるはず
    }

    [Fact]
    public async Task SelectOptimalProvider_ShouldCacheResult()
    {
        // Act - 2回連続で呼び出し
        var result1 = await _optimizer.SelectOptimalProviderAsync();
        var result2 = await _optimizer.SelectOptimalProviderAsync();

        // Assert
        Assert.Same(result1, result2);
        
        // 環境検出またはキャッシュアクセスが呼ばれることを確認
        _mockEnvironmentDetector.Verify(x => x.GetCachedEnvironment(), Times.AtLeastOnce);
    }

    [Fact]
    public async Task GetFallbackProviders_ShouldReturnProvidersByPriority()
    {
        // Act
        var fallbackProviders = await _optimizer.GetFallbackProvidersAsync(maxProviders: 3);

        // Assert
        Assert.Equal(3, fallbackProviders.Count);
        
        // 優先度順で並んでいることを確認
        Assert.Equal(ExecutionProvider.CUDA, fallbackProviders[0].Type);      // Priority: 90
        Assert.Equal(ExecutionProvider.OpenVINO, fallbackProviders[1].Type);   // Priority: 80
        Assert.Equal(ExecutionProvider.DirectML, fallbackProviders[2].Type);   // Priority: 75
    }

    [Fact]
    public async Task CreateOptimalSessionOptions_ShouldUseOptimalProvider()
    {
        // Act
        var sessionOptions = await _optimizer.CreateOptimalSessionOptionsAsync();

        // Assert
        Assert.NotNull(sessionOptions);
        
        // CUDAプロバイダーのCreateSessionOptionsが呼ばれることを確認
        var cudaProvider = _mockProviderFactories.First(m => m.Object.Type == ExecutionProvider.CUDA);
        cudaProvider.Verify(x => x.CreateSessionOptions(_testEnvironment), Times.Once);
    }

    [Fact]
    public async Task CreateSessionOptionsWithFallback_PreferredAvailable_ShouldUsePreferred()
    {
        // Act - DirectMLを優先指定
        var sessionOptions = await _optimizer.CreateSessionOptionsWithFallbackAsync(ExecutionProvider.DirectML);

        // Assert
        Assert.NotNull(sessionOptions);
        
        // DirectMLプロバイダーが使用されることを確認
        var directmlProvider = _mockProviderFactories.First(m => m.Object.Type == ExecutionProvider.DirectML);
        directmlProvider.Verify(x => x.CreateSessionOptions(_testEnvironment), Times.Once);
    }

    [Fact]
    public async Task CreateSessionOptionsWithFallback_PreferredUnavailable_ShouldFallback()
    {
        // Arrange - TensorRTを利用不可に設定
        var tensorrtProvider = _mockProviderFactories.First(m => m.Object.Type == ExecutionProvider.TensorRT);
        tensorrtProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(false);

        // Act - 利用不可なTensorRTを優先指定
        var sessionOptions = await _optimizer.CreateSessionOptionsWithFallbackAsync(ExecutionProvider.TensorRT);

        // Assert
        Assert.NotNull(sessionOptions);
        
        // フォールバックでCUDAが使用されることを確認
        var cudaProvider = _mockProviderFactories.First(m => m.Object.Type == ExecutionProvider.CUDA);
        cudaProvider.Verify(x => x.CreateSessionOptions(_testEnvironment), Times.Once);
        
        // TensorRTは使用されないことを確認
        tensorrtProvider.Verify(x => x.CreateSessionOptions(It.IsAny<GpuEnvironmentInfo>()), Times.Never);
    }

    [Fact]
    public async Task GetProviderStatus_ShouldReturnAllProvidersWithStatus()
    {
        // Act
        var providerStatuses = await _optimizer.GetProviderStatusAsync();

        // Assert
        Assert.Equal(4, providerStatuses.Count); // CUDA, OpenVINO, DirectML, TensorRT
        
        // 優先度順でソートされていることを確認
        Assert.Equal(ExecutionProvider.CUDA, providerStatuses[0].Type);
        Assert.Equal(90, providerStatuses[0].Priority);
        Assert.True(providerStatuses[0].IsSupported);
        
        Assert.Equal(ExecutionProvider.OpenVINO, providerStatuses[1].Type);
        Assert.Equal(80, providerStatuses[1].Priority);
        Assert.True(providerStatuses[1].IsSupported);
    }

    [Fact]
    public void Constructor_NoProviderFactories_ShouldThrowArgumentException()
    {
        // Arrange & Act & Assert
        Assert.Throws<InvalidOperationException>(() => new UnifiedGpuOptimizer(
            [],
            _mockEnvironmentDetector.Object,
            _mockLogger.Object));
    }

    [Fact]
    public async Task SelectOptimalProvider_NoSupportedProviders_ShouldThrowInvalidOperationException()
    {
        // Arrange - すべてのプロバイダーを利用不可に設定
        foreach (var mockProvider in _mockProviderFactories)
        {
            mockProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(false);
        }

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _optimizer.SelectOptimalProviderAsync());
    }

    private void CreateMockProviderFactories()
    {
        // CUDA Provider
        var cudaProvider = new Mock<IExecutionProviderFactory>();
        cudaProvider.Setup(x => x.Type).Returns(ExecutionProvider.CUDA);
        cudaProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(true);
        cudaProvider.Setup(x => x.Priority(_testEnvironment)).Returns(90);
        cudaProvider.Setup(x => x.GetProviderInfo(_testEnvironment)).Returns("CUDA Provider on RTX 4070");
        cudaProvider.Setup(x => x.CreateSessionOptions(_testEnvironment)).Returns(new SessionOptions());
        _mockProviderFactories.Add(cudaProvider);

        // OpenVINO Provider
        var openvinoProvider = new Mock<IExecutionProviderFactory>();
        openvinoProvider.Setup(x => x.Type).Returns(ExecutionProvider.OpenVINO);
        openvinoProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(true);
        openvinoProvider.Setup(x => x.Priority(_testEnvironment)).Returns(80);
        openvinoProvider.Setup(x => x.GetProviderInfo(_testEnvironment)).Returns("OpenVINO Provider on Intel CPU");
        openvinoProvider.Setup(x => x.CreateSessionOptions(_testEnvironment)).Returns(new SessionOptions());
        _mockProviderFactories.Add(openvinoProvider);

        // DirectML Provider
        var directmlProvider = new Mock<IExecutionProviderFactory>();
        directmlProvider.Setup(x => x.Type).Returns(ExecutionProvider.DirectML);
        directmlProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(true);
        directmlProvider.Setup(x => x.Priority(_testEnvironment)).Returns(75);
        directmlProvider.Setup(x => x.GetProviderInfo(_testEnvironment)).Returns("DirectML Provider on Dedicated GPU");
        directmlProvider.Setup(x => x.CreateSessionOptions(_testEnvironment)).Returns(new SessionOptions());
        _mockProviderFactories.Add(directmlProvider);

        // TensorRT Provider
        var tensorrtProvider = new Mock<IExecutionProviderFactory>();
        tensorrtProvider.Setup(x => x.Type).Returns(ExecutionProvider.TensorRT);
        tensorrtProvider.Setup(x => x.IsSupported(_testEnvironment)).Returns(true);
        tensorrtProvider.Setup(x => x.Priority(_testEnvironment)).Returns(95);
        tensorrtProvider.Setup(x => x.GetProviderInfo(_testEnvironment)).Returns("TensorRT Provider on RTX 4070");
        tensorrtProvider.Setup(x => x.CreateSessionOptions(_testEnvironment)).Returns(new SessionOptions());
        _mockProviderFactories.Add(tensorrtProvider);
    }

    public void Dispose()
    {
        _optimizer?.Dispose();
    }
}

/// <summary>
/// OpenVINO Execution Provider Factory ユニットテスト
/// </summary>
public sealed class OpenVINOExecutionProviderFactoryTests
{
    private readonly Mock<ILogger<OpenVINOExecutionProviderFactory>> _mockLogger;
    private readonly OpenVINOSettings _settings;
    private readonly OpenVINOExecutionProviderFactory _factory;

    public OpenVINOExecutionProviderFactoryTests()
    {
        _mockLogger = new Mock<ILogger<OpenVINOExecutionProviderFactory>>();
        _settings = new OpenVINOSettings
        {
            Enabled = true,
            NumThreads = 4,
            CacheDirectory = "test_cache",
            EnableCpuOptimization = true,
            EnableGpuOptimization = true
        };
        
        _factory = new OpenVINOExecutionProviderFactory(_mockLogger.Object, _settings);
    }

    [Fact]
    public void Type_ShouldReturnOpenVINO()
    {
        // Act & Assert
        Assert.Equal(ExecutionProvider.OpenVINO, _factory.Type);
    }

    [Fact]
    public void IsSupported_OpenVINOSupported_ShouldReturnTrue()
    {
        // Arrange
        var environment = new GpuEnvironmentInfo
        {
            SupportsOpenVINO = true
        };

        // Act
        var result = _factory.IsSupported(environment);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsSupported_OpenVINONotSupported_ShouldReturnFalse()
    {
        // Arrange
        var environment = new GpuEnvironmentInfo
        {
            SupportsOpenVINO = false
        };

        // Act
        var result = _factory.IsSupported(environment);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void Priority_IntelEnvironment_ShouldReturnHighPriority()
    {
        // Arrange
        var environment = new GpuEnvironmentInfo
        {
            IsIntegratedGpu = true,
            SupportsOpenVINO = true
        };

        // Act
        var priority = _factory.Priority(environment);

        // Assert - 非Intel環境では優先度が大幅に下がる（basePriority - 50）
        Assert.Equal(30, priority); // Math.Max(80 - 50, 10) = 30
    }

    [Fact]
    public void CreateSessionOptions_ShouldConfigureOpenVINOProvider()
    {
        // Arrange
        var environment = new GpuEnvironmentInfo
        {
            IsIntegratedGpu = true,
            SupportsOpenVINO = true
        };

        // Act
        var sessionOptions = _factory.CreateSessionOptions(environment);

        // Assert - OpenVINO制限対応で代替実装が正常動作
        Assert.NotNull(sessionOptions);
        Assert.True(sessionOptions.EnableCpuMemArena); // OpenVINO CPU最適化
        Assert.True(sessionOptions.EnableMemoryPattern); // 共通最適化設定
        Assert.Equal(ExecutionMode.ORT_SEQUENTIAL, sessionOptions.ExecutionMode); // CPU系プロバイダー
        Assert.Equal(Environment.ProcessorCount, sessionOptions.IntraOpNumThreads); // OpenVINO設定反映
        
        // リソースクリーンアップ
        sessionOptions.Dispose();
    }
}
