using System.Collections.Generic;

namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 画像フィルタインターフェース - 画像データに適用可能な変換フィルタを定義します
    /// </summary>
    public interface IImageFilter
    {
        /// <summary>
        /// 画像データにフィルタを適用します
        /// </summary>
        /// <param name="imageData">処理する生の画像データ</param>
        /// <param name="width">画像の幅（ピクセル単位）</param>
        /// <param name="height">画像の高さ（ピクセル単位）</param>
        /// <param name="stride">画像データのストライド（1行あたりのバイト数）</param>
        /// <returns>処理された画像データ</returns>
        IReadOnlyList<byte> Apply(IReadOnlyList<byte> imageData, int width, int height, int stride);
    }
}
