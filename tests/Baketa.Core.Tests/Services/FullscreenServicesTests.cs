using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Services;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.Services;

/// <summary>
/// フルスクリーン検出サービスのテスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class FullscreenDetectionServiceTests : IDisposable
{
    private readonly Mock<ILogger<WindowsFullscreenDetectionService>> _mockLogger;
    private readonly WindowsFullscreenDetectionService _detectionService;

    public FullscreenDetectionServiceTests()
    {
        _mockLogger = new Mock<ILogger<WindowsFullscreenDetectionService>>();
        _detectionService = new WindowsFullscreenDetectionService(_mockLogger.Object);
    }

    [Fact]
    public void Settings_ShouldReturnDefaultSettings()
    {
        // Act
        var settings = _detectionService.Settings;

        // Assert
        Assert.NotNull(settings);
        Assert.Equal(1000, settings.DetectionIntervalMs);
        Assert.Equal(5, settings.SizeTolerance);
        Assert.Equal(0.8, settings.MinConfidence);
    }

    [Fact]
    public void IsRunning_ShouldReturnFalseInitially()
    {
        // Assert
        Assert.False(_detectionService.IsRunning);
    }

    [Fact]
    public async Task DetectFullscreenAsync_WithInvalidHandle_ShouldReturnEmptyInfo()
    {
        // Act
        var result = await _detectionService.DetectFullscreenAsync(IntPtr.Zero);

        // Assert
        Assert.NotNull(result);
        Assert.False(result.IsFullscreen);
        Assert.Equal(0.0, result.Confidence);
        Assert.Equal(IntPtr.Zero, result.WindowHandle);
    }

    [Fact]
    public void UpdateDetectionSettings_ShouldUpdateSettings()
    {
        // Arrange
        var newSettings = new FullscreenDetectionSettings
        {
            DetectionIntervalMs = 2000,
            SizeTolerance = 10,
            MinConfidence = 0.9
        };

        // Act
        _detectionService.UpdateDetectionSettings(newSettings);

        // Assert
        var updatedSettings = _detectionService.Settings;
        Assert.Equal(2000, updatedSettings.DetectionIntervalMs);
        Assert.Equal(10, updatedSettings.SizeTolerance);
        Assert.Equal(0.9, updatedSettings.MinConfidence);
    }

    [Fact]
    public void UpdateDetectionSettings_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _detectionService.UpdateDetectionSettings(null!));
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act & Assert
        _detectionService.Dispose();

        // 二重Disposeのテスト
        _detectionService.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _detectionService?.Dispose();
        }
    }

    [Fact]
    public void FullscreenInfo_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var info = new FullscreenInfo
        {
            IsFullscreen = true,
            Confidence = 0.85,
            DetectionMethod = FullscreenDetectionMethod.Combined,
            ProcessName = "TestGame",
            WindowBounds = new Rectangle(0, 0, 1920, 1080),
            MonitorBounds = new Rectangle(0, 0, 1920, 1080)
        };

        // Act
        var result = info.ToString();

        // Assert
        Assert.Contains("IsFullscreen: True", result);
        Assert.Contains("Confidence: 0.85", result);
        Assert.Contains("Method: Combined", result);
        Assert.Contains("Process: TestGame", result);
    }

    [Fact]
    public void FullscreenDetectionSettings_Clone_ShouldCreateIndependentCopy()
    {
        // Arrange
        var originalSettings = new FullscreenDetectionSettings
        {
            DetectionIntervalMs = 1500,
            SizeTolerance = 8,
            MinConfidence = 0.75
        };

        // Act
        var clonedSettings = originalSettings.Clone();

        // Assert
        Assert.NotSame(originalSettings, clonedSettings);
        Assert.Equal(originalSettings.DetectionIntervalMs, clonedSettings.DetectionIntervalMs);
        Assert.Equal(originalSettings.SizeTolerance, clonedSettings.SizeTolerance);
        Assert.Equal(originalSettings.MinConfidence, clonedSettings.MinConfidence);

        // 変更が独立していることを確認
        clonedSettings.DetectionIntervalMs = 3000;
        Assert.NotEqual(originalSettings.DetectionIntervalMs, clonedSettings.DetectionIntervalMs);
    }
}

