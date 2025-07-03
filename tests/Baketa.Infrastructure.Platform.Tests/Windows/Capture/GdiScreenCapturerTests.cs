using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Factories;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Windows.Capture;

    /// <summary>
    /// テスト用 WindowsImageFactory モック
    /// </summary>
    internal sealed class WindowsImageFactoryMock : Baketa.Core.Abstractions.Factories.IWindowsImageFactory
    {
        public Task<IWindowsImage> CreateFromFileAsync(string _) => throw new NotImplementedException();
        public Task<IWindowsImage> CreateFromBytesAsync(byte[] _) => throw new NotImplementedException();
        public IWindowsImage CreateFromBitmap(Bitmap bitmap) => new Mock<IWindowsImage>().Object;
        public Task<IWindowsImage> CreateEmptyAsync(int _, int _1, Color? _2 = null) => throw new NotImplementedException();
    }
    
    /// <summary>
    /// GdiScreenCapturerのテストクラス
    /// </summary>
    [Collection("Windows系テスト")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Type is not internal", Justification = "テストクラスはxUnit要件により公開される必要がある")]
    public class GdiScreenCapturerTests
    {
        // 完全修飾名を使用して曖昧さを解消
        private readonly Mock<Baketa.Core.Abstractions.Factories.IWindowsImageFactory> _mockImageFactory;
        private readonly Mock<ILogger<GdiScreenCapturer>> _mockLogger;
        private readonly Mock<IWindowsImage> _mockWindowsImage;
        
        public GdiScreenCapturerTests()
        {
            _mockImageFactory = new Mock<Baketa.Core.Abstractions.Factories.IWindowsImageFactory>();
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "テストメソッドは可読性を優先")]
        public void DI_登録できること()
        {
            // Arrange
            var services = new ServiceCollection();
            
            // DIコンテナの設定
            services.AddLogging();
            services.AddTransient<Baketa.Core.Abstractions.Factories.IWindowsImageFactory, WindowsImageFactoryMock>();
            
            // テスト対象の拡張メソッドを呼び出し
            Baketa.Infrastructure.Platform.Windows.Capture.GdiScreenCapturerExtensions.AddGdiScreenCapturer(services);
            var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            
            // Assert
            var capturer = serviceProvider.GetService<IGdiScreenCapturer>();
            Assert.NotNull(capturer);
        }
        
        /// <summary>
        /// コンストラクタでNULLを許容しないこと
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "テストメソッドは可読性を優先")]
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
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "テストメソッドは可読性を優先")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task CaptureRegionAsync_無効なサイズの場合例外がスローされること(int width, int height)
        {
            // Arrange
            using var capturer = new GdiScreenCapturer(_mockImageFactory.Object, _mockLogger.Object);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                capturer.CaptureRegionAsync(new Rectangle(0, 0, width, height)));
        }
        
        /// <summary>
        /// ウィンドウキャプチャの無効なハンドルテスト
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1707:Identifiers should not contain underscores", Justification = "テストメソッドは可読性を優先")]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task CaptureWindowAsync_無効なハンドルの場合例外がスローされること()
        {
            // Arrange
            using var capturer = new GdiScreenCapturer(_mockImageFactory.Object, _mockLogger.Object);
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(() => 
                capturer.CaptureWindowAsync(IntPtr.Zero));
        }
    }
