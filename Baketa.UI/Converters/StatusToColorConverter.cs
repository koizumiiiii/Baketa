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
                TranslationStatus.Initializing => new SolidColorBrush(Color.Parse("#007BFF")), // 青（初期化中）
                TranslationStatus.Idle => new SolidColorBrush(Color.Parse("#6C757D")), // グレー（未選択）
                TranslationStatus.Ready => new SolidColorBrush(Color.Parse("#28A745")), // 緑（準備完了）
                TranslationStatus.Capturing => new SolidColorBrush(Color.Parse("#28A745")), // 緑
                TranslationStatus.ProcessingOCR => new SolidColorBrush(Color.Parse("#FD7E14")), // オレンジ
                TranslationStatus.Translating => new SolidColorBrush(Color.Parse("#FD7E14")), // オレンジ（翻訳中）
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
                "initializing" or "初期化中" => new SolidColorBrush(Color.Parse("#007BFF")),
                "idle" or "未選択" => new SolidColorBrush(Color.Parse("#6C757D")),
                "ready" or "準備完了" => new SolidColorBrush(Color.Parse("#28A745")),
                "capturing" or "キャプチャ中" => new SolidColorBrush(Color.Parse("#28A745")),
                "processingocr" or "ocr処理中" => new SolidColorBrush(Color.Parse("#FD7E14")),
                "translating" or "翻訳中" => new SolidColorBrush(Color.Parse("#FD7E14")),
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
