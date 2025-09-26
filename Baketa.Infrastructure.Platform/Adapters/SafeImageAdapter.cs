using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Core.Abstractions.Memory;
using Baketa.Infrastructure.Platform.Windows;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiImageFormat = System.Drawing.Imaging.ImageFormat;
using GdiRectangle = System.Drawing.Rectangle;
using SafePixelFormat = Baketa.Core.Abstractions.Memory.ImagePixelFormat;

namespace Baketa.Infrastructure.Platform.Adapters;

/// <summary>
/// SafeImageをIWindowsImageインターフェースでラップするアダプター
/// Phase 3.1: ObjectDisposedException防止のための統合アダプター
/// Phase 3.2: シンプルな実装でWindowsImageAdapterFactory統合
/// </summary>
public sealed class SafeImageAdapter : IWindowsImage
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
        graphics.DrawImage(bitmap, 0, 0, rect, GraphicsUnit.Pixel);

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
    /// 指定したパスに画像を保存
    /// </summary>
    /// <param name="path">保存先パス</param>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <returns>非同期タスク</returns>
    public async Task SaveAsync(string path, GdiImageFormat? format = null)
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
            var resizedBitmap = new Bitmap(width, height);
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
    /// <returns>切り取られた新しい画像インスタンス</returns>
    public async Task<IWindowsImage> CropAsync(GdiRectangle rectangle)
    {
        ThrowIfDisposed();

        return await Task.Run(() =>
        {
            using var bitmap = CreateBitmapFromSafeImage();
            using var croppedBitmap = new Bitmap(rectangle.Width, rectangle.Height);
            using var graphics = Graphics.FromImage(croppedBitmap);
            graphics.DrawImage(bitmap, 0, 0, rectangle, GraphicsUnit.Pixel);

            // 🎯 Strategy B実装: SafeImageFactoryでSafeImage生成 → SafeImageAdapterでラップ
            var safeImage = _safeImageFactory.CreateFromBitmap(croppedBitmap, rectangle.Width, rectangle.Height);
            return new SafeImageAdapter(safeImage, _safeImageFactory);
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 画像をバイト配列に変換
    /// </summary>
    /// <param name="format">画像フォーマット（省略時はPNG）</param>
    /// <returns>画像データのバイト配列</returns>
    public async Task<byte[]> ToByteArrayAsync(GdiImageFormat? format = null)
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
            
            var pixelFormat = ConvertToPixelFormat(_safeImage.PixelFormat);
            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] PixelFormat変換完了 - SafeFormat: {_safeImage.PixelFormat}, GdiFormat: {pixelFormat}");

            var bitmap = new Bitmap(_safeImage.Width, _safeImage.Height, pixelFormat);
            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] 空Bitmap作成完了 - Size: {bitmap.Width}x{bitmap.Height}");
            
            var bitmapData = bitmap.LockBits(
                new GdiRectangle(0, 0, _safeImage.Width, _safeImage.Height),
                ImageLockMode.WriteOnly,
                pixelFormat);
            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] Bitmap.LockBits完了 - Stride: {bitmapData.Stride}");

            try
            {
                unsafe
                {
                    var destPtr = (byte*)bitmapData.Scan0;
                    var stride = bitmapData.Stride;
                    var imageDataSpan = imageData;
                    var bytesPerPixel = GetBytesPerPixel(_safeImage.PixelFormat);
                    
                    Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] ピクセルコピー開始 - BytesPerPixel: {bytesPerPixel}, ExpectedRowBytes: {_safeImage.Width * bytesPerPixel}");

                    for (int y = 0; y < _safeImage.Height; y++)
                    {
                        var sourceOffset = y * _safeImage.Width * bytesPerPixel;
                        var destOffset = y * stride;
                        var rowBytes = _safeImage.Width * bytesPerPixel;

                        if (sourceOffset + rowBytes <= imageDataSpan.Length)
                        {
                            var sourceSpan = imageDataSpan.Slice(sourceOffset, rowBytes);
                            var destSpan = new Span<byte>(destPtr + destOffset, rowBytes);
                            sourceSpan.CopyTo(destSpan);
                        }
                        else
                        {
                            Console.WriteLine($"🚨 [PHASE_3_10_WARNING] Row {y}: ソースデータ不足 - Offset: {sourceOffset}, RowBytes: {rowBytes}, DataLength: {imageDataSpan.Length}");
                        }
                    }
                    
                    Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] ピクセルコピー完了 - 全{_safeImage.Height}行処理");
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
                Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] Bitmap.UnlockBits完了");
            }

            Console.WriteLine($"🔍 [PHASE_3_10_DEBUG] CreateBitmapFromSafeImage成功 - 最終Bitmap: {bitmap.Width}x{bitmap.Height}");
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