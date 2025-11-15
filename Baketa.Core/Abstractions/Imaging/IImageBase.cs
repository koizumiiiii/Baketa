using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging;

/// <summary>
/// 基本的な画像プロパティを提供する基底インターフェース
/// </summary>
public interface IImageBase : IDisposable
{
    /// <summary>
    /// 画像の幅（ピクセル単位）
    /// </summary>
    int Width { get; }

    /// <summary>
    /// 画像の高さ（ピクセル単位）
    /// </summary>
    int Height { get; }

    /// <summary>
    /// 画像のフォーマット
    /// </summary>
    ImageFormat Format { get; }

    /// <summary>
    /// 画像をバイト配列に変換します。
    /// </summary>
    /// <returns>画像データを表すバイト配列</returns>
    Task<byte[]> ToByteArrayAsync();
}
