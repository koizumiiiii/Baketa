using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Baketa.Core.Abstractions.Auth;

namespace Baketa.UI.Converters;

/// <summary>
/// パスワード強度をBoolean値に変換するコンバーター集
/// </summary>
public static class PasswordStrengthConverters
{
    /// <summary>
    /// パスワード強度がMedium以上かどうかを判定するコンバーター
    /// </summary>
    public static readonly IValueConverter IsMediumOrHigher =
        new FuncValueConverter<PasswordStrength, bool>(strength =>
            strength >= PasswordStrength.Medium);

    /// <summary>
    /// パスワード強度がStrongかどうかを判定するコンバーター
    /// </summary>
    public static readonly IValueConverter IsStrong =
        new FuncValueConverter<PasswordStrength, bool>(strength =>
            strength == PasswordStrength.Strong);
}
