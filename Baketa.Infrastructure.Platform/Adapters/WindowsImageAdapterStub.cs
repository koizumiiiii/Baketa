using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Platform.Windows;
using IWindowsImageFactoryInterface = Baketa.Core.Abstractions.Factories.IWindowsImageFactory;

namespace Baketa.Infrastructure.Platform.Adapters;

    /// <summary>
    /// IWindowsImageAdapterインターフェースの基本スタブ実装
    /// 注：実際の機能実装は後の段階で行います
    /// </summary>
    public class WindowsImageAdapterStub(IWindowsImageFactoryInterface? imageFactory = null) : IWindowsImageAdapter
    {        
        // ファクトリーインスタンスを保存（将来の拡張用）
#pragma warning disable CA1823 // 未使用のプライベートフィールドは使用予定があるため抑制
        private readonly IWindowsImageFactoryInterface? _imageFactory = imageFactory;
#pragma warning restore CA1823
        
        /// <summary>
        /// Windowsネイティブイメージ(IWindowsImage)をコアイメージ(IAdvancedImage)に変換します
        /// </summary>
        /// <param name="windowsImage">変換元のWindowsイメージ</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">windowsImageがnullの場合</exception>
        public IAdvancedImage ToAdvancedImage(IWindowsImage windowsImage)
        {
            ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
            
            // スタブ実装では既存のWindowsImageAdapterを利用
            return new WindowsImageAdapter(windowsImage);
        }

        /// <summary>
        /// Windowsネイティブイメージ(IWindowsImage)をコアイメージ(IImage)に変換します
        /// </summary>
        /// <param name="windowsImage">変換元のWindowsイメージ</param>
        /// <returns>変換後のImage</returns>
        /// <exception cref="ArgumentNullException">windowsImageがnullの場合</exception>
        public IImage ToImage(IWindowsImage windowsImage)
        {
            ArgumentNullException.ThrowIfNull(windowsImage, nameof(windowsImage));
            
            // IAdvancedImageはIImageを継承しているので、ToAdvancedImageの結果をそのまま返す
            return ToAdvancedImage(windowsImage);
        }

        /// <summary>
        /// コアイメージ(IAdvancedImage)をWindowsネイティブイメージ(IWindowsImage)に変換します
        /// </summary>
        /// <param name="advancedImage">変換元のAdvancedImage</param>
        /// <returns>変換後のWindowsイメージ</returns>
        /// <exception cref="ArgumentNullException">advancedImageがnullの場合</exception>
        /// <exception cref="InvalidOperationException">変換できない場合</exception>
        public async Task<IWindowsImage> FromAdvancedImageAsync(IAdvancedImage advancedImage)
        {
            ArgumentNullException.ThrowIfNull(advancedImage, nameof(advancedImage));
            
            // スタブ実装では単純にバイト配列を経由して変換
            var imageBytes = await advancedImage.ToByteArrayAsync().ConfigureAwait(false);
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            
            // 所有権移転のためのクローン作成
            var persistentBitmap = (Bitmap)bitmap.Clone();
            return new Windows.WindowsImage(persistentBitmap);
        }

        /// <summary>
        /// コアイメージ(IImage)をWindowsネイティブイメージ(IWindowsImage)に変換します
        /// </summary>
        /// <param name="image">変換元のImage</param>
        /// <returns>変換後のWindowsイメージ</returns>
        /// <exception cref="ArgumentNullException">imageがnullの場合</exception>
        /// <exception cref="InvalidOperationException">変換できない場合</exception>
        public async Task<IWindowsImage> FromImageAsync(IImage image)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(image));
            
            // IAdvancedImageの場合は特化したメソッドを使用
            if (image is IAdvancedImage advancedImage)
            {
                return await FromAdvancedImageAsync(advancedImage).ConfigureAwait(false);
            }
            
            // それ以外はバイト配列を経由して変換
            var imageBytes = await image.ToByteArrayAsync().ConfigureAwait(false);
            using var stream = new MemoryStream(imageBytes);
            using var bitmap = new Bitmap(stream);
            
            // 所有権移転のためのクローン作成
            var persistentBitmap = (Bitmap)bitmap.Clone();
            return new Windows.WindowsImage(persistentBitmap);
        }

        /// <summary>
        /// Bitmapからコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="bitmap">変換元のBitmap</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">bitmapがnullの場合</exception>
        public IAdvancedImage CreateAdvancedImageFromBitmap(Bitmap bitmap)
        {
            ArgumentNullException.ThrowIfNull(bitmap, nameof(bitmap));
            
            // BitmapをWindowsImageに変換し、それをAdvancedImageに変換
            var windowsImage = new Windows.WindowsImage((Bitmap)bitmap.Clone());
            return ToAdvancedImage(windowsImage);
        }

        /// <summary>
        /// バイト配列からコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="imageData">画像データのバイト配列</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">imageDataがnullの場合</exception>
        /// <exception cref="ArgumentException">無効な画像データの場合</exception>
        public Task<IAdvancedImage> CreateAdvancedImageFromBytesAsync(byte[] imageData)
        {
            ArgumentNullException.ThrowIfNull(imageData, nameof(imageData));
            
            try
            {
                using var stream = new MemoryStream(imageData);
                using var bitmap = new Bitmap(stream);
                
                // 所有権移転のためのクローン作成
                var persistentBitmap = (Bitmap)bitmap.Clone();
                var windowsImage = new Windows.WindowsImage(persistentBitmap);
                
                var result = ToAdvancedImage(windowsImage);
                return Task.FromResult(result);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException("無効な画像データです", nameof(imageData), ex);
            }
        }

        /// <summary>
        /// ファイルからコアイメージ(IAdvancedImage)を作成します
        /// </summary>
        /// <param name="filePath">画像ファイルのパス</param>
        /// <returns>変換後のAdvancedImage</returns>
        /// <exception cref="ArgumentNullException">filePathがnullの場合</exception>
        /// <exception cref="System.IO.FileNotFoundException">ファイルが存在しない場合</exception>
        /// <exception cref="ArgumentException">無効な画像データの場合</exception>
        /// <exception cref="IOException">ファイル読み込み時にエラーが発生した場合</exception>
        public async Task<IAdvancedImage> CreateAdvancedImageFromFileAsync(string filePath)
        {            
            ArgumentNullException.ThrowIfNull(filePath, nameof(filePath));
            
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("指定されたファイルが見つかりません", filePath);
            }

            try
            {
                // ファイルをバイト配列として読み込み
                var imageData = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
                return await CreateAdvancedImageFromBytesAsync(imageData).ConfigureAwait(false);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"ファイル '{filePath}' は有効な画像ではありません", nameof(filePath), ex);
            }
        }
    }
