using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

    /// <summary>
    /// 画像処理パイプラインを表すインターフェース
    /// </summary>
    public interface IImagePipeline
    {
        /// <summary>
        /// パイプラインに処理ステップを追加します
        /// </summary>
        /// <param name="pipelineStep">追加するパイプラインステップ</param>
        /// <returns>自身のインスタンス（メソッドチェーン用）</returns>
        IImagePipeline AddStep(IImagePipelineStep pipelineStep);
        
        /// <summary>
        /// 指定されたインデックスのステップを削除します
        /// </summary>
        /// <param name="index">削除するステップのインデックス</param>
        /// <returns>削除に成功した場合はtrue、そうでない場合はfalse</returns>
        bool RemoveStep(int index);
        
        /// <summary>
        /// 指定されたステップを削除します
        /// </summary>
        /// <param name="pipelineStep">削除するステップ</param>
        /// <returns>削除に成功した場合はtrue、そうでない場合はfalse</returns>
        bool RemoveStep(IImagePipelineStep pipelineStep);
        
        /// <summary>
        /// パイプラインのステップをクリアします
        /// </summary>
        void ClearSteps();
        
        /// <summary>
        /// 指定されたインデックスのステップを取得します
        /// </summary>
        /// <param name="index">取得するステップのインデックス</param>
        /// <returns>パイプラインステップ</returns>
        IImagePipelineStep GetStep(int index);
        
        /// <summary>
        /// 指定された名前のステップを取得します
        /// </summary>
        /// <param name="name">取得するステップの名前</param>
        /// <returns>パイプラインステップ、見つからない場合はnull</returns>
        IImagePipelineStep? GetStepByName(string name);
        
        /// <summary>
        /// パイプライン内のステップの数を取得します
        /// </summary>
        int StepCount { get; }
        
        /// <summary>
        /// すべてのステップを取得します
        /// </summary>
        IReadOnlyList<IImagePipelineStep> Steps { get; }
        
        /// <summary>
        /// パイプラインを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果と中間結果を含むパイプライン実行結果</returns>
        Task<PipelineResult> ExecuteAsync(IAdvancedImage input, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// パイプライン構成を名前付きプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">保存するプロファイル名</param>
        /// <returns>非同期タスク</returns>
        Task SaveProfileAsync(string profileName);
        
        /// <summary>
        /// 名前付きプロファイルからパイプライン構成を読み込みます
        /// </summary>
        /// <param name="profileName">読み込むプロファイル名</param>
        /// <returns>読み込まれたパイプライン</returns>
        Task<IImagePipeline> LoadProfileAsync(string profileName);
        
        /// <summary>
        /// 中間結果の保存モードを設定します
        /// </summary>
        IntermediateResultMode IntermediateResultMode { get; set; }
        
        /// <summary>
        /// パイプライン全体のエラーハンドリング戦略
        /// </summary>
        StepErrorHandlingStrategy GlobalErrorHandlingStrategy { get; set; }
        
        /// <summary>
        /// パイプラインイベントのリスナーを設定します
        /// </summary>
        IPipelineEventListener EventListener { get; set; }
    }
