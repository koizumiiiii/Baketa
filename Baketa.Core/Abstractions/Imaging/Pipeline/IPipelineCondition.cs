using System.Threading.Tasks;

namespace Baketa.Core.Abstractions.Imaging.Pipeline;

/// <summary>
/// パイプラインの条件評価を表すインターフェース
/// </summary>
public interface IPipelineCondition
{
    /// <summary>
    /// 条件を評価します
    /// </summary>
    /// <param name="input">入力画像</param>
    /// <param name="context">パイプライン実行コンテキスト</param>
    /// <returns>条件が真の場合はtrue、偽の場合はfalse</returns>
    Task<bool> EvaluateAsync(IAdvancedImage input, PipelineContext context);

    /// <summary>
    /// 条件の説明
    /// </summary>
    string Description { get; }
}
