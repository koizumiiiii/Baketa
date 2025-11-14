namespace Baketa.Core.Utilities;

/// <summary>
/// OCR結果のテキスト品質を検証し、翻訳に不適切なガベージテキストを検出するユーティリティ
/// </summary>
public static class TextQualityValidator
{
    /// <summary>
    /// 翻訳に不適切なパターン（ガベージテキスト検出用）
    /// </summary>
    private static readonly string[] s_garbagePatterns =
    [
        // 単一文字・記号のみ
        "^[0-9]$",           // 数字1文字のみ
        "^[A-Za-z]$",        // アルファベット1文字のみ
        "^[\\p{P}\\p{S}]$",  // 句読点・記号1文字のみ
        
        // 断片的・意味不明なパターン
        "^[\\.]{3,}",        // "....." 連続ドット
        "^[　\\s]+$",        // 空白・全角スペースのみ
        "^[\\p{P}\\p{S}\\s]+$", // 記号・空白のみ
        
        // 繰り返しパターン（OCRエラーで多発）
        "^(..)\\1{2,}$",     // 同じ2文字の3回以上繰り返し（例: "司司司司"）
        "^(.)\\1{4,}$",      // 同じ文字の5回以上繰り返し
        
        // 非常に短い断片（助詞のみなど）
        "^[のがでにをはと]$",    // 日本語助詞1文字のみ
        "^[about|the|and|or|in|on|at|to|for]$", // 英語前置詞・冠詞のみ
        
        // OCR特有の誤読パターン
        "^[\\|\\]{2,}$",     // "|" "}" 等の記号連続（縦線の誤読）
        "^[□■◇◆]{1,}$",     // 四角記号（文字化け）
    ];

    /// <summary>
    /// 日本語として意味のある最小文字数
    /// </summary>
    private const int MinMeaningfulJapaneseLength = 2;

    /// <summary>
    /// 英語として意味のある最小文字数
    /// </summary>
    private const int MinMeaningfulEnglishLength = 3;

    /// <summary>
    /// テキストが翻訳に適した品質かどうかを判定
    /// </summary>
    /// <param name="text">検証対象テキスト</param>
    /// <param name="sourceLanguage">ソース言語コード（ja, en等）</param>
    /// <returns>翻訳に適している場合true</returns>
    public static bool IsTranslationWorthy(string? text, string? sourceLanguage = null)
    {
        // null・空文字チェック
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleanText = text.Trim();

        // ガベージパターンチェック
        foreach (var pattern in s_garbagePatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanText, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return false;
            }
        }

        // 言語固有の長さチェック
        if (!string.IsNullOrEmpty(sourceLanguage))
        {
            return sourceLanguage.ToLowerInvariant() switch
            {
                "ja" => IsJapaneseTextMeaningful(cleanText),
                "en" => IsEnglishTextMeaningful(cleanText),
                _ => cleanText.Length >= 2 // デフォルト最小長
            };
        }

        // 言語不明時は汎用チェック
        return cleanText.Length >= 2;
    }

    /// <summary>
    /// 日本語テキストが意味のある長さかチェック
    /// </summary>
    private static bool IsJapaneseTextMeaningful(string text)
    {
        // ひらがな・カタカナ・漢字の文字数をカウント
        var meaningfulChars = 0;
        foreach (var c in text)
        {
            if (char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.OtherLetter ||
                IsJapaneseCharacter(c))
            {
                meaningfulChars++;
            }
        }

        return meaningfulChars >= MinMeaningfulJapaneseLength;
    }

    /// <summary>
    /// 英語テキストが意味のある長さかチェック
    /// </summary>
    private static bool IsEnglishTextMeaningful(string text)
    {
        // アルファベットの文字数をカウント
        var meaningfulChars = text.Count(char.IsLetter);
        return meaningfulChars >= MinMeaningfulEnglishLength;
    }

    /// <summary>
    /// 日本語文字かどうかを判定
    /// </summary>
    private static bool IsJapaneseCharacter(char c)
    {
        // ひらがな: U+3040-U+309F
        // カタカナ: U+30A0-U+30FF  
        // 漢字: U+4E00-U+9FAF
        var code = (int)c;
        return (code >= 0x3040 && code <= 0x309F) ||  // ひらがな
               (code >= 0x30A0 && code <= 0x30FF) ||  // カタカナ
               (code >= 0x4E00 && code <= 0x9FAF);    // 漢字
    }

    /// <summary>
    /// デバッグ用：テキストが拒否された理由を取得
    /// </summary>
    public static string GetRejectionReason(string? text, string? sourceLanguage = null)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "テキストがnullまたは空文字";

        var cleanText = text.Trim();

        // ガベージパターンチェック
        foreach (var pattern in s_garbagePatterns)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(cleanText, pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                return $"ガベージパターンに一致: {pattern}";
            }
        }

        // 長さチェック
        if (!string.IsNullOrEmpty(sourceLanguage))
        {
            return sourceLanguage.ToLowerInvariant() switch
            {
                "ja" when !IsJapaneseTextMeaningful(cleanText) =>
                    $"日本語として短すぎる（意味のある文字数: {cleanText.Count(IsJapaneseCharacter)}）",
                "en" when !IsEnglishTextMeaningful(cleanText) =>
                    $"英語として短すぎる（アルファベット数: {cleanText.Count(char.IsLetter)}）",
                _ => "品質チェック通過"
            };
        }

        return cleanText.Length < 2 ? "汎用最小長未満" : "品質チェック通過";
    }
}
