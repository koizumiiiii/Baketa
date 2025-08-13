using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.GPU;
using Baketa.Core.Settings;
using Baketa.Infrastructure.OCR.GPU;

namespace Baketa.Infrastructure.Tests.OCR.GPU;

/// <summary>
/// EnhancedGpuOcrAccelerator の基本テスト
/// </summary>
public class EnhancedGpuOcrAcceleratorTests
{
    private readonly Mock<IGpuEnvironmentDetector> _mockGpuDetector;
    private readonly Mock<ILogger<EnhancedGpuOcrAccelerator>> _mockLogger;
    private readonly OcrSettings _testSettings;
    private readonly MockOnnxSessionProvider _mockSessionProvider;

    public EnhancedGpuOcrAcceleratorTests()
    {
        _mockGpuDetector = new Mock<IGpuEnvironmentDetector>();
        _mockLogger = new Mock<ILogger<EnhancedGpuOcrAccelerator>>();
        _mockSessionProvider = new MockOnnxSessionProvider();
        
        _testSettings = new OcrSettings
        {
            EnableGpuAcceleration = true,
            OnnxExecutionProvider = "CPU", // テスト用にCPUを使用
            RecognitionLanguage = "ja",
            GpuSettings = new GpuOcrSettings
            {
                DetectionModelPath = @"test_models\detection.onnx",
                RecognitionModelPath = @"test_models\recognition.onnx",
                CpuThreadCount = 2,
                EnableWarmup = false // テスト用にウォームアップを無効化
            }
        };
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            _testSettings,
            _mockSessionProvider);

        // Assert
        Assert.NotNull(accelerator);
        Assert.Equal("Enhanced GPU OCR Accelerator", accelerator.EngineName);
        Assert.Equal("1.0.0", accelerator.EngineVersion);
        Assert.False(accelerator.IsInitialized);
        Assert.Equal("ja", accelerator.CurrentLanguage);
    }

    [Fact]
    public void Constructor_WithNullGpuDetector_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedGpuOcrAccelerator(null!, _mockLogger.Object, _testSettings, _mockSessionProvider));
    }

    [Fact]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedGpuOcrAccelerator(_mockGpuDetector.Object, null!, _testSettings, _mockSessionProvider));
    }

    [Fact]
    public void Constructor_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new EnhancedGpuOcrAccelerator(_mockGpuDetector.Object, _mockLogger.Object, null!, _mockSessionProvider));
    }

    [Fact]
    public void CurrentLanguage_ShouldReturnLanguageFromSettings()
    {
        // Arrange
        var settings = new OcrSettings { RecognitionLanguage = "en" };
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            settings,
            _mockSessionProvider);

        // Act & Assert
        Assert.Equal("en", accelerator.CurrentLanguage);
    }

    [Fact]
    public async Task InitializeAsync_WhenAlreadyInitialized_ShouldReturnTrue()
    {
        // Arrange
        var mockGpuInfo = new GpuEnvironmentInfo
        {
            GpuName = "Test GPU",
            RecommendedProviders = [ExecutionProvider.CPU]
        };

        _mockGpuDetector
            .Setup(x => x.DetectEnvironmentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockGpuInfo);

        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            _testSettings,
            _mockSessionProvider);

        // Act
        var firstResult = await accelerator.InitializeAsync();
        var secondResult = await accelerator.InitializeAsync();

        // Assert
        Assert.True(firstResult);
        Assert.True(secondResult);
        Assert.True(accelerator.IsInitialized);
    }

    [Fact]
    public void Settings_DisabledGpuAcceleration_ShouldUseCorrectProvider()
    {
        // Arrange
        var disabledSettings = new OcrSettings
        {
            EnableGpuAcceleration = false,
            OnnxExecutionProvider = "DirectML", // GPU無効なのでこれは無視される
            RecognitionLanguage = "ja"
        };

        // Act
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            disabledSettings,
            _mockSessionProvider);

        // Assert
        Assert.NotNull(accelerator);
        Assert.Equal("ja", accelerator.CurrentLanguage);
    }

    [Fact]
    public void Settings_CustomExecutionProvider_ShouldBeConfigurable()
    {
        // Arrange
        var customSettings = new OcrSettings
        {
            EnableGpuAcceleration = true,
            OnnxExecutionProvider = "DirectML",
            RecognitionLanguage = "en",
            GpuSettings = new GpuOcrSettings
            {
                EnableDirectMLOptimization = true,
                GpuDeviceId = 1
            }
        };

        // Act
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            customSettings,
            _mockSessionProvider);

        // Assert
        Assert.NotNull(accelerator);
        Assert.Equal("en", accelerator.CurrentLanguage);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Arrange
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            _testSettings,
            _mockSessionProvider);

        // Act & Assert
        var exception = Record.Exception(() => accelerator.Dispose());
        Assert.Null(exception);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var accelerator = new EnhancedGpuOcrAccelerator(
            _mockGpuDetector.Object,
            _mockLogger.Object,
            _testSettings,
            _mockSessionProvider);

        // Act & Assert
        accelerator.Dispose();
        var exception = Record.Exception(() => accelerator.Dispose());
        Assert.Null(exception);
    }
}