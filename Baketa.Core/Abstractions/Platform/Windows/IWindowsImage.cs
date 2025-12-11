using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;

namespace Baketa.Core.Abstractions.Platform.Windows;

/// <summary>
/// Windows固有の画像インターフェース
/// </summary>
public interface IWindowsImage : IDisposable
{
    /// <summary>
    /// 画像の幅
    /// </summary>
    int Width { get; }

    /// <summary>
    /// 画像の高さ
    /// </summary>
    int Height { get; }

    /// <summary>
    /// 🚀 [Issue #193] 元のキャプチャ幅（リサイズ前）
    /// GPUリサイズキャプチャの場合、リサイズ前の元サイズを保持
    /// 非リサイズキャプチャの場合はWidthと同じ値
    /// </summary>
    int OriginalWidth { get; }

    /// <summary>
    /// 🚀 [Issue #193] 元のキャプチャ高さ（リサイズ前）
    /// GPUリサイズキャプチャの場合、リサイズ前の元サイズを保持
    /// 非リサイズキャプチャの場合はHeightと同じ値
    /// </summary>
    int OriginalHeight { get; }

    /// <summary>
    /// ネイティブImageオブジェクトを取得（async版）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>System.Drawing.Image インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
    Task<Image> GetNativeImageAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ネイティブImageオブジェクトを取得
    /// </summary>
    /// <returns>System.Drawing.Image インスタンス</returns>
    Image GetNativeImage();

    /// <summary>
    /// Bitmapとして取得（async版）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>System.Drawing.Bitmap インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
    Task<Bitmap> GetBitmapAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Bitmapとして取得
    /// </summary>
    /// <returns>System.Drawing.Bitmap インスタンス</returns>
    Bitmap GetBitmap();

    /// <summary>
    /// 指定したパスに画像を保存
    /// </summary>
    /// <param name="path">保存先パス</param>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    Task SaveAsync(string path, System.Drawing.Imaging.ImageFormat? format = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像のサイズを変更
    /// </summary>
    /// <param name="width">新しい幅</param>
    /// <param name="height">新しい高さ</param>
    /// <returns>リサイズされた新しい画像インスタンス</returns>
    Task<IWindowsImage> ResizeAsync(int width, int height);

    /// <summary>
    /// 画像の一部を切り取る
    /// </summary>
    /// <param name="rectangle">切り取る領域</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>切り取られた新しい画像インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    Task<IWindowsImage> CropAsync(Rectangle rectangle, CancellationToken cancellationToken = default);

    /// <summary>
    /// 画像をバイト配列に変換
    /// </summary>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>画像データのバイト配列</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    Task<byte[]> ToByteArrayAsync(System.Drawing.Imaging.ImageFormat? format = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 🔥 [PHASE7.2] 生ピクセルデータへの直接アクセスを取得（ゼロコピー最適化）
    ///
    /// Phase 5.2G-AでWindowsImageに実装済み、Phase 7.2でIWindowsImageインターフェースに追加
    ///
    /// 用途:
    /// - OpenCV Mat.FromPixelData() でのゼロコピー処理
    /// - PNG デコード不要の高速画像処理
    ///
    /// 使用例:
    /// <code>
    /// using (var pixelLock = windowsImage.LockPixelData())
    /// {
    ///     unsafe
    ///     {
    ///         fixed (byte* dataPtr = pixelLock.Data)
    ///         {
    ///             var mat = Mat.FromPixelData(windowsImage.Height, windowsImage.Width,
    ///                 MatType.CV_8UC4, (IntPtr)dataPtr, pixelLock.Stride);
    ///             // ... Mat処理
    ///         }
    ///     }
    /// } // UnlockBits() 自動呼び出し
    /// </code>
    ///
    /// ⚠️ 重要:
    /// - 必ず using 文で使用すること（UnlockBits() 自動実行のため）
    /// - Dispose() 後の Data アクセスは禁止（AccessViolationException）
    /// </summary>
    /// <returns>生ピクセルデータロック（BGRA32形式、IDisposable）</returns>
    PixelDataLock LockPixelData();
}
