using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using IImageFactoryInterface = Baketa.Core.Abstractions.Factories.IImageFactory;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;
using Baketa.Core.Abstractions.Platform.Windows;
using Baketa.Infrastructure.Platform.Windows;

namespace Baketa.Infrastructure.Platform.Adapters
{
    // 注：IImageFactoryインターフェースは Baketa.Core.Abstractions.Factories.IImageFactory に移動しました

    /// <summary>
    /// WindowsImageAdapterFactory - Windows画像をIImageに変換するファクトリー
    /// </summary>
    public class WindowsImageAdapterFactory(IWindowsImageFactoryInterface windowsImageFactory) : IImageFactoryInterface
    {
        private readonly IWindowsImageFactoryInterface _windowsImageFactory = windowsImageFactory ?? throw new ArgumentNullException(nameof(windowsImageFactory));

        /// <summary>
        /// ファイルから画像を作成します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>作成された画像</returns>
        public async Task<IImage> CreateFromFileAsync(string filePath)
        {
            var windowsImage = await _windowsImageFactory.CreateFromFileAsync(filePath);
            return new WindowsImageAdapter(windowsImage);
        }
        
        /// <summary>
        /// バイト配列から画像を作成します。
        /// </summary>
        /// <param name="imageData">画像データ</param>
        /// <returns>作成された画像</returns>
        public async Task<IImage> CreateFromBytesAsync(byte[] imageData)
        {
            var windowsImage = await _windowsImageFactory.CreateFromBytesAsync(imageData);
            return new WindowsImageAdapter(windowsImage);
        }
        
        /// <summary>
        /// ストリームから画像を作成します。
        /// </summary>
        /// <param name="stream">画像データを含むストリーム</param>
        /// <returns>作成された画像</returns>
        public async Task<IImage> CreateFromStreamAsync(Stream stream)
        {
            // ストリームをバイト配列に変換
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream);
            var data = memoryStream.ToArray();
            
            return await CreateFromBytesAsync(data);
        }
        
        /// <summary>
        /// 指定されたサイズの空の画像を作成します。
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>作成された画像</returns>
        public async Task<IImage> CreateEmptyAsync(int width, int height)
        {
            var windowsImage = await _windowsImageFactory.CreateEmptyAsync(width, height);
            return new WindowsImageAdapter(windowsImage);
        }
        
        /// <summary>
        /// 高度な画像処理機能を持つ画像インスタンスに変換します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>高度な画像処理機能を持つ画像インスタンス</returns>
        public IAdvancedImage ConvertToAdvancedImage(IImage image)
        {
            // 注：ここでは実装されていません
            throw new NotImplementedException("このメソッドはまだ実装されていません");
        }
    }

    /// <summary>
    /// テスト用のIImageファクトリ実装
    /// </summary>
    public class TestImageAdapterFactory : IImageFactoryInterface
    {
        /// <summary>
        /// ファイルから画像を作成します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>作成された画像</returns>
        public Task<IImage> CreateFromFileAsync(string filePath)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        /// <summary>
        /// バイト配列から画像を作成します。
        /// </summary>
        /// <param name="imageData">画像データ</param>
        /// <returns>作成された画像</returns>
        public Task<IImage> CreateFromBytesAsync(byte[] imageData)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        /// <summary>
        /// ストリームから画像を作成します。
        /// </summary>
        /// <param name="stream">画像データを含むストリーム</param>
        /// <returns>作成された画像</returns>
        public Task<IImage> CreateFromStreamAsync(Stream stream)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        /// <summary>
        /// 指定されたサイズの空の画像を作成します。
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>作成された画像</returns>
        public Task<IImage> CreateEmptyAsync(int width, int height)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }

        /// <summary>
        /// 高度な画像処理機能を持つ画像インスタンスに変換します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>高度な画像処理機能を持つ画像インスタンス</returns>
        public IAdvancedImage ConvertToAdvancedImage(IImage image)
        {
            // テスト実装
            throw new NotImplementedException("テスト用ファクトリは実装が必要です");
        }
    }
}