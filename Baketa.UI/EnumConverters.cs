using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Baketa.UI;

/// <summary>
/// 列挙型から整数への変換コンバーター
/// </summary>
internal sealed class EnumToIntConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? _1, CultureInfo _2)
    {
        if (value is Enum enumValue)
        {
            return System.Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        if (value is int intValue && targetType.IsEnum)
        {
            return Enum.ToObject(targetType, intValue);
        }
        return Enum.GetValues(targetType).GetValue(0);
    }
}

/// <summary>
/// 列挙型の比較コンバーター
/// </summary>
internal sealed class EnumComparisonConverter : IValueConverter
{
    public object? Convert(object? value, Type _, object? parameter, CultureInfo _1)
    {
        if (value == null || parameter == null)
            return false;

        if (value is Enum enumValue && parameter is string parameterString)
        {
            // パラメーターが文字列の場合、列挙値に変換
            if (Enum.TryParse(enumValue.GetType(), parameterString, out var parameterEnum))
            {
                return enumValue.Equals(parameterEnum);
            }
        }

        // 直接比較
        return value.Equals(parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo _)
    {
        if (value is bool isSelected && isSelected && parameter != null)
        {
            if (parameter is string parameterString && targetType.IsEnum)
            {
                if (Enum.TryParse(targetType, parameterString, out var enumValue))
                {
                    return enumValue;
                }
            }
            return parameter;
        }
        return Enum.GetValues(targetType).GetValue(0);
    }
}
