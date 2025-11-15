using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Common;
using Baketa.Core.Extensions;

namespace Baketa.Core.Services.Imaging;

/// <summary>
/// IImageの基本実装
/// </summary>
/// <remarks>
/// コンストラクタ
/// </remarks>
/// <param name="pixelData">ピクセルデータ</param>
/// <param name="width">幅</param>
/// <param name="height">高さ</param>
/// <param name="format">画像フォーマット</param>
public class CoreImage(byte[] pixelData, int width, int height, ImageFormat format) : DisposableBase, IImage
{
    private byte[] _pixelData = pixelData ?? throw new ArgumentNullException(nameof(pixelData));

    /// <inheritdoc/>
    public int Width { get; } = width;

    /// <inheritdoc/>
    public int Height { get; } = height;

    /// <summary>
    /// 画像フォーマット
    /// </summary>
    public ImageFormat Format { get; } = format;

    /// <summary>
    /// ピクセルフォーマット（IImage拡張対応）
    /// </summary>
    public ImagePixelFormat PixelFormat
    {
        get
        {
            // ImageFormatからImagePixelFormatへの変換
            return Format switch
            {
                ImageFormat.Rgb24 => ImagePixelFormat.Rgb24,
                ImageFormat.Rgba32 => ImagePixelFormat.Rgba32,
                ImageFormat.Grayscale8 => ImagePixelFormat.Bgra32, // マッピング
                _ => ImagePixelFormat.Bgra32 // デフォルト
            };
        }
    }

    /// <inheritdoc/>
    public Task<byte[]> ToByteArrayAsync()
    {
        ThrowIfDisposed();
        return Task.FromResult<byte[]>([.. _pixelData]);
    }

    /// <summary>
    /// 画像データの読み取り専用メモリを取得（IImage拡張対応）
    /// </summary>
    /// <returns>画像データの読み取り専用メモリ</returns>
    public ReadOnlyMemory<byte> GetImageMemory()
    {
        ThrowIfDisposed();
        return new ReadOnlyMemory<byte>(_pixelData);
    }

    /// <summary>
    /// 🔥 [PHASE5.2G-A] 生ピクセルデータへの直接アクセス（CoreImageは非サポート）
    /// CoreImageはPNGエンコードデータ保持のため、生ピクセルアクセス不可
    /// </summary>
    public PixelDataLock LockPixelData() => throw new NotSupportedException("CoreImageは生ピクセルデータアクセスをサポートしません（WindowsImageを使用してください）");

    /// <summary>
    /// 画像データのバイト配列を取得します
    /// </summary>
    /// <returns>画像データのバイト配列</returns>
    public IReadOnlyList<byte> Bytes => [.. _pixelData];

    /// <inheritdoc/>
    public IImage Clone()
    {
        ThrowIfDisposed();
        var resultBytes = new byte[_pixelData.Length];
        Buffer.BlockCopy(_pixelData, 0, resultBytes, 0, _pixelData.Length);
        return new CoreImage(resultBytes, Width, Height, Format);
    }

    /// <inheritdoc/>
    public Task<IImage> ResizeAsync(int width, int height)
    {
        ThrowIfDisposed();

        // 実際の実装では適切なリサイズアルゴリズムを使用する
        // 空の配列を使用
        byte[] newData = [];

        // リサイズロジックを実装...

        return Task.FromResult<IImage>(new CoreImage(newData, width, height, Format));
    }

    /// <summary>
    /// オブジェクトが破棄されている場合に例外をスローします
    /// </summary>
    protected new void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(IsDisposed(), nameof(CoreImage));
    }

    /// <summary>
    /// ピクセルあたりのバイト数
    /// </summary>
    protected int BytesPerPixel => Format switch
    {
        ImageFormat.Rgb24 => 3,
        ImageFormat.Rgba32 => 4,
        ImageFormat.Grayscale8 => 1,
        _ => throw new NotSupportedException($"未サポートのフォーマット: {Format}")
    };

    /// <summary>
    /// マネージドリソースを解放します
    /// </summary>
    protected override void DisposeManagedResources()
    {
        _pixelData = [];
    }
}
