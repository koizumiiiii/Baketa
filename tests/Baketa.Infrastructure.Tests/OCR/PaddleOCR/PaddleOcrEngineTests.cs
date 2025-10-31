using System.Drawing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR;

/// <summary>
/// PaddleOcrEngineの単体テスト（安全版）
/// すべて SafeTestPaddleOcrEngine を使用してネットワークアクセスを回避
/// </summary>
public sealed class PaddleOcrEngineTests : IDisposable
{
    private readonly Mock<IModelPathResolver> _mockModelPathResolver;
    private readonly TestData.SafeTestPaddleOcrEngine _safeOcrEngine;
    private bool _disposed;

    public PaddleOcrEngineTests()
    {
        _mockModelPathResolver = new Mock<IModelPathResolver>();
        
        // 完全なモデルパス解決のモック設定
        _mockModelPathResolver.Setup(x => x.GetModelsRootDirectory())
            .Returns("test/models");
        _mockModelPathResolver.Setup(x => x.GetDetectionModelsDirectory())
            .Returns("test/detection");
        _mockModelPathResolver.Setup(x => x.GetRecognitionModelsDirectory(It.IsAny<string>()))
            .Returns("test/recognition");
        // CS1061修正: GetClassificationModelsDirectory()は存在しないため削除
        // 代わりにGetClassificationModelPathメソッドをモック
        _mockModelPathResolver.Setup(x => x.GetClassificationModelPath(It.IsAny<string>()))
            .Returns("test/classification/model.onnx");
        _mockModelPathResolver.Setup(x => x.GetDetectionModelPath(It.IsAny<string>()))
            .Returns("test/detection/model.onnx");
        _mockModelPathResolver.Setup(x => x.GetRecognitionModelPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("test/recognition/model.onnx");
        _mockModelPathResolver.Setup(x => x.GetClassificationModelPath(It.IsAny<string>()))
            .Returns("test/classification/model.onnx");
        _mockModelPathResolver.Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(false);
        _mockModelPathResolver.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()));
        
        // 安全なテスト用エンジンのみを使用
        _safeOcrEngine = new TestData.SafeTestPaddleOcrEngine(_mockModelPathResolver.Object, NullLogger<PaddleOcrEngine>.Instance, true);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        using var engine = new TestData.SafeTestPaddleOcrEngine(_mockModelPathResolver.Object, NullLogger<PaddleOcrEngine>.Instance, true);

