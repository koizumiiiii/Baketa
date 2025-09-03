using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using IWindowsImageInterface = Baketa.Core.Abstractions.Platform.Windows.IWindowsImage;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Windows;


    /// <summary>
    /// WindowsImage作成のファクトリ実装
    /// </summary>
    public class WindowsImageFactory : IWindowsImageFactoryInterface
    {
        private readonly ILogger<WindowsImageFactory>? _logger;
        private static readonly object _gdiLock = new(); // GDI+操作の同期化

        public WindowsImageFactory(ILogger<WindowsImageFactory>? logger = null)
        {
            _logger = logger;
        }
        /// <summary>
        /// Bitmapからの画像作成
        /// </summary>
        public IWindowsImageInterface CreateFromBitmap(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            return new WindowsImage(bitmap);
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
                    return new WindowsImage(bitmap);
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
                    return new WindowsImage(bitmap);
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
                
                return new WindowsImage(bitmap);
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
                    var sourceBitmap = ((WindowsImage)source).GetBitmap();
                    // 🔒 CRITICAL FIX: Bitmap競合状態防止のためクローン作成
                    sourceBitmapClone = new Bitmap(sourceBitmap);
                }

                resizedBitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

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
                    $"{sourceBitmapClone.Width}x{sourceBitmapClone.Height}", $"{width}x{height}", stopwatch.ElapsedMilliseconds, System.Threading.Thread.CurrentThread.ManagedThreadId);

                var result = new WindowsImage(resizedBitmap);
                resizedBitmap = null; // WindowsImageが所有権を取得
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
                    $"{width}x{height}", stopwatch.ElapsedMilliseconds, System.Threading.Thread.CurrentThread.ManagedThreadId);
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
        public IWindowsImageInterface CropImage(IWindowsImageInterface source, Rectangle cropArea)
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
                    var sourceBitmap = ((WindowsImage)source).GetBitmap();
                    
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
                croppedBitmap = new Bitmap(cropArea.Width, cropArea.Height, PixelFormat.Format32bppArgb);
                
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
                    cropArea, $"{sourceBitmapClone.Width}x{sourceBitmapClone.Height}", stopwatch.ElapsedMilliseconds, System.Threading.Thread.CurrentThread.ManagedThreadId);

                var result = new WindowsImage(croppedBitmap);
                croppedBitmap = null; // WindowsImageが所有権を取得
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
                    cropArea, stopwatch.ElapsedMilliseconds, System.Threading.Thread.CurrentThread.ManagedThreadId);
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
    }
