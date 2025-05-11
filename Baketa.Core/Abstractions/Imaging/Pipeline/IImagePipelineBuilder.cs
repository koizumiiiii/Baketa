using Baketa.Core.Abstractions.Imaging.Pipeline.Settings;

namespace Baketa.Core.Abstractions.Imaging.Pipeline
{
    /// <summary>
    /// パイプラインビルダーインターフェース
    /// </summary>
    public interface IImagePipelineBuilder
    {
        /// <summary>
        /// パイプラインに名前を設定します
        /// </summary>
        /// <param name="name">パイプライン名</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithName(string name);
        
        /// <summary>
        /// パイプラインに説明を設定します
        /// </summary>
        /// <param name="description">パイプライン説明</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithDescription(string description);
        
        /// <summary>
        /// パイプラインにフィルターを追加します
        /// </summary>
        /// <param name="filter">追加するフィルター</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder AddFilter(IImageFilter filter);
        
        /// <summary>
        /// パイプラインから指定位置のフィルターを削除します
        /// </summary>
        /// <param name="index">削除するフィルターのインデックス</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder RemoveFilterAt(int index);
        
        /// <summary>
        /// パイプラインの全フィルターをクリアします
        /// </summary>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder ClearFilters();
        
        /// <summary>
        /// パイプラインの中間結果モードを設定します
        /// </summary>
        /// <param name="mode">中間結果モード</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithIntermediateResultMode(IntermediateResultMode mode);
        
        /// <summary>
        /// パイプラインのエラーハンドリング戦略を設定します
        /// </summary>
        /// <param name="strategy">エラーハンドリング戦略</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder WithErrorHandlingStrategy(StepErrorHandlingStrategy strategy);
        
        /// <summary>
        /// 設定からパイプラインを構築します
        /// </summary>
        /// <param name="settings">パイプライン設定</param>
        /// <returns>ビルダー</returns>
        IImagePipelineBuilder FromSettings(ImagePipelineSettings settings);
        
        /// <summary>
        /// パイプラインを構築します
        /// </summary>
        /// <returns>構築されたパイプライン</returns>
        IImagePipeline Build();
    }
}