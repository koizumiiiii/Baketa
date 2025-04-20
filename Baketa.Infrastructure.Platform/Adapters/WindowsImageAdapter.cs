using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// WindowsイメージをコアイメージIImageに変換するアダプター
    /// </summary>
    public class WindowsImageAdapter(IWindowsImage windowsImage) : IImage
    {
        private readonly IWindowsImage _windowsImage = windowsImage ?? throw new ArgumentNullException(nameof(windowsImage));
        private bool _disposed;
        
        public int Width => _windowsImage.Width;
        
        public int Height => _windowsImage.Height;
        
        public IImage Clone()
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var clonedBitmap = new Bitmap(nativeImage);
            var clonedWindowsImage = new WindowsImage(clonedBitmap);
            
            return new WindowsImageAdapter(clonedWindowsImage);
        }
        
        public Task<byte[]> ToByteArrayAsync()
        {
            using var stream = new MemoryStream();
            var nativeImage = _windowsImage.GetNativeImage();
            nativeImage.Save(stream, ImageFormat.Png);
            return Task.FromResult(stream.ToArray());
        }
        
        public Task<IImage> ResizeAsync(int width, int height)
        {
            var nativeImage = _windowsImage.GetNativeImage();
            var resized = new Bitmap(nativeImage, width, height);
            var resizedWindowsImage = new WindowsImage(resized);
            
            return Task.FromResult<IImage>(new WindowsImageAdapter(resizedWindowsImage));
        }
        
        public void Dispose()
        {
            if (!_disposed)
            {
                _windowsImage.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }
    }
}