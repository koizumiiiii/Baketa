using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Memory;

namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// 標準的な画像操作機能を提供するインターフェース
    /// </summary>
    public interface IImage : IImageBase
    {
        /// <summary>
        /// ピクセルフォーマット（Gemini推奨拡張）
        /// </summary>
        ImagePixelFormat PixelFormat { get; }

        /// <summary>
        /// 画像データの読み取り専用メモリを取得（Gemini推奨拡張）
        /// </summary>
        /// <returns>画像データの読み取り専用メモリ</returns>
        ReadOnlyMemory<byte> GetImageMemory();

        /// <summary>
        /// 画像のクローンを作成します。
        /// </summary>
        /// <returns>元の画像と同じ内容を持つ新しい画像インスタンス</returns>
        IImage Clone();

        /// <summary>
        /// 画像のサイズを変更します。
        /// </summary>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた新しい画像インスタンス</returns>
        Task<IImage> ResizeAsync(int width, int height);
    }
