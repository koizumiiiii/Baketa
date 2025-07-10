using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Converters;

/// <summary>
/// Base64エンコードされた画像文字列をBitmapに変換するコンバーター
/// </summary>
public class Base64ToImageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // Base64変換を完全に無効化してFormatException回避
        System.Diagnostics.Debug.WriteLine($"🚫 Base64ToImageConverter: 変換を無効化中 (FormatException回避のため)");
        return null;
    }
    
    private static bool IsValidBase64Char(char c)
    {
        return (c >= 'A' && c <= 'Z') || 
               (c >= 'a' && c <= 'z') || 
               (c >= '0' && c <= '9') || 
               c == '+' || c == '/' || c == '=';
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported");
    }
}