using System.Buffers;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;

namespace Baketa.Infrastructure.Services.Memory;

/// <summary>
/// IImage→SafeImage変換の実装
/// Phase 3.13: ReadOnlyMemory&lt;byte&gt;ベースの効率的変換
/// </summary>
public sealed class ImageToSafeImageConverter : IImageToSafeImageConverter
{
    /// <inheritdoc/>
    public async Task<SafeImage> ConvertAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // IImageのGetImageMemory()を使用してデータ取得
        var imageMemory = image.GetImageMemory();

        // SafeImageを作成 - 効率化: 直接Span.CopyToを使用
        var arrayPool = ArrayPool<byte>.Shared;
        var rentedBuffer = arrayPool.Rent(imageMemory.Length);
        imageMemory.Span.CopyTo(rentedBuffer);

        // 🔥 [PHASE12.5] strideパラメータ追加（パディングなしの詰めデータ）
        var bytesPerPixel = GetBytesPerPixel(image.PixelFormat);
        var stride = image.Width * bytesPerPixel;

        return await Task.FromResult(new SafeImage(
            rentedBuffer,
            arrayPool,
            imageMemory.Length,
            image.Width,
            image.Height,
            image.PixelFormat,
            Guid.NewGuid(),
            stride
        )).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<SafeImage> ConvertFromMemoryAsync(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        // Phase 3.11で追加されたGetImageMemory()を活用
        var imageMemory = image.GetImageMemory();

        var arrayPool = ArrayPool<byte>.Shared;
        var rentedBuffer = arrayPool.Rent(imageMemory.Length);
        imageMemory.Span.CopyTo(rentedBuffer);

        // 🔥 [PHASE12.5] strideパラメータ追加（パディングなしの詰めデータ）
        var bytesPerPixel = GetBytesPerPixel(image.PixelFormat);
        var stride = image.Width * bytesPerPixel;

        return await Task.FromResult(new SafeImage(
            rentedBuffer,
            arrayPool,
            imageMemory.Length,
            image.Width,
            image.Height,
            image.PixelFormat,
            Guid.NewGuid(),
            stride
        )).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public SafeImage Convert(IImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        var imageMemory = image.GetImageMemory();

        var arrayPool = ArrayPool<byte>.Shared;
        var rentedBuffer = arrayPool.Rent(imageMemory.Length);
        imageMemory.Span.CopyTo(rentedBuffer);

        // 🔥 [PHASE12.5] strideパラメータ追加（パディングなしの詰めデータ）
        var bytesPerPixel = GetBytesPerPixel(image.PixelFormat);
        var stride = image.Width * bytesPerPixel;

        return new SafeImage(
            rentedBuffer,
            arrayPool,
            imageMemory.Length,
            image.Width,
            image.Height,
            image.PixelFormat,
            Guid.NewGuid(),
            stride
        );
    }

    /// <summary>
    /// PixelFormatごとのバイト数を取得
    /// Phase 12.5: stride計算に必要
    /// </summary>
    private static int GetBytesPerPixel(ImagePixelFormat format)
    {
        return format switch
        {
            ImagePixelFormat.Bgra32 => 4,
            ImagePixelFormat.Rgba32 => 4,
            ImagePixelFormat.Rgb24 => 3,
            ImagePixelFormat.Gray8 => 1,
            _ => 4 // デフォルト
        };
    }

}
