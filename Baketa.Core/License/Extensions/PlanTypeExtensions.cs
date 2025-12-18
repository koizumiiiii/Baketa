using Baketa.Core.License.Models;

namespace Baketa.Core.License.Extensions;

/// <summary>
/// PlanType enumの拡張メソッド
/// </summary>
public static class PlanTypeExtensions
{
    /// <summary>
    /// クラウドAI翻訳が利用可能かどうか
    /// </summary>
    public static bool HasCloudAiAccess(this PlanType plan) =>
        plan is PlanType.Pro or PlanType.Premia;

    /// <summary>
    /// 広告が表示されるかどうか
    /// </summary>
    public static bool ShowsAds(this PlanType plan) =>
        plan == PlanType.Free;

    /// <summary>
    /// 月間トークン上限を取得
    /// </summary>
    public static long GetMonthlyTokenLimit(this PlanType plan) => plan switch
    {
        PlanType.Pro => 4_000_000,
        PlanType.Premia => 8_000_000,
        _ => 0
    };

    /// <summary>
    /// 月額料金（円）を取得
    /// </summary>
    public static int GetMonthlyPriceYen(this PlanType plan) => plan switch
    {
        PlanType.Free => 0,
        PlanType.Standard => 100,
        PlanType.Pro => 300,
        PlanType.Premia => 500,
        _ => 0
    };

    /// <summary>
    /// 日本語表示名を取得
    /// </summary>
    public static string GetDisplayName(this PlanType plan) => plan switch
    {
        PlanType.Free => "無料プラン",
        PlanType.Standard => "スタンダードプラン",
        PlanType.Pro => "プロプラン",
        PlanType.Premia => "プレミアプラン",
        _ => "不明なプラン"
    };

    /// <summary>
    /// 英語表示名を取得
    /// </summary>
    public static string GetEnglishDisplayName(this PlanType plan) => plan switch
    {
        PlanType.Free => "Free Plan",
        PlanType.Standard => "Standard Plan",
        PlanType.Pro => "Pro Plan",
        PlanType.Premia => "Premia Plan",
        _ => "Unknown Plan"
    };

    /// <summary>
    /// プランの説明を取得
    /// </summary>
    public static string GetDescription(this PlanType plan) => plan switch
    {
        PlanType.Free => "ローカル翻訳のみ利用可能。広告表示あり。",
        PlanType.Standard => "ローカル翻訳利用可能。広告なし。",
        PlanType.Pro => "ローカル + クラウドAI翻訳利用可能。月400万トークン。",
        PlanType.Premia => "ローカル + クラウドAI翻訳利用可能。月800万トークン。優先サポート。",
        _ => "不明なプラン"
    };

    /// <summary>
    /// 指定された機能がこのプランで利用可能かどうか
    /// </summary>
    public static bool IsFeatureAvailable(this PlanType plan, FeatureType feature) => feature switch
    {
        FeatureType.LocalTranslation => true, // 全プランで利用可能
        FeatureType.CloudAiTranslation => plan.HasCloudAiAccess(),
        FeatureType.AdFree => plan != PlanType.Free,
        FeatureType.PrioritySupport => plan == PlanType.Premia,
        FeatureType.AdvancedOcrSettings => plan is PlanType.Pro or PlanType.Premia,
        FeatureType.BatchTranslation => plan is PlanType.Pro or PlanType.Premia,
        _ => false
    };

    /// <summary>
    /// プランの階層ランクを取得（比較用）
    /// </summary>
    public static int GetRank(this PlanType plan) => (int)plan;

    /// <summary>
    /// 別のプランへの変更がアップグレードかどうか
    /// </summary>
    public static bool IsUpgradeTo(this PlanType currentPlan, PlanType targetPlan) =>
        targetPlan.GetRank() > currentPlan.GetRank();

    /// <summary>
    /// 別のプランへの変更がダウングレードかどうか
    /// </summary>
    public static bool IsDowngradeTo(this PlanType currentPlan, PlanType targetPlan) =>
        targetPlan.GetRank() < currentPlan.GetRank();

    /// <summary>
    /// 有効なプラン値かどうか
    /// </summary>
    public static bool IsValid(this PlanType plan) =>
        Enum.IsDefined(plan);
}
