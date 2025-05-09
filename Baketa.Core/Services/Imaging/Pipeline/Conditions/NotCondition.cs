using System;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;

namespace Baketa.Core.Services.Imaging.Pipeline.Conditions
{
    /// <summary>
    /// 条件の否定を表す条件
    /// </summary>
    public class NotCondition : IPipelineCondition
    {
        private readonly IPipelineCondition _condition;
        
        /// <summary>
        /// 条件の説明
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 新しいNotConditionを作成します
        /// </summary>
        /// <param name="condition">否定する条件</param>
        public NotCondition(IPipelineCondition condition)
        {
            _condition = condition ?? throw new ArgumentNullException(nameof(condition));
            Description = $"NOT ({_condition.Description})";
        }

        /// <summary>
        /// 条件を評価します
        /// </summary>
        /// <param name="input">入力画像</param>
        /// <param name="context">パイプライン実行コンテキスト</param>
        /// <returns>条件が真の場合はtrue、偽の場合はfalse</returns>
        public async Task<bool> EvaluateAsync(IAdvancedImage input, PipelineContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            
            // 元の条件の否定を返す
            bool result = await _condition.EvaluateAsync(input, context).ConfigureAwait(false);
            return !result;
        }
    }
}
