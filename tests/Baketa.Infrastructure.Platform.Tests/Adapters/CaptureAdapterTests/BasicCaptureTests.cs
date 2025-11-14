using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.CaptureAdapterTests;

/// <summary>
/// CaptureAdapterの基本機能テスト
/// </summary>
public class BasicCaptureTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<IWindowsImageAdapter> _mockImageAdapter;
    private readonly Mock<IWindowsCapturer> _mockCapturer;
    private readonly Mock<IDifferenceDetector> _mockDifferenceDetector;
    private readonly CaptureAdapter _adapter;
    private bool _disposed;

    public BasicCaptureTests(ITestOutputHelper output)
    {
        _output = output;
        _mockImageAdapter = new Mock<IWindowsImageAdapter>();
        _mockCapturer = new Mock<IWindowsCapturer>();
        _mockDifferenceDetector = new Mock<IDifferenceDetector>();

        // モックの基本設定
        _mockCapturer.Setup(c => c.GetCaptureOptions())
            .Returns(new WindowsCaptureOptions());

        // デフォルトのアダプター（差分検出なし）
        _adapter = new CaptureAdapter(_mockImageAdapter.Object, _mockCapturer.Object);
    }

    [Fact]
    public void Constructor_NullImageAdapter_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CaptureAdapter(null!, _mockCapturer.Object));
    }

    [Fact]
    public void Constructor_NullCapturer_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new CaptureAdapter(_mockImageAdapter.Object, null!));
    }

    [Fact]
    public void Constructor_WithDifferenceDetector_InitializesCorrectly()
    {
        // Arrange & Act
        using var adapter = new CaptureAdapter(
            _mockImageAdapter.Object,
            _mockCapturer.Object,
            _mockDifferenceDetector.Object);

        // Assert
        Assert.NotNull(adapter);
        // 正常に初期化されていればOK
    }

    [Fact]
    public async Task CaptureScreenAsync_BasicCapture_ReturnsImage()
    {
        // Arrange
        var mockWindowsImage = new Mock<IWindowsImage>();
        var mockImage = new Mock<IImage>();

        _mockCapturer.Setup(c => c.CaptureScreenAsync())
            .ReturnsAsync(mockWindowsImage.Object);

        _mockImageAdapter.Setup(a => a.ToImage(mockWindowsImage.Object))
            .Returns(mockImage.Object);

        // Act
        var result = await _adapter.CaptureScreenAsync();

        // Assert
        Assert.Same(mockImage.Object, result);
        _mockCapturer.Verify(c => c.CaptureScreenAsync(), Times.Once);
        _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Once);
    }

    [Fact]
    public async Task CaptureRegionAsync_ValidRegion_ReturnsImage()
    {
        // Arrange
        var region = new Rectangle(10, 10, 100, 100);
        var mockWindowsImage = new Mock<IWindowsImage>();
        var mockImage = new Mock<IImage>();

        _mockCapturer.Setup(c => c.CaptureRegionAsync(region))
            .ReturnsAsync(mockWindowsImage.Object);

        _mockImageAdapter.Setup(a => a.ToImage(mockWindowsImage.Object))
            .Returns(mockImage.Object);

        // Act
        var result = await _adapter.CaptureRegionAsync(region);

        // Assert
        Assert.Same(mockImage.Object, result);
        _mockCapturer.Verify(c => c.CaptureRegionAsync(region), Times.Once);
        _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Once);
    }

    [Fact]
    public async Task CaptureRegionAsync_InvalidRegion_ThrowsArgumentException()
    {
        // Arrange
        var invalidRegion = new Rectangle(10, 10, 0, 100); // 幅が0で無効

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _adapter.CaptureRegionAsync(invalidRegion));
    }

    [Fact]
    public async Task CaptureWindowAsync_ValidHandle_ReturnsImage()
    {
        // Arrange
        var windowHandle = new IntPtr(12345);
        var mockWindowsImage = new Mock<IWindowsImage>();
        var mockImage = new Mock<IImage>();

        _mockCapturer.Setup(c => c.CaptureWindowAsync(windowHandle))
            .ReturnsAsync(mockWindowsImage.Object);

        _mockImageAdapter.Setup(a => a.ToImage(mockWindowsImage.Object))
            .Returns(mockImage.Object);

        // Act
        var result = await _adapter.CaptureWindowAsync(windowHandle);

        // Assert
        Assert.Same(mockImage.Object, result);
        _mockCapturer.Verify(c => c.CaptureWindowAsync(windowHandle), Times.Once);
        _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Once);
    }

    [Fact]
    public async Task CaptureWindowAsync_InvalidHandle_ThrowsArgumentException()
    {
        // Arrange
        var invalidHandle = IntPtr.Zero; // 無効なハンドル

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _adapter.CaptureWindowAsync(invalidHandle));
    }

    [Fact]
    public async Task CaptureClientAreaAsync_ValidHandle_ReturnsImage()
    {
        // Arrange
        var windowHandle = new IntPtr(12345);
        var mockWindowsImage = new Mock<IWindowsImage>();
        var mockImage = new Mock<IImage>();

        _mockCapturer.Setup(c => c.CaptureClientAreaAsync(windowHandle))
            .ReturnsAsync(mockWindowsImage.Object);

        _mockImageAdapter.Setup(a => a.ToImage(mockWindowsImage.Object))
            .Returns(mockImage.Object);

        // Act
        var result = await _adapter.CaptureClientAreaAsync(windowHandle);

        // Assert
        Assert.Same(mockImage.Object, result);
        _mockCapturer.Verify(c => c.CaptureClientAreaAsync(windowHandle), Times.Once);
        _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Once);
    }

    [Fact]
    public async Task CaptureClientAreaAsync_InvalidHandle_ThrowsArgumentException()
    {
        // Arrange
        var invalidHandle = IntPtr.Zero; // 無効なハンドル

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(async () =>
            await _adapter.CaptureClientAreaAsync(invalidHandle));
    }

    [Fact]
    public void SetCaptureOptions_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _adapter.SetCaptureOptions(null!));
    }

    [Fact]
    public void SetCaptureOptions_ValidOptions_SetsAndConvertsOptions()
    {
        // Arrange
        var options = new CaptureOptions
        {
            Quality = 90,
            IncludeCursor = true,
            CaptureInterval = 100
        };

        var windowsOptions = new WindowsCaptureOptions
        {
            Quality = 0, // 初期値
            IncludeCursor = false // 初期値
        };

        _mockCapturer.Setup(c => c.GetCaptureOptions())
            .Returns(windowsOptions);

        // Act
        _adapter.SetCaptureOptions(options);

        // Assert
        _mockCapturer.Verify(c => c.SetCaptureOptions(It.Is<WindowsCaptureOptions>(
            wo => wo.Quality == 90 && wo.IncludeCursor == true)), Times.Once);
    }

    [Fact]
    public void GetCaptureOptions_ReturnsCurrentOptions()
    {
        // Arrange
        var options = new CaptureOptions
        {
            Quality = 90,
            IncludeCursor = true,
            CaptureInterval = 100
        };

        _adapter.SetCaptureOptions(options);

        // Act
        var result = _adapter.GetCaptureOptions();

        // Assert
        Assert.Equal(options.Quality, result.Quality);
        Assert.Equal(options.IncludeCursor, result.IncludeCursor);
        Assert.Equal(options.CaptureInterval, result.CaptureInterval);
    }

    [Fact]
    public void AdaptCapturer_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _adapter.AdaptCapturer(null!));
    }

    [Fact]
    public void AdaptCapturer_ValidCapturer_ReturnsScreenCapturer()
    {
        // Arrange
        var capturer = new Mock<IWindowsCapturer>();

        // Act
        var result = _adapter.AdaptCapturer(capturer.Object);

        // Assert
        Assert.NotNull(result);
        Assert.IsAssignableFrom<IScreenCapturer>(result);
    }

    [Fact]
    public void ConvertToCoreOptions_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _adapter.ConvertToCoreOptions(null!));
    }

    [Fact]
    public void ConvertToCoreOptions_ValidOptions_ReturnsCoreOptions()
    {
        // Arrange
        var windowsOptions = new WindowsCaptureOptions
        {
            Quality = 80,
            IncludeCursor = true
        };

        // Act
        var result = _adapter.ConvertToCoreOptions(windowsOptions);

        // Assert
        Assert.Equal(windowsOptions.Quality, result.Quality);
        Assert.Equal(windowsOptions.IncludeCursor, result.IncludeCursor);
    }

    [Fact]
    public void ConvertToWindowsOptions_NullArgument_ThrowsArgumentNullException()
    {
        // Arrange & Act & Assert
        Assert.Throws<ArgumentNullException>(() => _adapter.ConvertToWindowsOptions(null!));
    }

    [Fact]
    public void ConvertToWindowsOptions_ValidOptions_ReturnsWindowsOptions()
    {
        // Arrange
        var coreOptions = new CaptureOptions
        {
            Quality = 75,
            IncludeCursor = true,
            CaptureInterval = 200
        };

        var currentWindowsOptions = new WindowsCaptureOptions
        {
            Quality = 0,
            IncludeCursor = false
        };

        _mockCapturer.Setup(c => c.GetCaptureOptions())
            .Returns(currentWindowsOptions);

        // Act
        var result = _adapter.ConvertToWindowsOptions(coreOptions);

        // Assert
        Assert.Equal(coreOptions.Quality, result.Quality);
        Assert.Equal(coreOptions.IncludeCursor, result.IncludeCursor);
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _adapter?.Dispose();
            }

            _disposed = true;
        }
    }

    ~BasicCaptureTests()
    {
        Dispose(false);
    }
}
