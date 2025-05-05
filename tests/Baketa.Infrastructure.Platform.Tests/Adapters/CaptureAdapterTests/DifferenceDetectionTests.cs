using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.CaptureAdapterTests
{
    /// <summary>
    /// CaptureAdapterの差分検出機能テスト
    /// </summary>
    public class DifferenceDetectionTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly Mock<IWindowsImageAdapter> _mockImageAdapter;
        private readonly Mock<IWindowsCapturer> _mockCapturer;
        private readonly Mock<IDifferenceDetector> _mockDifferenceDetector;
        private readonly CaptureAdapter _adapter;
        private bool _disposed;
        
        public DifferenceDetectionTests(ITestOutputHelper output)
        {
            _output = output;
            _mockImageAdapter = new Mock<IWindowsImageAdapter>();
            _mockCapturer = new Mock<IWindowsCapturer>();
            _mockDifferenceDetector = new Mock<IDifferenceDetector>();
            
            // モックの基本設定
            _mockCapturer.Setup(c => c.GetCaptureOptions())
                .Returns(new WindowsCaptureOptions());
            
            // 差分検出を有効にしたアダプター
            _adapter = new CaptureAdapter(
                _mockImageAdapter.Object, 
                _mockCapturer.Object,
                _mockDifferenceDetector.Object);
        }
        
        [Fact]
        public async Task CaptureScreenAsync_NoDifference_ReusesPreviousImage()
        {
            // Arrange
            var mockWindowsImage = new Mock<IWindowsImage>();
            var mockImage1 = new Mock<IImage>();
            var mockImage2 = new Mock<IImage>();
            
            // 画像のプロパティ設定
            mockImage1.Setup(i => i.Width).Returns(1024);
            mockImage1.Setup(i => i.Height).Returns(768);
            mockImage1.Setup(i => i.Clone()).Returns(mockImage1.Object);
            
            mockImage2.Setup(i => i.Width).Returns(1024);
            mockImage2.Setup(i => i.Height).Returns(768);
            
            // 1回目と2回目のキャプチャで異なる画像を返すよう設定
            _mockCapturer.SetupSequence(c => c.CaptureScreenAsync())
                .ReturnsAsync(mockWindowsImage.Object)
                .ReturnsAsync(mockWindowsImage.Object);
            
            _mockImageAdapter.SetupSequence(a => a.ToImage(mockWindowsImage.Object))
                .Returns(mockImage1.Object)
                .Returns(mockImage2.Object);
            
            // 差分なしと判定するよう設定
            _mockDifferenceDetector.Setup(d => d.HasSignificantDifferenceAsync(
                    It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()))
                .ReturnsAsync(false);
            
            // Act - 1回目のキャプチャ
            var result1 = await _adapter.CaptureScreenAsync();
            
            // Act - 2回目のキャプチャ（差分なしなので1回目の画像が再利用されるはず）
            var result2 = await _adapter.CaptureScreenAsync();
            
            // Assert
            Assert.Same(mockImage1.Object, result1);
            Assert.Same(mockImage1.Object, result2); // 同じオブジェクトが返されること
            
            _mockCapturer.Verify(c => c.CaptureScreenAsync(), Times.Exactly(2));
            _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Exactly(2));
            _mockDifferenceDetector.Verify(d => d.HasSignificantDifferenceAsync(
                It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()), Times.Once);
            
            // 2回目の画像は破棄されるはず
            mockImage2.Verify(i => i.Dispose(), Times.Once);
        }
        
        [Fact]
        public async Task CaptureScreenAsync_WithDifference_ReturnsNewImage()
        {
            // Arrange
            var mockWindowsImage = new Mock<IWindowsImage>();
            var mockImage1 = new Mock<IImage>();
            var mockImage2 = new Mock<IImage>();
            
            // 画像のプロパティ設定
            mockImage1.Setup(i => i.Width).Returns(1024);
            mockImage1.Setup(i => i.Height).Returns(768);
            mockImage1.Setup(i => i.Clone()).Returns(mockImage1.Object);
            
            mockImage2.Setup(i => i.Width).Returns(1024);
            mockImage2.Setup(i => i.Height).Returns(768);
            mockImage2.Setup(i => i.Clone()).Returns(mockImage2.Object);
            
            // 1回目と2回目のキャプチャで異なる画像を返すよう設定
            _mockCapturer.SetupSequence(c => c.CaptureScreenAsync())
                .ReturnsAsync(mockWindowsImage.Object)
                .ReturnsAsync(mockWindowsImage.Object);
            
            _mockImageAdapter.SetupSequence(a => a.ToImage(mockWindowsImage.Object))
                .Returns(mockImage1.Object)
                .Returns(mockImage2.Object);
            
            // 差分ありと判定するよう設定
            _mockDifferenceDetector.Setup(d => d.HasSignificantDifferenceAsync(
                    It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()))
                .ReturnsAsync(true);
            
            // Act - 1回目のキャプチャ
            var result1 = await _adapter.CaptureScreenAsync();
            
            // Act - 2回目のキャプチャ（差分ありなので新しい画像が返されるはず）
            var result2 = await _adapter.CaptureScreenAsync();
            
            // Assert
            Assert.Same(mockImage1.Object, result1);
            Assert.Same(mockImage2.Object, result2); // 新しい画像が返されること
            
            _mockCapturer.Verify(c => c.CaptureScreenAsync(), Times.Exactly(2));
            _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Exactly(2));
            _mockDifferenceDetector.Verify(d => d.HasSignificantDifferenceAsync(
                It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()), Times.Once);
            
            // 前回画像は破棄される
            mockImage1.Verify(i => i.Dispose(), Times.Once);
        }
        
        [Fact]
        public async Task CaptureScreenAsync_DifferenceDetectionError_ContinuesWithNewImage()
        {
            // Arrange
            var mockWindowsImage = new Mock<IWindowsImage>();
            var mockImage1 = new Mock<IImage>();
            var mockImage2 = new Mock<IImage>();
            
            // 画像のプロパティ設定
            mockImage1.Setup(i => i.Width).Returns(1024);
            mockImage1.Setup(i => i.Height).Returns(768);
            mockImage1.Setup(i => i.Clone()).Returns(mockImage1.Object);
            
            mockImage2.Setup(i => i.Width).Returns(1024);
            mockImage2.Setup(i => i.Height).Returns(768);
            mockImage2.Setup(i => i.Clone()).Returns(mockImage2.Object);
            
            // 1回目と2回目のキャプチャで異なる画像を返すよう設定
            _mockCapturer.SetupSequence(c => c.CaptureScreenAsync())
                .ReturnsAsync(mockWindowsImage.Object)
                .ReturnsAsync(mockWindowsImage.Object);
            
            _mockImageAdapter.SetupSequence(a => a.ToImage(mockWindowsImage.Object))
                .Returns(mockImage1.Object)
                .Returns(mockImage2.Object);
            
            // 差分検出時に例外が発生するよう設定
            _mockDifferenceDetector.Setup(d => d.HasSignificantDifferenceAsync(
                    It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()))
                .ThrowsAsync(new InvalidOperationException("差分検出に失敗しました"));
            
            // Act - 1回目のキャプチャ
            var result1 = await _adapter.CaptureScreenAsync();
            
            // Act - 2回目のキャプチャ（差分検出エラーが発生するが、処理は継続される）
            var result2 = await _adapter.CaptureScreenAsync();
            
            // Assert
            Assert.Same(mockImage1.Object, result1);
            Assert.Same(mockImage2.Object, result2); // 新しい画像が返されること
            
            _mockCapturer.Verify(c => c.CaptureScreenAsync(), Times.Exactly(2));
            _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Exactly(2));
            _mockDifferenceDetector.Verify(d => d.HasSignificantDifferenceAsync(
                It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()), Times.Once);
            
            // 前回画像は破棄される
            mockImage1.Verify(i => i.Dispose(), Times.Once);
        }
        
        [Fact]
        public async Task CaptureScreenAsync_SizeMismatch_TreatsAsDifference()
        {
            // Arrange
            var mockWindowsImage = new Mock<IWindowsImage>();
            var mockImage1 = new Mock<IImage>();
            var mockImage2 = new Mock<IImage>();
            
            // 画像のプロパティ設定（サイズが異なる）
            mockImage1.Setup(i => i.Width).Returns(1024);
            mockImage1.Setup(i => i.Height).Returns(768);
            mockImage1.Setup(i => i.Clone()).Returns(mockImage1.Object);
            
            mockImage2.Setup(i => i.Width).Returns(800); // 幅が異なる
            mockImage2.Setup(i => i.Height).Returns(768);
            mockImage2.Setup(i => i.Clone()).Returns(mockImage2.Object);
            
            // 1回目と2回目のキャプチャで異なる画像を返すよう設定
            _mockCapturer.SetupSequence(c => c.CaptureScreenAsync())
                .ReturnsAsync(mockWindowsImage.Object)
                .ReturnsAsync(mockWindowsImage.Object);
            
            _mockImageAdapter.SetupSequence(a => a.ToImage(mockWindowsImage.Object))
                .Returns(mockImage1.Object)
                .Returns(mockImage2.Object);
            
            // 差分検出は呼び出されないはず（サイズが異なるため）
            
            // Act - 1回目のキャプチャ
            var result1 = await _adapter.CaptureScreenAsync();
            
            // Act - 2回目のキャプチャ（サイズが異なるので差分ありとして扱われる）
            var result2 = await _adapter.CaptureScreenAsync();
            
            // Assert
            Assert.Same(mockImage1.Object, result1);
            Assert.Same(mockImage2.Object, result2); // 新しい画像が返されること
            
            _mockCapturer.Verify(c => c.CaptureScreenAsync(), Times.Exactly(2));
            _mockImageAdapter.Verify(a => a.ToImage(mockWindowsImage.Object), Times.Exactly(2));
            _mockDifferenceDetector.Verify(d => d.HasSignificantDifferenceAsync(
                It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()), Times.Never);
            
            // 前回画像は破棄される
            mockImage1.Verify(i => i.Dispose(), Times.Once);
        }
        
        [Fact]
        public async Task MultipleCaptures_DisposesResourcesCorrectly()
        {
            // Arrange
            int captureCount = 5;
            var mockImages = new Mock<IImage>[captureCount];
            var mockWindowsImage = new Mock<IWindowsImage>();
            
            // 全て同じサイズの画像を設定
            for (int i = 0; i < captureCount; i++)
            {
                mockImages[i] = new Mock<IImage>();
                mockImages[i].Setup(img => img.Width).Returns(1024);
                mockImages[i].Setup(img => img.Height).Returns(768);
                mockImages[i].Setup(img => img.Clone()).Returns(mockImages[i].Object);
            }
            
            // 連続したキャプチャでそれぞれ異なる画像を返す設定
            var captureSetup = _mockCapturer.SetupSequence(c => c.CaptureScreenAsync());
            var adapterSetup = _mockImageAdapter.SetupSequence(a => a.ToImage(mockWindowsImage.Object));
            
            for (int i = 0; i < captureCount; i++)
            {
                captureSetup = captureSetup.ReturnsAsync(mockWindowsImage.Object);
                adapterSetup = adapterSetup.Returns(mockImages[i].Object);
            }
            
            // 常に差分ありと判定
            _mockDifferenceDetector.Setup(d => d.HasSignificantDifferenceAsync(
                    It.IsAny<IImage>(), It.IsAny<IImage>(), It.IsAny<float>()))
                .ReturnsAsync(true);
            
            // Act - 連続したキャプチャを実行
            for (int i = 0; i < captureCount; i++)
            {
                var result = await _adapter.CaptureScreenAsync();
                Assert.Same(mockImages[i].Object, result);
            }
            
            // Assert - 古いイメージは全て破棄される
            for (int i = 0; i < captureCount - 1; i++) // 最後のイメージは保持される
            {
                mockImages[i].Verify(img => img.Dispose(), Times.Once);
            }
            
            // 最後のイメージは破棄されないはず
            mockImages[captureCount - 1].Verify(img => img.Dispose(), Times.Never);
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
        
        ~DifferenceDetectionTests()
        {
            Dispose(false);
        }
    }
}