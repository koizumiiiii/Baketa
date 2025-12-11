using System;
using System.Buffers;
using System.Drawing;
using System.Drawing.Imaging;
using Baketa.Core.Abstractions.Memory;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiRectangle = System.Drawing.Rectangle;

namespace Baketa.Application.Services.Memory;

/// <summary>
/// SafeImageインスタンス生成Factory実装
/// Core層とApplication層の依存関係を適切に管理するFactoryパターン
/// SafeImageの内部コンストラクタにアクセス可能なApplication層で実装
/// </summary>
public sealed class SafeImageFactory : ISafeImageFactory
{
    /// <summary>
    /// ArrayPool管理下のSafeImageインスタンスを生成
    /// Phase 12: stride引数追加（明示的Stride伝達）
    /// </summary>
    /// <param name="rentedBuffer">ArrayPoolから借用したバッファ</param>
    /// <param name="arrayPool">使用中のArrayPoolインスタンス</param>
    /// <param name="actualDataLength">実際のデータ長</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="pixelFormat">ピクセルフォーマット</param>
    /// <param name="id">一意識別ID</param>
    /// <param name="stride">1行あたりのバイト数（GDI+パディング含む）</param>
    /// <returns>生成されたSafeImageインスタンス</returns>
    public SafeImage CreateSafeImage(
        byte[] rentedBuffer,
        ArrayPool<byte> arrayPool,
        int actualDataLength,
        int width,
        int height,
        ImagePixelFormat pixelFormat,
        Guid id,
        int stride)
    {
        // Phase 3: Factory パターンによる安全なSafeImageインスタンス生成
        // Clean Architecture原則を維持しつつ、内部コンストラクタアクセス問題を解決
        // Phase 12: stride値をSafeImageコンストラクタに渡す
        return new SafeImage(rentedBuffer, arrayPool, actualDataLength, width, height, pixelFormat, id, stride);
    }

