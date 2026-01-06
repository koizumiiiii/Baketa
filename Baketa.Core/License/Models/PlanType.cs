namespace Baketa.Core.License.Models;

/// <summary>
/// サブスクリプションプランの種類
/// Issue #125: Standardプランを廃止
/// Issue #257: Pro/Premium/Ultimate 3段階構成に改定
/// </summary>
public enum PlanType
{
    /// <summary>
    /// 無料プラン - ローカル翻訳のみ
    /// </summary>
    Free = 0,

    /// <summary>
    /// プロプラン ($3/月) - ライトゲーマー向け
    /// ローカル + クラウドAI翻訳 (1,000万トークン/月)
    /// ノベルゲームで約10時間分
    /// </summary>
    Pro = 1,

    /// <summary>
    /// プレミアムプラン ($5/月) - カジュアルゲーマー向け
    /// ローカル + クラウドAI翻訳 (2,000万トークン/月)
    /// ノベルゲームで約21時間分
    /// </summary>
    Premium = 2,

    /// <summary>
    /// アルティメットプラン ($9/月) - ヘビーゲーマー向け
    /// ローカル + クラウドAI翻訳 (5,000万トークン/月)
    /// ノベルゲームで約52時間分
    /// </summary>
    Ultimate = 3
}