        // Assert
        Assert.NotNull(engine);
        Assert.Equal("PaddleOCR (Test)", engine.EngineName);
        Assert.Equal("2.7.0.3", engine.EngineVersion);
        Assert.False(engine.IsInitialized);
        Assert.Null(engine.CurrentLanguage);
    }

    [Fact]
    public void Constructor_WithNullModelPathResolver_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new TestData.SafeTestPaddleOcrEngine(null!, NullLogger<PaddleOcrEngine>.Instance, true));
    }

    [Fact]
    public async Task InitializeAsync_WithValidSettings_ShouldReturnTrue()
    {
        // Arrange
        var settings = new OcrEngineSettings
        {
            Language = "eng",
            EnableMultiThread = false,
            WorkerCount = 2
        };

        // Act
        var result = await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);

        // Assert
        Assert.True(result);
        Assert.True(_safeOcrEngine.IsInitialized);
        Assert.Equal("eng", _safeOcrEngine.CurrentLanguage);
    }

    [Fact]
    public async Task InitializeAsync_WithInvalidSettings_ShouldThrowException()
    {
        // Arrange
        var settings = new OcrEngineSettings
        {
            Language = "", // Invalid language
            DetectionThreshold = -1.0 // Invalid threshold
        };

        // Act & Assert
        // SafeTestPaddleOcrEngine では検証で例外を投げる
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _safeOcrEngine.InitializeAsync(settings)).ConfigureAwait(false);
        
        Assert.False(_safeOcrEngine.IsInitialized);
    }

    [Fact]
    public async Task RecognizeAsync_WithoutInitialization_ShouldThrowInvalidOperationException()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);
        mockImage.Setup(x => x.Height).Returns(100);
        mockImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(new byte[100]);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _safeOcrEngine.RecognizeAsync(mockImage.Object)).ConfigureAwait(false);
    }

    [Fact]
    public async Task RecognizeAsync_WithNullImage_ShouldThrowArgumentNullException()
    {
        // Arrange
        var settings = new OcrEngineSettings { Language = "eng" };
        await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);

        // Act & Assert
        // 🎯 [OPTION_B] 型キャストを明示してメソッドオーバーロードを明確化
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _safeOcrEngine.RecognizeAsync((IImage)null!)).ConfigureAwait(false);
    }

    [Fact]
    public async Task RecognizeAsync_WithValidImage_ShouldReturnOcrResult()
    {
        // Arrange
        var settings = new OcrEngineSettings { Language = "eng" };
        await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);
        
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);
        mockImage.Setup(x => x.Height).Returns(100);
        mockImage.Setup(x => x.ToByteArrayAsync())
            .ReturnsAsync([1, 2, 3, 4]); // IDE0300修正: コレクション式を使用

        // Act
        var result = await _safeOcrEngine.RecognizeAsync(mockImage.Object).ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("eng", result.LanguageCode);
        Assert.Equal(mockImage.Object, result.SourceImage);
        Assert.Null(result.RegionOfInterest);
    }

    [Fact]
    public async Task RecognizeAsync_WithROI_ShouldReturnOcrResultWithROI()
    {
        // Arrange
        var settings = new OcrEngineSettings { Language = "eng" };
        await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);
        
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(100);
        mockImage.Setup(x => x.Height).Returns(100);
        mockImage.Setup(x => x.ToByteArrayAsync())
            .ReturnsAsync([1, 2, 3, 4]); // IDE0300修正: コレクション式を使用

        var roi = new Rectangle(10, 10, 50, 50);

        // Act
        var result = await _safeOcrEngine.RecognizeAsync(mockImage.Object, roi).ConfigureAwait(false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(roi, result.RegionOfInterest);
    }

    [Fact]
    public void GetSettings_ShouldReturnCurrentSettings()
    {
        // Act
        var settings = _safeOcrEngine.GetSettings();

        // Assert
        Assert.NotNull(settings);
        Assert.Equal("jpn", settings.Language); // Default value
        Assert.True(settings.IsValid());
    }

    [Fact]
    public async Task ApplySettingsAsync_WithValidSettings_ShouldUpdateSettings()
    {
        // Arrange
        var initialSettings = new OcrEngineSettings { Language = "eng" };
        await _safeOcrEngine.InitializeAsync(initialSettings).ConfigureAwait(false);
        
        var newSettings = new OcrEngineSettings
        {
            Language = "jpn",
            DetectionThreshold = 0.4,
            RecognitionThreshold = 0.6
        };

        // Act
        await _safeOcrEngine.ApplySettingsAsync(newSettings).ConfigureAwait(false);

        // Assert
        var currentSettings = _safeOcrEngine.GetSettings();
        Assert.Equal("jpn", currentSettings.Language);
        Assert.Equal(0.4, currentSettings.DetectionThreshold);
        Assert.Equal(0.6, currentSettings.RecognitionThreshold);
    }

    [Fact]
    public async Task ApplySettingsAsync_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange
        var initialSettings = new OcrEngineSettings { Language = "eng" };
        await _safeOcrEngine.InitializeAsync(initialSettings).ConfigureAwait(false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _safeOcrEngine.ApplySettingsAsync(null!)).ConfigureAwait(false);
    }

    [Fact]
    public void GetAvailableLanguages_ShouldReturnSupportedLanguages()
    {
        // Act
        var languages = _safeOcrEngine.GetAvailableLanguages();

        // Assert
        Assert.NotNull(languages);
        Assert.Contains("eng", languages);
        Assert.Contains("jpn", languages);
    }

    [Fact]
    public void GetAvailableModels_ShouldReturnSupportedModels()
    {
        // Act
        var models = _safeOcrEngine.GetAvailableModels();

        // Assert
        Assert.NotNull(models);
        Assert.Contains("standard", models);
    }

    [Fact]
    public async Task IsLanguageAvailableAsync_WithSupportedLanguage_ShouldReturnTrue()
    {
        // Act
        var isAvailable = await _safeOcrEngine.IsLanguageAvailableAsync("jpn").ConfigureAwait(false);

        // Assert
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task IsLanguageAvailableAsync_WithUnsupportedLanguage_ShouldReturnFalse()
    {
        // Act
        var isAvailable = await _safeOcrEngine.IsLanguageAvailableAsync("xxx").ConfigureAwait(false);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public void GetPerformanceStats_ShouldReturnStats()
    {
        // Act
        var stats = _safeOcrEngine.GetPerformanceStats();

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.TotalProcessedImages);
        Assert.Equal(0, stats.ErrorCount);
        Assert.True(stats.SuccessRate >= 0.0 && stats.SuccessRate <= 1.0);
    }

    [Fact]
    public void Dispose_ShouldReleaseResources()
    {
        // Act
        _safeOcrEngine.Dispose();

        // Assert - No exception should be thrown
        Assert.False(_safeOcrEngine.IsInitialized);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Act & Assert - Multiple calls should not throw
        _safeOcrEngine.Dispose();
        _safeOcrEngine.Dispose();
    }

    [Fact]
    public async Task InitializeAsync_CalledMultipleTimes_ShouldReturnTrue()
    {
        // Arrange
        var settings = new OcrEngineSettings { Language = "eng" };

        // Act
        var result1 = await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);
        var result2 = await _safeOcrEngine.InitializeAsync(settings).ConfigureAwait(false);

        // Assert
        Assert.True(result1);
        Assert.True(result2); // Should return true even if already initialized
    }

    private void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _safeOcrEngine?.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OcrEngineSettingsの単体テスト
