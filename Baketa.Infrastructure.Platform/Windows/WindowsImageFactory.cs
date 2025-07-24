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
            try
            {
                var sourceBitmap = ((WindowsImage)source).GetBitmap();
                var resizedBitmap = new Bitmap(width, height);

                using var graphics = Graphics.FromImage(resizedBitmap);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;

                graphics.DrawImage(sourceBitmap, 0, 0, width, height);

                stopwatch.Stop();
                _logger?.LogInformation("画像リサイズ完了: {OriginalSize} → {NewSize}, 処理時間={ElapsedMs}ms",
                    $"{sourceBitmap.Width}x{sourceBitmap.Height}", $"{width}x{height}", stopwatch.ElapsedMilliseconds);

                return new WindowsImage(resizedBitmap);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogError(ex, "画像リサイズ失敗: {TargetSize}, 処理時間={ElapsedMs}ms", 
                    $"{width}x{height}", stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像のリサイズに失敗しました: {width}x{height}", ex);
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
            try
            {
                var sourceBitmap = ((WindowsImage)source).GetBitmap();
                
                // 境界チェック
                if (cropArea.X < 0 || cropArea.Y < 0 ||
                    cropArea.Right > sourceBitmap.Width || cropArea.Bottom > sourceBitmap.Height)
                {
                    throw new ArgumentException($"切り出し領域が画像の境界を超えています: {cropArea}, 画像サイズ: {sourceBitmap.Width}x{sourceBitmap.Height}");
                }

                var croppedBitmap = new Bitmap(cropArea.Width, cropArea.Height);

                using var graphics = Graphics.FromImage(croppedBitmap);
                graphics.DrawImage(sourceBitmap, 0, 0, cropArea, GraphicsUnit.Pixel);

                stopwatch.Stop();
                _logger?.LogInformation("画像切り出し完了: 領域={CropArea} (元画像: {OriginalSize}), 処理時間={ElapsedMs}ms",
                    cropArea, $"{sourceBitmap.Width}x{sourceBitmap.Height}", stopwatch.ElapsedMilliseconds);

                return new WindowsImage(croppedBitmap);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger?.LogError(ex, "画像切り出し失敗: 領域={CropArea}, 処理時間={ElapsedMs}ms", 
                    cropArea, stopwatch.ElapsedMilliseconds);
                throw new InvalidOperationException($"画像の切り出しに失敗しました: {cropArea}", ex);
            }
        }
    }
