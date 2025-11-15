using System;
using System.Buffers;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Rectangle = Baketa.Core.Abstractions.Memory.Rectangle;

namespace Baketa.Core.Extensions;

/// <summary>
/// IImage インターフェースの拡張メソッド
/// </summary>
public static class IImageExtensions
{
    /// <summary>
    /// ArrayPool&lt;byte&gt;を使用した効率的なbyte配列取得（Phase 5.2 メモリリーク対策）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>
    /// プールから借りたbyte配列。呼び出し側は必ずfinally句でArrayPool&lt;byte&gt;.Shared.Return()を実行すること。
    /// </returns>
    /// <remarks>
    /// <para>【重要】メモリリーク防止のため、以下のパターンで使用すること:</para>
    /// <code>
    /// byte[]? pooledArray = null;
    /// try
    /// {
    ///     pooledArray = await image.ToPooledByteArrayAsync(cancellationToken).ConfigureAwait(false);
    ///     // pooledArrayを使用した処理
    /// }
    /// finally
    /// {
    ///     if (pooledArray != null)
    ///     {
    ///         ArrayPool&lt;byte&gt;.Shared.Return(pooledArray);
    ///     }
    /// }
    /// </code>
    /// <para>
    /// ArrayPool&lt;byte&gt;.Shared.Rent()は要求サイズ以上の配列を返す可能性があるため、
    /// 実際のデータサイズが必要な場合は ToPooledByteArrayWithLengthAsync() を使用すること。
    /// </para>
    /// </remarks>
    public static async Task<byte[]> ToPooledByteArrayAsync(
        this IImageBase image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);
        var pooledArray = ArrayPool<byte>.Shared.Rent(imageData.Length);

        Array.Copy(imageData, pooledArray, imageData.Length);

        return pooledArray;
    }

    /// <summary>
    /// ArrayPool&lt;byte&gt;を使用した効率的なbyte配列取得（実際のデータサイズ付き）
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>
    /// (pooledArray, actualLength) タプル。pooledArrayはプールから借りた配列、actualLengthは実際のデータサイズ。
    /// 呼び出し側は必ずfinally句でArrayPool&lt;byte&gt;.Shared.Return(pooledArray)を実行すること。
    /// </returns>
    /// <remarks>
    /// <para>【重要】メモリリーク防止のため、以下のパターンで使用すること:</para>
    /// <code>
    /// byte[]? pooledArray = null;
    /// try
    /// {
    ///     (pooledArray, var actualLength) = await image.ToPooledByteArrayWithLengthAsync(cancellationToken).ConfigureAwait(false);
    ///     // pooledArray[0..actualLength] でデータにアクセス
    ///     var imageData = new ReadOnlySpan&lt;byte&gt;(pooledArray, 0, actualLength);
    /// }
    /// finally
    /// {
    ///     if (pooledArray != null)
    ///     {
    ///         ArrayPool&lt;byte&gt;.Shared.Return(pooledArray);
    ///     }
    /// }
    /// </code>
    /// <para>
    /// ArrayPool&lt;byte&gt;.Shared.Rent()は要求サイズ以上の配列を返すため、
    /// actualLengthを使用して実際のデータ範囲のみを処理すること。
    /// </para>
    /// </remarks>
    public static async Task<(byte[] pooledArray, int actualLength)> ToPooledByteArrayWithLengthAsync(
        this IImageBase image,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(image);

        var imageData = await image.ToByteArrayAsync().ConfigureAwait(false);
        var pooledArray = ArrayPool<byte>.Shared.Rent(imageData.Length);

        Array.Copy(imageData, pooledArray, imageData.Length);

        return (pooledArray, imageData.Length);
    }

    /// <summary>
    /// 画像の指定領域のピクセルデータをバイト配列として取得します
    /// </summary>
    /// <param name="image">対象画像</param>
    /// <param name="x">開始X座標</param>
    /// <param name="y">開始Y座標</param>
    /// <param name="width">幅</param>
    /// <param name="height">高さ</param>
    /// <returns>ピクセルデータのバイト配列</returns>
    public static async Task<byte[]> GetPixelsAsync(this IImage image, int x, int y, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(image, nameof(image));

        // バウンダリチェック
        if (x < 0 || y < 0 || x + width > image.Width || y + height > image.Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "指定された領域が画像の範囲を超えています");
        }

        // IAdvancedImageへのキャストを試みる
        if (image is IAdvancedImage advancedImage)
        {
            // 指定領域を抽出
            if (x > 0 || y > 0 || width < image.Width || height < image.Height)
            {
                var region = new Rectangle(x, y, width, height);
                var croppedImage = await advancedImage.ExtractRegionAsync(region).ConfigureAwait(false);
                return await croppedImage.ToByteArrayAsync().ConfigureAwait(false);
            }
        }

        // キャストできない場合や全体領域の場合は、直接バイト配列を取得
        return await image.ToByteArrayAsync().ConfigureAwait(false);
    }
}