    /// <summary>
    /// BitmapからSafeImageインスタンスを生成
    /// Phase 3.2: WindowsImageAdapterFactory統合のために追加
    /// </summary>
    /// <param name="bitmap">ソースBitmap</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <returns>生成されたSafeImageインスタンス</returns>
    public SafeImage CreateFromBitmap(Bitmap bitmap, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));

        // BitmapからPixelFormatを変換
        var pixelFormat = ConvertPixelFormat(bitmap.PixelFormat);

        // 必要なバッファサイズを計算
        var bytesPerPixel = GetBytesPerPixel(pixelFormat);
        var dataLength = width * height * bytesPerPixel;

        // ArrayPoolからバッファを借用
        var arrayPool = ArrayPool<byte>.Shared;
        var rentedBuffer = arrayPool.Rent(dataLength);

        try
        {
            // BitmapデータをArrayPoolバッファにコピー
            var bitmapData = bitmap.LockBits(
                new GdiRectangle(0, 0, width, height),
                ImageLockMode.ReadOnly,
                bitmap.PixelFormat);

            try
            {
                unsafe
                {
                    var sourcePtr = (byte*)bitmapData.Scan0;
                    var stride = bitmapData.Stride;

                    for (int y = 0; y < height; y++)
                    {
                        var sourceOffset = y * stride;
                        var destOffset = y * width * bytesPerPixel;
                        var rowBytes = width * bytesPerPixel;

                        var sourceSpan = new Span<byte>(sourcePtr + sourceOffset, rowBytes);
                        var destSpan = new Span<byte>(rentedBuffer, destOffset, rowBytes);
                        sourceSpan.CopyTo(destSpan);
                    }
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            // SafeImageインスタンスを生成
            // 🔥 [PHASE12.2] このメソッドはパディングを除去して詰めたデータを作成
            // 実際のstride = width * bytesPerPixel（パディングなし）
            var id = Guid.NewGuid();
            var actualStride = width * bytesPerPixel;
            return new SafeImage(rentedBuffer, arrayPool, dataLength, width, height, pixelFormat, id, actualStride);
        }
        catch
        {
            // エラー発生時はバッファを返却
            arrayPool.Return(rentedBuffer);
            throw;
        }
    }

    /// <summary>
    /// ネイティブメモリポインタから直接SafeImageインスタンスを生成
    /// Issue #193: Clone()を廃止し、中間Bitmap作成を排除してLOH圧迫を防止
    /// </summary>
    /// <param name="bgraData">BGRAピクセルデータへのポインタ</param>
    /// <param name="width">画像幅</param>
    /// <param name="height">画像高さ</param>
    /// <param name="stride">1行あたりのバイト数</param>
    /// <returns>生成されたSafeImageインスタンス</returns>
    public SafeImage CreateFromNativePointer(IntPtr bgraData, int width, int height, int stride)
    {
        if (bgraData == IntPtr.Zero)
            throw new ArgumentException("bgraDataがnullです", nameof(bgraData));
        if (width <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "幅は正の値である必要があります");
        if (height <= 0)
            throw new ArgumentOutOfRangeException(nameof(height), "高さは正の値である必要があります");
        if (stride <= 0)
            throw new ArgumentOutOfRangeException(nameof(stride), "strideは正の値である必要があります");

        // BGRA32 = 4バイト/ピクセル
        const int bytesPerPixel = 4;
        var pixelFormat = ImagePixelFormat.Bgra32;

        // 実際のデータ長（パディングなし）
        var dataLength = width * height * bytesPerPixel;
        var rowBytes = width * bytesPerPixel;

        // ArrayPoolからバッファを借用
        var arrayPool = ArrayPool<byte>.Shared;
        var rentedBuffer = arrayPool.Rent(dataLength);

        try
        {
            unsafe
            {
                var sourcePtr = (byte*)bgraData;

                // ネイティブメモリから直接ArrayPoolバッファにコピー（stride考慮）
                for (int y = 0; y < height; y++)
                {
                    var sourceOffset = y * stride;
                    var destOffset = y * rowBytes;

                    var sourceSpan = new Span<byte>(sourcePtr + sourceOffset, rowBytes);
                    var destSpan = new Span<byte>(rentedBuffer, destOffset, rowBytes);
                    sourceSpan.CopyTo(destSpan);
                }
            }

            // SafeImageインスタンスを生成
            var id = Guid.NewGuid();
            var actualStride = rowBytes; // パディングなし
            return new SafeImage(rentedBuffer, arrayPool, dataLength, width, height, pixelFormat, id, actualStride);
        }
        catch
        {
            // エラー発生時はバッファを返却
            arrayPool.Return(rentedBuffer);
            throw;
        }
    }

    /// <summary>
    /// System.Drawing.Imaging.PixelFormatをImagePixelFormatに変換
    /// </summary>
    /// <param name="format">System.Drawing.Imaging.PixelFormat</param>
    /// <returns>変換されたImagePixelFormat</returns>
    private static ImagePixelFormat ConvertPixelFormat(GdiPixelFormat format)
    {
        return format switch
        {
            GdiPixelFormat.Format32bppArgb => ImagePixelFormat.Bgra32,
            GdiPixelFormat.Format32bppRgb => ImagePixelFormat.Bgra32,
            // 🔥 [ULTRATHINK_PHASE10.6] Format24bppRgb → Bgr24 (正しいマッピング)
            // GDI+ Format24bppRgbは実際にBGRバイトオーダーで保存される（Microsoft仕様）
            // これによりRGB/BGRバイトオーダー不一致によるRGBノイズ問題が解消される
            GdiPixelFormat.Format24bppRgb => ImagePixelFormat.Bgr24,
            GdiPixelFormat.Format8bppIndexed => ImagePixelFormat.Gray8,
            _ => ImagePixelFormat.Bgra32 // デフォルト
        };
    }

    /// <summary>
    /// PixelFormatごとのバイト数を取得
    /// </summary>
    /// <param name="format">PixelFormat</param>
    /// <returns>1ピクセルあたりのバイト数</returns>
    private static int GetBytesPerPixel(ImagePixelFormat format)
    {
        return format switch
        {
            ImagePixelFormat.Bgra32 => 4,
            ImagePixelFormat.Rgba32 => 4,
            ImagePixelFormat.Rgb24 => 3,
            ImagePixelFormat.Bgr24 => 3,  // 🔥 [ULTRATHINK_PHASE10.6] BGR24も3バイト/ピクセル
            ImagePixelFormat.Gray8 => 1,
            _ => 4
        };
    }
}
