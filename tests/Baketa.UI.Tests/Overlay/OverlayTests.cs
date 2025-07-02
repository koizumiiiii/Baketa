using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Baketa.Core.UI.Overlay;
using Baketa.Core.UI.Geometry;
using Xunit;
using Xunit.Abstractions;
using Point = Baketa.Core.UI.Geometry.Point;
using Size = Baketa.Core.UI.Geometry.Size;
using Rect = Baketa.Core.UI.Geometry.Rect;

namespace Baketa.UI.Tests.Overlay;

/// <summary>
/// オーバーレイ機能のテストクラス
/// Test Explorerで認識・実行可能
/// </summary>
public sealed class OverlayTests : IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OverlayTests> _logger;
    private readonly ITestOutputHelper _output;
    private bool _disposed;
    
    public OverlayTests(ITestOutputHelper output)
    {
        _output = output;
        
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
        _logger = _serviceProvider.GetRequiredService<ILogger<OverlayTests>>();
    }
    
    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        // 実際のオーバーレイサービスが実装されたらここに追加
    }
    
    /// <summary>
    /// 基本的な幾何型のテスト
    /// </summary>
    [Fact]
    public void GeometryTypesShouldWorkCorrectly()
    {
        // Arrange & Act
        var point = new Point(100, 200);
        var size = new Size(300, 400);
        var rect = new Rect(point, size);
        
        // Assert
        Assert.Equal(100, point.X);
        Assert.Equal(200, point.Y);
        Assert.Equal(300, size.Width);
        Assert.Equal(400, size.Height);
        Assert.Equal(100, rect.X);
        Assert.Equal(200, rect.Y);
        Assert.Equal(300, rect.Width);
        Assert.Equal(400, rect.Height);
        
        _output.WriteLine("✅ 幾何型の基本動作確認完了");
    }
    
    /// <summary>
    /// 矩形の包含テスト
    /// </summary>
    [Fact]
    public void RectContainsShouldWork()
    {
        // Arrange
        var rect = new Rect(10, 10, 100, 100);
        var insidePoint = new Point(50, 50);
        var outsidePoint = new Point(150, 150);
        
        // Act & Assert
        Assert.True(rect.Contains(insidePoint));
        Assert.False(rect.Contains(outsidePoint));
        
        _output.WriteLine("✅ 矩形の包含判定テスト完了");
    }
    
    /// <summary>
    /// 矩形の交差テスト
    /// </summary>
    [Fact]
    public void RectIntersectionShouldWork()
    {
        // Arrange
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(50, 50, 100, 100);
        var rect3 = new Rect(200, 200, 100, 100);
        
        // Act & Assert
        Assert.True(rect1.IntersectsWith(rect2));
        Assert.False(rect1.IntersectsWith(rect3));
        
        var intersection = rect1.Intersect(rect2);
        Assert.False(intersection.IsEmpty);
        Assert.Equal(50, intersection.Width);
        Assert.Equal(50, intersection.Height);
        
        _output.WriteLine("✅ 矩形の交差判定テスト完了");
    }
    
    /// <summary>
    /// 矩形の結合テスト
    /// </summary>
    [Fact]
    public void RectUnionShouldWork()
    {
        // Arrange
        var rect1 = new Rect(0, 0, 50, 50);
        var rect2 = new Rect(25, 25, 50, 50);
        
        // Act
        var union = rect1.Union(rect2);
        
        // Assert
        Assert.Equal(0, union.X);
        Assert.Equal(0, union.Y);
        Assert.Equal(75, union.Width);
        Assert.Equal(75, union.Height);
        
        _output.WriteLine("✅ 矩形の結合テスト完了");
    }
    
    /// <summary>
    /// 矩形の変形テスト
    /// </summary>
    [Fact]
    public void RectTransformationsShouldWork()
    {
        // Arrange
        var rect = new Rect(10, 10, 80, 60);
        
        // Act & Assert - 膨張
        var inflated = rect.Inflate(5);
        Assert.Equal(5, inflated.X);
        Assert.Equal(5, inflated.Y);
        Assert.Equal(90, inflated.Width);
        Assert.Equal(70, inflated.Height);
        
        // Act & Assert - 移動
        var offset = rect.Offset(10, 20);
        Assert.Equal(20, offset.X);
        Assert.Equal(30, offset.Y);
        Assert.Equal(80, offset.Width);
        Assert.Equal(60, offset.Height);
        
        _output.WriteLine("✅ 矩形の変形テスト完了");
    }
    
    /// <summary>
    /// 空の矩形テスト
    /// </summary>
    [Fact]
    public void RectEmptyShouldBehaveCorrectly()
    {
        // Arrange
        var empty = Rect.Empty;
        var zeroSize = new Rect(10, 10, 0, 0);
        var negativeSize = new Rect(10, 10, -5, 10);
        
        // Act & Assert
        Assert.True(empty.IsEmpty);
        Assert.True(zeroSize.IsEmpty);
        Assert.True(negativeSize.IsEmpty);
        
        var normal = new Rect(10, 10, 50, 50);
        Assert.False(normal.IsEmpty);
        
        _output.WriteLine("✅ 空の矩形テスト完了");
    }
    
    /// <summary>
    /// サイズとポイントの基本テスト
    /// </summary>
    [Fact]
    public void PointAndSizeBasicOperationsShouldWork()
    {
        // Arrange & Act
        var zero = Point.Zero;
        var empty = Size.Empty;
        
        // Assert
        Assert.Equal(0, zero.X);
        Assert.Equal(0, zero.Y);
        Assert.Equal(0, empty.Width);
        Assert.Equal(0, empty.Height);
        
        // 等価性テスト
        var point1 = new Point(10, 20);
        var point2 = new Point(10, 20);
        var point3 = new Point(10, 21);
        
        Assert.Equal(point1, point2);
        Assert.NotEqual(point1, point3);
        
        _output.WriteLine("✅ ポイントとサイズの基本操作テスト完了");
    }
    
    /// <summary>
    /// 文字列表現テスト
    /// </summary>
    [Fact]
    public void GeometryTypesToStringShouldWork()
    {
        // Arrange
        var point = new Point(10.5, 20.7);
        var size = new Size(100, 200);
        var rect = new Rect(5, 10, 50, 75);
        
        // Act
        var pointStr = point.ToString();
        var sizeStr = size.ToString();
        var rectStr = rect.ToString();
        
        // Assert
        Assert.Contains("10.5", pointStr, StringComparison.Ordinal);
        Assert.Contains("20.7", pointStr, StringComparison.Ordinal);
        Assert.Contains("100", sizeStr, StringComparison.Ordinal);
        Assert.Contains("200", sizeStr, StringComparison.Ordinal);
        Assert.Contains("5", rectStr, StringComparison.Ordinal);
        Assert.Contains("10", rectStr, StringComparison.Ordinal);
        Assert.Contains("50", rectStr, StringComparison.Ordinal);
        Assert.Contains("75", rectStr, StringComparison.Ordinal);
        
        _output.WriteLine($"✅ 文字列表現テスト完了");
        _output.WriteLine($"  Point: {pointStr}");
        _output.WriteLine($"  Size: {sizeStr}");
        _output.WriteLine($"  Rect: {rectStr}");
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
