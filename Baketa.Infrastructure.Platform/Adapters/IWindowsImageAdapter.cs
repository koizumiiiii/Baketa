using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using System.Drawing;
using System.Drawing.Imaging;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// Windows固有の画像実装と抽象化レイヤーの間のアダプターインターフェース
    /// </summary>
    public interface IWindowsImageAdapter
    {
        /// <summary>
        /// Windowsネイティブイメージ(IWindowsImage)をコアイメージ(IAdvancedImage)に変換します
        /// </summary>
        /// <param name="windowsImage">変換元のWindowsイメージ</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">windowsImageがnullの場合</exception>
        IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage);

        /// <summary>
        /// Windowsネイティブイメージ(IWindowsImage)をコアイメージ(IImage)に変換します
        /// </summary>
        /// <param name="windowsImage">変換元のWindowsイメージ</param>
        /// <returns>変換後のImage</returns>
        /// <exception cref="ArgumentNullException">windowsImageがnullの場合</exception>
        IImage ToImage(IWindowsImage windowsImage);

        /// <summary>
        /// コアイメージ(IAdvancedImage)をWindowsネイティブイメージ(IWindowsImage)に変換します
        /// </summary>
        /// <param name="advancedImage">変換元のAdvancedImage</param>
        /// <returns>変換後のWindowsイメージ</returns>
        /// <exception cref="ArgumentNullException">advancedImageがnullの場合</exception>
        /// <exception cref="InvalidOperationException">変換できない場合</exception>
        Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage);

        /// <summary>
        /// コアイメージ(IImage)をWindowsネイティブイメージ(IWindowsImage)に変換します
        /// </summary>
        /// <param name="image">変換元のImage</param>
        /// <returns>変換後のWindowsイメージ</returns>
        /// <exception cref="ArgumentNullException">imageがnullの場合</exception>
        /// <exception cref="InvalidOperationException">変換できない場合</exception>
        Task<IWindowsImage> FromImageAsync(IImage image);

        /// <summary>
        /// Bitmapからコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="bitmap">変換元のBitmap</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">bitmapがnullの場合</exception>
        IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap);

        /// <summary>
        /// バイト配列からコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="imageData">画像データのバイト配列</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">imageDataがnullの場合</exception>
        /// <exception cref="ArgumentException">無効な画像データの場合</exception>
        Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData);

        /// <summary>
        /// ファイルからコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="filePath">画像ファイルのパス</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">filePathがnullの場合</exception>
        /// <exception cref="System.IO.FileNotFoundException">ファイルが存在しない場合</exception>
        /// <exception cref="ArgumentException">無効な画像データの場合</exception>
        Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath);
    }
}