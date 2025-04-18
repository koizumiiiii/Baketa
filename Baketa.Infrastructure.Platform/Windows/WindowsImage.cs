using System;
using System.Drawing;
using Baketa.Infrastructure.Platform.Abstractions;

namespace Baketa.Infrastructure.Platform.Windows
{
    /// <summary>
    /// Windows画像の実装
    /// </summary>
    public class WindowsImage(Bitmap bitmap) : IWindowsImage
    {
        private readonly Bitmap _bitmap = bitmap ?? throw new ArgumentNullException(nameof(bitmap));
        private bool _disposed;

        /// <summary>
        /// 画像の幅を取得
        /// </summary>
        public int Width => _bitmap.Width;

        /// <summary>
        /// 画像の高さを取得
        /// </summary>
        public int Height => _bitmap.Height;

        /// <summary>
        /// ネイティブImageオブジェクトを取得
        /// </summary>
        /// <returns>System.Drawing.Image インスタンス</returns>
        public Image GetNativeImage()
        {
            ThrowIfDisposed();
            return _bitmap;
        }

        /// <summary>
        /// Bitmapとして取得
        /// </summary>
        /// <returns>System.Drawing.Bitmap インスタンス</returns>
        public Bitmap GetBitmap()
        {
            ThrowIfDisposed();
            return _bitmap;
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            if (!_disposed)
            {
                _bitmap?.Dispose();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// オブジェクトが破棄済みの場合に例外をスロー
        /// </summary>
        private void ThrowIfDisposed()
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(WindowsImage));
        }
    }
}