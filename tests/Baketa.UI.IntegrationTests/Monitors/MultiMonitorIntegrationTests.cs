using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.UI.Monitors;
using Baketa.Core.UI.Geometry;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.UI.IntegrationTests.Monitors;

/// <summary>
/// マルチモニター統合テストクラス
/// Test Explorerで認識・実行可能
/// </summary>
public sealed class MultiMonitorIntegrationTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MultiMonitorIntegrationTests> _logger;
    private readonly ITestOutputHelper _output;
    private bool _disposed;
    
    public MultiMonitorIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<MultiMonitorIntegrationTests>>();
    }
    
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        // 実際のマルチモニターサービスが実装されたらここに追加
    }
    
    /// <summary>
    /// マルチモニター環境の検出テスト
    /// </summary>
    [Fact]
    public void MultiMonitorDetectionShouldWork()
    {
        // Arrange - 利用可能な画面数を取得
        var screenCount = System.Windows.Forms.Screen.AllScreens.Length;
        
        // Act & Assert
        Assert.True(screenCount > 0, "少なくとも1つの画面が必要です");
        
        _output.WriteLine($"✅ 検出された画面数: {screenCount}");
        
        if (screenCount > 1)
        {
            _output.WriteLine("🔵 マルチモニター環境が検出されました");
        }
        else
        {
            _output.WriteLine("🟡 シングルモニター環境です");
        }
    }
    
    /// <summary>
    /// 画面情報の取得テスト
    /// </summary>
    [Fact]
    public void ScreenInformationShouldBeAccessible()
    {
        // Arrange
        var screens = System.Windows.Forms.Screen.AllScreens;
        
        // Act & Assert
        for (int i = 0; i < screens.Length; i++)
        {
            var screen = screens[i];
            
            Assert.NotNull(screen);
            Assert.NotNull(screen.DeviceName);
            
            var bounds = screen.Bounds;
            var workingArea = screen.WorkingArea;
            
            Assert.True(bounds.Width > 0);
            Assert.True(bounds.Height > 0);
            Assert.True(workingArea.Width > 0);
            Assert.True(workingArea.Height > 0);
            
            _output.WriteLine($"📺 画面 {i + 1}:");
            _output.WriteLine($"  デバイス名: {screen.DeviceName}");
            _output.WriteLine($"  範囲: {bounds.Width}x{bounds.Height} at ({bounds.X}, {bounds.Y})");
            _output.WriteLine($"  作業領域: {workingArea.Width}x{workingArea.Height} at ({workingArea.X}, {workingArea.Y})");
            _output.WriteLine($"  プライマリ: {screen.Primary}");
            _output.WriteLine($"  BitsPerPixel: {screen.BitsPerPixel}");
        }
        
        _output.WriteLine("✅ 画面情報の取得テスト完了");
    }
    
    /// <summary>
    /// プライマリ画面の識別テスト
    /// </summary>
    [Fact]
    public void PrimaryScreenShouldBeIdentifiable()
    {
        // Arrange
        var primaryScreen = System.Windows.Forms.Screen.PrimaryScreen;
        var allScreens = System.Windows.Forms.Screen.AllScreens;
        
        // Act & Assert
        Assert.NotNull(primaryScreen);
        Assert.True(primaryScreen.Primary);
        
        var primaryCount = allScreens.Count(s => s.Primary);
        Assert.Equal(1, primaryCount); // プライマリ画面は1つだけ
        
        _output.WriteLine($"✅ プライマリ画面: {primaryScreen.DeviceName}");
        _output.WriteLine($"  範囲: {primaryScreen.Bounds}");
    }
    
    /// <summary>
    /// 仮想スクリーン座標の検証テスト
    /// </summary>
    [Fact]
    public void VirtualScreenCoordinatesShouldBeValid()
    {
        // Arrange
        var virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        var allScreens = System.Windows.Forms.Screen.AllScreens;
        
        // Act & Assert
        Assert.True(virtualScreen.Width > 0);
        Assert.True(virtualScreen.Height > 0);
        
        // すべての画面が仮想スクリーン内に含まれることを確認
        foreach (var screen in allScreens)
        {
            var screenBounds = screen.Bounds;
            
            // 画面の一部が仮想スクリーン内にあることを確認
            var intersects = virtualScreen.IntersectsWith(screenBounds);
            Assert.True(intersects, $"画面 {screen.DeviceName} が仮想スクリーン範囲外にあります");
        }
        
        _output.WriteLine($"✅ 仮想スクリーン: {virtualScreen.Width}x{virtualScreen.Height} at ({virtualScreen.X}, {virtualScreen.Y})");
    }
    
    /// <summary>
    /// DPI情報の取得テスト
    /// </summary>
    [Fact]
    public void DpiInformationShouldBeAccessible()
    {
        // Arrange & Act
        using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        var dpiX = graphics.DpiX;
        var dpiY = graphics.DpiY;
        
        // Assert
        Assert.True(dpiX > 0);
        Assert.True(dpiY > 0);
        
        // 一般的なDPI値の範囲内であることを確認
        Assert.True(dpiX >= 96 && dpiX <= 480); // 96 DPI (100%) から 480 DPI (500%)
        Assert.True(dpiY >= 96 && dpiY <= 480);
        
        var scaleFactorX = dpiX / 96.0;
        var scaleFactorY = dpiY / 96.0;
        
        _output.WriteLine($"✅ DPI情報:");
        _output.WriteLine($"  DPI: {dpiX} x {dpiY}");
        _output.WriteLine($"  スケールファクター: {scaleFactorX:F2} x {scaleFactorY:F2}");
    }
    
    /// <summary>
    /// マルチモニター座標変換の基本テスト
    /// </summary>
    [Fact]
    public void CoordinateTransformationShouldWork()
    {
        // Arrange
        var screens = System.Windows.Forms.Screen.AllScreens;
        
        if (screens.Length < 2)
        {
            _output.WriteLine("⚠️  マルチモニター環境ではないため、座標変換テストをスキップします");
            return;
        }
        
        var screen1 = screens[0];
        var screen2 = screens[1];
        
        // Act - 座標変換のシミュレーション
        var pointOnScreen1 = new System.Drawing.Point(
            screen1.Bounds.X + screen1.Bounds.Width / 2,
            screen1.Bounds.Y + screen1.Bounds.Height / 2);
        
        var relativeX = (double)(pointOnScreen1.X - screen1.Bounds.X) / screen1.Bounds.Width;
        var relativeY = (double)(pointOnScreen1.Y - screen1.Bounds.Y) / screen1.Bounds.Height;
        
        var pointOnScreen2 = new System.Drawing.Point(
            screen2.Bounds.X + (int)(relativeX * screen2.Bounds.Width),
            screen2.Bounds.Y + (int)(relativeY * screen2.Bounds.Height));
        
        // Assert
        Assert.True(screen1.Bounds.Contains(pointOnScreen1));
        Assert.True(screen2.Bounds.Contains(pointOnScreen2));
        
        _output.WriteLine($"✅ 座標変換テスト:");
        _output.WriteLine($"  画面1の点: ({pointOnScreen1.X}, {pointOnScreen1.Y})");
        _output.WriteLine($"  画面2の点: ({pointOnScreen2.X}, {pointOnScreen2.Y})");
        _output.WriteLine($"  相対位置: ({relativeX:F2}, {relativeY:F2})");
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    private void Dispose(bool disposing)
    {
        if (!_disposed && disposing)
        {
            if (_serviceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
            _disposed = true;
        }
    }
}
