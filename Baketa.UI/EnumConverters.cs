using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace Baketa.UI;

    /// <summary>
    /// 列挙型を整数値に変換するコンバーター
    /// </summary>
    internal sealed class EnumToIntConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is Enum enumValue)
            {
                return System.Convert.ToInt32(enumValue, CultureInfo.InvariantCulture);
            }
            return 0;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is int intValue && targetType.IsEnum)
            {
                return Enum.ToObject(targetType, intValue);
            }

            if (targetType.IsEnum)
            {
                return Enum.GetValues(targetType).GetValue(0)!;
            }

            return null;
        }
    }

    /// <summary>
    /// 列挙型の比較を行うコンバーター
    /// </summary>
    internal class EnumComparisonConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value != null && parameter != null)
            {
                return value.Equals(parameter);
            }
            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue && parameter != null)
            {
                return parameter;
            }

            if (targetType.IsEnum)
            {
                return Enum.GetValues(targetType).GetValue(0)!;
            }

            return null;
        }
    }
