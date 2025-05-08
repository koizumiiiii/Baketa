using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging.Filters;

namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// OCR最適化パイプラインを構築するビルダーインターフェース
    /// </summary>
    public interface IOcrPipelineBuilder
    {
        /// <summary>
        /// 標準的なOCRパイプラインを構築します
        /// </summary>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline BuildStandardPipeline();
        
        /// <summary>
        /// 最小限のOCRパイプラインを構築します
        /// </summary>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline BuildMinimalPipeline();
        
        /// <summary>
        /// エッジ検出に基づくOCRパイプラインを構築します
        /// </summary>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline BuildEdgeBasedPipeline();
        
        /// <summary>
        /// カスタムOCRパイプラインを構築します
        /// </summary>
        /// <param name="filterTypes">使用するフィルタータイプの配列</param>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline BuildCustomPipeline(params OcrFilterType[] filterTypes);
        
        /// <summary>
        /// 名前付きプロファイルからOCRパイプラインを読み込みます
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>読み込まれたパイプライン</returns>
        Task<IImagePipeline> LoadPipelineFromProfileAsync(string profileName);
        
        /// <summary>
        /// 現在のOCRパイプラインを名前付きプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">プロファイル名</param>
        /// <returns>非同期タスク</returns>
        Task SavePipelineToProfileAsync(string profileName);
    }
}
