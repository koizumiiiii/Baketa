namespace Baketa.Core.License.Models;

/// <summary>
/// サブスクリプションプランの種類
/// Issue #125: Standardプランを廃止し、3段階構成に簡素化
/// </summary>
public enum PlanType
{
    /// <summary>
    /// 無料プラン - ローカル翻訳のみ
    /// </summary>
    Free = 0,

    /// <summary>
    /// プロプラン (300円/月) - ローカル + クラウドAI翻訳 (400万トークン/月)
    /// </summary>
    Pro = 1,

    /// <summary>
    /// プレミアプラン (500円/月) - ローカル + クラウドAI翻訳 (800万トークン/月)
    /// </summary>
    Premia = 2
}
