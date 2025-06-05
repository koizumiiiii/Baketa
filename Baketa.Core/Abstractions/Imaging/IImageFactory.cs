using System;
using System.IO;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging;

    /// <summary>
    /// 画像オブジェクトの生成を担当するファクトリーインターフェース
    /// </summary>
    [Obsolete("このインターフェースは非推奨です。代わりに Baketa.Core.Abstractions.Factories.IImageFactory を使用してください。")]
    public interface IImageFactory
    {
        /// <summary>
        /// ファイルから画像を作成します。
        /// </summary>
        /// <param name="filePath">画像ファイルパス</param>
        /// <returns>作成された画像</returns>
        Task<IImage> CreateFromFileAsync(string filePath);
        
        /// <summary>
        /// バイト配列から画像を作成します。
        /// </summary>
        /// <param name="imageData">画像データ</param>
        /// <returns>作成された画像</returns>
        Task<IImage> CreateFromBytesAsync(byte[] imageData);
        
        /// <summary>
        /// ストリームから画像を作成します。
        /// </summary>
        /// <param name="stream">画像データを含むストリーム</param>
        /// <returns>作成された画像</returns>
        Task<IImage> CreateFromStreamAsync(Stream stream);
        
        /// <summary>
        /// 指定されたサイズの空の画像を作成します。
        /// </summary>
        /// <param name="width">画像の幅</param>
        /// <param name="height">画像の高さ</param>
        /// <returns>作成された画像</returns>
        Task<IImage> CreateEmptyAsync(int width, int height);
        
        /// <summary>
        /// 高度な画像処理機能を持つ画像インスタンスに変換します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>高度な画像処理機能を持つ画像インスタンス</returns>
        IAdvancedImage ConvertToAdvancedImage(IImage image);
    }
