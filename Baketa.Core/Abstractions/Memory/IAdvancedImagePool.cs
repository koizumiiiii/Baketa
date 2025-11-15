using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Memory;

/// <summary>
/// IAdvancedImage専用のオブジェクトプール
/// </summary>
public interface IAdvancedImagePool : IObjectPool<IAdvancedImage>
{
    /// <summary>
    /// 指定されたサイズとピクセル形式のIAdvancedImageを取得
    /// </summary>
    /// <param name="width">幅</param>
    /// <param name="height">高さ</param>
    /// <param name="pixelFormat">ピクセル形式</param>
    /// <returns>プールされた画像オブジェクトまたは新規作成オブジェクト</returns>
    IAdvancedImage AcquireImage(int width, int height, PixelFormat pixelFormat);

    /// <summary>
    /// 既存の画像と同じサイズ・形式のIAdvancedImageを取得
    /// </summary>
    /// <param name="templateImage">テンプレート画像</param>
    /// <returns>プールされた画像オブジェクトまたは新規作成オブジェクト</returns>
    IAdvancedImage GetCompatible(IAdvancedImage templateImage);
}

/// <summary>
/// ピクセル形式の定義
/// </summary>
public enum PixelFormat
{
    /// <summary>8ビットグレースケール</summary>
    Gray8,

    /// <summary>24ビットRGB</summary>
    Rgb24,

    /// <summary>32ビットRGBA</summary>
    Rgba32,

    /// <summary>32ビットBGRA</summary>
    Bgra32
}
