namespace Baketa.Core.Utilities;

/// <summary>
/// 翻訳結果の有効性検証ユーティリティ
/// UI動作: 処理失敗時の不正な翻訳結果表示を包括的に防止
/// </summary>
public static class TranslationValidator
{
    private static readonly string[] s_errorPatterns =
    [
        "Translation Error:",
        "[翻訳エラー]",
        "翻訳エラーが発生しました",
        "[Translation Failed]",
        "[Translation Timeout]",
        "Connection Error",
        "Network Error",
        "Server Error",
        "Service Unavailable",
        "セマフォ取得タイムアウト",
        "タイムアウトしました",
        "接続に失敗",
        "サーバーエラー",
        "処理に失敗"
    ];

    /// <summary>
    /// 翻訳結果が失敗・エラー・無効かどうかを包括的に判定
    /// </summary>
    /// <param name="translatedText">翻訳結果テキスト</param>
    /// <param name="originalText">原文テキスト（省略可能）</param>
    /// <returns>有効な翻訳結果の場合true</returns>
    public static bool IsValid(string? translatedText, string? originalText = null)
    {
        // null・空文字チェックのみ - その他は全て表示
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }
            
        // 明らかなエラーメッセージの場合のみ除外
        // システムエラーメッセージは表示しない
        if (translatedText.StartsWith("Error:", StringComparison.OrdinalIgnoreCase) ||
            translatedText.StartsWith("Exception:", StringComparison.OrdinalIgnoreCase) ||
            translatedText.StartsWith("Translation Error:", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        
        // それ以外は全て有効な翻訳結果として扱う
        // OCR結果が不完全でも、ユーザーが判断できるよう表示する
        return true;
    }
}