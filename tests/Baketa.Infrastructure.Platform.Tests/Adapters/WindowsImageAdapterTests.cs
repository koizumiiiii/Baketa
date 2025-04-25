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
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1515:Type is not internal", Justification = "テストクラスはxUnit要件により公開される必要がある")]
    public class WindowsImageAdapterTests
    {
        /// <summary>
        /// ToAdvancedImageメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public void ToAdvancedImageWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
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
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => adapter.ToImage(null!));
        }
        
        /// <summary>
        /// FromAdvancedImageAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task FromAdvancedImageAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.FromAdvancedImageAsync(null!));
        }
        
        /// <summary>
        /// FromImageAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task FromImageAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.FromImageAsync(null!));
        }
        
        /// <summary>
        /// CreateAdvancedImageFromBitmapメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        public void CreateAdvancedImageFromBitmapWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            Assert.Throws<ArgumentNullException>(() => adapter.CreateAdvancedImageFromBitmap(null!));
        }
        
        /// <summary>
        /// CreateAdvancedImageFromBytesAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task CreateAdvancedImageFromBytesAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.CreateAdvancedImageFromBytesAsync(null!));
        }
        
        /// <summary>
        /// CreateAdvancedImageFromFileAsyncメソッドがnull引数でArgumentNullExceptionをスローすることを確認
        /// </summary>
        [Fact]
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2007:Consider calling ConfigureAwait on the awaited task", Justification = "テストメソッドではConfigureAwaitを使用しない")]
        public async Task CreateAdvancedImageFromFileAsyncWithNullArgumentThrowsArgumentNullException()
        {
            // Arrange
            using var adapter = new WindowsImageAdapterStub();
            
            // Act & Assert
            await Assert.ThrowsAsync<ArgumentNullException>(() => adapter.CreateAdvancedImageFromFileAsync(null!));
        }
    }
}