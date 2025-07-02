using Baketa.Core.UI.Geometry;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Overlay.Positioning;
using Baketa.UI.Overlay.Positioning;
using Baketa.Core.UI.Overlay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Core.Tests.OverlayPositioning;

/// <summary>
/// Issue #69 オーバーレイ位置・サイズ管理システムのテスト
/// MultiMonitorOverlayManagerに依存しないコア機能のみをテスト
/// </summary>
public sealed class OverlayPositionManagerTests : IDisposable
{
    private readonly Mock<ITextMeasurementService> _mockTextMeasurementService;
    private readonly Mock<ILogger<OverlayPositionManager>> _mockLogger;
    private readonly OverlayPositionManager? _positionManager;
    
    public OverlayPositionManagerTests()
    {
        _mockTextMeasurementService = new Mock<ITextMeasurementService>();
        _mockLogger = new Mock<ILogger<OverlayPositionManager>>();
        
        // MultiMonitorOverlayManagerに依存するテストはスキップし、
        // コア機能のみをテストするアプローチを取る
        _positionManager = null; // 依存関係が解決されるまではnull
    }
    
    [Fact]
    public async Task CoreGeometry_Operations_ShouldWorkCorrectly()
    {
        // Arrange & Act - Core型の基本操作をテスト
        var point = new CorePoint(100, 200);
        var size = new CoreSize(300, 150);
        var rect = new CoreRect(point, size);
        var monitor = CreateTestMonitor();
        
        await Task.CompletedTask;
        
        // Assert
        Assert.Equal(100, point.X);
        Assert.Equal(200, point.Y);
        Assert.Equal(300, size.Width);
        Assert.Equal(150, size.Height);
        Assert.Equal(new CorePoint(100, 200), rect.TopLeft);
        Assert.True(monitor.IsPrimary);
    }
    
    [Fact]
    public void OverlayPositionSettings_Defaults_ShouldBeValid()
    {
        // Arrange & Act
        var translationSettings = OverlayPositionSettings.ForTranslation;
        var debugSettings = OverlayPositionSettings.ForDebug;
        
        // Assert
        Assert.Equal(OverlayPositionMode.OcrRegionBased, translationSettings.PositionMode);
        Assert.Equal(OverlaySizeMode.ContentBased, translationSettings.SizeMode);
        Assert.True(translationSettings.MaxSize.Width > 0);
        Assert.True(translationSettings.MaxSize.Height > 0);
        Assert.True(translationSettings.MinSize.Width > 0);
        Assert.True(translationSettings.MinSize.Height > 0);
        
        Assert.Equal(OverlayPositionMode.Fixed, debugSettings.PositionMode);
        Assert.Equal(OverlaySizeMode.Fixed, debugSettings.SizeMode);
    }
    
    [Fact]
    public void TextRegion_Properties_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var bounds = new CoreRect(10, 20, 100, 50);
        var textRegion = new TextRegion(
            Bounds: bounds,
            Text: "Test Text",
            Confidence: 0.95,
            DetectedAt: DateTimeOffset.Now
        );
        
