using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace Baketa.UI.Converters;

/// <summary>
/// 言語に基づいてフォントファミリーを選択するコンバーター
/// 日本語・英語: LINE Font、その他: Noto Sans
/// </summary>
public class LanguageFontConverter : IValueConverter
{
    /// <summary>
    /// 通常フォント用のコンバーター
    /// </summary>
    public static readonly LanguageFontConverter Regular = new();
    
    /// <summary>
    /// 太字フォント用のコンバーター
    /// </summary>
    public static readonly LanguageFontConverter Bold = new() { IsBold = true };
    
    /// <summary>
    /// 太字フォントかどうか
    /// </summary>
    public bool IsBold { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        try
        {
            if (value is not string language)
            {
                return GetDefaultFont();
            }

            // 言語に基づいてフォントを選択
            var fontKey = GetFontKey(language);
            
            // Avaloniaのリソース系からフォントファミリーを取得
            if (Avalonia.Application.Current?.Resources?.TryGetResource(fontKey, null, out var resource) == true 
                && resource is FontFamily fontFamily)
            {
                return fontFamily;
            }

            return GetDefaultFont();
        }
        catch (Exception)
        {
            return GetDefaultFont();
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported");
    }

    /// <summary>
    /// 言語に基づいてフォントキーを取得
    /// </summary>
    private string GetFontKey(string language)
    {
        var suffix = IsBold ? "-Bold" : "";
        
        return language?.ToLowerInvariant() switch
        {
            "japanese" or "ja" or "jp" => $"JapaneseFont{suffix}",
            "english" or "en" or "eng" => $"EnglishFont{suffix}",
            _ => $"OtherLanguageFont{suffix}"
        };
    }

    /// <summary>
    /// デフォルトフォントを取得
    /// </summary>
    private static FontFamily GetDefaultFont()
    {
        // Avaloniaのデフォルトフォントを返す
        return FontFamily.Default;
    }
}