using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Baketa.Application.Services.Translation;

namespace Baketa.UI.Converters;

/// <summary>
/// 翻訳ステータスを対応する色に変換するコンバーター
/// </summary>
public class StatusToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is TranslationStatus status)
        {
            return status switch
            {
                TranslationStatus.Idle => new SolidColorBrush(Color.Parse("#6C757D")), // グレー
                TranslationStatus.Capturing => new SolidColorBrush(Color.Parse("#28A745")), // 緑
                TranslationStatus.ProcessingOCR => new SolidColorBrush(Color.Parse("#FD7E14")), // オレンジ
                TranslationStatus.Translating => new SolidColorBrush(Color.Parse("#17A2B8")), // 青
                TranslationStatus.Completed => new SolidColorBrush(Color.Parse("#28A745")), // 緑
                TranslationStatus.Error => new SolidColorBrush(Color.Parse("#DC3545")), // 赤
                TranslationStatus.Cancelled => new SolidColorBrush(Color.Parse("#6C757D")), // グレー
                _ => new SolidColorBrush(Color.Parse("#6C757D")) // デフォルトはグレー
            };
        }

        if (value is string statusString)
        {
            return statusString.ToLowerInvariant() switch
            {
                "idle" or "待機中" => new SolidColorBrush(Color.Parse("#6C757D")),
                "capturing" or "キャプチャ中" => new SolidColorBrush(Color.Parse("#28A745")),
                "processingocr" or "ocr処理中" => new SolidColorBrush(Color.Parse("#FD7E14")),
                "translating" or "翻訳中" => new SolidColorBrush(Color.Parse("#17A2B8")),
                "completed" or "完了" => new SolidColorBrush(Color.Parse("#28A745")),
                "error" or "エラー" => new SolidColorBrush(Color.Parse("#DC3545")),
                "cancelled" or "キャンセル" => new SolidColorBrush(Color.Parse("#6C757D")),
                _ => new SolidColorBrush(Color.Parse("#6C757D"))
            };
        }

        return new SolidColorBrush(Color.Parse("#6C757D"));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException("ConvertBack is not supported");
    }
}