using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Extensions;

    /// <summary>
    /// IImage インターフェースの拡張メソッド
    /// </summary>
    public static class IImageExtensions
    {
        /// <summary>
        /// 画像の指定領域のピクセルデータをバイト配列として取得します
        /// </summary>
        /// <param name="image">対象画像</param>
        /// <param name="x">開始X座標</param>
        /// <param name="y">開始Y座標</param>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <returns>ピクセルデータのバイト配列</returns>
        public static async Task<byte[]> GetPixelsAsync(this IImage image, int x, int y, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(image));
            
            // バウンダリチェック
            if (x < 0 || y < 0 || x + width > image.Width || y + height > image.Height)
            {
                throw new ArgumentOutOfRangeException(nameof(x), "指定された領域が画像の範囲を超えています");
            }
            
            // IAdvancedImageへのキャストを試みる
            if (image is IAdvancedImage advancedImage)
            {
                // 指定領域を抽出
                if (x > 0 || y > 0 || width < image.Width || height < image.Height)
                {
                    var region = new Rectangle(x, y, width, height);
                    var croppedImage = await advancedImage.ExtractRegionAsync(region).ConfigureAwait(false);
                    return await croppedImage.ToByteArrayAsync().ConfigureAwait(false);
                }
            }
            
            // キャストできない場合や全体領域の場合は、直接バイト配列を取得
            return await image.ToByteArrayAsync().ConfigureAwait(false);
        }
    }
