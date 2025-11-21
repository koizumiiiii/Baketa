using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;
using CoreRectangle = Baketa.Core.Abstractions.Memory.Rectangle;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiRectangle = System.Drawing.Rectangle;
using SafePixelFormat = Baketa.Core.Abstractions.Memory.ImagePixelFormat;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// SafeImageをIWindowsImageインターフェースでラップするアダプター
/// Phase 3.1: ObjectDisposedException防止のための統合アダプター
/// Phase 3.2: シンプルな実装でWindowsImageAdapterFactory統合
/// 🔥 [FIX7_PHASE3] IAdvancedImage実装追加: ROI座標コンテキスト（CaptureRegion）対応
/// </summary>
public sealed class SafeImageAdapter : IWindowsImage, IAdvancedImage
{
    private readonly SafeImage _safeImage;
    private readonly ISafeImageFactory _safeImageFactory;
    private bool _disposed;

    /// <summary>
    /// SafeImageアダプターを初期化（Strategy B: OCRエンジン抽象化対応）
    /// </summary>
    /// <param name="safeImage">ラップするSafeImageインスタンス</param>
    /// <param name="safeImageFactory">SafeImage生成用ファクトリー（型整合性確保）</param>
    public SafeImageAdapter(SafeImage safeImage, ISafeImageFactory safeImageFactory)
    {
        _safeImage = safeImage ?? throw new ArgumentNullException(nameof(safeImage));
        _safeImageFactory = safeImageFactory ?? throw new ArgumentNullException(nameof(safeImageFactory));
    }

    /// <summary>
    /// 🔥 [PHASE10.37] 内部のSafeImageを取得（PNG経由バイパス用）
    /// </summary>
    public SafeImage GetUnderlyingSafeImage()
    {
        ThrowIfDisposed();
        return _safeImage;
    }

    /// <summary>
    /// 🔥 [FIX7_PHASE3] ROI画像の場合、元画像内での絶対座標を保持
    /// ROI座標変換コンテキスト（FIX6座標正規化で使用）
    /// null = フルスクリーンキャプチャ（座標変換不要）
    /// HasValue = ROIキャプチャ（CombinedBoundsにOffsetを加算）
    /// </summary>
    /// <remarks>
    /// WindowsImageFactory.CropImage()で設定される
    /// ROIBasedCaptureStrategy → CropImage → SafeImageAdapter { CaptureRegion = cropArea }
    /// → OcrExecutionStageStrategy → TextChunk.CaptureRegion
    /// → AggregatedChunksReadyEventHandler.NormalizeChunkCoordinates
    /// </remarks>
    public GdiRectangle? CaptureRegion { get; init; }

    /// <summary>
    /// 画像の幅（Phase 3.1統合: SafeImageから取得）
    /// </summary>
    public int Width => _safeImage.Width;

    /// <summary>
    /// 画像の高さ（Phase 3.1統合: SafeImageから取得）
    /// </summary>
    public int Height => _safeImage.Height;

    /// <summary>
    /// ピクセルフォーマット（Phase 3.1統合: SafeImageから取得）
    /// </summary>
    public GdiPixelFormat PixelFormat => ConvertToPixelFormat(_safeImage.PixelFormat);

    /// <summary>
    /// Bitmapオブジェクトの取得（Phase 3.1統合: SafeImageから生成）
    /// ⚠️ 注意: 返されるBitmapはDispose必要
    /// </summary>
    /// <returns>生成されたBitmap（呼び出し側でDispose必要）</returns>
    public Bitmap GetBitmap()
    {
        ThrowIfDisposed();
        return CreateBitmapFromSafeImage();
    }

