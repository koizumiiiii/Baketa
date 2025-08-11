using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace Baketa.UI.Converters;

/// <summary>
/// 翻訳状態からアイコンへの変換
/// </summary>
public class TranslationStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isActive)
        {
            var resourceKey = isActive ? "StopIcon" : "PlayIcon";
            return Avalonia.Application.Current?.FindResource(resourceKey);
        }
        return Avalonia.Application.Current?.FindResource("PlayIcon");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 表示状態からアイコンへの変換
/// </summary>
public class VisibilityStateToIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isVisible)
        {
            var resourceKey = isVisible ? "ViewIcon" : "HideIcon";
            return Avalonia.Application.Current?.FindResource(resourceKey);
        }
        return Avalonia.Application.Current?.FindResource("ViewIcon");
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 文字列がnull/空でないかをboolに変換するコンバーター
/// </summary>
public class StringNotNullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return !string.IsNullOrEmpty(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// 文字列がnull/空かをboolに変換するコンバーター
/// </summary>
public class StringNullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Boolean値をオーバーレイの高さに変換するコンバーター
/// true（折り畳み状態）の場合は40、false（展開状態）の場合は430の高さを返す
/// </summary>
public class BooleanToHeightConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isCollapsed)
        {
            return isCollapsed ? 40.0 : 430.0;
        }
        return 430.0; // デフォルトは展開状態
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double height)
        {
            return height <= 40.0;
        }
        return false;
    }
}