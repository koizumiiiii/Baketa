using System.Collections.Generic;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// パイプラインで使用する画像フィルターのインターフェース
    /// IImagePipelineStepを拡張してフィルター固有の機能を提供します
    /// </summary>
    public interface IImagePipelineFilter : IImagePipelineStep
    {
        /// <summary>
        /// フィルターのカテゴリ
        /// </summary>
        string Category { get; }
        
        /// <summary>
        /// パラメータ一覧を取得
        /// </summary>
        /// <returns>パラメータと値のディクショナリ</returns>
        IDictionary<string, object> GetParameters();
        
        /// <summary>
        /// 非同期でフィルターを適用
        /// </summary>
        /// <param name="inputImage">入力画像</param>
        /// <returns>処理後の画像</returns>
        Task<IAdvancedImage> ApplyAsync(IAdvancedImage inputImage);
    }
}