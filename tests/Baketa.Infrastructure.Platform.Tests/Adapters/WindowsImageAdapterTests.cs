using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Adapters;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Platform.Tests.Adapters
{
    /// <summary>
    /// WindowsImageAdapterのテストクラス
    /// </summary>
    internal class WindowsImageAdapterTests
    {
        /// <summary>
        /// ToAdvancedImageメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public void ToAdvancedImageWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => adapter.ToAdvancedImage(null!));
        }
        
        /// <summary>
        /// ToImageメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public void ToImageWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => adapter.ToImage(null!));
        }
        
        /// <summary>
        /// FromAdvancedImageAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public async Task FromAdvancedImageAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.FromAdvancedImageAsync(null!)).ConfigureAwait(false);
        }
        
        /// <summary>
        /// FromImageAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public async Task FromImageAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.FromImageAsync(null!)).ConfigureAwait(false);
        }
        
        /// <summary>
        /// CreateAdvancedImageFromBitmapメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public void CreateAdvancedImageFromBitmapWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => adapter.CreateAdvancedImageFromBitmap(null!));
        }
        
        /// <summary>
        /// CreateAdvancedImageFromBytesAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public async Task CreateAdvancedImageFromBytesAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.CreateAdvancedImageFromBytesAsync(null!)).ConfigureAwait(false);
        }
        
        /// <summary>
        /// CreateAdvancedImageFromFileAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public async Task CreateAdvancedImageFromFileAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.CreateAdvancedImageFromFileAsync(null!)).ConfigureAwait(false);
        }
    }
}