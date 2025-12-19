namespace Baketa.Core.Payment.Models;

/// <summary>
/// 課金サイクル
/// </summary>
public enum BillingCycle
{
    /// <summary>月額課金</summary>
    Monthly = 0,

    /// <summary>年額課金（20%割引）</summary>
    Yearly = 1
}

/// <summary>
/// BillingCycle拡張メソッド
/// </summary>
public static class BillingCycleExtensions
{
    /// <summary>
    /// 課金サイクルの表示名を取得
    /// </summary>
    public static string GetDisplayName(this BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "月額",
        BillingCycle.Yearly => "年額",
        _ => "不明"
    };

    /// <summary>
    /// 課金サイクルの英語表示名を取得
    /// </summary>
    public static string GetEnglishDisplayName(this BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "Monthly",
        BillingCycle.Yearly => "Yearly",
        _ => "Unknown"
    };

    /// <summary>
    /// FastSpring製品API名用のサフィックスを取得
    /// </summary>
    public static string GetProductSuffix(this BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => "monthly",
        BillingCycle.Yearly => "yearly",
        _ => "monthly"
    };

    /// <summary>
    /// 次回請求日までの日数を取得
    /// </summary>
    public static int GetDaysUntilNextBilling(this BillingCycle cycle) => cycle switch
    {
        BillingCycle.Monthly => 30,
        BillingCycle.Yearly => 365,
        _ => 30
    };

    /// <summary>
    /// 年額の場合の割引率を取得（0-100）
    /// </summary>
    public static int GetDiscountPercentage(this BillingCycle cycle) => cycle switch
    {
        BillingCycle.Yearly => 20,
        _ => 0
    };
}
