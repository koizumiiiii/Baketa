using System;
using System.Drawing;
using Baketa.Core.Interfaces.Image;
using Baketa.Infrastructure.Platform.Abstractions;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    /// <summary>
    /// IImageファクトリインターフェース
    /// </summary>
    public interface IImageFactory
    {
        /// <summary>
        /// ファイルパスから画像を作成
        /// </summary>
        IImage CreateFromFile(string filePath);

        /// <summary>
        /// バイト配列から画像を作成
        /// </summary>
        IImage CreateFromBytes(byte[] imageData);

        /// <summary>
        /// 指定サイズの空の画像を作成
        /// </summary>
        IImage CreateEmpty(int width, int height);
    }

    /// <summary>
    /// WindowsImageAdapterFactory - Windows画像をIImageに変換するファクトリー
    /// </summary>
    public class WindowsImageAdapterFactory(IWindowsImageFactory windowsImageFactory) : IImageFactory
    {
        private readonly IWindowsImageFactory _windowsImageFactory = windowsImageFactory ?? throw new ArgumentNullException(nameof(windowsImageFactory));

        /// <summary>
        /// ファイルパスから画像を作成
        /// </summary>
        public IImage CreateFromFile(string filePath)
        {
            var windowsImage = _windowsImageFactory.CreateFromFile(filePath);
            return new WindowsImageAdapter(windowsImage);
        }

        /// <summary>
        /// バイト配列から画像を作成
        /// </summary>
        public IImage CreateFromBytes(byte[] imageData)
        {
            var windowsImage = _windowsImageFactory.CreateFromBytes(imageData);
            return new WindowsImageAdapter(windowsImage);
        }

        /// <summary>
        /// 指定サイズの空の画像を作成
        /// </summary>
        public IImage CreateEmpty(int width, int height)
        {
            var bitmap = new Bitmap(width, height);
            var windowsImage = _windowsImageFactory.CreateFromBitmap(bitmap);
            return new WindowsImageAdapter(windowsImage);
        }
    }

    /// <summary>
    /// テスト用のIImageファクトリ実装
    /// </summary>
    public class TestImageAdapterFactory : IImageFactory
    {
        public IImage CreateFromFile(string filePath)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        public IImage CreateFromBytes(byte[] imageData)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        public IImage CreateEmpty(int width, int height)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }
    }
}