/// <summary>
/// フルスクリーン最適化サービスのテスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class FullscreenOptimizationServiceTests : IDisposable
{
    private readonly Mock<IFullscreenDetectionService> _mockDetectionService;
    private readonly Mock<IAdvancedCaptureService> _mockCaptureService;
    private readonly Mock<ILogger<WindowsFullscreenOptimizationService>> _mockLogger;
    private readonly WindowsFullscreenOptimizationService _optimizationService;

    public FullscreenOptimizationServiceTests()
    {
        _mockDetectionService = new Mock<IFullscreenDetectionService>();
        _mockCaptureService = new Mock<IAdvancedCaptureService>();
        _mockLogger = new Mock<ILogger<WindowsFullscreenOptimizationService>>();

        _optimizationService = new WindowsFullscreenOptimizationService(
            _mockDetectionService.Object,
            _mockCaptureService.Object,
            _mockLogger.Object);
    }

    [Fact]
    public void Status_ShouldReturnDisabledInitially()
    {
        // Assert
        Assert.Equal(FullscreenOptimizationStatus.Disabled, _optimizationService.Status);
    }

    [Fact]
    public void IsEnabled_ShouldReturnTrueInitially()
    {
        // Assert
        Assert.True(_optimizationService.IsEnabled);
    }

    [Fact]
    public void IsOptimizationActive_ShouldReturnFalseInitially()
    {
        // Assert
        Assert.False(_optimizationService.IsOptimizationActive);
    }

    [Fact]
    public void CurrentFullscreenInfo_ShouldReturnNullInitially()
    {
        // Assert
        Assert.Null(_optimizationService.CurrentFullscreenInfo);
    }

    [Fact]
    public void Statistics_ShouldReturnValidStatistics()
    {
        // Act
        var stats = _optimizationService.Statistics;

        // Assert
        Assert.NotNull(stats);
        Assert.Equal(0, stats.OptimizationAppliedCount);
        Assert.Equal(0, stats.OptimizationRemovedCount);
        Assert.Equal(0, stats.ErrorCount);
    }

    [Fact]
    public void ResetStatistics_ShouldResetAllCounters()
    {
        // Arrange
        var stats = _optimizationService.Statistics;
        stats.OptimizationAppliedCount = 5;
        stats.OptimizationRemovedCount = 3;
        stats.ErrorCount = 1;

        // Act
        _optimizationService.ResetStatistics();

        // Assert
        Assert.Equal(0, stats.OptimizationAppliedCount);
        Assert.Equal(0, stats.OptimizationRemovedCount);
        Assert.Equal(0, stats.ErrorCount);
    }

    [Fact]
    public async Task ApplyOptimizationAsync_WithNullInfo_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _optimizationService.ApplyOptimizationAsync(null!));
    }

    [Fact]
    public void FullscreenOptimizationStats_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var stats = new FullscreenOptimizationStats
        {
            OptimizationAppliedCount = 3,
            OptimizationRemovedCount = 2,
            ErrorCount = 1,
            CurrentOptimizedWindow = "TestGame.exe - Game Window"
        };

        // Act
        var result = stats.ToString();

        // Assert
        Assert.Contains("適用回数: 3", result);
        Assert.Contains("解除回数: 2", result);
        Assert.Contains("エラー: 1", result);
        Assert.Contains("TestGame.exe - Game Window", result);
    }

    [Fact]
    public void FullscreenOptimizationStats_Reset_ShouldResetAllValues()
    {
        // Arrange
        var stats = new FullscreenOptimizationStats
        {
            OptimizationAppliedCount = 5,
            OptimizationRemovedCount = 3,
            ErrorCount = 2,
            LastOptimizationTime = DateTime.Now,
            LastErrorTime = DateTime.Now,
            CurrentOptimizedWindow = "TestWindow"
        };

        // Act
        stats.Reset();

        // Assert
        Assert.Equal(0, stats.OptimizationAppliedCount);
        Assert.Equal(0, stats.OptimizationRemovedCount);
        Assert.Equal(0, stats.ErrorCount);
        Assert.Null(stats.LastOptimizationTime);
        Assert.Null(stats.LastErrorTime);
        Assert.Null(stats.CurrentOptimizedWindow);
    }

    [Fact]
    public void Dispose_ShouldNotThrow()
    {
        // Act & Assert
        _optimizationService.Dispose();

        // 二重Disposeのテスト
        _optimizationService.Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {
            _optimizationService?.Dispose();
        }
    }
}

/// <summary>
/// フルスクリーンイベント引数のテスト
/// </summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "Test method naming convention")]
public class FullscreenEventArgsTests
{
    [Fact]
    public void FullscreenOptimizationAppliedEventArgs_Constructor_ShouldSetProperties()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo
        {
            IsFullscreen = true,
            ProcessName = "TestGame"
        };
        var optimizedSettings = new Baketa.Core.Settings.CaptureSettings
        {
            CaptureIntervalMs = 300
        };
        var originalSettings = new Baketa.Core.Settings.CaptureSettings
        {
            CaptureIntervalMs = 200
        };

        // Act
        var eventArgs = new FullscreenOptimizationAppliedEventArgs(
            fullscreenInfo, optimizedSettings, originalSettings);

        // Assert
        Assert.Same(fullscreenInfo, eventArgs.FullscreenInfo);
        Assert.Same(optimizedSettings, eventArgs.OptimizedSettings);
        Assert.Same(originalSettings, eventArgs.OriginalSettings);
        Assert.True(eventArgs.AppliedTime <= DateTime.Now);
    }

    [Fact]
    public void FullscreenOptimizationRemovedEventArgs_Constructor_ShouldSetProperties()
    {
        // Arrange
        var restoredSettings = new Baketa.Core.Settings.CaptureSettings();
        var reason = "Test reason";

        // Act
        var eventArgs = new FullscreenOptimizationRemovedEventArgs(restoredSettings, reason);

        // Assert
        Assert.Same(restoredSettings, eventArgs.RestoredSettings);
        Assert.Equal(reason, eventArgs.Reason);
        Assert.True(eventArgs.RemovedTime <= DateTime.Now);
    }

    [Fact]
    public void FullscreenOptimizationErrorEventArgs_Constructor_ShouldSetProperties()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var message = "Custom error message";

        // Act
        var eventArgs = new FullscreenOptimizationErrorEventArgs(exception, message);

        // Assert
        Assert.Same(exception, eventArgs.Exception);
        Assert.Equal(message, eventArgs.Message);
        Assert.True(eventArgs.ErrorTime <= DateTime.Now);
    }

    [Fact]
    public void FullscreenOptimizationErrorEventArgs_WithNullMessage_ShouldUseExceptionMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");

        // Act
        var eventArgs = new FullscreenOptimizationErrorEventArgs(exception);

        // Assert
        Assert.Equal(exception.Message, eventArgs.Message);
    }
}
