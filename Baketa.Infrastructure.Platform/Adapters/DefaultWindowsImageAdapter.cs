using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Common;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// IWindowsImageAdapterインターフェースの標準実装クラス
/// </summary>
public class DefaultWindowsImageAdapter : DisposableBase, IWindowsImageAdapter
{
    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IAdvancedImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のAdvancedImage</returns>
    public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        return new WindowsImageAdapter(windowsImage);
    }

    /// <summary>
    /// Windowsネイティブイメージをコアイメージ(IImage)に変換します
    /// </summary>
    /// <param name="windowsImage">変換元のWindowsイメージ</param>
    /// <returns>変換後のImage</returns>
    public IImage ToImage(IWindowsImage windowsImage)
    {
        ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
        // IAdvancedImageはIImageを継承しているので、ToAdvancedImageの結果をそのまま返す
        return ToAdvancedImage(windowsImage);
    }

    /// <summary>
    /// コアイメージ(IAdvancedImage)をWindowsネイティブイメージに変換します
    /// </summary>
    /// <param name="advancedImage">変換元のAdvancedImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    public async Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
    {
        ArgumentNullException.ThrowIfNull(advancedImage, nameof(advancedImage));
        
        // バイト配列を経由して変換
        var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
        using var stream = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(stream);
        
        // 所有権移転のためのクローン作成
        var persistentBitmap = (Bitmap)bitmap.Clone();
        return new WindowsImage(persistentBitmap);
    }

    /// <summary>
    /// コアイメージ(IImage)をWindowsネイティブイメージに変換します
    /// </summary>
    /// <param name="image">変換元のImage</param>
    /// <returns>変換後のWindowsイメージ</returns>
    public async Task<IWindowsImage> FromImageAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));
        
        // IAdvancedImageの場合は特化したメソッドを使用
        if (image is IAdvancedImage advancedImage)
        {
            return await FromAdvancedImageAsync(advancedImage).ConfigureAwait(false);
        }
        
        // それ以外はバイト配列を経由して変換
        var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
        using var stream = new MemoryStream(imageBytes);
        using var bitmap = new Bitmap(stream);
        
        // 所有権移転のためのクローン作成
        var persistentBitmap = (Bitmap)bitmap.Clone();
        return new WindowsImage(persistentBitmap);
    }

    /// <summary>
    /// Bitmapからコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="bitmap">変換元のBitmap</param>
    /// <returns>変換後のAdvancedImage</returns>
    public IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));
        
        // BitmapをWindowsImageに変換し、それをAdvancedImageに変換
        var windowsImage = new WindowsImage((Bitmap)bitmap.Clone());
        return ToAdvancedImage(windowsImage);
    }

    /// <summary>
    /// バイト配列からコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="imageData">画像データのバイト配列</param>
    /// <returns>変換後のAdvancedImage</returns>
    public Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));
        
        try
        {
            using var stream = new MemoryStream(imageData);
            using var bitmap = new Bitmap(stream);
            
            // 所有権移転のためのクローン作成
            var persistentBitmap = (Bitmap)bitmap.Clone();
            var windowsImage = new WindowsImage(persistentBitmap);
            
            var result = ToAdvancedImage(windowsImage);
            return Task.FromResult(result);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("無効な画像データです", nameof(imageData), ex);
        }
    }

    /// <summary>
    /// ファイルからコアイメージ(IAdvancedImage)を作成します
    /// </summary>
    /// <param name="filePath">画像ファイルのパス</param>
    /// <returns>変換後のAdvancedImage</returns>
    public async Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath, nameof(filePath));
        
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("指定されたファイルが見つかりません", filePath);
        }

        try
        {
            // ファイルをバイト配列として読み込み
            var imageData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            return await CreateAdvancedImageFromBytesAsync(imageData).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"ファイル '{filePath}' は有効な画像ではありません", nameof(filePath), ex);
        }
    }
}
