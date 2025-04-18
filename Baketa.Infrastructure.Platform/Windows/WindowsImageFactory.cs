using System;
using System.Drawing;
using Baketa.Infrastructure.Platform.Abstractions;

namespace Baketa.Infrastructure.Platform.Windows
{
    /// <summary>
    /// WindowsImage作成のためのファクトリインターフェース
    /// </summary>
    public interface IWindowsImageFactory
    {
        /// <summary>
        /// Bitmapからの画像作成
        /// </summary>
        IWindowsImage CreateFromBitmap(Bitmap bitmap);

        /// <summary>
        /// ファイルパスからの画像作成
        /// </summary>
        IWindowsImage CreateFromFile(string filePath);

        /// <summary>
        /// バイト配列からの画像作成
        /// </summary>
        IWindowsImage CreateFromBytes(byte[] imageData);
    }

    /// <summary>
    /// WindowsImage作成のファクトリ実装
    /// </summary>
    public class WindowsImageFactory : IWindowsImageFactory
    {
        /// <summary>
        /// Bitmapからの画像作成
        /// </summary>
        public IWindowsImage CreateFromBitmap(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap);
            return new WindowsImage(bitmap);
        }

        /// <summary>
        /// ファイルパスからの画像作成
        /// </summary>
        public IWindowsImage CreateFromFile(string filePath)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath, nameof(filePath));

            try
            {
                var bitmap = new Bitmap(filePath);
                return new WindowsImage(bitmap);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"画像ファイルの読み込みに失敗しました: {filePath}", ex);
            }
        }

        /// <summary>
        /// バイト配列からの画像作成
        /// </summary>
        public IWindowsImage CreateFromBytes(byte[] imageData)
        {
            ArgumentNullException.ThrowIfNull(imageData);
            if (imageData.Length == 0)
                throw new ArgumentException("画像データが空です", nameof(imageData));

            try
            {
                using var stream = new System.IO.MemoryStream(imageData);
                var bitmap = new Bitmap(stream);
                return new WindowsImage(bitmap);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("バイトデータからの画像作成に失敗しました", ex);
            }
        }
    }
}