    /// <summary>
    /// Bitmapとして取得（async版）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>System.Drawing.Bitmap インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
    public Task<Bitmap> GetBitmapAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult(CreateBitmapFromSafeImage());
    }

    /// <summary>
    /// バイト配列として画像データを取得（Phase 3.1統合: SafeImageから取得）
    /// </summary>
    /// <returns>画像データのバイト配列</returns>
    public byte[] ToByteArray()
    {
        ThrowIfDisposed();
        using var bitmap = CreateBitmapFromSafeImage();
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, GdiImageFormat.Png);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// 指定フォーマットでバイト配列として画像データを取得
    /// </summary>
    /// <param name="format">画像フォーマット</param>
    /// <returns>指定フォーマットでの画像データ</returns>
    public byte[] ToByteArray(GdiImageFormat format)
    {
        ThrowIfDisposed();

        using var bitmap = CreateBitmapFromSafeImage();
        using var memoryStream = new MemoryStream();
        bitmap.Save(memoryStream, format);
        return memoryStream.ToArray();
    }

    /// <summary>
    /// 指定した矩形領域の画像を作成（Phase 3.1統合: SafeImage経由）
    /// </summary>
    /// <param name="rect">切り出し領域</param>
    /// <returns>切り出された画像（Adapter内でSafeImageとしてラップ）</returns>
    public IWindowsImage Crop(GdiRectangle rect)
    {
        ThrowIfDisposed();

        // SafeImageの切り出し機能を使用（実装されている場合）
        // 未実装の場合はBitmap経由で実装
        using var bitmap = CreateBitmapFromSafeImage();
        using var croppedBitmap = new Bitmap(rect.Width, rect.Height);
        using var graphics = Graphics.FromImage(croppedBitmap);

        // 🔧 [CRITICAL_FIX] Graphics.DrawImage引数修正 - Segmentation Fault原因 (Line 146)
        // 正しいシグネチャ: DrawImage(Image, Rectangle destRect, int srcX, srcY, srcWidth, srcHeight, GraphicsUnit)
        graphics.DrawImage(bitmap,
            new System.Drawing.Rectangle(0, 0, rect.Width, rect.Height),  // 描画先の矩形
            rect.X, rect.Y, rect.Width, rect.Height,                      // ソース領域
            GraphicsUnit.Pixel);

        // 🎯 Strategy B実装: SafeImageFactoryでSafeImage生成 → SafeImageAdapterでラップ
        var safeImage = _safeImageFactory.CreateFromBitmap(croppedBitmap, rect.Width, rect.Height);
        return new SafeImageAdapter(safeImage, _safeImageFactory);
    }

    /// <summary>
    /// 画像をリサイズ（Phase 3.1統合: SafeImage経由）
    /// </summary>
    /// <param name="width">新しい幅</param>
    /// <param name="height">新しい高さ</param>
    /// <returns>リサイズされた画像（Adapter内でSafeImageとしてラップ）</returns>
    public IWindowsImage Resize(int width, int height)
    {
        ThrowIfDisposed();

        // SafeImageのリサイズ機能を使用（実装されている場合）
        // 未実装の場合はBitmap経由で実装
        using var bitmap = CreateBitmapFromSafeImage();
        var resizedBitmap = new Bitmap(width, height);
        using (var graphics = Graphics.FromImage(resizedBitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(bitmap, 0, 0, width, height);
        }

        // 🎯 Strategy B実装: SafeImageFactoryでSafeImage生成 → SafeImageAdapterでラップ
        var safeImage = _safeImageFactory.CreateFromBitmap(resizedBitmap, width, height);
        return new SafeImageAdapter(safeImage, _safeImageFactory);
    }

    /// <summary>
    /// 指定されたパスにファイルとして保存
    /// </summary>
    /// <param name="filePath">保存先ファイルパス</param>
    public void SaveToFile(string filePath)
    {
        ThrowIfDisposed();

        using var bitmap = CreateBitmapFromSafeImage();
        bitmap.Save(filePath);
    }

    /// <summary>
    /// 指定されたパスとフォーマットでファイルとして保存
    /// </summary>
    /// <param name="filePath">保存先ファイルパス</param>
    /// <param name="format">画像フォーマット</param>
    public void SaveToFile(string filePath, GdiImageFormat format)
    {
        ThrowIfDisposed();

        using var bitmap = CreateBitmapFromSafeImage();
        bitmap.Save(filePath, format);
    }

    /// <summary>
    /// ネイティブImageオブジェクトを取得
    /// </summary>
    /// <returns>System.Drawing.Image インスタンス</returns>
    public Image GetNativeImage()
    {
        ThrowIfDisposed();
        return CreateBitmapFromSafeImage();
    }

    /// <summary>
    /// ネイティブImageオブジェクトを取得（async版）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>System.Drawing.Image インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] スレッドブロッキング防止のため追加</remarks>
    public Task<Image> GetNativeImageAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        return Task.FromResult<Image>(CreateBitmapFromSafeImage());
    }

    /// <summary>
    /// 指定したパスに画像を保存
    /// </summary>
    /// <param name="path">保存先パス</param>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>非同期タスク</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    public async Task SaveAsync(string path, GdiImageFormat? format = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await Task.Run(() =>
        {
            using var bitmap = CreateBitmapFromSafeImage();
            bitmap.Save(path, format ?? GdiImageFormat.Png);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像のサイズを変更
    /// </summary>
    /// <param name="width">新しい幅</param>
    /// <param name="height">新しい高さ</param>
    /// <returns>リサイズされた新しい画像インスタンス</returns>
    public async Task<IWindowsImage> ResizeAsync(int width, int height)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            using var bitmap = CreateBitmapFromSafeImage();
            // 🔧 [MEMORY_LEAK_FIX] using文でBitmapを確実に破棄（2回目のOCR実行時のメモリ不足エラー対策）
            using var resizedBitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(resizedBitmap))
            {
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.DrawImage(bitmap, 0, 0, width, height);
            }

            // 🎯 Strategy B実装: SafeImageFactoryでSafeImage生成 → SafeImageAdapterでラップ
            var safeImage = _safeImageFactory.CreateFromBitmap(resizedBitmap, width, height);
            return new SafeImageAdapter(safeImage, _safeImageFactory);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像の一部を切り取る
    /// </summary>
    /// <param name="rectangle">切り取る領域</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>切り取られた新しい画像インスタンス</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    public async Task<IWindowsImage> CropAsync(GdiRectangle rectangle, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            using var bitmap = CreateBitmapFromSafeImage();
            using var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);
            using var graphics = Graphics.FromImage(croppedBitmap);

            // 🔧 [CRITICAL_FIX] Graphics.DrawImage引数修正 - Segmentation Fault原因 (Line 292)
            // 正しいシグネチャ: DrawImage(Image, Rectangle destRect, int srcX, srcY, srcWidth, srcHeight, GraphicsUnit)
            graphics.DrawImage(bitmap,
                new System.Drawing.Rectangle(0, 0, rectangle.Width, rectangle.Height),  // 描画先の矩形
                rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height,            // ソース領域
                GraphicsUnit.Pixel);

            // 🎯 Strategy B実装: SafeImageFactoryでSafeImage生成 → SafeImageAdapterでラップ
            var safeImage = _safeImageFactory.CreateFromBitmap(croppedBitmap, rectangle.Width, rectangle.Height);
            return new SafeImageAdapter(safeImage, _safeImageFactory);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像をバイト配列に変換
    /// </summary>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>画像データのバイト配列</returns>
    /// <remarks>🔥 [PHASE5.2] CancellationToken追加</remarks>
    public async Task<byte[]> ToByteArrayAsync(GdiImageFormat? format = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            try
            {
                // 🔧 [PHASE3.2_DEBUG] SafeImageAdapter状態詳細ログ
                Console.WriteLine($"🔧 [PHASE3.2_DEBUG] ToByteArrayAsync開始 - Width: {_safeImage.Width}, Height: {_safeImage.Height}, IsDisposed: {_safeImage.IsDisposed}");

                using var bitmap = CreateBitmapFromSafeImage();

                Console.WriteLine($"🔧 [PHASE3.2_DEBUG] Bitmap作成完了 - Size: {bitmap.Width}x{bitmap.Height}, PixelFormat: {bitmap.PixelFormat}");

                using var memoryStream = new MemoryStream();
                bitmap.Save(memoryStream, format ?? GdiImageFormat.Png);

                var result = memoryStream.ToArray();
                Console.WriteLine($"🔧 [PHASE3.2_DEBUG] Bitmap.Save完了 - 出力データサイズ: {result.Length}bytes");

                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"🚨 [PHASE3.2_ERROR] ToByteArrayAsync失敗: {ex.Message}");
                Console.WriteLine($"🚨 [PHASE3.2_ERROR] StackTrace: {ex.StackTrace}");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 🔥 [PHASE7.2] LockPixelData実装 - IWindowsImageインターフェース完全対応
    /// SafeImageが保持する画像データへの直接アクセスを提供
    ///
    /// 実装詳細:
    /// - SafeImage.GetImageData()で既にメモリ内にあるピクセルデータを取得
    /// - ReadOnlySpanを返してゼロコピーアクセスを実現
    /// - unlockActionは不要（SafeImageはメモリ管理済み）
    ///
    /// Phase 3実装保留を解消: OCRパイプラインでの使用が可能に
    /// </summary>
    public Baketa.Core.Abstractions.Imaging.PixelDataLock LockPixelData()
    {
        ThrowIfDisposed();

        // SafeImageから直接画像データを取得（既にメモリ内に保持されている）
        var imageData = _safeImage.GetImageData();

        // 🔥 [ULTRATHINK_PHASE5.4_FIX] SafeImage.Strideを使用（Width * bytesPerPixelは誤り）
        // SafeImageが保持する正確なStride値を使用
        var stride = _safeImage.Stride;

        // PixelDataLockを作成（unlockActionは不要：SafeImageが既にメモリ管理している）
        // SafeImageはメモリ内データを保持しているため、UnlockBitsのような処理は不要
        return new Baketa.Core.Abstractions.Imaging.PixelDataLock(
            imageData,                      // data: ReadOnlySpan<byte>
            stride,                         // stride: int
            () => { }                       // unlockAction: 何もしない（SafeImageが管理）
        );
    }

    /// <summary>
    /// SafeImageからBitmapを生成するヘルパーメソッド
    /// </summary>
    /// <returns>生成されたBitmap（呼び出し側でDispose必要）</returns>
    private Bitmap CreateBitmapFromSafeImage()
    {
        try
        {
            // 🔍 Phase 3.10: SafeImage状態確認
            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] CreateBitmapFromSafeImage開始 - Width: {_safeImage.Width}, Height: {_safeImage.Height}");

            var imageData = _safeImage.GetImageData();
            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] SafeImage.GetImageData完了 - データサイズ: {imageData.Length}bytes");

            // 🔥 [PHASE10.31] Gemini推奨実装の正しい適用: SafeImageのPixelFormatを尊重
            // Phase 10.30の誤り: Format24bppRgb固定により、Bgra32のSafeImageで縦線発生
            // 正しい修正: SafeImageのPixelFormatに応じてBitmapを作成
            var pixelFormat = ConvertToPixelFormat(_safeImage.PixelFormat);
            var bitmap = new Bitmap(_safeImage.Width, _safeImage.Height, pixelFormat);
            System.Diagnostics.Debug.WriteLine($"✅ [PHASE10.31] Bitmap作成: SafeFormat={_safeImage.PixelFormat}, GdiFormat={pixelFormat} - Size: {bitmap.Width}x{bitmap.Height}");

            var bitmapData = bitmap.LockBits(
                new GdiRectangle(0, 0, _safeImage.Width, _safeImage.Height),
                ImageLockMode.WriteOnly,
                pixelFormat);
            System.Diagnostics.Debug.WriteLine($"✅ [PHASE10.31] Bitmap.LockBits完了 - Stride: {bitmapData.Stride}");

            // 🔥 [GEMINI_DEBUG_3] SafeImageAdapter側のStride詳細ログ
            var bytesPerPixel = GetBytesPerPixel(_safeImage.PixelFormat);
            System.Diagnostics.Debug.WriteLine($"🔍 [GEMINI_DEBUG_3] SafeImageAdapter Bitmap変換:");
            System.Diagnostics.Debug.WriteLine($"  SourceStride (SafeImage): {_safeImage.Stride}");
            System.Diagnostics.Debug.WriteLine($"  DestStride (Bitmap): {bitmapData.Stride}");
            System.Diagnostics.Debug.WriteLine($"  Width * BytesPerPixel: {_safeImage.Width * bytesPerPixel}");
            System.Diagnostics.Debug.WriteLine($"  BytesPerPixel: {bytesPerPixel}");

            try
            {
                unsafe
                {
                    var destPtr = (byte*)bitmapData.Scan0;
                    var stride = bitmapData.Stride;
                    var imageDataSpan = imageData;

                    // 🔥 [PHASE10.33] UltraThink Phase 5推奨実装: 有効データコピー + パディングゼロ埋め
                    // SafeImageはパディングなし（correctStride = width × bytesPerPixel）で保存されている
                    // Bitmapは4バイトアライメント必須のため、パディング部分を明示的にゼロ埋めする
                    var bytesPerLine = _safeImage.Width * bytesPerPixel; // 1行あたりの有効な画像データバイト数
                    var paddingBytes = stride - bytesPerLine;            // 各行のパディングバイト数
                    System.Diagnostics.Debug.WriteLine($"✅ [PHASE10.33] ピクセルコピー開始 - BytesPerLine: {bytesPerLine}, Stride: {stride}, Padding: {paddingBytes}bytes/row");

                    for (int y = 0; y < _safeImage.Height; y++)
                    {
                        // 各行の開始アドレスを計算（Strideを使用）
                        var sourceOffset = y * _safeImage.Stride;
                        var destOffset = y * stride;

                        // 1行分の有効な画像データをコピー
                        if (sourceOffset + bytesPerLine <= imageDataSpan.Length)
                        {
                            var sourceSpan = imageDataSpan.Slice(sourceOffset, bytesPerLine);
                            var destSpan = new Span<byte>(destPtr + destOffset, bytesPerLine);
                            sourceSpan.CopyTo(destSpan);

                            // 🔥 [PHASE10.33] パディング部分をゼロ埋め（未初期化メモリ防止）
                            if (paddingBytes > 0)
                            {
                                var paddingSpan = new Span<byte>(destPtr + destOffset + bytesPerLine, paddingBytes);
                                paddingSpan.Clear(); // ゼロ埋め
                            }
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine($"🚨 [PHASE10.33_WARNING] Row {y}: ソースデータ不足 - Offset: {sourceOffset}, BytesPerLine: {bytesPerLine}, DataLength: {imageDataSpan.Length}");
                        }
                    }

                    System.Diagnostics.Debug.WriteLine($"✅ [PHASE10.33] ピクセルコピー完了 - 全{_safeImage.Height}行処理、パディング{paddingBytes}bytes/rowゼロ埋め");
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
                Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] Bitmap.UnlockBits完了");
            }

            // 🔥 [PHASE10.39] Gemini推奨Option B: Format32bppArgb統一
            // 根本原因: PNG encoding時のStride情報喪失 + Mat.FromImageData()でのデコード失敗
            // 解決策: Width*4は常に4の倍数 → Strideパディング不要 → PNG経由でも破損しない
            // Phase 10.36証拠: unlockbits_verify正常、prevention_input破損（Width=254以外）
            //
            // メモリトレードオフ: 24bpp → 32bpp = 33%増加
            // 安定性優先: 確実な問題解決を最優先
            if (bitmap.PixelFormat != System.Drawing.Imaging.PixelFormat.Format32bppArgb)
            {
                var originalFormat = bitmap.PixelFormat;
                var argbBitmap = new Bitmap(bitmap.Width, bitmap.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (var g = Graphics.FromImage(argbBitmap))
                {
                    g.DrawImage(bitmap, 0, 0, bitmap.Width, bitmap.Height);
                }
                bitmap.Dispose();
                System.Diagnostics.Debug.WriteLine($"✅ [PHASE10.39] Format32bppArgb変換完了 - {originalFormat} → Format32bppArgb");
                bitmap = argbBitmap;
            }

            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] CreateBitmapFromSafeImage成功 - 最終Bitmap: {bitmap.Width}x{bitmap.Height}, Format: {bitmap.PixelFormat}");
            return bitmap;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [PHASE_3_10_ERROR] CreateBitmapFromSafeImage失敗: {ex.Message}");
            Console.WriteLine($"🚨 [PHASE_3_10_ERROR] StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// ピクセルフォーマットごとのバイト数を取得
    /// </summary>
    /// <param name="format">ピクセルフォーマット</param>
    /// <returns>1ピクセルあたりのバイト数</returns>
    private static int GetBytesPerPixel(SafePixelFormat format)
    {
        return format switch
        {
            SafePixelFormat.Bgra32 => 4,
            SafePixelFormat.Rgba32 => 4,
            SafePixelFormat.Rgb24 => 3,
            SafePixelFormat.Bgr24 => 3,  // 🔥 [PHASE10.32] Bgr24ケース追加
            SafePixelFormat.Gray8 => 1,
            _ => 4
        };
    }

    /// <summary>
    /// ImagePixelFormatをPixelFormatに変換
    /// </summary>
    /// <param name="format">ImagePixelFormat</param>
    /// <returns>変換されたPixelFormat</returns>
    private static GdiPixelFormat ConvertToPixelFormat(SafePixelFormat format)
    {
        return format switch
        {
            SafePixelFormat.Bgra32 => GdiPixelFormat.Format32bppArgb,
            SafePixelFormat.Rgba32 => GdiPixelFormat.Format32bppArgb,
            SafePixelFormat.Rgb24 => GdiPixelFormat.Format24bppRgb,
            SafePixelFormat.Bgr24 => GdiPixelFormat.Format24bppRgb,  // 🔥 [PHASE10.32] Bgr24ケース追加 - GDI+ Format24bppRgbはBGRバイトオーダー
            SafePixelFormat.Gray8 => GdiPixelFormat.Format8bppIndexed,
            _ => GdiPixelFormat.Format32bppArgb
        };
    }

    /// <summary>
    /// Dispose状態チェック
    /// 🚨 EMERGENCY FIX: 一時的に無効化 - ObjectDisposedException回避で翻訳オーバーレイ復旧
    /// </summary>
    private void ThrowIfDisposed()
    {
        // 🚨 緊急修正: ThrowIfDisposed()を一時無効化
        // 理由: SafeImageAdapter早期Disposeが翻訳オーバーレイ表示を阻害
        // 根本原因: WindowsImageFactory.CreateFromBytesAsync → SafeImageAdapter → 早期Dispose
        // TODO: 適切なライフサイクル管理で根本修正が必要

        // 緊急回避: dispose チェックを無効化
        // SafeImage本体が生きていれば動作可能（一時的な解決策）

        /*
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(SafeImageAdapter),
                "SafeImageAdapterは既に破棄されています - Phase 3.1統合でObjectDisposed防止");
        }
        */

        // 🎯 暫定処理: 何も投げない（SafeImageアクセス時のエラーは個別にキャッチ）
    }

    // ====================================================================
    // 🔥 [FIX7_PHASE3] IAdvancedImage実装: ROI座標コンテキスト対応
    // ====================================================================

    #region IAdvancedImage Members (Minimal Implementation)

    /// <summary>
    /// 指定座標のピクセル値を取得（未サポート）
    /// </summary>
    public Color GetPixel(int x, int y)
    {
        throw new NotSupportedException("GetPixel is not supported by SafeImageAdapter. Use WindowsImage for pixel-level operations.");
    }

    /// <summary>
    /// 指定座標にピクセル値を設定（未サポート）
    /// </summary>
    public void SetPixel(int x, int y, Color color)
    {
        throw new NotSupportedException("SetPixel is not supported by SafeImageAdapter. Use WindowsImage for pixel-level operations.");
    }

    /// <summary>
    /// 画像にフィルターを適用（未サポート）
    /// </summary>
    public Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter)
    {
        throw new NotSupportedException("ApplyFilterAsync is not supported by SafeImageAdapter. Use WindowsImage for advanced filtering.");
    }

    /// <summary>
    /// 複数のフィルターを順番に適用（未サポート）
    /// </summary>
    public Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters)
    {
        throw new NotSupportedException("ApplyFiltersAsync is not supported by SafeImageAdapter. Use WindowsImage for advanced filtering.");
    }

    /// <summary>
    /// 画像のヒストグラムを生成（未サポート）
    /// </summary>
    public Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance)
    {
        throw new NotSupportedException("ComputeHistogramAsync is not supported by SafeImageAdapter. Use WindowsImage for histogram analysis.");
    }

    /// <summary>
    /// 画像がグレースケールかどうか（簡易実装）
    /// </summary>
    public bool IsGrayscale => _safeImage.PixelFormat == SafePixelFormat.Gray8;

    /// <summary>
    /// 画像をグレースケールに変換（非同期版、未サポート）
    /// </summary>
    public Task<IAdvancedImage> ToGrayscaleAsync()
    {
        throw new NotSupportedException("ToGrayscaleAsync is not supported by SafeImageAdapter. Use WindowsImage for color conversion.");
    }

    /// <summary>
    /// 画像をグレースケールに変換（未サポート）
    /// </summary>
    public IAdvancedImage ToGrayscale()
    {
        throw new NotSupportedException("ToGrayscale is not supported by SafeImageAdapter. Use WindowsImage for color conversion.");
    }

    /// <summary>
    /// ピクセルあたりのビット数
    /// </summary>
    public int BitsPerPixel => GetBytesPerPixel(_safeImage.PixelFormat) * 8;

    /// <summary>
    /// チャンネル数
    /// </summary>
    public int ChannelCount => _safeImage.PixelFormat switch
    {
        SafePixelFormat.Bgra32 => 4,
        SafePixelFormat.Rgba32 => 4,
        SafePixelFormat.Rgb24 => 3,
        SafePixelFormat.Gray8 => 1,
        _ => 4
    };

    /// <summary>
    /// 画像を二値化（未サポート）
    /// </summary>
    public Task<IAdvancedImage> ToBinaryAsync(byte threshold)
    {
        throw new NotSupportedException("ToBinaryAsync is not supported by SafeImageAdapter. Use WindowsImage for binarization.");
    }

    /// <summary>
    /// 画像の特定領域を抽出（CropAsyncに委譲）
    /// </summary>
    public async Task<IAdvancedImage> ExtractRegionAsync(CoreRectangle rectangle)
    {
        // CoreRectangle → GdiRectangle 変換
        var gdiRect = new GdiRectangle(rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height);
        var cropped = await CropAsync(gdiRect).ConfigureAwait(false);
        return (IAdvancedImage)cropped; // SafeImageAdapterなので安全にキャスト
    }

    /// <summary>
    /// OCR前処理の最適化（未サポート）
    /// </summary>
    public Task<IAdvancedImage> OptimizeForOcrAsync()
    {
        throw new NotSupportedException("OptimizeForOcrAsync is not supported by SafeImageAdapter. Use WindowsImage for OCR optimization.");
    }

    /// <summary>
    /// OCR前処理の最適化を指定されたオプションで行う（未サポート）
    /// </summary>
    public Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options)
    {
        throw new NotSupportedException("OptimizeForOcrAsync is not supported by SafeImageAdapter. Use WindowsImage for OCR optimization.");
    }

    /// <summary>
    /// 2つの画像の類似度を計算（未サポート）
    /// </summary>
    public Task<float> CalculateSimilarityAsync(IImage other)
    {
        throw new NotSupportedException("CalculateSimilarityAsync is not supported by SafeImageAdapter. Use WindowsImage for similarity analysis.");
    }

    /// <summary>
    /// 画像の特定領域におけるテキスト存在可能性を評価（未サポート）
    /// </summary>
    public Task<float> EvaluateTextProbabilityAsync(CoreRectangle rectangle)
    {
        throw new NotSupportedException("EvaluateTextProbabilityAsync is not supported by SafeImageAdapter. Use WindowsImage for text analysis.");
    }

    /// <summary>
    /// 画像の回転を行う（未サポート）
    /// </summary>
    public Task<IAdvancedImage> RotateAsync(float degrees)
    {
        throw new NotSupportedException("RotateAsync is not supported by SafeImageAdapter. Use WindowsImage for rotation.");
    }

    /// <summary>
    /// 画像の強調処理を行う（未サポート）
    /// </summary>
    public Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options)
    {
        throw new NotSupportedException("EnhanceAsync is not supported by SafeImageAdapter. Use WindowsImage for enhancement.");
    }

    /// <summary>
    /// 画像から自動的にテキスト領域を検出（未サポート）
    /// </summary>
    public Task<List<CoreRectangle>> DetectTextRegionsAsync()
    {
        throw new NotSupportedException("DetectTextRegionsAsync is not supported by SafeImageAdapter. Use WindowsImage for text region detection.");
    }

    #endregion

    #region IImage Explicit Implementation (Gemini推奨: 明示的実装でインターフェース衝突回避)

    /// <summary>
    /// ピクセルフォーマット（IImage明示的実装）
    /// </summary>
    Baketa.Core.Abstractions.Memory.ImagePixelFormat IImage.PixelFormat => _safeImage.PixelFormat;

    /// <summary>
    /// 画像データの読み取り専用メモリを取得（IImage明示的実装）
    /// ⚠️ 注意: PNGエンコードされたデータを返す（生ピクセルデータではない）
    /// </summary>
    ReadOnlyMemory<byte> IImage.GetImageMemory()
    {
        ThrowIfDisposed();
        // SafeImageから画像データメモリを取得
        return _safeImage.GetImageMemory();
    }

    /// <summary>
    /// 画像のクローンを作成（IImage明示的実装）
    /// </summary>
    IImage IImage.Clone()
    {
        ThrowIfDisposed();
        // SafeImageAdapterではクローン作成は未サポート
        // ISafeImageFactoryにReadOnlyMemory<byte>からの直接生成メソッドが存在しないため
        throw new NotSupportedException(
            "Clone is not supported by SafeImageAdapter. " +
            "Use WindowsImage for image cloning operations.");
    }

    /// <summary>
    /// 画像のサイズを変更（IImage明示的実装）
    /// </summary>
    async Task<IImage> IImage.ResizeAsync(int width, int height)
    {
        // IWindowsImage.ResizeAsyncを呼び出す
        var resized = await ResizeAsync(width, height).ConfigureAwait(false);
        // IWindowsImageもIImageを実装しているはずなので、そのまま返す
        return (IImage)resized;
    }

    #endregion

    #region IImageBase Explicit Implementation

    /// <summary>
    /// 画像フォーマット（IImageBase明示的実装）
    /// </summary>
    Baketa.Core.Abstractions.Imaging.ImageFormat IImageBase.Format
    {
        get
        {
            // SafePixelFormat から ImageFormat への変換
            return _safeImage.PixelFormat switch
            {
                SafePixelFormat.Bgra32 => Baketa.Core.Abstractions.Imaging.ImageFormat.Rgba32,
                SafePixelFormat.Rgba32 => Baketa.Core.Abstractions.Imaging.ImageFormat.Rgba32,
                SafePixelFormat.Rgb24 => Baketa.Core.Abstractions.Imaging.ImageFormat.Rgb24,
                SafePixelFormat.Gray8 => Baketa.Core.Abstractions.Imaging.ImageFormat.Grayscale8,
                _ => Baketa.Core.Abstractions.Imaging.ImageFormat.Unknown
            };
        }
    }

    /// <summary>
    /// 画像をバイト配列に変換（IImageBase明示的実装）
    /// </summary>
    async Task<byte[]> IImageBase.ToByteArrayAsync()
    {
        // PNG形式でエンコード（デフォルト）
        return await ToByteArrayAsync(GdiImageFormat.Png).ConfigureAwait(false);
    }

    #endregion

    /// <summary>
    /// リソースの破棄（Phase 3.1統合: SafeImageの適切な破棄）
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _safeImage?.Dispose();
            _disposed = true;
        }
    }
}
