using System;
using System.Drawing;
using Xunit;
using Baketa.Core.Events.EventTypes;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Settings;

namespace Baketa.Core.Tests.Events;

/// <summary>
/// フルスクリーンイベントクラスのテスト
/// </summary>
public class FullscreenEventsTests
{
    [Fact]
    public void FullscreenStateChangedEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo
        {
            IsFullscreen = true,
            ProcessName = "TestGame",
            WindowTitle = "Game Window",
            Confidence = 0.9
        };
        var previousState = false;
        
        // Act
        var eventData = new FullscreenStateChangedEvent(fullscreenInfo, previousState);
        
        // Assert
        Assert.Same(fullscreenInfo, eventData.FullscreenInfo);
        Assert.Equal(previousState, eventData.PreviousFullscreenState);
        Assert.Equal("FullscreenStateChanged", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenStateChangedEvent_WithNullInfo_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new FullscreenStateChangedEvent(null!));
    }
    
    [Fact]
    public void FullscreenStateChangedEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo
        {
            IsFullscreen = true,
            ProcessName = "TestGame",
            Confidence = 0.85
        };
        var eventData = new FullscreenStateChangedEvent(fullscreenInfo);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenStateChanged", result);
        Assert.Contains("TestGame", result);
        Assert.Contains("Fullscreen", result);
        Assert.Contains("0.85", result);
    }
    
    [Fact]
    public void FullscreenOptimizationAppliedEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo
        {
            ProcessName = "TestGame",
            IsFullscreen = true
        };
        var optimizedSettings = new CaptureSettings
        {
            CaptureIntervalMs = 300,
            CaptureQuality = 80
        };
        var originalSettings = new CaptureSettings
        {
            CaptureIntervalMs = 200,
            CaptureQuality = 90
        };
        
        // Act
        var eventData = new FullscreenOptimizationAppliedEvent(
            fullscreenInfo, optimizedSettings, originalSettings);
        
        // Assert
        Assert.Same(fullscreenInfo, eventData.FullscreenInfo);
        Assert.Same(optimizedSettings, eventData.OptimizedSettings);
        Assert.Same(originalSettings, eventData.OriginalSettings);
        Assert.Equal("FullscreenOptimizationApplied", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenOptimizationAppliedEvent_WithNullInfo_ShouldThrowArgumentNullException()
    {
        // Arrange
        var settings = new CaptureSettings();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FullscreenOptimizationAppliedEvent(null!, settings));
    }
    
    [Fact]
    public void FullscreenOptimizationAppliedEvent_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo();
        
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FullscreenOptimizationAppliedEvent(fullscreenInfo, null!));
    }
    
    [Fact]
    public void FullscreenOptimizationAppliedEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var fullscreenInfo = new FullscreenInfo { ProcessName = "TestGame" };
        var optimizedSettings = new CaptureSettings
        {
            CaptureIntervalMs = 300,
            CaptureQuality = 80
        };
        var eventData = new FullscreenOptimizationAppliedEvent(fullscreenInfo, optimizedSettings);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenOptimizationApplied", result);
        Assert.Contains("TestGame", result);
        Assert.Contains("300ms", result);
        Assert.Contains("80%", result);
    }
    
    [Fact]
    public void FullscreenOptimizationRemovedEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var restoredSettings = new CaptureSettings { CaptureIntervalMs = 200 };
        var reason = "Window mode detected";
        var windowInfo = "TestGame.exe";
        
        // Act
        var eventData = new FullscreenOptimizationRemovedEvent(restoredSettings, reason, windowInfo);
        
        // Assert
        Assert.Same(restoredSettings, eventData.RestoredSettings);
        Assert.Equal(reason, eventData.Reason);
        Assert.Equal(windowInfo, eventData.WindowInfo);
        Assert.Equal("FullscreenOptimizationRemoved", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenOptimizationRemovedEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var restoredSettings = new CaptureSettings { CaptureIntervalMs = 200 };
        var reason = "Test reason";
        var eventData = new FullscreenOptimizationRemovedEvent(restoredSettings, reason);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenOptimizationRemoved", result);
        Assert.Contains("Test reason", result);
        Assert.Contains("200ms", result);
    }
    
    [Fact]
    public void FullscreenOptimizationErrorEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var context = "Test context";
        var errorMessage = "Custom error message";
        
        // Act
        var eventData = new FullscreenOptimizationErrorEvent(exception, context, errorMessage);
        
        // Assert
        Assert.Same(exception, eventData.Exception);
        Assert.Equal(context, eventData.Context);
        Assert.Equal(errorMessage, eventData.ErrorMessage);
        Assert.Equal("FullscreenOptimizationError", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenOptimizationErrorEvent_WithNullException_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FullscreenOptimizationErrorEvent(null!));
    }
    
    [Fact]
    public void FullscreenOptimizationErrorEvent_WithNullMessage_ShouldUseExceptionMessage()
    {
        // Arrange
        var exception = new InvalidOperationException("Exception message");
        
        // Act
        var eventData = new FullscreenOptimizationErrorEvent(exception);
        
        // Assert
        Assert.Equal(exception.Message, eventData.ErrorMessage);
    }
    
    [Fact]
    public void FullscreenOptimizationErrorEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var exception = new InvalidOperationException("Test exception");
        var context = "Test context";
        var eventData = new FullscreenOptimizationErrorEvent(exception, context);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenOptimizationError", result);
        Assert.Contains("Test exception", result);
        Assert.Contains("Test context", result);
    }
    
    [Fact]
    public void FullscreenDetectionStartedEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var settings = new FullscreenDetectionSettings
        {
            DetectionIntervalMs = 1500,
            MinConfidence = 0.75
        };
        
        // Act
        var eventData = new FullscreenDetectionStartedEvent(settings);
        
        // Assert
        Assert.Same(settings, eventData.Settings);
        Assert.Equal("FullscreenDetectionStarted", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenDetectionStartedEvent_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new FullscreenDetectionStartedEvent(null!));
    }
    
    [Fact]
    public void FullscreenDetectionStartedEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var settings = new FullscreenDetectionSettings
        {
            DetectionIntervalMs = 1500,
            MinConfidence = 0.75
        };
        var eventData = new FullscreenDetectionStartedEvent(settings);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenDetectionStarted", result);
        Assert.Contains("1500ms", result);
        Assert.Contains("0.75", result);
    }
    
    [Fact]
    public void FullscreenDetectionStoppedEvent_Constructor_ShouldSetProperties()
    {
        // Arrange
        var reason = "Manual stop";
        var duration = TimeSpan.FromMinutes(5);
        
        // Act
        var eventData = new FullscreenDetectionStoppedEvent(reason, duration);
        
        // Assert
        Assert.Equal(reason, eventData.Reason);
        Assert.Equal(duration, eventData.RunDuration);
        Assert.Equal("FullscreenDetectionStopped", eventData.Name);
        Assert.Equal("Capture", eventData.Category);
    }
    
    [Fact]
    public void FullscreenDetectionStoppedEvent_ToString_ShouldReturnFormattedString()
    {
        // Arrange
        var reason = "Test reason";
        var duration = TimeSpan.FromHours(1).Add(TimeSpan.FromMinutes(30)).Add(TimeSpan.FromSeconds(45));
        var eventData = new FullscreenDetectionStoppedEvent(reason, duration);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("FullscreenDetectionStopped", result);
        Assert.Contains("Test reason", result);
        Assert.Contains("01:30:45", result);
    }
    
    [Fact]
    public void FullscreenDetectionStoppedEvent_WithNullDuration_ShouldShowUnknown()
    {
        // Arrange
        var reason = "Test reason";
        var eventData = new FullscreenDetectionStoppedEvent(reason, null);
        
        // Act
        var result = eventData.ToString();
        
        // Assert
        Assert.Contains("Unknown", result);
    }
}
