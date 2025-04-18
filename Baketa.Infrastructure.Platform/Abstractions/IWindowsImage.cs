using System;
using System.Drawing;

namespace Baketa.Infrastructure.Platform.Abstractions
{
    /// <summary>
    /// Windows固有画像インターフェース
    /// </summary>
    public interface IWindowsImage : IDisposable
    {
        /// <summary>
        /// 画像の幅
        /// </summary>
        int Width { get; }

        /// <summary>
        /// 画像の高さ
        /// </summary>
        int Height { get; }

        /// <summary>
        /// ネイティブImageオブジェクトを取得
        /// </summary>
        /// <returns>System.Drawing.Image インスタンス</returns>
        Image GetNativeImage();

        /// <summary>
        /// Bitmapとして取得
        /// </summary>
        /// <returns>System.Drawing.Bitmap インスタンス</returns>
        Bitmap GetBitmap();
    }
}