using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline;

    /// <summary>
    /// 画像処理パイプラインの実装
    /// </summary>
    public class ImagePipeline : IImagePipeline
    {
        private readonly List<IImagePipelineStep> _steps = new();
        private readonly ILogger<ImagePipeline> _logger;
        
        /// <summary>
        /// 中間結果の保存モード
        /// </summary>
        public IntermediateResultMode IntermediateResultMode { get; set; } = IntermediateResultMode.None;
        
        /// <summary>
        /// パイプライン全体のエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy GlobalErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;
        
        /// <summary>
        /// パイプラインイベントのリスナー
        /// </summary>
        public IPipelineEventListener EventListener { get; set; }
        
        /// <summary>
        /// すべてのステップを取得します
        /// </summary>
        public IReadOnlyList<IImagePipelineStep> Steps => _steps;
        
        /// <summary>
        /// パイプライン内のステップの数を取得します
        /// </summary>
        public int StepCount => _steps.Count;

        /// <summary>
        /// 新しいImagePipelineを作成します
        /// </summary>
        /// <param name="logger">ロガー</param>
        /// <param name="eventListener">イベントリスナー (省略可能)</param>
        public ImagePipeline(ILogger<ImagePipeline> logger, IPipelineEventListener? eventListener = null)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            EventListener = eventListener ?? new DefaultPipelineEventListener(logger);
        }

        /// <summary>
        /// パイプラインに処理ステップを追加します
        /// </summary>
        /// <param name="pipelineStep">追加するパイプラインステップ</param>
        /// <returns>自身のインスタンス（メソッドチェーン用）</returns>
        public IImagePipeline AddStep(IImagePipelineStep pipelineStep)
        {
            ArgumentNullException.ThrowIfNull(pipelineStep);
            
            _steps.Add(pipelineStep);
            _logger.LogDebug("パイプラインにステップ '{StepName}' を追加しました", pipelineStep.Name);
            
            return this;
        }

        /// <summary>
        /// 指定されたインデックスのステップを削除します
        /// </summary>
        /// <param name="index">削除するステップのインデックス</param>
        /// <returns>削除に成功した場合はtrue、そうでない場合はfalse</returns>
        public bool RemoveStep(int index)
        {
            if (index < 0 || index >= _steps.Count)
            {
                _logger.LogWarning("無効なインデックス {Index} のステップを削除しようとしました", index);
                return false;
            }
            
            var step = _steps[index];
            _steps.RemoveAt(index);
            _logger.LogDebug("パイプラインからステップ '{StepName}' (インデックス: {Index}) を削除しました", step.Name, index);
            
            return true;
        }

        /// <summary>
        /// 指定されたステップを削除します
        /// </summary>
        /// <param name="pipelineStep">削除するステップ</param>
        /// <returns>削除に成功した場合はtrue、そうでない場合はfalse</returns>
        public bool RemoveStep(IImagePipelineStep pipelineStep)
        {
            ArgumentNullException.ThrowIfNull(pipelineStep);
            
            var removed = _steps.Remove(pipelineStep);
            
            if (removed)
            {
                _logger.LogDebug("パイプラインからステップ '{StepName}' を削除しました", pipelineStep.Name);
            }
            else
            {
                _logger.LogWarning("ステップ '{StepName}' はパイプラインに存在しないため削除できませんでした", pipelineStep.Name);
            }
            
            return removed;
        }

        /// <summary>
        /// パイプラインのステップをクリアします
        /// </summary>
        public void ClearSteps()
        {
            _steps.Clear();
            _logger.LogDebug("パイプラインのすべてのステップをクリアしました");
        }

        /// <summary>
        /// 指定されたインデックスのステップを取得します
        /// </summary>
        /// <param name="index">取得するステップのインデックス</param>
        /// <returns>パイプラインステップ</returns>
        public IImagePipelineStep GetStep(int index)
        {
            if (index < 0 || index >= _steps.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), $"インデックス {index} は範囲外です。有効な範囲: 0～{_steps.Count - 1}");
            }
            
            return _steps[index];
        }

        /// <summary>
        /// 指定された名前のステップを取得します
        /// </summary>
        /// <param name="name">取得するステップの名前</param>
        /// <returns>パイプラインステップ、見つからない場合はnull</returns>
        public IImagePipelineStep? GetStepByName(string name)
        {
            ArgumentException.ThrowIfNullOrEmpty(name, nameof(name));
            
            // FirstOrDefaultを使用して、見つからない場合はnullを返す
            return _steps.FirstOrDefault(s => s.Name == name);
        }

        /// <summary>
        /// パイプラインを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果と中間結果を含むパイプライン実行結果</returns>
        public async Task<PipelineResult> ExecuteAsync(IAdvancedImage input, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // 実行開始時間を記録
            var stopwatch = Stopwatch.StartNew();
            
            // 中間結果を保存するディクショナリ
            var intermediateResults = new Dictionary<string, IAdvancedImage>();
            
            try
            {
                // パイプライン実行コンテキストを作成
                var context = new PipelineContext(
                    _logger,
                    IntermediateResultMode,
                    GlobalErrorHandlingStrategy,
                    EventListener,
                    cancellationToken);
                
                // パイプライン実行開始イベントを通知
                await EventListener.OnPipelineStartAsync(this, input).ConfigureAwait(false);
                
                // ステップが0の場合は入力をそのまま返す
                if (_steps.Count == 0)
                {
                    _logger.LogWarning("パイプラインにステップが追加されていないため、入力がそのまま出力されます");
                    
                    var emptyResult = new PipelineResult(
                        input,
                        intermediateResults,
                        stopwatch.ElapsedMilliseconds,
                        IntermediateResultMode,
                        0);
                    
                    await EventListener.OnPipelineCompleteAsync(this, emptyResult).ConfigureAwait(false);
                    
                    return emptyResult;
                }
                
                // 現在の画像（最初は入力画像）
                var currentImage = input;
                int executedStepCount = 0;
                
                // 各ステップを順番に実行
                for (int i = 0; i < _steps.Count; i++)
                {
                    try
                    {
                        // キャンセルチェック
                        if (cancellationToken.IsCancellationRequested)
                        {
                            _logger.LogInformation("パイプラインの実行がキャンセルされました");
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        
                        var step = _steps[i];
                        _logger.LogDebug("ステップ {StepIndex}/{StepCount} '{StepName}' を実行中...", i + 1, _steps.Count, step.Name);
                        
                        // ステップを実行
                        var stepResult = await step.ExecuteAsync(currentImage, context, cancellationToken).ConfigureAwait(false);
                        executedStepCount++;
                        
                        // 中間結果を保存するかどうかを判断
                        if (ShouldSaveIntermediateResult(step.Name, context))
                        {
                            intermediateResults[step.Name] = stepResult;
                        }
                        
                        // 次のステップの入力として使用
                        currentImage = stepResult;
                    }
                    catch (OperationCanceledException)
                    {
                        // キャンセル例外は上位に伝播
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // ステップのエラー処理
                        context.Logger.LogError(ex, "ステップ '{StepName}' の実行中にエラーが発生しました", _steps[i].Name);
                        
                        // エラーハンドリング戦略に基づいた処理
                        switch (_steps[i].ErrorHandlingStrategy)
                        {
                            case StepErrorHandlingStrategy.StopExecution:
                                throw; // 例外をそのまま伝播
                                
                            case StepErrorHandlingStrategy.SkipStep:
                                context.Logger.LogWarning("ステップ '{StepName}' はスキップされました", _steps[i].Name);
                                // currentImageをそのまま使用して次のステップへ
                                break;
                                
                            case StepErrorHandlingStrategy.UseFallback:
                                // フォールバック処理
                                var fallbackResult = await context.EventListener.OnStepErrorAsync(_steps[i], ex, context).ConfigureAwait(false);
                                if (fallbackResult != null)
                                {
                                    currentImage = fallbackResult;
                                }
                                else
                                {
                                    context.Logger.LogWarning("ステップ '{StepName}' のフォールバック処理が提供されなかったため、入力をそのまま使用します", _steps[i].Name);
                                }
                                break;
                                
                            case StepErrorHandlingStrategy.LogAndContinue:
                                context.Logger.LogWarning("ステップ '{StepName}' でエラーが発生しましたが、処理を継続します", _steps[i].Name);
                                // currentImageをそのまま使用して次のステップへ
                                break;
                                
                            default:
                                // 指定されたエラーハンドリング戦略の値が不正
                                var strategy = _steps[i].ErrorHandlingStrategy;
                                string paramName = nameof(StepErrorHandlingStrategy);
                                throw new ArgumentOutOfRangeException(
                                    paramName,
                                    strategy,
                                    $"不明なエラーハンドリング戦略: {strategy}");
                        }
                    }
                }
                
                // 結果オブジェクトを作成
                var result = new PipelineResult(
                    currentImage,
                    intermediateResults,
                    stopwatch.ElapsedMilliseconds,
                    IntermediateResultMode,
                    executedStepCount);
                
                // パイプライン実行完了イベントを通知
                await EventListener.OnPipelineCompleteAsync(this, result).ConfigureAwait(false);
                
                _logger.LogInformation(
                    "パイプラインの実行が完了しました: {StepCount}ステップ, {ExecutionTime}ms",
                    executedStepCount,
                    stopwatch.ElapsedMilliseconds);
                
                return result;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("パイプラインの実行がキャンセルされました");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "パイプラインの実行中にエラーが発生しました");
                
                // エラーイベントを通知
                var context = new PipelineContext(
                    _logger,
                    IntermediateResultMode,
                    GlobalErrorHandlingStrategy,
                    EventListener,
                    cancellationToken);
                
                await EventListener.OnPipelineErrorAsync(this, ex, context).ConfigureAwait(false);
                
                throw;
            }
            finally
            {
                stopwatch.Stop();
            }
        }

        /// <summary>
        /// パイプライン構成を名前付きプロファイルとして保存します
        /// </summary>
        /// <param name="profileName">保存するプロファイル名</param>
        /// <returns>非同期タスク</returns>
        public async Task SaveProfileAsync(string profileName)
        {
            ArgumentException.ThrowIfNullOrEmpty(profileName, nameof(profileName));
            
            // IPipelineProfileManagerがDIで注入される場合は、
            // そのインスタンスを使用してプロファイルを保存します。
            // ここでは、この実装はプレースホルダーとなります。
            
            _logger.LogInformation("パイプラインプロファイル '{ProfileName}' を保存します", profileName);
            
            // 実際の永続化は、DIで注入されたIPipelineProfileManagerを
            // 使用するコードで行う必要があります
            await Task.CompletedTask.ConfigureAwait(false);
        }

        /// <summary>
        /// 名前付きプロファイルからパイプライン構成を読み込みます
        /// </summary>
        /// <param name="profileName">読み込むプロファイル名</param>
        /// <returns>読み込まれたパイプライン</returns>
        public async Task<IImagePipeline> LoadProfileAsync(string profileName)
        {
            ArgumentException.ThrowIfNullOrEmpty(profileName, nameof(profileName));
            
            // IPipelineProfileManagerがDIで注入される場合は、
            // そのインスタンスを使用してプロファイルを読み込みます。
            // ここでは、この実装はプレースホルダーとなります。
            
            _logger.LogInformation("パイプラインプロファイル '{ProfileName}' を読み込みます", profileName);
            
            // 実際の永続化は、DIで注入されたIPipelineProfileManagerを
            // 使用するコードで行う必要があります
            await Task.CompletedTask.ConfigureAwait(false);
            
            return this;
        }

        /// <summary>
        /// 中間結果を保存するかどうかを判断します
        /// </summary>
        /// <param name="stepName">ステップ名</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>保存する場合はtrue、そうでない場合はfalse</returns>
        private bool ShouldSaveIntermediateResult(string stepName, PipelineContext context)
        {
            if (IntermediateResultMode == IntermediateResultMode.All)
            {
                return true;
            }
            
            if (IntermediateResultMode == IntermediateResultMode.None)
            {
                return false;
            }
            
            // コンテキストからステップ固有の判断を得る
            return context.ShouldSaveIntermediateResult(stepName);
        }

        /// <summary>
        /// デフォルトのパイプラインイベントリスナー
        /// </summary>
        private sealed class DefaultPipelineEventListener : IPipelineEventListener
        {
            private readonly ILogger _logger;
            
            /// <summary>
            /// 新しいDefaultPipelineEventListenerを作成します
            /// </summary>
            /// <param name="logger">ロガー</param>
            public DefaultPipelineEventListener(ILogger logger)
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }
            
            /// <summary>
            /// パイプライン実行開始時に呼び出されます
            /// </summary>
            /// <param name="pipeline">実行されるパイプライン</param>
            /// <param name="input">入力画像</param>
            public Task OnPipelineStartAsync(IImagePipeline pipeline, IAdvancedImage input)
            {
                _logger.LogDebug("パイプラインの実行を開始します（{StepCount}ステップ）", pipeline.StepCount);
                return Task.CompletedTask;
            }
            
            /// <summary>
            /// パイプライン実行完了時に呼び出されます
            /// </summary>
            /// <param name="pipeline">実行されたパイプライン</param>
            /// <param name="result">パイプライン実行結果</param>
            public Task OnPipelineCompleteAsync(IImagePipeline pipeline, PipelineResult result)
            {
                _logger.LogDebug(
                    "パイプラインの実行が完了しました（実行時間: {ElapsedTime}ms、実行ステップ数: {ExecutedStepCount}）",
                    result.ProcessingTimeMs,
                    result.ExecutedStepCount);
                return Task.CompletedTask;
            }
            
            /// <summary>
            /// ステップ実行開始時に呼び出されます
            /// </summary>
            /// <param name="pipelineStep">実行されるステップ</param>
            /// <param name="input">入力画像</param>
            /// <param name="context">パイプライン実行コンテキスト</param>
            public Task OnStepStartAsync(IImagePipelineStep pipelineStep, IAdvancedImage input, PipelineContext context)
            {
                _logger.LogTrace("ステップ '{StepName}' の実行を開始します", pipelineStep.Name);
                return Task.CompletedTask;
            }
            
            /// <summary>
            /// ステップ実行完了時に呼び出されます
            /// </summary>
            /// <param name="pipelineStep">実行されたステップ</param>
            /// <param name="output">出力画像</param>
            /// <param name="context">パイプライン実行コンテキスト</param>
            /// <param name="elapsedMilliseconds">実行時間（ミリ秒）</param>
            public Task OnStepCompleteAsync(IImagePipelineStep pipelineStep, IAdvancedImage output, PipelineContext context, long elapsedMilliseconds)
            {
                _logger.LogTrace("ステップ '{StepName}' の実行が完了しました（実行時間: {ElapsedTime}ms）", pipelineStep.Name, elapsedMilliseconds);
                return Task.CompletedTask;
            }
            
            /// <summary>
            /// ステップ実行エラー時に呼び出されます
            /// </summary>
            /// <param name="pipelineStep">エラーが発生したステップ</param>
            /// <param name="exception">発生した例外</param>
            /// <param name="context">パイプライン実行コンテキスト</param>
            /// <returns>エラー処理後の結果画像、またはnull</returns>
            public Task<IAdvancedImage?> OnStepErrorAsync(IImagePipelineStep pipelineStep, Exception exception, PipelineContext context)
            {
                _logger.LogError(exception, "ステップ '{StepName}' の実行中にエラーが発生しました", pipelineStep.Name);
                return Task.FromResult<IAdvancedImage?>(null);
            }
            
            /// <summary>
            /// パイプライン実行エラー時に呼び出されます
            /// </summary>
            /// <param name="pipeline">エラーが発生したパイプライン</param>
            /// <param name="exception">発生した例外</param>
            /// <param name="context">パイプライン実行コンテキスト</param>
            public Task OnPipelineErrorAsync(IImagePipeline pipeline, Exception exception, PipelineContext context)
            {
                _logger.LogError(exception, "パイプラインの実行中にエラーが発生しました");
                return Task.CompletedTask;
            }
        }
    }
