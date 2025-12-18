namespace Baketa.Core.License.Models;

/// <summary>
/// サブスクリプションプランの種類
/// </summary>
public enum PlanType
{
    /// <summary>
    /// 無料プラン - ローカル翻訳のみ、広告表示あり
    /// </summary>
    Free = 0,

    /// <summary>
    /// スタンダードプラン (100円/月) - ローカル翻訳のみ、広告なし
    /// </summary>
    Standard = 1,

    /// <summary>
    /// プロプラン (300円/月) - ローカル + クラウドAI翻訳 (400万トークン/月)
    /// </summary>
    Pro = 2,

    /// <summary>
    /// プレミアプラン (500円/月) - ローカル + クラウドAI翻訳 (800万トークン/月)
    /// </summary>
    Premia = 3
}
