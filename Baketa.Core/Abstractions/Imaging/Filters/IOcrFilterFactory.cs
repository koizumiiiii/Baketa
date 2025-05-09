using System.Collections.Generic;

namespace Baketa.Core.Abstractions.Imaging.Filters
{
    /// <summary>
    /// OCR最適化フィルターの種類
    /// </summary>
    public enum OcrFilterType
    {
        /// <summary>
        /// グレースケール変換
        /// </summary>
        Grayscale,
        
        /// <summary>
        /// コントラスト強調
        /// </summary>
        ContrastEnhancement,
        
        /// <summary>
        /// ノイズ除去
        /// </summary>
        NoiseReduction,
        
        /// <summary>
        /// 二値化
        /// </summary>
        Threshold,
        
        /// <summary>
        /// モルフォロジー処理
        /// </summary>
        Morphology,
        
        /// <summary>
        /// エッジ検出
        /// </summary>
        EdgeDetection
    }

    /// <summary>
    /// OCR最適化フィルターのファクトリーインターフェース
    /// </summary>
    public interface IOcrFilterFactory
    {
        /// <summary>
        /// 指定されたタイプのOCRフィルターを作成します
        /// </summary>
        /// <param name="filterType">フィルタータイプ</param>
        /// <returns>作成されたフィルター</returns>
        Baketa.Core.Abstractions.Imaging.IImageFilter CreateFilter(OcrFilterType filterType);
        
        /// <summary>
        /// 利用可能なOCRフィルターの一覧を取得します
        /// </summary>
        /// <returns>フィルター名とタイプのディクショナリ</returns>
        IReadOnlyDictionary<string, OcrFilterType> GetAvailableFilters();
        
        /// <summary>
        /// 標準的なOCR前処理パイプラインを作成します
        /// </summary>
        /// <returns>フィルターの配列</returns>
        Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateStandardOcrPipeline();
        
        /// <summary>
        /// 最小限のOCR前処理パイプラインを作成します
        /// </summary>
        /// <returns>フィルターの配列</returns>
        Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateMinimalOcrPipeline();
        
        /// <summary>
        /// エッジ検出に基づくOCR前処理パイプラインを作成します
        /// </summary>
        /// <returns>フィルターの配列</returns>
        Baketa.Core.Abstractions.Imaging.IImageFilter[] CreateEdgeBasedOcrPipeline();
    }
}
