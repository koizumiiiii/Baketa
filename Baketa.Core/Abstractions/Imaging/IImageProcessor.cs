using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 画像処理機能を提供するインターフェース
    /// </summary>
    public interface IImageProcessor
    {
        /// <summary>
        /// OCR処理のための画像を最適化します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>OCR用に最適化された画像</returns>
        Task<IImage> OptimizeForOcrAsync(IImage image);
        
        /// <summary>
        /// 画像のノイズを除去します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <param name="strength">ノイズ除去の強度（0.0〜1.0）</param>
        /// <returns>ノイズが除去された画像</returns>
        Task<IImage> RemoveNoiseAsync(IImage image, float strength = 0.5f);
        
        /// <summary>
        /// 画像のコントラストを調整します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <param name="factor">コントラスト調整係数（1.0が元の画像、大きいほど強調）</param>
        /// <returns>コントラストが調整された画像</returns>
        Task<IImage> AdjustContrastAsync(IImage image, float factor);
        
        /// <summary>
        /// 画像の明るさを調整します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <param name="factor">明るさ調整係数（0.0〜2.0、1.0が元の画像）</param>
        /// <returns>明るさが調整された画像</returns>
        Task<IImage> AdjustBrightnessAsync(IImage image, float factor);
        
        /// <summary>
        /// 画像のテキスト領域を検出します。
        /// </summary>
        /// <param name="image">元の画像</param>
        /// <returns>検出されたテキスト領域の配列</returns>
        Task<TextRegion[]> DetectTextRegionsAsync(IImage image);
    }
    
    /// <summary>
    /// テキスト領域を表す構造体
    /// </summary>
    public readonly struct TextRegion(int x, int y, int width, int height, float confidence)
    {
        /// <summary>
        /// X座標
        /// </summary>
        public int X { get; } = x;
        
        /// <summary>
        /// Y座標
        /// </summary>
        public int Y { get; } = y;
        
        /// <summary>
        /// 幅
        /// </summary>
        public int Width { get; } = width;
        
        /// <summary>
        /// 高さ
        /// </summary>
        public int Height { get; } = height;
        
        /// <summary>
        /// テキスト領域である確率（0.0〜1.0）
        /// </summary>
        public float Confidence { get; } = confidence;
    }
}