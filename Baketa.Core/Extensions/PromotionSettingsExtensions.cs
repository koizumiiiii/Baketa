using System;
using System.Globalization;
using Baketa.Core.Abstractions.Settings;

namespace Baketa.Core.Extensions;

/// <summary>
/// [Issue #237] IPromotionSettings拡張メソッド
/// </summary>
public static class PromotionSettingsExtensions
{
    /// <summary>
    /// プロモーションが現在有効かどうかを判定
    /// </summary>
    /// <param name="settings">プロモーション設定</param>
    /// <returns>プロモーションが有効期限内であればtrue</returns>
    public static bool IsCurrentlyActive(this IPromotionSettings settings)
    {
        if (settings is null)
            return false;

        if (!settings.PromotionPlanType.HasValue || string.IsNullOrEmpty(settings.PromotionExpiresAt))
            return false;

        if (!DateTime.TryParse(settings.PromotionExpiresAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expiresAt))
            return false;

        return expiresAt > DateTime.UtcNow;
    }
}
