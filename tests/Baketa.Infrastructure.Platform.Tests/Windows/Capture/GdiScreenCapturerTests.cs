using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Windows.Capture
{
    /// <summary>
    /// GdiScreenCapturerのテストクラス
    /// </summary>
    [Collection("Windows系テスト")]
    public class GdiScreenCapturerTests
    {
        private readonly Mock<IWindowsImageFactory> _mockImageFactory;
        private readonly Mock<ILogger<GdiScreenCapturer>> _mockLogger;
        private readonly Mock<IWindowsImage> _mockWindowsImage;
        
        public GdiScreenCapturerTests()
        {
            _mockImageFactory = new Mock<IWindowsImageFactory>();
            _mockLogger = new Mock<ILogger<GdiScreenCapturer>>();
            _mockWindowsImage = new Mock<IWindowsImage>();
            
            // モック設定
            _mockWindowsImage.Setup(x => x.Width).Returns(800);
            _mockWindowsImage.Setup(x => x.Height).Returns(600);
            
            _mockImageFactory
                .Setup(x => x.CreateFromBitmap(It.IsAny<Bitmap>()))
                .Returns(_mockWindowsImage.Object);
        }
        
        /// <summary>
        /// 依存性注入の登録テスト
        /// </summary>
        [Fact]
        public void DI_登録できること()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // Act
            services.AddGdiScreenCapturer();
            var provider = services.BuildServiceProvider();
            
            // Assert
            var capturer = provider.GetService<IGdiScreenCapturer>();
            Assert.NotNull(capturer);
        }
        
        /// <summary>
        /// コンストラクタでNULLを許容しないこと
        /// </summary>
        [Fact]
        public void コンストラクタ_NULLを許容しないこと()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => 
                new GdiScreenCapturer(null!));
        }
        
        /// <summary>
        /// 領域キャプチャの無効なサイズテスト
        /// </summary>
        [Theory]
        [InlineData(0, 100)]
        [InlineData(100, 0)]
        [InlineData(-1, 100)]
        [InlineData(100, -1)]
        public async Task CaptureRegionAsync_無効なサイズの場合例外がスローされること(int width, int height)
        {
            // Arrange
            var capturer = new GdiScreenCapturer(_mockImageFactory.Object, _mockLogger.Object);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                capturer.CaptureRegionAsync(new Rectangle(0, 0, width, height)));
        }
        
        /// <summary>
        /// ウィンドウキャプチャの無効なハンドルテスト
        /// </summary>
        [Fact]
        public async Task CaptureWindowAsync_無効なハンドルの場合例外がスローされること()
        {
            // Arrange
            var capturer = new GdiScreenCapturer(_mockImageFactory.Object, _mockLogger.Object);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                capturer.CaptureWindowAsync(IntPtr.Zero));
        }
    }
}
