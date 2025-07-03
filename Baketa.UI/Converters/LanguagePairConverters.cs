using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Baketa.UI.Models;

namespace Baketa.UI.Converters;

/// <summary>
/// 言語コードから言語情報への変換コンバーター
/// </summary>
internal sealed class LanguageCodeToLanguageInfoConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string languageCode)
        {
            return AvailableLanguages.SupportedLanguages
                .FirstOrDefault(lang => lang.Code == languageCode);
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is LanguageInfo languageInfo)
        {
            return languageInfo.Code;
        }
        return string.Empty;
    }
}

/// <summary>
/// 翻訳戦略の表示用変換コンバーター
/// </summary>
internal sealed class TranslationStrategyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TranslationStrategy strategy)
        {
            return strategy switch
            {
                TranslationStrategy.Direct => "直接翻訳",
                TranslationStrategy.TwoStage => "2段階翻訳",
                _ => value.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            return text switch
            {
                "直接翻訳" => TranslationStrategy.Direct,
                "2段階翻訳" => TranslationStrategy.TwoStage,
                _ => TranslationStrategy.Direct
            };
        }
        return TranslationStrategy.Direct;
    }
}

/// <summary>
/// 中国語変種の表示用変換コンバーター
/// </summary>
internal sealed class ChineseVariantConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is ChineseVariant variant)
        {
            return variant switch
            {
                ChineseVariant.Auto => "自動選択",
                ChineseVariant.Simplified => "簡体字",
                ChineseVariant.Traditional => "繁体字",
                ChineseVariant.Cantonese => "広東語",
                _ => value.ToString()
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string text)
        {
            return text switch
            {
                "自動選択" => ChineseVariant.Auto,
                "簡体字" => ChineseVariant.Simplified,
                "繁体字" => ChineseVariant.Traditional,
                "広東語" => ChineseVariant.Cantonese,
                _ => ChineseVariant.Auto
            };
        }
        return ChineseVariant.Auto;
    }
}

/// <summary>
/// ブール値から背景色への変換コンバーター
/// </summary>
internal sealed class BoolToBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isEnabled)
        {
            // TODO: 実際のブラシリソースに置き換える
            return isEnabled ? 
                new SolidColorBrush(Colors.Transparent) : 
                new SolidColorBrush(Color.FromArgb(50, 128, 128, 128));
        }
        return new SolidColorBrush(Colors.Transparent);
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// コレクションの条件付き個数計算コンバーター
/// </summary>
internal sealed class CollectionCountConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable collection && parameter is string condition)
        {
            try
            {
                var items = collection.Cast<object>().ToList();
                
                // 条件解析（例: "IsEnabled=True"）
                if (condition.Contains('=', StringComparison.Ordinal))
                {
                    var parts = condition.Split('=');
                    if (parts.Length == 2)
                    {
                        var propertyName = parts[0].Trim();
                        var expectedValue = parts[1].Trim();
                        
                        return items.Count(item => 
                        {
                            var property = item.GetType().GetProperty(propertyName);
                            if (property != null)
                            {
                                var propertyValue = property.GetValue(item);
                                return string.Equals(
                                    propertyValue?.ToString(), 
                                    expectedValue, 
                                    StringComparison.OrdinalIgnoreCase);
                            }
                            return false;
                        });
                    }
                }
                
                return items.Count;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidCastException)
            {
                // コレクション操作やリフレクション関連の例外のみキャッチ
                return 0;
            }
        }
        
        if (value is IEnumerable simpleCollection)
        {
            return simpleCollection.Cast<object>().Count();
        }
        
        return 0;
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// コレクションの平均値計算コンバーター
/// </summary>
internal sealed class CollectionAverageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is IEnumerable collection && parameter is string propertyName)
        {
            try
            {
                var items = collection.Cast<object>().ToList();
                var values = new List<double>();
                
                foreach (var item in items)
                {
                    var property = item.GetType().GetProperty(propertyName);
                    if (property != null)
                    {
                        var propertyValue = property.GetValue(item);
                        if (propertyValue != null && 
                            double.TryParse(propertyValue.ToString(), out var numericValue))
                        {
                            values.Add(numericValue);
                        }
                    }
                }
                
                return values.Count > 0 ? values.Average() : 0.0;
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or InvalidCastException or FormatException)
            {
                // コレクション操作やリフレクション、数値変換関連の例外のみキャッチ
                return 0.0;
            }
        }
        
        return 0.0;
    }

    public object? ConvertBack(object? value, Type targetType, object? _, CultureInfo _1)
    {
        throw new NotImplementedException();
    }
}
