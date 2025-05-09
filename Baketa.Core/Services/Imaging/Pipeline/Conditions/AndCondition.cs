using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Imaging;
using Baketa.Core.Abstractions.Imaging.Pipeline;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.Services.Imaging.Pipeline.Conditions
{
    /// <summary>
    /// 複数の条件をAND演算で結合する条件
    /// </summary>
    public class AndCondition : IPipelineCondition
    {
        private readonly List<IPipelineCondition> _conditions;
        
        /// <summary>
        /// 条件の説明
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// 新しいAndConditionを作成します
        /// </summary>
        /// <param name="conditions">組み合わせる条件のリスト</param>
        public AndCondition(IEnumerable<IPipelineCondition> conditions)
        {
            _conditions = conditions?.ToList() ?? throw new ArgumentNullException(nameof(conditions));
            
            if (_conditions.Count == 0)
            {
                throw new ArgumentException("条件リストが空です", nameof(conditions));
            }
            
            Description = string.Join(" AND ", _conditions.Select(c => $"({c.Description})"));
        }

        /// <summary>
        /// 新しいAndConditionを作成します
        /// </summary>
        /// <param name="conditions">組み合わせる条件の配列</param>
        public AndCondition(params IPipelineCondition[] conditions)
            : this(conditions.AsEnumerable())
        {
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
            
            // すべての条件を評価し、1つでもfalseがあればfalseを返す
            foreach (var condition in _conditions)
            {
                if (!await condition.EvaluateAsync(input, context).ConfigureAwait(false))
                {
                    return false;
                }
                
                // キャンセルチェック
                if (context.CancellationToken.IsCancellationRequested)
                {
                    context.Logger.LogInformation("条件評価がキャンセルされました");
                    context.CancellationToken.ThrowIfCancellationRequested();
                }
            }
            
            // すべての条件がtrueならtrueを返す
            return true;
        }
    }
}
