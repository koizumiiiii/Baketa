using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging
{
    /// <summary>
    /// 高度な画像処理機能を提供するインターフェース
    /// </summary>
    public interface IAdvancedImage : IImage
    {
        /// <summary>
        /// 指定座標のピクセル値を取得します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <returns>ピクセル値</returns>
        Color GetPixel(int x, int y);
        
        /// <summary>
        /// 指定座標にピクセル値を設定します
        /// </summary>
        /// <param name="x">X座標</param>
        /// <param name="y">Y座標</param>
        /// <param name="color">設定する色</param>
        void SetPixel(int x, int y, Color color);
        
        /// <summary>
        /// 画像にフィルターを適用します
        /// </summary>
        /// <param name="filter">適用するフィルター</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyFilterAsync(IImageFilter filter);
        
        /// <summary>
        /// 複数のフィルターを順番に適用します
        /// </summary>
        /// <param name="filters">適用するフィルターのコレクション</param>
        /// <returns>フィルター適用後の新しい画像</returns>
        Task<IAdvancedImage> ApplyFiltersAsync(IEnumerable<IImageFilter> filters);
        
        /// <summary>
        /// 画像のヒストグラムを生成します
        /// </summary>
        /// <param name="channel">対象チャンネル</param>
        /// <returns>ヒストグラムデータ</returns>
        Task<int[]> ComputeHistogramAsync(ColorChannel channel = ColorChannel.Luminance);
        
        /// <summary>
        /// 画像をグレースケールに変換します
        /// </summary>
        /// <returns>グレースケール変換された新しい画像</returns>
        Task<IAdvancedImage> ToGrayscaleAsync();
        
        /// <summary>
        /// 画像を二値化します
        /// </summary>
        /// <param name="threshold">閾値（0～255）</param>
        /// <returns>二値化された新しい画像</returns>
        Task<IAdvancedImage> ToBinaryAsync(byte threshold);
        
        /// <summary>
        /// 画像の特定領域を抽出します
        /// </summary>
        /// <param name="rectangle">抽出する領域</param>
        /// <returns>抽出された新しい画像</returns>
        Task<IAdvancedImage> ExtractRegionAsync(Rectangle rectangle);
        
        /// <summary>
        /// OCR前処理の最適化を行います
        /// </summary>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        Task<IAdvancedImage> OptimizeForOcrAsync();
        
        /// <summary>
        /// OCR前処理の最適化を指定されたオプションで行います
        /// </summary>
        /// <param name="options">最適化オプション</param>
        /// <returns>OCR向けに最適化された新しい画像</returns>
        Task<IAdvancedImage> OptimizeForOcrAsync(OcrImageOptions options);
        
        /// <summary>
        /// 2つの画像の類似度を計算します
        /// </summary>
        /// <param name="other">比較対象の画像</param>
        /// <returns>0.0〜1.0の類似度（1.0が完全一致）</returns>
        Task<float> CalculateSimilarityAsync(IImage other);
        
        /// <summary>
        /// 画像の特定領域におけるテキスト存在可能性を評価します
        /// </summary>
        /// <param name="rectangle">評価する領域</param>
        /// <returns>テキスト存在可能性（0.0〜1.0）</returns>
        Task<float> EvaluateTextProbabilityAsync(Rectangle rectangle);
        
        /// <summary>
        /// 画像の回転を行います
        /// </summary>
        /// <param name="degrees">回転角度（度数法）</param>
        /// <returns>回転された新しい画像</returns>
        Task<IAdvancedImage> RotateAsync(float degrees);
        
        /// <summary>
        /// 画像の強調処理を行います
        /// </summary>
        /// <param name="options">強調オプション</param>
        /// <returns>強調処理された新しい画像</returns>
        Task<IAdvancedImage> EnhanceAsync(ImageEnhancementOptions options);
        
        /// <summary>
        /// 画像から自動的にテキスト領域を検出します
        /// </summary>
        /// <returns>検出されたテキスト領域の矩形リスト</returns>
        Task<List<Rectangle>> DetectTextRegionsAsync();
    }
    
    /// <summary>
    /// 色チャンネルを表す列挙型
    /// </summary>
    public enum ColorChannel
    {
        /// <summary>
        /// 赤チャンネル
        /// </summary>
        Red,
        
        /// <summary>
        /// 緑チャンネル
        /// </summary>
        Green,
        
        /// <summary>
        /// 青チャンネル
        /// </summary>
        Blue,
        
        /// <summary>
        /// アルファチャンネル（透明度）
        /// </summary>
        Alpha,
        
        /// <summary>
        /// 輝度（明るさ）
        /// </summary>
        Luminance
    }
}