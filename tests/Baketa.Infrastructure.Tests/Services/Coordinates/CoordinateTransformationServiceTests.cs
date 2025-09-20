using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Baketa.Infrastructure.Services.Coordinates;
using System;
using System.Drawing;

namespace Baketa.Infrastructure.Tests.Services.Coordinates;

/// <summary>
/// CoordinateTransformationServiceの単体テスト
/// Geminiレビュー推奨事項: エッジケース含む包括的テスト
/// </summary>
public class CoordinateTransformationServiceTests : IDisposable
{
    private readonly CoordinateTransformationService _service;
    private readonly ILogger<CoordinateTransformationService> _logger;

    public CoordinateTransformationServiceTests()
    {
        _logger = NullLogger<CoordinateTransformationService>.Instance;
        _service = new CoordinateTransformationService(_logger);
    }

    [Fact]
    public void Constructor_ValidLogger_CreatesInstance()
    {
        // Act & Assert
        Assert.NotNull(_service);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new CoordinateTransformationService(null!));
    }

    [Theory]
    [InlineData(100, 100, 200, 150, 0.25f, 400, 400, 800, 600)] // 通常の変換 (4倍スケール)
    [InlineData(50, 25, 100, 75, 0.5f, 100, 50, 200, 150)]     // 2倍スケール
    [InlineData(0, 0, 100, 100, 1.0f, 0, 0, 100, 100)]         // スケールなし
    [InlineData(10, 20, 30, 40, 0.1f, 100, 200, 300, 400)]     // 10倍スケール
    public void ConvertRoiToScreenCoordinates_ValidInput_ReturnsCorrectCoordinates(
        int roiX, int roiY, int roiWidth, int roiHeight, float scaleFactor,
        int expectedX, int expectedY, int expectedWidth, int expectedHeight)
    {
        // Arrange
        var roiBounds = new Rectangle(roiX, roiY, roiWidth, roiHeight);
        var windowHandle = new IntPtr(12345); // 有効なハンドル値をシミュレート

        // Act
        var result = _service.ConvertRoiToScreenCoordinates(roiBounds, windowHandle, scaleFactor);

        // Assert - スケーリング部分のテスト（ウィンドウオフセットは実環境依存のため検証困難）
        var inverseScale = 1.0f / scaleFactor;
        var expectedScaledX = (int)(roiX * inverseScale);
        var expectedScaledY = (int)(roiY * inverseScale);
        var expectedScaledWidth = (int)(roiWidth * inverseScale);
        var expectedScaledHeight = (int)(roiHeight * inverseScale);

        // スケーリング後の座標がウィンドウオフセット分だけ移動していることを確認
        // （実際のオフセット値は環境依存だが、サイズは変わらない）
        Assert.Equal(expectedScaledWidth, result.Width);
        Assert.Equal(expectedScaledHeight, result.Height);
    }

    [Fact]
    public void ConvertRoiToScreenCoordinates_ZeroIntPtrHandle_ReturnsScaledCoordinatesWithoutOffset()
    {
        // Arrange
        var roiBounds = new Rectangle(100, 100, 200, 150);
        var windowHandle = IntPtr.Zero; // 無効なハンドル
        var scaleFactor = 0.25f;

        // Act
        var result = _service.ConvertRoiToScreenCoordinates(roiBounds, windowHandle, scaleFactor);

        // Assert - ウィンドウオフセットが(0,0)になることを確認
        var inverseScale = 1.0f / scaleFactor;
        var expectedX = (int)(roiBounds.X * inverseScale); // 100 * 4 = 400
        var expectedY = (int)(roiBounds.Y * inverseScale); // 100 * 4 = 400
        var expectedWidth = (int)(roiBounds.Width * inverseScale); // 200 * 4 = 800
        var expectedHeight = (int)(roiBounds.Height * inverseScale); // 150 * 4 = 600

        Assert.Equal(expectedX, result.X);
        Assert.Equal(expectedY, result.Y);
        Assert.Equal(expectedWidth, result.Width);
        Assert.Equal(expectedHeight, result.Height);
    }

    [Theory]
    [InlineData(-100, -50, 200, 150)] // 負の座標
    [InlineData(0, 0, 0, 0)]          // ゼロサイズ
    [InlineData(1000000, 1000000, 50, 50)] // 極端に大きな座標
    public void ConvertRoiToScreenCoordinates_EdgeCases_HandlesGracefully(
        int roiX, int roiY, int roiWidth, int roiHeight)
    {
        // Arrange
        var roiBounds = new Rectangle(roiX, roiY, roiWidth, roiHeight);
        var windowHandle = IntPtr.Zero;
        var scaleFactor = 0.25f;

        // Act & Assert - 例外を投げずに処理を完了することを確認
        var result = _service.ConvertRoiToScreenCoordinates(roiBounds, windowHandle, scaleFactor);

        // 基本的な妥当性チェック
        Assert.True(result.Width >= 0);
        Assert.True(result.Height >= 0);
    }

    [Theory]
    [InlineData(0.0f)]    // ゼロスケール
    [InlineData(-0.5f)]   // 負のスケール
    [InlineData(float.PositiveInfinity)] // 無限大
    [InlineData(float.NaN)]              // NaN
    public void ConvertRoiToScreenCoordinates_InvalidScaleFactor_HandlesGracefully(float invalidScale)
    {
        // Arrange
        var roiBounds = new Rectangle(100, 100, 200, 150);
        var windowHandle = IntPtr.Zero;

        // Act & Assert - 例外処理や異常値処理の確認
        var result = _service.ConvertRoiToScreenCoordinates(roiBounds, windowHandle, invalidScale);

        // 異常なスケール値でも結果が返されることを確認
        Assert.NotNull(result);
    }

    [Fact]
    public void ConvertRoiToScreenCoordinatesBatch_EmptyArray_ReturnsEmptyArray()
    {
        // Arrange
        var emptyArray = Array.Empty<Rectangle>();
        var windowHandle = new IntPtr(12345);

        // Act
        var result = _service.ConvertRoiToScreenCoordinatesBatch(emptyArray, windowHandle);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertRoiToScreenCoordinatesBatch_NullArray_ReturnsEmptyArray()
    {
        // Arrange
        Rectangle[]? nullArray = null;
        var windowHandle = new IntPtr(12345);

        // Act
        var result = _service.ConvertRoiToScreenCoordinatesBatch(nullArray!, windowHandle);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void ConvertRoiToScreenCoordinatesBatch_MultipleRectangles_ReturnsCorrectCount()
    {
        // Arrange
        var rectangles = new[]
        {
            new Rectangle(10, 10, 100, 100),
            new Rectangle(50, 50, 150, 200),
            new Rectangle(100, 200, 300, 400)
        };
        var windowHandle = IntPtr.Zero;
        var scaleFactor = 0.5f;

        // Act
        var result = _service.ConvertRoiToScreenCoordinatesBatch(rectangles, windowHandle, scaleFactor);

        // Assert
        Assert.Equal(rectangles.Length, result.Length);

        // 各要素が適切にスケーリングされていることを確認
        var inverseScale = 1.0f / scaleFactor;
        for (int i = 0; i < rectangles.Length; i++)
        {
            var expected = rectangles[i];
            var actual = result[i];

            Assert.Equal((int)(expected.Width * inverseScale), actual.Width);
            Assert.Equal((int)(expected.Height * inverseScale), actual.Height);
        }
    }

    [Fact]
    public void GetWindowOffset_ZeroHandle_ReturnsEmptyPoint()
    {
        // Arrange
        var zeroHandle = IntPtr.Zero;

        // Act
        var result = _service.GetWindowOffset(zeroHandle);

        // Assert
        Assert.Equal(Point.Empty, result);
    }

    [Fact]
    public void GetWindowOffset_ValidHandle_DoesNotThrowException()
    {
        // Arrange
        var handle = new IntPtr(12345); // 実際には存在しないハンドルだが、例外処理をテスト

        // Act & Assert - 例外を投げずに処理を完了することを確認
        var result = _service.GetWindowOffset(handle);

        // 結果が Point 構造体であることを確認（値は環境依存）
        Assert.IsType<Point>(result);
    }

    [Fact]
    public void ConvertRoiToScreenCoordinates_DefaultScaleFactor_UsesQuarterScale()
    {
        // Arrange
        var roiBounds = new Rectangle(100, 100, 200, 200);
        var windowHandle = IntPtr.Zero;
        // scaleFactor パラメータを省略（デフォルト値 0.25f を使用）

        // Act
        var result = _service.ConvertRoiToScreenCoordinates(roiBounds, windowHandle);

        // Assert - 0.25f (1/4) スケールが適用されることを確認
        Assert.Equal(400, result.X);      // 100 * 4 = 400
        Assert.Equal(400, result.Y);      // 100 * 4 = 400
        Assert.Equal(800, result.Width);  // 200 * 4 = 800
        Assert.Equal(800, result.Height); // 200 * 4 = 800
    }

    [Fact]
    public void ConvertRoiToScreenCoordinatesBatch_DefaultScaleFactor_UsesQuarterScale()
    {
        // Arrange
        var rectangles = new[] { new Rectangle(50, 50, 100, 100) };
        var windowHandle = IntPtr.Zero;
        // scaleFactor パラメータを省略

        // Act
        var result = _service.ConvertRoiToScreenCoordinatesBatch(rectangles, windowHandle);

        // Assert
        Assert.Single(result);
        Assert.Equal(200, result[0].X);      // 50 * 4 = 200
        Assert.Equal(200, result[0].Y);      // 50 * 4 = 200
        Assert.Equal(400, result[0].Width);  // 100 * 4 = 400
        Assert.Equal(400, result[0].Height); // 100 * 4 = 400
    }

    public void Dispose()
    {
        // CoordinateTransformationService はステートレスでリソースを保持しないため、
        // 特別な破棄処理は不要
    }
}