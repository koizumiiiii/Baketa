using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプライン実行結果を表すクラス
/// </summary>
/// <remarks>
/// パイプライン実行結果を作成します
/// </remarks>
/// <param name="result">パイプライン処理の最終結果</param>
/// <param name="intermediateResults">各ステップの中間結果</param>
/// <param name="processingTimeMs">パイプライン実行の処理時間（ミリ秒）</param>
/// <param name="intermediateResultMode">中間結果の保存モード</param>
/// <param name="executedStepCount">実行されたステップ数</param>
public class PipelineResult(
        IAdvancedImage result,
        Dictionary<string, IAdvancedImage> intermediateResults,
        long processingTimeMs,
        IntermediateResultMode intermediateResultMode,
        int executedStepCount) : IDisposable
    {
        private readonly Dictionary<string, IAdvancedImage> _intermediateResults = intermediateResults ?? [];
        private bool _disposed;

    /// <summary>
    /// パイプライン処理の最終結果
    /// </summary>
    public IAdvancedImage Result { get; } = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>
    /// 各ステップの中間結果
    /// </summary>
    public IReadOnlyDictionary<string, IAdvancedImage> IntermediateResults => 
            new ReadOnlyDictionary<string, IAdvancedImage>(_intermediateResults);

    /// <summary>
    /// パイプライン実行の処理時間（ミリ秒）
    /// </summary>
    public long ProcessingTimeMs { get; } = processingTimeMs;

    /// <summary>
    /// 中間結果の保存モード
    /// </summary>
    public IntermediateResultMode IntermediateResultMode { get; } = intermediateResultMode;

    /// <summary>
    /// 実行されたステップ数
    /// </summary>
    public int ExecutedStepCount { get; } = executedStepCount;

    /// <summary>
    /// 指定されたステップの中間結果を取得します
    /// </summary>
    /// <param name="stepName">ステップ名</param>
    /// <returns>中間結果、存在しない場合はnull</returns>
    public IAdvancedImage? GetIntermediateResult(string stepName)
        {
            return _intermediateResults.TryGetValue(stepName, out var result) ? result : null;
        }

        /// <summary>
        /// 中間結果を追加します
        /// </summary>
        /// <param name="stepName">ステップ名</param>
        /// <param name="result">中間結果</param>
        public void AddIntermediateResult(string stepName, IAdvancedImage result)
        {
            if (string.IsNullOrEmpty(stepName))
            {
                throw new ArgumentException("ステップ名が無効です", nameof(stepName));
            }
            
            _intermediateResults[stepName] = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// リソースを解放します
        /// </summary>
        /// <param name="disposing">マネージドリソースを解放する場合はtrue</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                // 中間結果のリソース解放
                foreach (var result in _intermediateResults.Values)
                {
                    result.Dispose();
                }
                
                _intermediateResults.Clear();
                
                // 最終結果のリソース解放（結果は外部で使用される可能性があるため注意）
                // Result?.Dispose();
            }

            _disposed = true;
        }

        /// <summary>
        /// ファイナライザ
        /// </summary>
        ~PipelineResult()
        {
            Dispose(false);
        }
    }
