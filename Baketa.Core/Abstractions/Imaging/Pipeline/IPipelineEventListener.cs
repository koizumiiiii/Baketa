using System;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

    /// <summary>
    /// パイプラインイベントリスナーを表すインターフェース
    /// </summary>
    public interface IPipelineEventListener
    {
        /// <summary>
        /// パイプライン実行開始時に呼び出されます
        /// </summary>
        /// <param name="pipeline">実行されるパイプライン</param>
        /// <param name="input">入力画像</param>
        /// <returns>非同期タスク</returns>
        Task OnPipelineStartAsync(IImagePipeline pipeline, IAdvancedImage input);
        
        /// <summary>
        /// パイプライン実行完了時に呼び出されます
        /// </summary>
        /// <param name="pipeline">実行されたパイプライン</param>
        /// <param name="result">パイプライン実行結果</param>
        /// <returns>非同期タスク</returns>
        Task OnPipelineCompleteAsync(IImagePipeline pipeline, PipelineResult result);
        
        /// <summary>
        /// ステップ実行開始時に呼び出されます
        /// </summary>
        /// <param name="pipelineStep">実行されるステップ</param>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>非同期タスク</returns>
        Task OnStepStartAsync(IImagePipelineStep pipelineStep, IAdvancedImage input, PipelineContext context);
        
        /// <summary>
        /// ステップ実行完了時に呼び出されます
        /// </summary>
        /// <param name="pipelineStep">実行されたステップ</param>
        /// <param name="output">出力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="elapsedMilliseconds">実行時間（ミリ秒）</param>
        /// <returns>非同期タスク</returns>
        Task OnStepCompleteAsync(IImagePipelineStep pipelineStep, IAdvancedImage output, PipelineContext context, long elapsedMilliseconds);
        
        /// <summary>
        /// ステップ実行エラー時に呼び出されます
        /// </summary>
        /// <param name="pipelineStep">エラーが発生したステップ</param>
        /// <param name="exception">発生した例外</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>エラー処理後の結果画像、またはnull</returns>
        Task<IAdvancedImage?> OnStepErrorAsync(IImagePipelineStep pipelineStep, Exception exception, PipelineContext context);
        
        /// <summary>
        /// パイプライン実行エラー時に呼び出されます
        /// </summary>
        /// <param name="pipeline">エラーが発生したパイプライン</param>
        /// <param name="exception">発生した例外</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>非同期タスク</returns>
        Task OnPipelineErrorAsync(IImagePipeline pipeline, Exception exception, PipelineContext context);
    }
