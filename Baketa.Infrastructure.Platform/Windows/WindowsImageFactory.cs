using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using IWindowsImageInterface = Baketa.Core.Abstractions.Platform.Windows.IWindowsImage;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Windows;


    /// <summary>
    /// WindowsImage作成のファクトリ実装
    /// </summary>
    public class WindowsImageFactory : IWindowsImageFactoryInterface
    {
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
    }
