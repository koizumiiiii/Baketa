using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Baketa.Infrastructure.Platform.Windows;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Platform.Tests.Adapters.DefaultWindowsImageAdapterTests
{
    /// <summary>
    /// DefaultWindowsImageAdapterの基本変換機能のテスト
    /// </summary>
    public class BasicConversionTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly DefaultWindowsImageAdapter _adapter;
        private bool _disposed;
        
        public BasicConversionTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _adapter = new DefaultWindowsImageAdapter();
            
            // テストデータの準備
            AdapterTestHelper.EnsureTestDataExists();
        }
        
        [Fact]
        public void ToImage_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _adapter.ToImage(null!));
        }
        
        [Theory]
        [InlineData(320, 240)]
        [InlineData(1024, 768)]
        [InlineData(1920, 1080)]
        public void ToImage_ValidImage_ReturnsCorrectDimensions(int width, int height)
        {
            // Arrange
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(width, height);
            
            // Act
            using var result = _adapter.ToImage(windowsImage);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
        }
        
        [Fact]
        public void ToAdvancedImage_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            Assert.Throws<ArgumentNullException>(() => _adapter.ToAdvancedImage(null!));
        }
        
        [Theory]
        [InlineData(320, 240)]
        [InlineData(1024, 768)]
        public void ToAdvancedImage_ValidImage_ReturnsCorrectDimensions(int width, int height)
        {
            // Arrange
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(width, height);
            
            // Act
            using var result = _adapter.ToAdvancedImage(windowsImage);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
            Assert.IsAssignableFrom<IAdvancedImage>(result);
        }
        
        [Fact]
        public async Task FromImageAsync_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => 
                await _adapter.FromImageAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
        }
        
        [Theory]
        [InlineData(320, 240)]
        [InlineData(1024, 768)]
        public async Task FromImageAsync_ValidImage_ReturnsCorrectDimensions(int width, int height)
        {
            // Arrange
            // まずIImageオブジェクトを作成
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(width, height);
            using var image = _adapter.ToImage(windowsImage);
            
            // Act
            using var result = await _adapter.FromImageAsync(image).ConfigureAwait(false);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
        }
        
        [Fact]
        public async Task FromAdvancedImageAsync_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => 
                await _adapter.FromAdvancedImageAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
        }
        
        [Theory]
        [InlineData(320, 240)]
        [InlineData(1024, 768)]
        public async Task FromAdvancedImageAsync_ValidImage_ReturnsCorrectDimensions(int width, int height)
        {
            // Arrange
            // まずIAdvancedImageオブジェクトを作成
            using var windowsImage = AdapterTestHelper.CreateMockWindowsImage(width, height);
            using var image = _adapter.ToAdvancedImage(windowsImage);
            
            // Act
            using var result = await _adapter.FromAdvancedImageAsync(image).ConfigureAwait(false);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(width, result.Width);
            Assert.Equal(height, result.Height);
        }
        
        [Fact]
        public async Task CreateAdvancedImageFromBytesAsync_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => 
                await _adapter.CreateAdvancedImageFromBytesAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
        }
        
        [Fact]
        public async Task CreateAdvancedImageFromBytesAsync_ValidData_ReturnsCorrectImage()
        {
            // Arrange
            using var testBitmap = AdapterTestHelper.CreateTestImage(640, 480);
            var imageData = AdapterTestHelper.ConvertImageToBytes(testBitmap);
            
            // Act
            var result = await _adapter.CreateAdvancedImageFromBytesAsync(imageData).ConfigureAwait(false);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(640, result.Width);
            Assert.Equal(480, result.Height);
            Assert.IsAssignableFrom<IAdvancedImage>(result);
        }
        
        [Fact]
        public async Task CreateAdvancedImageFromBytesAsync_InvalidData_ThrowsArgumentException()
        {
            // Arrange
            var invalidData = new byte[] { 0, 1, 2, 3, 4, 5 }; // 無効な画像データ
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentException>(async () => 
                await _adapter.CreateAdvancedImageFromBytesAsync(invalidData).ConfigureAwait(false)).ConfigureAwait(false);
        }
        
        [Fact]
        public async Task CreateAdvancedImageFromFileAsync_NullArgument_ThrowsArgumentNullException()
        {
            // Arrange & Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(async () => 
                await _adapter.CreateAdvancedImageFromFileAsync(null!).ConfigureAwait(false)).ConfigureAwait(false);
        }
        
        [Fact]
        public async Task CreateAdvancedImageFromFileAsync_NonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            string filePath = "non_existent_file.png";
            
            // Act & Assert
            await Assert.ThrowsAsync<System.IO.FileNotFoundException>(async () => 
                await _adapter.CreateAdvancedImageFromFileAsync(filePath).ConfigureAwait(false)).ConfigureAwait(false);
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
                    _adapter.Dispose();
                }
                
                _disposed = true;
            }
        }
        
        ~BasicConversionTests()
        {
            Dispose(false);
        }
    }
}