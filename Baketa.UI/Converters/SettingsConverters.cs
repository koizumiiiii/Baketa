using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Baketa.Core.Settings;

namespace Baketa.UI.Converters;

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
    /// UiSizeを日本語文字列に変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UiSize.Small => "小（コンパクト）",
            UiSize.Medium => "中（標準）",
            UiSize.Large => "大（見やすさ重視）",
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
    /// UiThemeを日本語文字列に変換します
    /// </summary>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            UiTheme.Light => "ライト",
            UiTheme.Dark => "ダーク",
            UiTheme.Auto => "自動（システム設定に従う）",
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
        return value is true ? "基本設定に戻す" : "詳細設定を表示";
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