        // Assert
        Assert.Equal(bounds, textRegion.Bounds);
        Assert.Equal("Test Text", textRegion.Text);
        Assert.Equal(0.95, textRegion.Confidence);
        Assert.True(textRegion.IsValid);
    }
    
    [Fact]
    public void OverlayPositionInfo_Properties_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var position = new CorePoint(100, 200);
        var size = new CoreSize(300, 150);
        var monitor = CreateTestMonitor();
        var method = PositionCalculationMethod.OcrBelowText;
        
        var positionInfo = new OverlayPositionInfo(
            Position: position,
            Size: size,
            SourceTextRegion: null,
            Monitor: monitor,
            CalculationMethod: method
        );
        
        // Assert
        Assert.Equal(position, positionInfo.Position);
        Assert.Equal(size, positionInfo.Size);
        Assert.Equal(monitor, positionInfo.Monitor);
        Assert.Equal(method, positionInfo.CalculationMethod);
        Assert.True(positionInfo.IsValid);
    }
    
    [Fact]
    public void TranslationInfo_Properties_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var translationId = Guid.NewGuid();
        var bounds = new CoreRect(50, 100, 200, 40);
        var sourceRegion = new TextRegion(
            Bounds: bounds,
            Text: "Source",
            Confidence: 0.9,
            DetectedAt: DateTimeOffset.Now
        );
        
        var translationInfo = new TranslationInfo
        {
            SourceText = "Source text",
            TranslatedText = "Translated text",
            SourceRegion = sourceRegion,
            TranslationId = translationId
        };
        
        // Assert
        Assert.Equal("Source text", translationInfo.SourceText);
        Assert.Equal("Translated text", translationInfo.TranslatedText);
        Assert.Equal(sourceRegion, translationInfo.SourceRegion);
        Assert.Equal(translationId, translationInfo.TranslationId);
        Assert.True(translationInfo.IsValid);
    }
    
    [Fact]
    public void GameWindowInfo_Properties_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var windowHandle = new nint(12345);
        var position = new CorePoint(100, 50);
        var size = new CoreSize(800, 600);
        var monitor = CreateTestMonitor();
        
        var gameWindowInfo = new GameWindowInfo
        {
            WindowHandle = windowHandle,
            WindowTitle = "Test Game",
            Position = position,
            Size = size,
            ClientPosition = position,
            ClientSize = size,
            IsFullScreen = false,
            IsMaximized = true,
            IsMinimized = false,
            IsActive = true,
            Monitor = monitor
        };
        
        // Assert
        Assert.Equal(windowHandle, gameWindowInfo.WindowHandle);
        Assert.Equal("Test Game", gameWindowInfo.WindowTitle);
        Assert.Equal(position, gameWindowInfo.Position);
        Assert.Equal(size, gameWindowInfo.Size);
        Assert.False(gameWindowInfo.IsFullScreen);
        Assert.True(gameWindowInfo.IsMaximized);
        Assert.False(gameWindowInfo.IsMinimized);
        Assert.True(gameWindowInfo.IsActive);
        Assert.Equal(monitor, gameWindowInfo.Monitor);
    }
    
    [Fact]
    public void CoreRect_Intersection_ShouldWorkCorrectly()
    {
        // Arrange
        var rect1 = new CoreRect(10, 10, 100, 80);
        var rect2 = new CoreRect(50, 30, 100, 80);
        
        // Act
        var intersection = rect1.Intersect(rect2);
        
        // Assert
        Assert.Equal(50, intersection.X);
        Assert.Equal(30, intersection.Y);
        Assert.Equal(60, intersection.Width); // 10+100-50 = 60
        Assert.Equal(60, intersection.Height); // 10+80-30 = 60
        Assert.True(intersection.Width > 0);
        Assert.True(intersection.Height > 0);
    }
    
    [Fact]
    public void CoreVector_Operations_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var vector1 = new CoreVector(10, 20);
        var vector2 = new CoreVector(5, -10);
        var zeroVector = CoreVector.Zero;
        
        // Assert
        Assert.Equal(10, vector1.X);
        Assert.Equal(20, vector1.Y);
        Assert.Equal(5, vector2.X);
        Assert.Equal(-10, vector2.Y);
        Assert.Equal(0, zeroVector.X);
        Assert.Equal(0, zeroVector.Y);
        
        // 等価性テスト
        var vector1Copy = new CoreVector(10, 20);
        Assert.Equal(vector1, vector1Copy);
        Assert.NotEqual(vector1, vector2);
    }
    
    [Fact]
    public void TextMeasurementInfo_Properties_ShouldWorkCorrectly()
    {
        // Arrange & Act
        var textSize = new CoreSize(250, 60);
        var measuredAt = DateTimeOffset.Now;
        
        var measurementInfo = new TextMeasurementInfo(
            TextSize: textSize,
            LineCount: 2,
            CharacterCount: 25,
            UsedFontSize: 16,
            FontFamily: "Yu Gothic UI",
            MeasuredAt: measuredAt
        );
        
        // Assert
        Assert.Equal(textSize, measurementInfo.TextSize);
        Assert.Equal(2, measurementInfo.LineCount);
        Assert.Equal(25, measurementInfo.CharacterCount);
        Assert.Equal(16, measurementInfo.UsedFontSize);
        Assert.Equal("Yu Gothic UI", measurementInfo.FontFamily);
        Assert.Equal(measuredAt, measurementInfo.MeasuredAt);
        Assert.True(measurementInfo.IsValid);
        
        // 推奨オーバーレイサイズの計算をテスト
        var recommendedSize = measurementInfo.RecommendedOverlaySize;
        Assert.True(recommendedSize.Width >= textSize.Width);
        Assert.True(recommendedSize.Height >= textSize.Height);
    }
    
    [Fact]
    public void PositionCalculationMethod_AllValues_ShouldBeDefined()
    {
        // Arrange & Act - 列挙型の全ての値をテスト
        var allMethods = Enum.GetValues<PositionCalculationMethod>();
        
        // Assert
        Assert.Contains(PositionCalculationMethod.OcrBelowText, allMethods);
        Assert.Contains(PositionCalculationMethod.OcrAboveText, allMethods);
        Assert.Contains(PositionCalculationMethod.OcrRightOfText, allMethods);
        Assert.Contains(PositionCalculationMethod.OcrLeftOfText, allMethods);
        Assert.Contains(PositionCalculationMethod.FixedPosition, allMethods);
        Assert.Contains(PositionCalculationMethod.FallbackPosition, allMethods);
        
        // 各値が明確に定義されていることを確認
        Assert.True(allMethods.Length >= 6);
        
        // ToString()が正常に動作することを確認
        foreach (var method in allMethods)
        {
            var stringValue = method.ToString();
            Assert.False(string.IsNullOrEmpty(stringValue));
        }
    }
    
    private static MonitorInfo CreateTestMonitor()
    {
        return new MonitorInfo(
            Handle: nint.Zero,
            Name: "Test Monitor",
            DeviceId: "TEST",
            Bounds: new Baketa.Core.UI.Geometry.Rect(0, 0, 1920, 1080),
            WorkArea: new Baketa.Core.UI.Geometry.Rect(0, 0, 1920, 1040), // タスクバー分を除く
            IsPrimary: true,
            DpiX: 96,
            DpiY: 96
        );
    }
    
    public void Dispose()
    {
        _positionManager?.DisposeAsync().AsTask().GetAwaiter().GetResult();
        // _multiMonitorManagerはnullのため呼び出しをスキップ
        GC.SuppressFinalize(this);
    }
}