/// </summary>
public class OcrEngineSettingsTests
{
    [Fact]
    public void DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var settings = new OcrEngineSettings();

        // Assert
        Assert.Equal("jpn", settings.Language);
        Assert.Equal(0.09, settings.DetectionThreshold);
        Assert.Equal(0.16, settings.RecognitionThreshold);
        Assert.Equal("standard", settings.ModelName);
        Assert.Equal(200, settings.MaxDetections);
        Assert.False(settings.UseDirectionClassification);
        Assert.False(settings.UseGpu);
        Assert.Equal(0, settings.GpuDeviceId);
        Assert.False(settings.EnableMultiThread);
        Assert.Equal(2, settings.WorkerCount);
    }

    [Theory]
    [InlineData("jpn", 0.3, 0.5, "standard", 200, 0, 2, true)]
    [InlineData("eng", 0.4, 0.6, "v2", 50, 1, 4, true)]
    public void IsValid_WithValidSettings_ShouldReturnTrue(
        string language, double detThreshold, double recThreshold, 
        string modelName, int maxDetections, int gpuDeviceId, int workerCount, bool expected)
    {
        // Arrange
        var settings = new OcrEngineSettings
        {
            Language = language,
            DetectionThreshold = detThreshold,
            RecognitionThreshold = recThreshold,
            ModelName = modelName,
            MaxDetections = maxDetections,
            GpuDeviceId = gpuDeviceId,
            WorkerCount = workerCount
        };

        // Act
        var isValid = settings.IsValid();

        // Assert
        Assert.Equal(expected, isValid);
    }

    [Theory]
    [InlineData("", 0.3, 0.5, "standard", 200, 0, 2, false)] // Empty language
    [InlineData("jpn", -0.1, 0.5, "standard", 200, 0, 2, false)] // Invalid detection threshold
    [InlineData("jpn", 0.3, 1.5, "standard", 200, 0, 2, false)] // Invalid recognition threshold
    [InlineData("jpn", 0.3, 0.5, "", 200, 0, 2, false)] // Empty model name
    [InlineData("jpn", 0.3, 0.5, "standard", 0, 0, 2, false)] // Invalid max detections
    [InlineData("jpn", 0.3, 0.5, "standard", 200, -1, 2, false)] // Invalid GPU device ID
    [InlineData("jpn", 0.3, 0.5, "standard", 200, 0, 0, false)] // Invalid worker count
    public void IsValid_WithInvalidSettings_ShouldReturnFalse(
        string language, double detThreshold, double recThreshold, 
        string modelName, int maxDetections, int gpuDeviceId, int workerCount, bool expected)
    {
        // Arrange
        var settings = new OcrEngineSettings
        {
            Language = language,
            DetectionThreshold = detThreshold,
            RecognitionThreshold = recThreshold,
            ModelName = modelName,
            MaxDetections = maxDetections,
            GpuDeviceId = gpuDeviceId,
            WorkerCount = workerCount
        };

        // Act
        var isValid = settings.IsValid();

        // Assert
        Assert.Equal(expected, isValid);
    }

    [Fact]
    public void Clone_ShouldCreateExactCopy()
    {
        // Arrange
        var original = new OcrEngineSettings
        {
            Language = "eng",
            DetectionThreshold = 0.4,
            RecognitionThreshold = 0.6,
            ModelName = "v2",
            MaxDetections = 50,
            UseDirectionClassification = true,
            UseGpu = true,
            GpuDeviceId = 1,
            EnableMultiThread = true,
            WorkerCount = 4
        };

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.Language, clone.Language);
        Assert.Equal(original.DetectionThreshold, clone.DetectionThreshold);
        Assert.Equal(original.RecognitionThreshold, clone.RecognitionThreshold);
        Assert.Equal(original.ModelName, clone.ModelName);
        Assert.Equal(original.MaxDetections, clone.MaxDetections);
        Assert.Equal(original.UseDirectionClassification, clone.UseDirectionClassification);
        Assert.Equal(original.UseGpu, clone.UseGpu);
        Assert.Equal(original.GpuDeviceId, clone.GpuDeviceId);
        Assert.Equal(original.EnableMultiThread, clone.EnableMultiThread);
        Assert.Equal(original.WorkerCount, clone.WorkerCount);
    }
}
