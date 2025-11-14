using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;
using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Adapters;


/// <summary>
/// WindowsImageAdapterFactory - Windows画像をIImageに変換するファクトリー
/// Phase 3.2: SafeImageFactory統合対応
/// </summary>
public class WindowsImageAdapterFactory : IImageFactoryInterface
{
    private readonly IWindowsImageFactoryInterface _windowsImageFactory;
    private readonly ISafeImageFactory _safeImageFactory;

    /// <summary>
    /// WindowsImageAdapterFactoryコンストラクタ
    /// </summary>
    /// <param name="windowsImageFactory">Windows画像ファクトリインターフェース</param>
    /// <param name="safeImageFactory">SafeImage ファクトリインターフェース</param>
    public WindowsImageAdapterFactory(
        IWindowsImageFactoryInterface windowsImageFactory,
        ISafeImageFactory safeImageFactory)
    {
        ArgumentNullException.ThrowIfNull(windowsImageFactory, nameof(windowsImageFactory));
        ArgumentNullException.ThrowIfNull(safeImageFactory, nameof(safeImageFactory));
        _windowsImageFactory = windowsImageFactory;
        _safeImageFactory = safeImageFactory;
    }

    /// <summary>
    /// ファイルから画像を作成します。
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <returns>作成された画像</returns>
    public async Task<IImage> CreateFromFileAsync(string filePath)
    {
        var windowsImage = await _windowsImageFactory.CreateFromFileAsync(filePath).ConfigureAwait(false);
        return new WindowsImageAdapter(windowsImage);
    }

    /// <summary>
    /// バイト配列から画像を作成します。
    /// Phase 3.2: SafeImage統合による ObjectDisposedException防止
    /// </summary>
    /// <param name="imageData">画像データ</param>
    /// <returns>作成された画像</returns>
    public async Task<IImage> CreateFromBytesAsync(byte[] imageData)
    {
        // 🎯 Phase 3.2: SafeImageを使用してObjectDisposedException防止
        using var bitmap = new Bitmap(new MemoryStream(imageData));
        var width = bitmap.Width;
        var height = bitmap.Height;

        // SafeImageFactoryでメモリ安全な画像を作成
        var safeImage = _safeImageFactory.CreateFromBitmap(bitmap, width, height);

        // SafeImageからBitmapを再構築
        using var safeImageAdapter = new SafeImageAdapter(safeImage, _safeImageFactory);
        var safeBitmap = safeImageAdapter.GetBitmap();

        // WindowsImageを作成し、WindowsImageAdapterでラップしてIImage対応
        var windowsImage = new WindowsImage(safeBitmap);
        return new WindowsImageAdapter(windowsImage);
    }

    /// <summary>
    /// ストリームから画像を作成します。
    /// </summary>
    /// <param name="stream">画像データを含むストリーム</param>
    /// <returns>作成された画像</returns>
    public async Task<IImage> CreateFromStreamAsync(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream, nameof(stream));

        // ストリームをバイト配列に変換
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream).ConfigureAwait(false);
        var data = memoryStream.ToArray();

        return await CreateFromBytesAsync(data).ConfigureAwait(false);
    }

    /// <summary>
    /// 指定されたサイズの空の画像を作成します。
    /// </summary>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    /// <returns>作成された画像</returns>
    public async Task<IImage> CreateEmptyAsync(int width, int height)
    {
        var windowsImage = await _windowsImageFactory.CreateEmptyAsync(width, height).ConfigureAwait(false);
        return new WindowsImageAdapter(windowsImage);
    }

    /// <summary>
    /// 高度な画像処理機能を持つ画像インスタンスに変換します。
    /// </summary>
    /// <param name="image">元の画像</param>
    /// <returns>高度な画像処理機能を持つ画像インスタンス</returns>
    public IAdvancedImage ConvertToAdvancedImage(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));

        // 既にIAdvancedImageの場合はそのまま返す
        if (image is IAdvancedImage advancedImage)
        {
            return advancedImage;
        }

        // WindowsImageAdapterを使用している場合はクローンして変換
        if (image is WindowsImageAdapter windowsAdapter)
        {
            // クローンを作成してそのまま返す（既にIAdvancedImageを実装している）
            return (IAdvancedImage)windowsAdapter.Clone();
        }

        // それ以外の場合はバイト配列経由で変換
        throw new NotImplementedException("このタイプの画像の変換はまだ実装されていません");
    }
}

/// <summary>
/// テスト用のIImageファクトリ実装
/// </summary>
public class TestImageAdapterFactory : IImageFactoryInterface
{
    /// <summary>
    /// ファイルから画像を作成します。
    /// </summary>
    /// <param name="filePath">画像ファイルパス</param>
    /// <returns>作成された画像</returns>
    public Task<IImage> CreateFromFileAsync(string filePath)
    {
        // テスト実装
        throw new NotImplementedException("テスト用ファクトリは実装が必要です");
    }

    /// <summary>
    /// バイト配列から画像を作成します。
    /// </summary>
    /// <param name="imageData">画像データ</param>
    /// <returns>作成された画像</returns>
    public Task<IImage> CreateFromBytesAsync(byte[] imageData)
    {
        // テスト実装
        throw new NotImplementedException("テスト用ファクトリは実装が必要です");
    }

    /// <summary>
    /// ストリームから画像を作成します。
    /// </summary>
    /// <param name="stream">画像データを含むストリーム</param>
    /// <returns>作成された画像</returns>
    public Task<IImage> CreateFromStreamAsync(Stream stream)
    {
        // テスト実装
        throw new NotImplementedException("テスト用ファクトリは実装が必要です");
    }

    /// <summary>
    /// 指定されたサイズの空の画像を作成します。
    /// </summary>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    /// <returns>作成された画像</returns>
    public Task<IImage> CreateEmptyAsync(int width, int height)
    {
        // テスト実装
        throw new NotImplementedException("テスト用ファクトリは実装が必要です");
    }

    /// <summary>
    /// 高度な画像処理機能を持つ画像インスタンスに変換します。
    /// </summary>
    /// <param name="image">元の画像</param>
    /// <returns>高度な画像処理機能を持つ画像インスタンス</returns>
    public IAdvancedImage ConvertToAdvancedImage(IImage image)
    {
        // テスト実装
        throw new NotImplementedException("テスト用ファクトリは実装が必要です");
    }
}
