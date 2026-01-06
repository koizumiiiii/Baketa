using Baketa.Core.License.Models;

namespace Baketa.Core.License.Extensions;

/// <summary>
/// PlanType enumの拡張メソッド
/// Issue #125: Standardプラン廃止、広告機能廃止に対応
/// Issue #257: Pro/Premium/Ultimate 3段階構成に改定
/// </summary>
public static class PlanTypeExtensions
{
    /// <summary>
    /// クラウドAI翻訳が利用可能かどうか
    /// </summary>
    public static bool HasCloudAiAccess(this PlanType plan) =>
        plan is PlanType.Pro or PlanType.Premium or PlanType.Ultimate;

    /// <summary>
    /// 広告が表示されるかどうか
    /// Issue #125: 広告機能は廃止、全プランで広告なし
    /// </summary>
    [Obsolete("Issue #125: 広告機能は廃止されました。常にfalseを返します。")]
    public static bool ShowsAds(this PlanType plan) => false;

    /// <summary>
    /// 月間トークン上限を取得
    /// Issue #257: Pro 1000万, Premium 2000万, Ultimate 5000万
    /// </summary>
    public static long GetMonthlyTokenLimit(this PlanType plan) => plan switch
    {
        PlanType.Pro => 10_000_000,      // 1,000万トークン（約10時間）
        PlanType.Premium => 20_000_000,  // 2,000万トークン（約21時間）
        PlanType.Ultimate => 50_000_000, // 5,000万トークン（約52時間）
        _ => 0
    };

    /// <summary>
    /// 月額料金（USD）を取得
    /// Issue #257: Patreon準拠のドル建て価格
    /// </summary>
    public static decimal GetMonthlyPriceUsd(this PlanType plan) => plan switch
    {
        PlanType.Free => 0m,
        PlanType.Pro => 3m,
        PlanType.Premium => 5m,
        PlanType.Ultimate => 9m,
        _ => 0m
    };

    /// <summary>
    /// 月額料金（円）を取得
    /// </summary>
    [Obsolete("Issue #257: USD建て価格に移行。GetMonthlyPriceUsd()を使用してください。")]
    public static int GetMonthlyPriceYen(this PlanType plan) => plan switch
    {
        PlanType.Free => 0,
        PlanType.Pro => 450,     // $3 x 約150円
        PlanType.Premium => 750, // $5 x 約150円
        PlanType.Ultimate => 1350, // $9 x 約150円
        _ => 0
    };

    /// <summary>
    /// 日本語表示名を取得
    /// </summary>
    public static string GetDisplayName(this PlanType plan) => plan switch
    {
        PlanType.Free => "無料プラン",
        PlanType.Pro => "プロプラン",
        PlanType.Premium => "プレミアムプラン",
        PlanType.Ultimate => "アルティメットプラン",
        _ => "不明なプラン"
    };

    /// <summary>
    /// 英語表示名を取得
    /// </summary>
    public static string GetEnglishDisplayName(this PlanType plan) => plan switch
    {
        PlanType.Free => "Free Plan",
        PlanType.Pro => "Pro Plan",
        PlanType.Premium => "Premium Plan",
        PlanType.Ultimate => "Ultimate Plan",
        _ => "Unknown Plan"
    };

    /// <summary>
    /// プランの説明を取得
    /// </summary>
    public static string GetDescription(this PlanType plan) => plan switch
    {
        PlanType.Free => "ローカル翻訳のみ利用可能。",
        PlanType.Pro => "ライトゲーマー向け。月1,000万トークン（約10時間分）。",
        PlanType.Premium => "カジュアルゲーマー向け。月2,000万トークン（約21時間分）。",
        PlanType.Ultimate => "ヘビーゲーマー向け。月5,000万トークン（約52時間分）。",
        _ => "不明なプラン"
    };

    /// <summary>
    /// 指定された機能がこのプランで利用可能かどうか
    /// </summary>
    public static bool IsFeatureAvailable(this PlanType plan, FeatureType feature) => feature switch
    {
        FeatureType.LocalTranslation => true, // 全プランで利用可能
        FeatureType.CloudAiTranslation => plan.HasCloudAiAccess(),
        FeatureType.AdFree => true, // Issue #125: 広告機能廃止、全プランで広告なし
        FeatureType.PrioritySupport => plan is PlanType.Premium or PlanType.Ultimate,
        FeatureType.AdvancedOcrSettings => plan is PlanType.Pro or PlanType.Premium or PlanType.Ultimate,
        FeatureType.BatchTranslation => plan is PlanType.Pro or PlanType.Premium or PlanType.Ultimate,
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
