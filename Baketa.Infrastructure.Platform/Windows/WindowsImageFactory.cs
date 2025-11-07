using System;
using System.Buffers;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Memory;
using Baketa.Infrastructure.Platform.Adapters;
using IWindowsImageInterface = Baketa.Core.Abstractions.Platform.Windows.IWindowsImage;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;
using GdiPixelFormat = System.Drawing.Imaging.PixelFormat;
using GdiRectangle = System.Drawing.Rectangle;
using SafePixelFormat = Baketa.Core.Abstractions.Memory.ImagePixelFormat;

namespace Baketa.Infrastructure.Platform.Windows;


    /// <summary>
    /// WindowsImage作成のファクトリ実装
    /// Phase 3.1統合: SafeImage使用によるObjectDisposedException防止
    /// </summary>
    public class WindowsImageFactory : IWindowsImageFactoryInterface
    {
        private readonly ILogger<WindowsImageFactory>? _logger;
        private readonly ISafeImageFactory _safeImageFactory;
        private readonly IImageLifecycleManager _imageLifecycleManager;
        private static readonly object _gdiLock = new(); // GDI+操作の同期化

        public WindowsImageFactory(
            ISafeImageFactory safeImageFactory,
            IImageLifecycleManager imageLifecycleManager,
            ILogger<WindowsImageFactory>? logger = null)
        {
            _safeImageFactory = safeImageFactory ?? throw new ArgumentNullException(nameof(safeImageFactory));
            _imageLifecycleManager = imageLifecycleManager ?? throw new ArgumentNullException(nameof(imageLifecycleManager));
            _logger = logger;
        }
        /// <summary>
        /// Bitmapからの画像作成（Phase 3.1統合: SafeImage使用）
        /// </summary>
        public IWindowsImageInterface CreateFromBitmap(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            // Phase 3.1: BitmapからSafeImageを生成
            var safeImage = CreateSafeImageFromBitmap(bitmap);
            return new SafeImageAdapter(safeImage, _safeImageFactory);
        }

        /// <summary>
        /// ファイルパスからの画像作成
        /// </summary>
        public async Task<IWindowsImageInterface> CreateFromFileAsync(string filePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath, nameof(filePath));

            return await Task.Run(() =>
            {
                try
                {
                    var bitmap = new Bitmap(filePath);
                    // Phase 3.1: BitmapからSafeImageを生成
                    var safeImage = CreateSafeImageFromBitmap(bitmap);
                    bitmap.Dispose(); // 元のBitmapは破棄
                    return new SafeImageAdapter(safeImage, _safeImageFactory);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"画像ファイルの読み込みに失敗しました: {filePath}", ex);
                }
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// バイト配列からの画像作成
        /// </summary>
        public async Task<IWindowsImageInterface> CreateFromBytesAsync(byte[] data)
        {
            ArgumentNullException.ThrowIfNull(data);
            if (data.Length == 0)
                throw new ArgumentException("画像データが空です", nameof(data));

            return await Task.Run(() =>
            {
                try
                {
                    using var stream = new MemoryStream(data);
                    var bitmap = new Bitmap(stream);
                    // Phase 3.1: BitmapからSafeImageを生成
                    var safeImage = CreateSafeImageFromBitmap(bitmap);
                    bitmap.Dispose(); // 元のBitmapは破棄
                    return new SafeImageAdapter(safeImage, _safeImageFactory);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("バイトデータからの画像作成に失敗しました", ex);
                }
            }).ConfigureAwait(false);
        }
        
        /// <summary>
        /// 指定されたサイズの空の画像を作成
        /// </summary>
        /// <param name="width">幅</param>
        /// <param name="height">高さ</param>
        /// <param name="backgroundColor">背景色（省略時は透明）</param>
        /// <returns>Windows画像</returns>
        public async Task<IWindowsImageInterface> CreateEmptyAsync(int width, int height, Color? backgroundColor = null)
        {
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"無効なサイズが指定されました: {width}x{height}");
            
            return await Task.Run(() =>
            {
                var bitmap = new Bitmap(width, height);

                // 背景色が指定されていれば塗りつぶす
                if (backgroundColor.HasValue)
                {
                    using var g = Graphics.FromImage(bitmap);
                    using var brush = new SolidBrush(backgroundColor.Value);
                    g.FillRectangle(brush, 0, 0, width, height);
                }

                // Phase 3.1: BitmapからSafeImageを生成
                var safeImage = CreateSafeImageFromBitmap(bitmap);
                bitmap.Dispose(); // 元のBitmapは破棄
                return new SafeImageAdapter(safeImage, _safeImageFactory);
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// 画像をリサイズ
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="width">新しい幅</param>
        /// <param name="height">新しい高さ</param>
        /// <returns>リサイズされた画像</returns>
        public IWindowsImageInterface ResizeImage(IWindowsImageInterface source, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (width <= 0 || height <= 0)
                throw new ArgumentException($"無効なサイズが指定されました: {width}x{height}");

            var stopwatch = Stopwatch.StartNew();
            Bitmap? resizedBitmap = null;
            Bitmap? sourceBitmapClone = null;
            
            try
            {
                // スレッドセーフティのため、GDI+操作を同期化
                lock (_gdiLock)
                {
                    var sourceBitmap = source.GetBitmap();
                    // 🔒 CRITICAL FIX: Bitmap競合状態防止のためクローン作成
                    sourceBitmapClone = new Bitmap(sourceBitmap);
                }

                resizedBitmap = new Bitmap(width, height, GdiPixelFormat.Format32bppArgb);

                using (var graphics = Graphics.FromImage(resizedBitmap))
                {
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                    // 🔒 Thread-safe DrawImage呼び出し
                    lock (_gdiLock)
                    {
                        graphics.DrawImage(sourceBitmapClone, 0, 0, width, height);
                    }
                }

                stopwatch.Stop();
                _logger?.LogDebug("🎯 Thread-safe ResizeImage完了: {OriginalSize} → {NewSize}, 処理時間={ElapsedMs}ms, スレッド={ThreadId}",
                    $"{sourceBitmapClone.Width}x{sourceBitmapClone.Height}", $"{width}x{height}", stopwatch.ElapsedMilliseconds, Environment.CurrentManagedThreadId);

                // Phase 3.1: BitmapからSafeImageを生成
                var safeImage = CreateSafeImageFromBitmap(resizedBitmap);
                resizedBitmap.Dispose(); // 元のBitmapは破棄
                var result = new SafeImageAdapter(safeImage, _safeImageFactory);
                resizedBitmap = null; // SafeImageが所有権を取得
                return result;
            }
            catch (OutOfMemoryException memEx)
            {
                stopwatch.Stop();
                _logger?.LogError(memEx, "💥 ResizeImage - メモリ不足: {TargetSize}, 処理時間={ElapsedMs}ms", 
                    $"{width}x{height}", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像リサイズ中にメモリ不足が発生: {width}x{height}", memEx);
            }
            catch (ArgumentException argEx) when (argEx.Message.Contains("Parameter is not valid"))
            {
                stopwatch.Stop();
                _logger?.LogError(argEx, "💥 ResizeImage - GDI+パラメータエラー: {TargetSize}, 処理時間={ElapsedMs}ms", 
                    $"{width}x{height}", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像リサイズ中にGDI+エラーが発生: {width}x{height}", argEx);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogError(ex, "💥 ResizeImage - 予期しないエラー: {TargetSize}, 処理時間={ElapsedMs}ms, スレッド={ThreadId}", 
                    $"{width}x{height}", stopwatch.ElapsedMilliseconds, Environment.CurrentManagedThreadId);
                throw new InvalidOperationException($"画像のリサイズに失敗しました: {width}x{height}", ex);
            }
            finally
            {
                // リソースクリーンアップ（エラー時）
                try
                {
                    sourceBitmapClone?.Dispose();
                    // エラー時のみresizedBitmapを破棄（正常時はWindowsImageが管理）
                    resizedBitmap?.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogWarning(cleanupEx, "⚠️ ResizeImage - リソースクリーンアップ時に警告: {TargetSize}", $"{width}x{height}");
                }
            }
        }

        /// <summary>
        /// 画像の指定領域を切り出し
        /// </summary>
        /// <param name="source">元画像</param>
        /// <param name="cropArea">切り出し領域</param>
        /// <returns>切り出された画像</returns>
        public IWindowsImageInterface CropImage(IWindowsImageInterface source, GdiRectangle cropArea)
        {
            ArgumentNullException.ThrowIfNull(source);
            if (cropArea.Width <= 0 || cropArea.Height <= 0)
                throw new ArgumentException($"無効な切り出し領域が指定されました: {cropArea}");

            var stopwatch = Stopwatch.StartNew();
            Bitmap? croppedBitmap = null;
            Bitmap? sourceBitmapClone = null;
            
            try
            {
                // スレッドセーフティのため、GDI+操作を同期化
                lock (_gdiLock)
                {
                    var sourceBitmap = source.GetBitmap();
                    
                    // 境界チェック
                    if (cropArea.X < 0 || cropArea.Y < 0 ||
                        cropArea.Right > sourceBitmap.Width || cropArea.Bottom > sourceBitmap.Height)
                    {
                        throw new ArgumentException($"切り出し領域が画像の境界を超えています: {cropArea}, 画像サイズ: {sourceBitmap.Width}x{sourceBitmap.Height}");
                    }

                    // 🔒 CRITICAL FIX: Bitmap競合状態防止のためクローン作成
                    sourceBitmapClone = new Bitmap(sourceBitmap);
                }

                // ロック外でBitmap操作実行（パフォーマンス向上）
                croppedBitmap = new Bitmap(cropArea.Width, cropArea.Height, GdiPixelFormat.Format32bppArgb);
                
                using (var graphics = Graphics.FromImage(croppedBitmap))
                {
                    // 高品質設定でレンダリング
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                    
                    // 🔒 Thread-safe DrawImage呼び出し
                    lock (_gdiLock)
                    {
                        graphics.DrawImage(sourceBitmapClone, 0, 0, cropArea, GraphicsUnit.Pixel);
                    }
                }

                stopwatch.Stop();
                _logger?.LogDebug("🎯 Thread-safe CropImage完了: 領域={CropArea} (元画像: {OriginalSize}), 処理時間={ElapsedMs}ms, スレッド={ThreadId}",
                    cropArea, $"{sourceBitmapClone.Width}x{sourceBitmapClone.Height}", stopwatch.ElapsedMilliseconds, Environment.CurrentManagedThreadId);

                // Phase 3.1: BitmapからSafeImageを生成
                var safeImage = CreateSafeImageFromBitmap(croppedBitmap);
                croppedBitmap.Dispose(); // 元のBitmapは破棄

                // 🔥 [FIX7_PHASE3] ROI座標コンテキスト設定: CaptureRegion = cropArea
                // WindowsImageFactory.CropImage → SafeImageAdapter { CaptureRegion = cropArea }
                // → OcrExecutionStageStrategy → TextChunk.CaptureRegion
                // → AggregatedChunksReadyEventHandler.NormalizeChunkCoordinates
                var result = new SafeImageAdapter(safeImage, _safeImageFactory)
                {
                    CaptureRegion = cropArea  // ROI画像の元画像内での絶対座標
                };

                _logger?.LogInformation("🔥 [FIX7_PHASE3] CaptureRegion設定完了: ({X},{Y}) {Width}x{Height}",
                    cropArea.X, cropArea.Y, cropArea.Width, cropArea.Height);

                croppedBitmap = null; // SafeImageが所有権を取得
                return result;
            }
            catch (OutOfMemoryException memEx)
            {
                stopwatch.Stop();
                _logger?.LogError(memEx, "💥 CropImage - メモリ不足: 領域={CropArea}, 処理時間={ElapsedMs}ms", 
                    cropArea, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像切り出し中にメモリ不足が発生: {cropArea}", memEx);
            }
            catch (ArgumentException argEx) when (argEx.Message.Contains("Parameter is not valid"))
            {
                stopwatch.Stop();
                _logger?.LogError(argEx, "💥 CropImage - GDI+パラメータエラー: 領域={CropArea}, 処理時間={ElapsedMs}ms", 
                    cropArea, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像切り出し中にGDI+エラーが発生: {cropArea}", argEx);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogError(ex, "💥 CropImage - 予期しないエラー: 領域={CropArea}, 処理時間={ElapsedMs}ms, スレッド={ThreadId}", 
                    cropArea, stopwatch.ElapsedMilliseconds, Environment.CurrentManagedThreadId);
                throw new InvalidOperationException($"画像の切り出しに失敗しました: {cropArea}", ex);
            }
            finally
            {
                // リソースクリーンアップ（エラー時）
                try
                {
                    sourceBitmapClone?.Dispose();
                    // エラー時のみcroppedBitmapを破棄（正常時はWindowsImageが管理）
                    croppedBitmap?.Dispose();
                }
                catch (Exception cleanupEx)
                {
                    _logger?.LogWarning(cleanupEx, "⚠️ CropImage - リソースクリーンアップ時に警告: 領域={CropArea}", cropArea);
                }
            }
        }

        /// <summary>
        /// BitmapからSafeImageを生成するヘルパーメソッド（Phase 3.1統合）
        /// </summary>
        /// <param name="bitmap">変換元Bitmap</param>
        /// <returns>生成されたSafeImage</returns>
        private SafeImage CreateSafeImageFromBitmap(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);

            try
            {
                // 🎯 UltraThink修正: PNG変換を除去し、生ピクセルデータを直接取得
                var rect = new GdiRectangle(0, 0, bitmap.Width, bitmap.Height);
                var bitmapData = bitmap.LockBits(rect, ImageLockMode.ReadOnly, bitmap.PixelFormat);

                try
                {
                    // ピクセルデータサイズ計算
                    var stride = Math.Abs(bitmapData.Stride);
                    var pixelDataSize = stride * bitmap.Height;

                    // ArrayPool<byte>からバッファを借用
                    var arrayPool = ArrayPool<byte>.Shared;
                    var rentedBuffer = arrayPool.Rent(pixelDataSize);

                    // 生ピクセルデータを直接コピー（高品質・高速）
                    unsafe
                    {
                        var srcPtr = (byte*)bitmapData.Scan0;
                        fixed (byte* dstPtr = rentedBuffer)
                        {
                            System.Runtime.CompilerServices.Unsafe.CopyBlock(dstPtr, srcPtr, (uint)pixelDataSize);
                        }
                    }

                    // PixelFormatをImagePixelFormatに変換
                    var pixelFormat = ConvertToImagePixelFormat(bitmap.PixelFormat, _logger);

                    // SafeImageを生成（生ピクセルデータ使用）
                    var safeImage = _safeImageFactory.CreateSafeImage(
                        rentedBuffer: rentedBuffer,
                        arrayPool: arrayPool,
                        actualDataLength: pixelDataSize,
                        width: bitmap.Width,
                        height: bitmap.Height,
                        pixelFormat: pixelFormat,
                        id: Guid.NewGuid(),
                        stride: stride);  // 🔥 [PHASE12.1] GDI+ Stride値を明示的に渡す

                    _logger?.LogDebug("✅ UltraThink修正: SafeImage高品質生成完了 - {Width}x{Height}, Format={Format}, RawPixelSize={Size}bytes",
                        bitmap.Width, bitmap.Height, pixelFormat, pixelDataSize);

                    return safeImage;
                }
                finally
                {
                    bitmap.UnlockBits(bitmapData);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "💥 BitmapからSafeImage生成に失敗: {Width}x{Height}, Format={Format}",
                    bitmap.Width, bitmap.Height, bitmap.PixelFormat);
                throw new InvalidOperationException($"BitmapからSafeImageの生成に失敗しました: {bitmap.Width}x{bitmap.Height}", ex);
            }
        }

        /// <summary>
        /// PixelFormatをImagePixelFormatに変換
        /// </summary>
        /// <param name="format">変換元PixelFormat</param>
        /// <param name="logger">ロガー（予期しないフォーマット検出時の警告用）</param>
        /// <returns>変換されたImagePixelFormat</returns>
        private static SafePixelFormat ConvertToImagePixelFormat(GdiPixelFormat format, ILogger? logger = null)
        {
            return format switch
            {
                GdiPixelFormat.Format32bppArgb => SafePixelFormat.Bgra32,
                GdiPixelFormat.Format24bppRgb => SafePixelFormat.Rgb24,
                GdiPixelFormat.Format8bppIndexed => SafePixelFormat.Gray8,
                _ => HandleUnexpectedPixelFormat(format, logger)
            };
        }

        /// <summary>
        /// 予期しないPixelFormatを処理し、警告ログを出力
        /// </summary>
        /// <param name="format">予期しないPixelFormat</param>
        /// <param name="logger">ロガー</param>
        /// <returns>デフォルトのピクセルフォーマット</returns>
        private static SafePixelFormat HandleUnexpectedPixelFormat(GdiPixelFormat format, ILogger? logger)
        {
            logger?.LogWarning("予期しないPixelFormat {PixelFormat} が検出されました。デフォルトのBgra32にフォールバックします。", format);
            return SafePixelFormat.Bgra32;
        }
    }
