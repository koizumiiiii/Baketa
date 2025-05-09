using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline
{
    /// <summary>
    /// 条件分岐を処理するパイプラインステップ
    /// </summary>
    public class ConditionalPipelineStep : IImagePipelineStep
    {
        private readonly IPipelineCondition _condition;
        private readonly IImagePipelineStep _trueStep;
        private readonly IImagePipelineStep? _falseStep;
        
        /// <summary>
        /// ステップの名前
        /// </summary>
        public string Name { get; }
        
        /// <summary>
        /// ステップの説明
        /// </summary>
        public string Description { get; }
        
        /// <summary>
        /// ステップのパラメータ定義
        /// </summary>
        public IReadOnlyCollection<PipelineStepParameter> Parameters => [];
        
        /// <summary>
        /// ステップのエラーハンドリング戦略
        /// </summary>
        public StepErrorHandlingStrategy ErrorHandlingStrategy { get; set; } = StepErrorHandlingStrategy.StopExecution;

        /// <summary>
        /// 新しいConditionalPipelineStepを作成します
        /// </summary>
        /// <param name="name">ステップの名前</param>
        /// <param name="condition">評価する条件</param>
        /// <param name="trueStep">条件がtrueの場合に実行するステップ</param>
        /// <param name="falseStep">条件がfalseの場合に実行するステップ（省略可能）</param>
        public ConditionalPipelineStep(
            string name,
            IPipelineCondition condition,
            IImagePipelineStep trueStep,
            IImagePipelineStep? falseStep = null)
        {
            Name = string.IsNullOrEmpty(name) ? $"Conditional({condition.Description})" : name;
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            _trueStep = trueStep ?? throw new ArgumentNullException(nameof(trueStep));
            _falseStep = falseStep;
            
            Description = _falseStep != null
                ? $"条件 '{_condition.Description}' に基づいて '{_trueStep.Name}' または '{_falseStep.Name}' を実行します"
                : $"条件 '{_condition.Description}' に基づいて '{_trueStep.Name}' を実行するかスキップします";
        }

        /// <summary>
        /// ステップを実行します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <param name="cancellationToken">キャンセレーショントークン</param>
        /// <returns>処理結果画像</returns>
        public async Task<IAdvancedImage> ExecuteAsync(IAdvancedImage input, PipelineContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            
            // 条件の評価
            bool conditionResult;
            try
            {
                // コンストラクタでnullチェックをしているが、CA1062を満たすためここでも再確認
                ArgumentNullException.ThrowIfNull(_condition, nameof(_condition));
                
                conditionResult = await _condition.EvaluateAsync(input, context).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                context.Logger.LogError(ex, "条件 '{ConditionDescription}' の評価中にエラーが発生しました", _condition.Description);
                
                switch (ErrorHandlingStrategy)
                {
                    case StepErrorHandlingStrategy.StopExecution:
                        throw;
                        
                    case StepErrorHandlingStrategy.SkipStep:
                    case StepErrorHandlingStrategy.LogAndContinue:
                        context.Logger.LogWarning("条件評価に失敗したため、条件ステップをスキップします");
                        return input;
                        
                    case StepErrorHandlingStrategy.UseFallback:
                        // CA1062を満たすためにイベントリスナーのnullチェックを追加
                        ArgumentNullException.ThrowIfNull(context.EventListener, nameof(context) + "." + nameof(context.EventListener));
                        var fallbackResult = await context.EventListener.OnStepErrorAsync(this, ex, context).ConfigureAwait(false);
                        if (fallbackResult != null)
                        {
                            return fallbackResult;
                        }
                        context.Logger.LogWarning("条件ステップのフォールバック処理が提供されなかったため、入力をそのまま返します");
                        return input;
                        
                    default:
                        throw new ArgumentOutOfRangeException(
                            nameof(ErrorHandlingStrategy),  // パラメータ名
                            ErrorHandlingStrategy,        // 実際の値
                            $"不明なエラーハンドリング戦略: {ErrorHandlingStrategy}");  // メッセージ
                }
            }
            
            // 条件結果に基づいてステップを実行
            if (conditionResult)
            {
                ArgumentNullException.ThrowIfNull(_trueStep, nameof(_trueStep));
                context.Logger.LogDebug("条件 '{ConditionDescription}' がtrueと評価されたため、ステップ '{StepName}' を実行します", _condition.Description, _trueStep.Name);
                return await _trueStep.ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
            }
            else if (_falseStep != null)
            {
                ArgumentNullException.ThrowIfNull(_falseStep, nameof(_falseStep));
                context.Logger.LogDebug("条件 '{ConditionDescription}' がfalseと評価されたため、ステップ '{StepName}' を実行します", _condition.Description, _falseStep.Name);
                return await _falseStep.ExecuteAsync(input, context, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                context.Logger.LogDebug("条件 '{ConditionDescription}' がfalseと評価され、falseステップが指定されていないため、入力をそのまま返します", _condition.Description);
                return input;
            }
        }

        /// <summary>
        /// パラメータ値を設定します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <param name="value">設定する値</param>
        public void SetParameter(string parameterName, object value)
        {
            throw new NotSupportedException($"条件ステップは直接のパラメータをサポートしていません。パラメータ '{parameterName}' は条件またはサブステップで設定してください。");
        }

        /// <summary>
        /// パラメータ値を取得します
        /// </summary>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public object GetParameter(string parameterName)
        {
            throw new NotSupportedException($"条件ステップは直接のパラメータをサポートしていません。パラメータ '{parameterName}' は条件またはサブステップで取得してください。");
        }

        /// <summary>
        /// パラメータ値をジェネリック型で取得します
        /// </summary>
        /// <typeparam name="T">取得する型</typeparam>
        /// <param name="parameterName">パラメータ名</param>
        /// <returns>パラメータ値</returns>
        public T GetParameter<T>(string parameterName)
        {
            throw new NotSupportedException($"条件ステップは直接のパラメータをサポートしていません。パラメータ '{parameterName}' は条件またはサブステップで取得してください。");
        }

        /// <summary>
        /// 出力画像情報を取得します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <returns>出力画像の情報</returns>
        public ImageInfo GetOutputImageInfo(IAdvancedImage input)
        {
            ArgumentNullException.ThrowIfNull(input);
            
            // 条件の評価結果が不明なので、入力と同じ情報を返す
            // もしくは、trueとfalseの両方のステップの結果を比較して、より大きい方を返すなどの方法も考えられる
            return new ImageInfo
            {
                Width = input.Width,
                Height = input.Height,
                Format = input.Format,
                Channels = GetChannelCount(input.Format)
            };
        }

        /// <summary>
        /// 画像フォーマットからチャンネル数を取得します
        /// </summary>
        /// <param name="format">画像フォーマット</param>
        /// <returns>チャンネル数</returns>
        private static int GetChannelCount(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.Rgb24 => 3,
                ImageFormat.Rgba32 => 4,
                ImageFormat.Grayscale8 => 1,
                ImageFormat.Png => 4,
                ImageFormat.Jpeg => 3,
                ImageFormat.Bmp => 3,
                _ => throw new ArgumentException($"未サポートの画像フォーマット: {format}", nameof(format))
            };
        }
    }
}
