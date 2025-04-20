using System;
using System.Drawing;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Platform.Windows
{
    /// <summary>
    /// Windows固有の画像インターフェース
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
        
        /// <summary>
        /// 指定したパスに画像を保存
        /// </summary>
        /// <param name="path">保存先パス</param>
        /// <param name="format">画像フォーマット（省略時はPNG）</param>
        /// <returns>非同期タスク</returns>
        Task SaveAsync(string path, System.Drawing.Imaging.ImageFormat? format = null);
        
        /// <summary>
        /// 画像のサイズを変更
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        Task<IWindowsImage> ResizeAsync(int width, int height);
        
        /// <summary>
        /// 画像の一部を切り取る
        /// </summary>
        /// <param name="rectangle">切り取る領域</param>
        /// <returns>切り取られた新しい画像インスタンス</returns>
        Task<IWindowsImage> CropAsync(Rectangle rectangle);
        
        /// <summary>
        /// 画像をバイト配列に変換
        /// </summary>
        /// <param name="format">画像フォーマット（省略時はPNG）</param>
        /// <returns>画像データのバイト配列</returns>
        Task<byte[]> ToByteArrayAsync(System.Drawing.Imaging.ImageFormat? format = null);
    }
}