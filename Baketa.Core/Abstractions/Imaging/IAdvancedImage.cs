using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 高度な画像処理機能を提供するインターフェース
    /// </summary>
    public interface IAdvancedImage : IImage
    {
        /// <summary>
        /// 画像にフィルターを適用します。
        /// </summary>
        /// <param name="filterType">適用するフィルタータイプ</param>
        /// <returns>フィルターが適用された新しい画像インスタンス</returns>
        Task<IImage> ApplyFilterAsync(ImageFilterType filterType);
        
        /// <summary>
        /// 2つの画像の類似度を計算します。
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        Task<float> CalculateSimilarityAsync(IImage other);
        
        /// <summary>
        /// 画像の特定領域を切り出します。
        /// </summary>
        /// <param name="x">切り出し開始X座標</param>
        /// <param name="y">切り出し開始Y座標</param>
        /// <param name="width">切り出し幅</param>
        /// <param name="height">切り出し高さ</param>
        /// <returns>切り出された新しい画像インスタンス</returns>
        Task<IImage> CropAsync(int x, int y, int width, int height);
        
        /// <summary>
        /// 画像の回転を行います。
        /// </summary>
        /// <param name="degrees">回転角度（度数法）</param>
        /// <returns>回転された新しい画像インスタンス</returns>
        Task<IImage> RotateAsync(float degrees);
    }
    
    /// <summary>
    /// 画像フィルタータイプ
    /// </summary>
    public enum ImageFilterType
    {
        /// <summary>グレースケール</summary>
        Grayscale,
        
        /// <summary>ぼかし</summary>
        Blur,
        
        /// <summary>シャープ化</summary>
        Sharpen,
        
        /// <summary>エッジ検出</summary>
        EdgeDetection,
        
        /// <summary>コントラスト強調</summary>
        ContrastEnhancement,
        
        /// <summary>二値化</summary>
        Binarize,
        
        /// <summary>ガンマ補正</summary>
        GammaCorrection
    }
}