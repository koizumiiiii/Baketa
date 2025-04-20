using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;

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
        
        /// <summary>
        /// 指定したパスに画像を保存
        /// </summary>
        /// <param name="path">保存先パス</param>
        /// <param name="format">画像フォーマット（省略時はPNG）</param>
        /// <returns>非同期タスク</returns>
        public async Task SaveAsync(string path, ImageFormat? format = null)
        {
            ThrowIfDisposed();
            
            // フォーマットが指定されていない場合はPNGを使用
            format ??= ImageFormat.Png;
            
            await Task.Run(() => _bitmap.Save(path, format));
        }
        
        /// <summary>
        /// 画像のサイズを変更
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        public async Task<IWindowsImage> ResizeAsync(int width, int height)
        {
            ThrowIfDisposed();
            
            return await Task.Run(() => 
            {
                var resizedBitmap = new Bitmap(_bitmap, width, height);
                return new WindowsImage(resizedBitmap);
            });
        }
        
        /// <summary>
        /// 画像の一部を切り取る
        /// </summary>
        /// <param name="rectangle">切り取る領域</param>
        /// <returns>切り取られた新しい画像インスタンス</returns>
        public async Task<IWindowsImage> CropAsync(Rectangle rectangle)
        {
            ThrowIfDisposed();
            
            return await Task.Run(() => 
            {
                // 範囲チェック
                if (rectangle.X < 0 || rectangle.Y < 0 || 
                    rectangle.X + rectangle.Width > _bitmap.Width || 
                    rectangle.Y + rectangle.Height > _bitmap.Height)
                {
                    throw new ArgumentOutOfRangeException(nameof(rectangle), "切り取り範囲が画像の範囲外です");
                }
                
                // 切り抜き
                var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);
                using var g = Graphics.FromImage(croppedBitmap);
                g.DrawImage(_bitmap, 
                    new Rectangle(0, 0, rectangle.Width, rectangle.Height),
                    rectangle,
                    GraphicsUnit.Pixel);
                
                return new WindowsImage(croppedBitmap);
            });
        }
        
        /// <summary>
        /// 画像をバイト配列に変換
        /// </summary>
        /// <param name="format">画像フォーマット（省略時はPNG）</param>
        /// <returns>画像データのバイト配列</returns>
        public async Task<byte[]> ToByteArrayAsync(ImageFormat? format = null)
        {
            ThrowIfDisposed();
            
            // フォーマットが指定されていない場合はPNGを使用
            format ??= ImageFormat.Png;
            
            return await Task.Run(() => 
            {
                using var stream = new MemoryStream();
                _bitmap.Save(stream, format);
                return stream.ToArray();
            });
        }
    }
}