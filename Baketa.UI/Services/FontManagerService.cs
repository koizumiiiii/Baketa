using System;
using Avalonia.Media;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// フォント管理サービス
/// 言語設定に基づいて適切なフォントを提供
/// </summary>
public interface IFontManagerService
{
    /// <summary>
    /// 指定した言語に適したフォントファミリーを取得
    /// </summary>
    FontFamily GetFontFamily(string language, bool isBold = false);

    /// <summary>
    /// 現在の設定に基づくデフォルトフォントファミリーを取得
    /// </summary>
    FontFamily GetDefaultFontFamily(bool isBold = false);

    /// <summary>
    /// 翻訳結果表示用のフォントファミリーを取得
    /// </summary>
    FontFamily GetTranslationFontFamily(string sourceLanguage, string targetLanguage, bool isBold = false);
}

/// <summary>
/// フォント管理サービスの実装
/// </summary>
public class FontManagerService(ILogger<FontManagerService> logger) : IFontManagerService
{
    private readonly ILogger<FontManagerService> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private string _currentSourceLanguage = "Japanese";
    private string _currentTargetLanguage = "English";

    public FontFamily GetFontFamily(string language, bool isBold = false)
    {
        try
        {
            var fontKey = GetFontResourceKey(language, isBold);

            // Avaloniaのアプリケーションリソースからフォントを取得
            if (Avalonia.Application.Current?.Resources?.TryGetResource(fontKey, null, out var resource) == true
                && resource is FontFamily fontFamily)
            {
                _logger.LogDebug("フォント取得成功: 言語={Language}, 太字={IsBold}, フォント={FontKey}",
                    language, isBold, fontKey);
                return fontFamily;
            }

            _logger.LogWarning("フォントリソースが見つかりません: {FontKey}", fontKey);
            return FontFamily.Default;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "フォント取得エラー: 言語={Language}, 太字={IsBold}", language, isBold);
            return FontFamily.Default;
        }
    }

    public FontFamily GetDefaultFontFamily(bool isBold = false)
    {
        // デフォルトは日本語フォント
        return GetFontFamily(_currentSourceLanguage, isBold);
    }

    public FontFamily GetTranslationFontFamily(string sourceLanguage, string targetLanguage, bool isBold = false)
    {
        // 翻訳先言語のフォントを優先
        return GetFontFamily(targetLanguage, isBold);
    }

    /// <summary>
    /// 言語に基づいてフォントリソースキーを取得
    /// </summary>
    private static string GetFontResourceKey(string language, bool isBold)
    {
        var suffix = isBold ? "-Bold" : "";

        return NormalizeLanguageCode(language) switch
        {
            "japanese" => $"JapaneseFont{suffix}",
            "english" => $"EnglishFont{suffix}",
            _ => $"OtherLanguageFont{suffix}"
        };
    }

    /// <summary>
    /// 言語コードを正規化
    /// </summary>
    private static string NormalizeLanguageCode(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return "japanese";

        return language.ToLowerInvariant() switch
        {
            "japanese" or "ja" or "jp" or "jpn" => "japanese",
            "english" or "en" or "eng" or "us" or "gb" => "english",
            _ => "other"
        };
    }

    /// <summary>
    /// 設定変更イベントを処理
    /// </summary>
    public void HandleSettingsChanged(SettingsChangedEvent settingsEvent)
    {
        if (settingsEvent != null)
        {
            _currentSourceLanguage = settingsEvent.SourceLanguage;
            _currentTargetLanguage = settingsEvent.TargetLanguage;

            _logger.LogInformation("フォント設定更新: 翻訳元={SourceLanguage}, 翻訳先={TargetLanguage}",
                _currentSourceLanguage, _currentTargetLanguage);
        }
    }
}
