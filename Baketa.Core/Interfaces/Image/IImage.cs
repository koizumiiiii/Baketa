using System;
using System.Threading.Tasks;

namespace Baketa.Core.Interfaces.Image
{
    /// <summary>
    /// 画像抽象化の基本インターフェース
    /// </summary>
    // 注: 後の段階で非推奨化予定
    // [Obsolete("このインターフェースは非推奨です。代わりに Baketa.Core.Abstractions.Imaging.IImage を使用してください。")]
    public interface IImage : IDisposable
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
        /// 画像のクローンを作成
        /// </summary>
        /// <returns>新しい画像インスタンス</returns>
        IImage Clone();

        /// <summary>
        /// 画像をバイト配列に変換
        /// </summary>
        /// <returns>画像データのバイト配列</returns>
        Task<byte[]> ToByteArrayAsync();

        /// <summary>
        /// 画像をリサイズ
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        Task<IImage> ResizeAsync(int width, int height);
    }
}