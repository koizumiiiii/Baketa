using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Baketa.Core.Settings;
using Baketa.UI.Resources;

namespace Baketa.UI.Converters;

/// <summary>
/// 設定画面用コンバーターの静的参照クラス
/// </summary>
public static class SettingsConverters
{
    /// <summary>
    /// UiThemeを日本語表示名に変換するコンバーター
    /// </summary>
    public static readonly UiThemeToStringConverter ThemeToDisplayName = UiThemeToStringConverter.Instance;

    /// <summary>
    /// UiSizeを日本語表示名に変換するコンバーター
    /// </summary>
    public static readonly UiSizeToStringConverter SizeToDisplayName = UiSizeToStringConverter.Instance;
}

/// <summary>
/// UiSizeを日本語文字列に変換するコンバーター
/// </summary>
public sealed class UiSizeToStringConverter : IValueConverter
{
    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static readonly UiSizeToStringConverter Instance = new();

    /// <summary>
    /// UiSizeをローカライズされた文字列に変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UiSize.Small => Strings.Size_Small,
            UiSize.Medium => Strings.Size_Medium,
            UiSize.Large => Strings.Size_Large,
            null => null,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// 逆変換（未対応）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// booleanを状態色に変換するコンバーター
/// </summary>
public sealed class BoolToStatusColorConverter : IValueConverter
{
    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static readonly BoolToStatusColorConverter Instance = new();

    /// <summary>
    /// booleanを状態色に変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 変更あり（true）：オレンジ、変更なし（false）：グリーン
        return value switch
        {
            true => new SolidColorBrush(Color.FromRgb(255, 165, 0)),  // オレンジ
            false => new SolidColorBrush(Color.FromRgb(34, 139, 34)),   // グリーン
            _ => new SolidColorBrush(Color.FromRgb(128, 128, 128))        // グレー（null等）
        };
    }

    /// <summary>
    /// 逆変換（未対応）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// UiThemeを日本語文字列に変換するコンバーター
/// </summary>
public sealed class UiThemeToStringConverter : IValueConverter
{
    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static readonly UiThemeToStringConverter Instance = new();

    /// <summary>
    /// UiThemeをローカライズされた文字列に変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UiTheme.Light => Strings.Theme_Light,
            UiTheme.Dark => Strings.Theme_Dark,
            UiTheme.Auto => Strings.Theme_Auto,
            null => null,
            _ => value.ToString()
        };
    }

    /// <summary>
    /// 逆変換（未対応）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// booleanを詳細設定ボタンテキストに変換するコンバーター
/// </summary>
public sealed class BoolToAdvancedSettingsTextConverter : IValueConverter
{
    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static readonly BoolToAdvancedSettingsTextConverter Instance = new();

    /// <summary>
    /// booleanを詳細設定ボタンテキストに変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? Strings.Settings_Advanced_Hide : Strings.Settings_Advanced_Show;
    }

    /// <summary>
    /// 逆変換（未対応）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

/// <summary>
/// booleanを展開アイコンに変換するコンバーター
/// </summary>
public sealed class BoolToExpandIconConverter : IValueConverter
{
    /// <summary>
    /// シングルトンインスタンス
    /// </summary>
    public static readonly BoolToExpandIconConverter Instance = new();

    /// <summary>
    /// booleanを展開アイコンのパスデータに変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // 展開時（true）：上向き矢印、折りたたみ時（false）：下向き矢印
        return value switch
        {
            true => "M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z",  // 上向き矢印
            false => "M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z",   // 下向き矢印
            _ => "M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z"      // デフォルト（下向き）
        };
    }

    /// <summary>
    /// 逆変換（未対応）
    /// </summary>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
