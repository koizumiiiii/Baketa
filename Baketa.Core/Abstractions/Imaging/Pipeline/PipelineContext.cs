using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプライン実行コンテキスト
/// </summary>
/// <remarks>
/// 新しいパイプライン実行コンテキストを作成します
/// </remarks>
/// <param name="logger">ロガー</param>
/// <param name="intermediateResultMode">中間結果の保存モード</param>
/// <param name="globalErrorHandlingStrategy">パイプライン全体のエラー処理戦略</param>
/// <param name="eventListener">イベントリスナー</param>
/// <param name="cancellationToken">キャンセレーショントークン</param>
public class PipelineContext(
        ILogger logger,
        IntermediateResultMode intermediateResultMode = IntermediateResultMode.None,
        StepErrorHandlingStrategy globalErrorHandlingStrategy = StepErrorHandlingStrategy.StopExecution,
        IPipelineEventListener? eventListener = null,
        CancellationToken cancellationToken = default)
{
        private readonly Dictionary<string, object> _data = [];
        private readonly HashSet<string> _stepsToSaveResults = [];
        
        /// <summary>
        /// パイプライン実行に関連するデータを保存する辞書
        /// </summary>
        public IDictionary<string, object> Data => _data;

    /// <summary>
    /// ロガー
    /// </summary>
    public ILogger Logger { get; } = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// キャンセレーショントークン
    /// </summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>
    /// 中間結果の保存モード
    /// </summary>
    public IntermediateResultMode IntermediateResultMode { get; } = intermediateResultMode;

    /// <summary>
    /// パイプライン全体のエラー処理戦略
    /// </summary>
    public StepErrorHandlingStrategy GlobalErrorHandlingStrategy { get; } = globalErrorHandlingStrategy;

    /// <summary>
    /// イベントリスナー
    /// </summary>
    public IPipelineEventListener EventListener { get; } = eventListener is not null ? eventListener : new NullPipelineEventListener();

    /// <summary>
    /// 特定のステップの中間結果を保存するように設定します
    /// </summary>
    /// <param name="stepName">ステップ名</param>
    public void SaveIntermediateResultForStep(string stepName)
        {
            if (!string.IsNullOrEmpty(stepName))
            {
                _stepsToSaveResults.Add(stepName);
            }
        }
        
        /// <summary>
        /// 特定のステップの中間結果を保存するかどうかを判断します
        /// </summary>
        /// <param name="stepName">ステップ名</param>
        /// <returns>保存する場合はtrue、そうでない場合はfalse</returns>
        public bool ShouldSaveIntermediateResult(string stepName)
        {
            if (IntermediateResultMode == IntermediateResultMode.None)
            {
                return false;
            }
            
            if (IntermediateResultMode == IntermediateResultMode.All)
            {
                return true;
            }
            
            if (IntermediateResultMode == IntermediateResultMode.SelectedSteps)
            {
                return _stepsToSaveResults.Contains(stepName);
            }
            
            // DebugOnlyとOnErrorの場合は別途評価
            return false;
        }
        
        /// <summary>
        /// ジェネリックなデータ値を取得します
        /// </summary>
        /// <typeparam name="T">取得するデータの型</typeparam>
        /// <param name="key">キー</param>
        /// <param name="defaultValue">キーが存在しない場合のデフォルト値</param>
        /// <returns>取得したデータ、またはデフォルト値</returns>
        /// <exception cref="ArgumentNullException">keyがnullの場合</exception>
        [return: MaybeNull]
        public T RetrieveDataValue<T>(string key, [AllowNull] T defaultValue = default)
        {
            ArgumentNullException.ThrowIfNull(key);

            if (_data.TryGetValue(key, out var value) && value is T typedValue)
            {
                return typedValue;
            }
            
            return defaultValue;
        }
        
        /// <summary>
        /// ステップの実行をキャンセルするかどうかを判断します
        /// </summary>
        /// <returns>キャンセルする場合はtrue、そうでない場合はfalse</returns>
        public bool ShouldCancelExecution()
        {
            return CancellationToken.IsCancellationRequested;
        }

        /// <summary>
        /// 空のイベントリスナー実装
        /// </summary>
        private sealed class NullPipelineEventListener : IPipelineEventListener
        {
            public Task OnPipelineStartAsync(IImagePipeline pipeline, IAdvancedImage input) => Task.CompletedTask;
            public Task OnPipelineCompleteAsync(IImagePipeline pipeline, PipelineResult result) => Task.CompletedTask;
            public Task OnStepStartAsync(IImagePipelineStep pipelineStep, IAdvancedImage input, PipelineContext context) => Task.CompletedTask;
            public Task OnStepCompleteAsync(IImagePipelineStep pipelineStep, IAdvancedImage output, PipelineContext context, long elapsedMilliseconds) => Task.CompletedTask;
            
            public Task<IAdvancedImage?> OnStepErrorAsync(IImagePipelineStep pipelineStep, Exception exception, PipelineContext context)
            {
                // nullを返すためのTask<IAdvancedImage?>を安全に作成
                return Task.FromResult<IAdvancedImage?>(null);
            }
            
            public Task OnPipelineErrorAsync(IImagePipeline pipeline, Exception exception, PipelineContext context) => Task.CompletedTask;
        }
    }
