using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Baketa.UI.Services;

namespace Baketa.UI.Converters;

/// <summary>
/// ブール値からアイコンテキストへの変換コンバーター
/// </summary>
internal sealed class BoolToIconConverter : IValueConverter
{
    public static readonly BoolToIconConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? "✅" : "❌";
        }
        return "❓";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// クラウドエンジン状態変換コンバーター
/// </summary>
internal sealed class CloudEngineStatusConverter : IMultiValueConverter
{
    public static readonly CloudEngineStatusConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 &&
            values[0] is bool isEnabled &&
            values[1] is bool isHealthy &&
            values[2] is bool isOnline)
        {
            if (!isEnabled) return "プレミアム必須";
            if (!isOnline) return "オフライン";
            if (!isHealthy) return "エラー";
            return "正常";
        }
        return "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 有効な項目数カウントコンバーター
/// </summary>
internal sealed class EnabledCountConverter : IValueConverter
{
    public static readonly EnabledCountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable collection)
        {
            try
            {
                return collection.Cast<object>()
                    .Count(item =>
                    {
                        var property = item.GetType().GetProperty("IsEnabled");
                        return property?.GetValue(item) is bool enabled && enabled;
                    });
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                return 0;
            }
        }
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 状態からアイコンへの変換コンバーター
/// </summary>
internal sealed class StatusToIconConverter : IMultiValueConverter
{
    public static readonly StatusToIconConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 3 &&
            values[0] is bool isSaving &&
            values[1] is bool isLoading &&
            values[2] is bool hasChanges)
        {
            if (isSaving) return "💾";
            if (isLoading) return "⏳";
            if (hasChanges) return "📝";
            return "✅";
        }
        return "ℹ️";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// エンジンタイプから表示名への変換コンバーター
/// </summary>
internal sealed class EngineToDisplayConverter : IValueConverter
{
    public static readonly EngineToDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value?.ToString() is string engineType)
        {
            return engineType switch
            {
                "LocalOnly" => "LocalOnly",
                "CloudOnly" => "CloudOnly",
                _ => engineType
            };
        }
        return "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 列挙型とブール値の相互変換コンバーター
/// </summary>
internal sealed class EnumToBoolConverter : IValueConverter
{
    public static readonly EnumToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null || parameter == null)
            return false;

        return value.ToString() == parameter.ToString();
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo _)
    {
        if (value is bool isChecked && isChecked && parameter != null)
        {
            return Enum.Parse(targetType, parameter.ToString()!);
        }
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// 文字列からブール値への変換コンバーター
/// </summary>
internal sealed class StringToBoolConverter : IValueConverter
{
    public static readonly StringToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrWhiteSpace(value?.ToString());
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// オブジェクトからブール値への変換コンバーター
/// </summary>
internal sealed class ObjectToBoolConverter : IValueConverter
{
    public static readonly ObjectToBoolConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// オブジェクト等価比較コンバーター
/// </summary>
internal sealed class ObjectEqualsConverter : IValueConverter
{
    public static readonly ObjectEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Equals(value, parameter);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo _)
    {
        if (value is bool isEqual && isEqual)
            return parameter;
        return AvaloniaProperty.UnsetValue;
    }
}

/// <summary>
/// ブール値から状態テキストへの変換コンバーター
/// </summary>
internal sealed class BoolToStatusTextConverter : IValueConverter
{
    public static readonly BoolToStatusTextConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isHealthy)
        {
            return isHealthy ? "正常" : "エラー";
        }
        return "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 翻訳戦略表示名変換コンバーター
/// </summary>
internal sealed class StrategyToDisplayConverter : IValueConverter
{
    public static readonly StrategyToDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Models.TranslationStrategy strategy)
        {
            return strategy switch
            {
                Models.TranslationStrategy.Direct => "直接翻訳",
                Models.TranslationStrategy.TwoStage => "2段階翻訳",
                _ => "不明"
            };
        }
        return value?.ToString() ?? "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 中国語変種表示名変換コンバーター
/// </summary>
internal sealed class ChineseVariantToDisplayConverter : IValueConverter
{
    public static readonly ChineseVariantToDisplayConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is Models.ChineseVariant variant)
        {
            return variant switch
            {
                Models.ChineseVariant.Auto => "自動選択",
                Models.ChineseVariant.Simplified => "簡体字",
                Models.ChineseVariant.Traditional => "繁体字",
                Models.ChineseVariant.Cantonese => "広東語",
                _ => "不明"
            };
        }
        return value?.ToString() ?? "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 言語コードからフラグ絵文字への変換コンバーター
/// </summary>
internal sealed class LanguageToFlagConverter : IValueConverter
{
    public static readonly LanguageToFlagConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string languageCode)
        {
            var language = Models.AvailableLanguages.SupportedLanguages
                .FirstOrDefault(l => l.Code == languageCode);
            return language?.Flag ?? "🌐";
        }
        return "🌐";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// ブール値から有効/無効テキストへの変換コンバーター
/// </summary>
internal sealed class BoolToEnabledConverter : IValueConverter
{
    public static readonly BoolToEnabledConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            return isEnabled ? "有効" : "無効";
        }
        return "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// ブール値から変更状態テキストへの変換コンバーター
/// </summary>
internal sealed class BoolToChangesConverter : IValueConverter
{
    public static readonly BoolToChangesConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool hasChanges)
        {
            return hasChanges ? "未保存" : "保存済み";
        }
        return "不明";
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}
