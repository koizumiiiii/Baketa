using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Memory;

namespace Baketa.Core.Abstractions.Imaging;

/// <summary>
/// 生ピクセルデータへの直接アクセスを提供するIDisposableラッパー
/// 🔥 [PHASE5.2G-A] ゼロコピー最適化: Bitmap.LockBits()のライフサイクル管理
///
/// 使用方法:
/// <code>
/// using (var pixelLock = image.LockPixelData())
/// {
///     var pixelData = pixelLock.Data;
///     var stride = pixelLock.Stride;
///     unsafe
///     {
///         fixed (byte* dataPtr = pixelData)
///         {
///             var mat = Mat.FromPixelData(image.Height, image.Width,
///                 MatType.CV_8UC4, (IntPtr)dataPtr, stride);
///             // ... Mat処理
///         }
///     }
/// } // Dispose()でUnlockBits()が自動呼び出し
/// </code>
/// </summary>
public readonly ref struct PixelDataLock
{
    private readonly Action? _unlockAction;

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="data">生ピクセルデータ（BGRA32形式）</param>
    /// <param name="stride">行バイト数（幅*4 + パディング）</param>
    /// <param name="unlockAction">Dispose時に実行するUnlockBits()アクション</param>
    /// <remarks>🔥 [PHASE5.2G-A] Phase 3でWindowsImageから呼び出すためpublicに変更</remarks>
    public PixelDataLock(ReadOnlySpan<byte> data, int stride, Action unlockAction)
    {
        Data = data;
        Stride = stride;
        _unlockAction = unlockAction;
    }

    /// <summary>
    /// 生ピクセルデータ（BGRA32形式）
    /// </summary>
    public ReadOnlySpan<byte> Data { get; }

    /// <summary>
    /// 行バイト数（stride/step）
    /// メモリアライメントのため、幅*4と異なる場合がある
    /// Mat.FromPixelData()に必須パラメータ
    /// </summary>
    public int Stride { get; }

    /// <summary>
    /// Bitmap.UnlockBits()を呼び出してロックを解放
    /// </summary>
    public void Dispose()
    {
        _unlockAction?.Invoke();
    }
}

/// <summary>
/// 標準的な画像操作機能を提供するインターフェース
/// </summary>
public interface IImage : IImageBase
{
    /// <summary>
    /// ピクセルフォーマット（Gemini推奨拡張）
    /// </summary>
    ImagePixelFormat PixelFormat { get; }

    /// <summary>
    /// 画像データの読み取り専用メモリを取得（Gemini推奨拡張）
    /// ⚠️ 注意: PNGエンコードされたデータを返す（生ピクセルデータではない）
    /// 生ピクセルデータが必要な場合は LockPixelData() を使用すること
    /// </summary>
    /// <returns>画像データの読み取り専用メモリ（PNGエンコード）</returns>
    ReadOnlyMemory<byte> GetImageMemory();

    /// <summary>
    /// 🔥 [PHASE5.2G-A] 生ピクセルデータへの直接アクセスを取得（ゼロコピー最適化）
    ///
    /// 用途:
    /// - OpenCV Mat.FromPixelData() でのゼロコピー処理
    /// - PNG デコード不要の高速画像処理
    /// - メモリアロケーション削減（~8.3MB/フレーム）
    ///
    /// 効果:
    /// - PNG デコード時間削減: 15-60ms/フレーム
    /// - メモリコピー削減: ~8.3MB/フレーム
    /// - GC 圧力削減: 大幅改善
    ///
    /// 使用例:
    /// <code>
    /// using (var pixelLock = image.LockPixelData())
    /// {
    ///     unsafe
    ///     {
    ///         fixed (byte* dataPtr = pixelLock.Data)
    ///         {
    ///             var mat = Mat.FromPixelData(image.Height, image.Width,
    ///                 MatType.CV_8UC4, (IntPtr)dataPtr, pixelLock.Stride);
    ///             // ... Mat処理（PNG デコード不要）
    ///         }
    ///     }
    /// } // UnlockBits() 自動呼び出し
    /// </code>
    ///
    /// ⚠️ 重要:
    /// - 必ず using 文で使用すること（UnlockBits() 自動実行のため）
    /// - Dispose() 後の Data アクセスは禁止（AccessViolationException）
    /// - fixed ブロック外での ReadOnlySpan アクセス禁止
    /// </summary>
    /// <returns>生ピクセルデータロック（BGRA32形式、IDisposable）</returns>
    PixelDataLock LockPixelData();

    /// <summary>
    /// 画像のクローンを作成します。
    /// </summary>
    /// <returns>元の画像と同じ内容を持つ新しい画像インスタンス</returns>
    IImage Clone();

    /// <summary>
    /// 画像のサイズを変更します。
    /// </summary>
    /// <param name="width">新しい幅</param>
    /// <param name="height">新しい高さ</param>
    /// <returns>リサイズされた新しい画像インスタンス</returns>
    Task<IImage> ResizeAsync(int width, int height);
}
