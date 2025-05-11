using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// パイプラインで使用する画像フィルターを表すインターフェース
    /// IImagePipelineStepを継承し、画像処理パイプラインで使用可能な処理を定義します
    /// 既存のIImageFilterとの衝突を避けるためにIPipelineImageFilterという名前で実装しています
    /// </summary>
    public interface IPipelineImageFilter : IImagePipelineStep
    {
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        string Category { get; }
        
        /// <summary>
        /// パラメータの辞書
        /// </summary>
        new IReadOnlyDictionary<string, object> Parameters { get; }
        
        /// <summary>
        /// フィルターを適用します
        /// </summary>
        /// <param name="image">入力画像</param>
        /// <returns>処理結果画像</returns>
        Task<IAdvancedImage> ApplyAsync(IAdvancedImage image);
        
        /// <summary>
        /// すべてのパラメータを取得します
        /// </summary>
        /// <returns>パラメータのディクショナリ</returns>
        IReadOnlyDictionary<string, object> GetParameters();
    }
}