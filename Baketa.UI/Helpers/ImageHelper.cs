using System;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Baketa.UI.Helpers;

/// <summary>
/// 画像読み込み用のヘルパークラス
/// </summary>
public static class ImageHelper
{
    /// <summary>
    /// AvaloniaリソースURIからBitmapオブジェクトを読み込みます。
    /// </summary>
    /// <param name="uri">avares:// スキームを含むリソースURI。</param>
    /// <returns>読み込まれたBitmapオブジェクト。</returns>
    /// <exception cref="ArgumentNullException">uriがnullの場合にスローされます。</exception>
    public static Bitmap LoadFromAvaloniaResource(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);

        // AssetLoaderを使用してリソースストリームを開き、Bitmapを生成します。
        return new Bitmap(AssetLoader.Open(uri));
    }
}
