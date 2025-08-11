using System.Text.RegularExpressions;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 文字形状類似性に基づく誤認識パターンの修正システム（簡素化版）
/// 確実で安全な修正のみを実行し、誤修正リスクを最小化
/// </summary>
public static partial class CharacterSimilarityCorrector
{
    // 記号修正パターン（安全な修正のみ）
    [GeneratedRegex(@"\.\.\.\.\.\.")]
    private static partial Regex SixDotLeaderPattern();

    [GeneratedRegex(@"\?")]
    private static partial Regex QuestionMarkPattern();

    [GeneratedRegex(@"!")]
    private static partial Regex ExclamationMarkPattern();

    // 明らかな数字パターンの修正（確実なもののみ）
    [GeneratedRegex(@"HP\s*([0-9]+)/([0-9]+)")]
    private static partial Regex HpDisplayPattern();

    [GeneratedRegex(@"MP\s*([0-9]+)/([0-9]+)")]
    private static partial Regex MpDisplayPattern();

    [GeneratedRegex(@"LV\s*([0-9]+)")]
    private static partial Regex LevelDisplayPattern();

    /// <summary>
    /// 修正信頼度を評価
    /// </summary>
    /// <param name="originalText">元テキスト</param>
    /// <param name="correctedText">修正後テキスト</param>
    /// <returns>信頼度（0.0-1.0）</returns>
    public static double EvaluateCorrectionConfidence(string originalText, string correctedText)
    {
        if (string.IsNullOrWhiteSpace(originalText) || string.IsNullOrWhiteSpace(correctedText))
            return 0.0;

        if (originalText == correctedText)
            return 1.0;

        // 簡素化：基本的な信頼度を返す
        return 0.8;
    }

    /// <summary>
    /// 文字形状類似性修正を適用（簡素化版）
    /// </summary>
    /// <param name="ocrText">OCR認識テキスト</param>
    /// <param name="enableLogging">ログ出力を有効にするか</param>
    /// <returns>修正後テキスト</returns>
    public static string CorrectSimilarityErrors(string ocrText, bool enableLogging = false)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return ocrText;

        var correctedText = ocrText;
        var corrections = new List<string>();

        // 安全な記号修正のみ実行
        correctedText = ApplySymbolCorrections(correctedText, corrections, enableLogging);

        // ゲーム特有の確実な修正のみ実行
        correctedText = ApplyGameSpecificCorrections(correctedText, corrections, enableLogging);

        // ログ出力
        if (enableLogging && corrections.Count > 0)
        {
            Console.WriteLine($"🔧 文字類似性修正結果:");
            Console.WriteLine($"   📝 元テキスト: 「{ocrText}」");
            Console.WriteLine($"   ✅ 修正後: 「{correctedText}」");
            Console.WriteLine($"   🔄 適用修正: {corrections.Count}件");
            foreach (var correction in corrections)
            {
                Console.WriteLine($"      - {correction}");
            }
        }

        return correctedText;
    }

    /// <summary>
    /// 記号・句読点修正を適用（安全なもののみ）
    /// </summary>
    private static string ApplySymbolCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, SixDotLeaderPattern(), "......", "6点リーダー正規化", corrections, enableLogging);
        result = ApplyCorrection(result, QuestionMarkPattern(), "？", "半角?→全角？", corrections, enableLogging);
        result = ApplyCorrection(result, ExclamationMarkPattern(), "！", "半角!→全角！", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// ゲーム特有表現修正を適用（確実なもののみ）
    /// </summary>
    private static string ApplyGameSpecificCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, HpDisplayPattern(), "HP $1/$2", "HP表示正規化", corrections, enableLogging);
        result = ApplyCorrection(result, MpDisplayPattern(), "MP $1/$2", "MP表示正規化", corrections, enableLogging);
        result = ApplyCorrection(result, LevelDisplayPattern(), "LV $1", "レベル表示正規化", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// 修正処理を適用するヘルパーメソッド
    /// </summary>
    private static string ApplyCorrection(string text, Regex pattern, string replacement, string description, List<string> corrections, bool enableLogging)
    {
        if (!pattern.IsMatch(text))
            return text;

        var result = pattern.Replace(text, replacement);
        corrections.Add(description);

        if (enableLogging)
        {
            Console.WriteLine($"      🔄 {description}: 「{text}」→「{result}」");
        }

        return result;
    }
}