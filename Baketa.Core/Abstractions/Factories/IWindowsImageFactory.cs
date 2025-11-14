using System;
using System.Drawing;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;

namespace Baketa.Core.Abstractions.Factories;

/// <summary>
/// Windows画像ファクトリインターフェース
/// </summary>
public interface IWindowsImageFactory
{
    /// <summary>
    /// ファイルから画像を作成
    /// </summary>
    /// <param name="filePath">ファイルパス</param>
    /// <returns>Windows画像</returns>
    Task<IWindowsImage> CreateFromFileAsync(string filePath);

    /// <summary>
    /// バイト配列から画像を作成
    /// </summary>
    /// <param name="data">画像データ</param>
    /// <returns>Windows画像</returns>
    Task<IWindowsImage> CreateFromBytesAsync(byte[] data);

    /// <summary>
    /// Bitmapから画像を作成
    /// </summary>
    /// <param name="bitmap">Bitmap</param>
    /// <returns>Windows画像</returns>
    IWindowsImage CreateFromBitmap(Bitmap bitmap);

    /// <summary>
    /// 指定されたサイズの空の画像を作成
    /// </summary>
    /// <param name="width">幅</param>
    /// <param name="height">高さ</param>
    /// <param name="backgroundColor">背景色（省略時は透明）</param>
    /// <returns>Windows画像</returns>
    Task<IWindowsImage> CreateEmptyAsync(int width, int height, Color? backgroundColor = null);

    /// <summary>
    /// 画像をリサイズ
    /// </summary>
    /// <param name="source">元画像</param>
    /// <param name="width">新しい幅</param>
    /// <param name="height">新しい高さ</param>
    /// <returns>リサイズされた画像</returns>
    IWindowsImage ResizeImage(IWindowsImage source, int width, int height);

    /// <summary>
    /// 画像の指定領域を切り出し
    /// </summary>
    /// <param name="source">元画像</param>
    /// <param name="cropArea">切り出し領域</param>
    /// <returns>切り出された画像</returns>
    IWindowsImage CropImage(IWindowsImage source, Rectangle cropArea);
}
