using System.Text.RegularExpressions;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 文字形状類似性に基づく誤認識パターンの修正システム
/// 日本語ゲームテキストでよく発生する誤認識パターンを自動修正
/// </summary>
public static partial class CharacterSimilarityCorrector
{
    // 漢字類似性修正パターン
    [GeneratedRegex(@"([。、\s]?)瞬だけ")]
    private static partial Regex ShunDakePattern();

    [GeneratedRegex(@"瞬間だけ")]
    private static partial Regex ShunkanDakePattern();

    [GeneratedRegex(@"瞬時")]
    private static partial Regex ShunjiPattern();

    [GeneratedRegex(@"臭ささ")]
    private static partial Regex KusasasaPattern();

    [GeneratedRegex(@"生臭ささ")]
    private static partial Regex NamagusasasaPattern();

    [GeneratedRegex(@"感しる")]
    private static partial Regex KanshiruPattern();

    [GeneratedRegex(@"感する")]
    private static partial Regex KansuruPattern();

    [GeneratedRegex(@"困錐")]
    private static partial Regex KonsuiPattern();

    [GeneratedRegex(@"複錐")]
    private static partial Regex FukusuiPattern();

    [GeneratedRegex(@"([0-9]+)個")]
    private static partial Regex NumberKoPattern();

    [GeneratedRegex(@"第([0-9]+)")]
    private static partial Regex DaiNumberPattern();

    [GeneratedRegex(@"([0-9]+)番")]
    private static partial Regex NumberBanPattern();

    [GeneratedRegex(@"([ぁ-ん])ぞ([ぁ-んァ-ヶー])")]
    private static partial Regex ZoToMoPattern();

    // かな類似性修正パターン
    [GeneratedRegex(@"ぞ")]
    private static partial Regex ZoPattern();

    [GeneratedRegex(@"ク")]
    private static partial Regex KuPattern();

    [GeneratedRegex(@"サ")]
    private static partial Regex SaPattern();

    [GeneratedRegex(@"タ")]
    private static partial Regex TaPattern();

    // 記号修正パターン
    [GeneratedRegex(@"\.\.\.\.\.\.")]
    private static partial Regex SixDotLeaderPattern();

    [GeneratedRegex(@"\.\.\.")]
    private static partial Regex ThreeDotLeaderPattern();

    [GeneratedRegex(@"。。。")]
    private static partial Regex KutenLeaderPattern();

    [GeneratedRegex(@"、、、")]
    private static partial Regex ToutenLeaderPattern();

    [GeneratedRegex(@"\?")]
    private static partial Regex QuestionMarkPattern();

    [GeneratedRegex(@"!")]
    private static partial Regex ExclamationMarkPattern();

    // ゲーム特有修正パターン
    [GeneratedRegex(@"HP\s*([0-9]+)/([0-9]+)")]
    private static partial Regex HpDisplayPattern();

    [GeneratedRegex(@"MP\s*([0-9]+)/([0-9]+)")]
    private static partial Regex MpDisplayPattern();

    [GeneratedRegex(@"LV\s*([0-9]+)")]
    private static partial Regex LevelDisplayPattern();

    [GeneratedRegex(@"([ぁ-んァ-ヶー一-龠]+)x([0-9]+)")]
    private static partial Regex ItemCountPattern();

    // 文脈修正パターン
    [GeneratedRegex(@"それより([ぁ-んァ-ヶー一-龠]+)てくれ")]
    private static partial Regex SoreyoriPattern();

    [GeneratedRegex(@"([ぁ-んァ-ヶー一-龠]+)でよくわからない")]
    private static partial Regex WakaranaiaPattern();

    [GeneratedRegex(@"ちょっと([ぁ-んァ-ヶー一-龠]+)もある")]
    private static partial Regex ChottoAruPattern();

    [GeneratedRegex(@"([ぁ-んァ-ヶー一-龠]+)も感じる")]
    private static partial Regex KanjiruMoPattern();

    [GeneratedRegex(@"([ぁ-んァ-ヶー一-龠]+)を感じる")]
    private static partial Regex KanjiruWoPattern();

    [GeneratedRegex(@"一瞬だけ([ぁ-んァ-ヶー一-龠]+)")]
    private static partial Regex IcshunDakePattern();

    /// <summary>
    /// OCR認識結果を文字形状類似性に基づいて修正
    /// </summary>
    /// <param name="ocrText">OCR認識結果テキスト</param>
    /// <param name="enableLogging">修正ログの出力を有効にするか</param>
    /// <returns>修正後のテキスト</returns>
    public static string CorrectSimilarityErrors(string ocrText, bool enableLogging = false)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
            return ocrText;

        var correctedText = ocrText;
        var corrections = new List<string>();

        // 1. 漢字類似性修正
        correctedText = ApplyKanjiCorrections(correctedText, corrections, enableLogging);

        // 2. かな類似性修正  
        correctedText = ApplyKanaCorrections(correctedText, corrections, enableLogging);

        // 3. 記号・句読点修正
        correctedText = ApplySymbolCorrections(correctedText, corrections, enableLogging);

        // 4. ゲーム特有表現修正
        correctedText = ApplyGameSpecificCorrections(correctedText, corrections, enableLogging);

        // 5. 文脈修正
        correctedText = ApplyContextualCorrections(correctedText, corrections, enableLogging);

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
    /// 漢字類似性修正を適用
    /// </summary>
    private static string ApplyKanjiCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, ShunDakePattern(), "$1一瞬だけ", "「瞬だけ」→「一瞬だけ」", corrections, enableLogging);
        result = ApplyCorrection(result, ShunkanDakePattern(), "一瞬間だけ", "「瞬間だけ」→「一瞬間だけ」", corrections, enableLogging);
        result = ApplyCorrection(result, ShunjiPattern(), "一瞬時", "「瞬時」→「一瞬時」", corrections, enableLogging);
        result = ApplyCorrection(result, KusasasaPattern(), "臭さも", "「臭ささ」→「臭さも」", corrections, enableLogging);
        result = ApplyCorrection(result, NamagusasasaPattern(), "生臭さも", "「生臭ささ」→「生臭さも」", corrections, enableLogging);
        result = ApplyCorrection(result, KanshiruPattern(), "感じる", "「感しる」→「感じる」", corrections, enableLogging);
        result = ApplyCorrection(result, KansuruPattern(), "感じる", "「感する」→「感じる」", corrections, enableLogging);
        result = ApplyCorrection(result, KonsuiPattern(), "困難", "「困錐」→「困難」", corrections, enableLogging);
        result = ApplyCorrection(result, FukusuiPattern(), "複雑", "「複錐」→「複雑」", corrections, enableLogging);
        result = ApplyCorrection(result, NumberKoPattern(), "$1個", "数字+個の正規化", corrections, enableLogging);
        result = ApplyCorrection(result, DaiNumberPattern(), "第$1", "「第○」の正規化", corrections, enableLogging);
        result = ApplyCorrection(result, NumberBanPattern(), "$1番", "「○番」の正規化", corrections, enableLogging);
        result = ApplyCorrection(result, ZoToMoPattern(), "$1も$2", "「ぞ」→「も」助詞修正", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// かな類似性修正を適用
    /// </summary>
    private static string ApplyKanaCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, ZoPattern(), "も", "「ぞ」→「も」", corrections, enableLogging);
        result = ApplyCorrection(result, KuPattern(), "グ", "「ク」→「グ」（濁点）", corrections, enableLogging);
        result = ApplyCorrection(result, SaPattern(), "ザ", "「サ」→「ザ」（濁点）", corrections, enableLogging);
        result = ApplyCorrection(result, TaPattern(), "ダ", "「タ」→「ダ」（濁点）", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// 記号・句読点修正を適用
    /// </summary>
    private static string ApplySymbolCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, SixDotLeaderPattern(), "......", "6点リーダー正規化", corrections, enableLogging);
        result = ApplyCorrection(result, ThreeDotLeaderPattern(), "...", "3点リーダー正規化", corrections, enableLogging);
        result = ApplyCorrection(result, KutenLeaderPattern(), "...", "句点連続→リーダー", corrections, enableLogging);
        result = ApplyCorrection(result, ToutenLeaderPattern(), "...", "読点連続→リーダー", corrections, enableLogging);
        result = ApplyCorrection(result, QuestionMarkPattern(), "？", "半角?→全角？", corrections, enableLogging);
        result = ApplyCorrection(result, ExclamationMarkPattern(), "！", "半角!→全角！", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// ゲーム特有表現修正を適用
    /// </summary>
    private static string ApplyGameSpecificCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, HpDisplayPattern(), "HP $1/$2", "HP表示正規化", corrections, enableLogging);
        result = ApplyCorrection(result, MpDisplayPattern(), "MP $1/$2", "MP表示正規化", corrections, enableLogging);
        result = ApplyCorrection(result, LevelDisplayPattern(), "LV $1", "レベル表示正規化", corrections, enableLogging);
        result = ApplyCorrection(result, ItemCountPattern(), "$1×$2", "アイテム個数表示", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// 文脈修正を適用
    /// </summary>
    private static string ApplyContextualCorrections(string text, List<string> corrections, bool enableLogging)
    {
        var result = text;

        result = ApplyCorrection(result, SoreyoriPattern(), "それより$1てくれ", "「それより～してくれ」パターン", corrections, enableLogging);
        result = ApplyCorrection(result, WakaranaiaPattern(), "$1でよくわからない", "理解困難表現", corrections, enableLogging);
        result = ApplyCorrection(result, ChottoAruPattern(), "ちょっと$1もある", "「ちょっと～もある」パターン", corrections, enableLogging);
        result = ApplyCorrection(result, KanjiruMoPattern(), "$1も感じる", "感覚表現", corrections, enableLogging);
        result = ApplyCorrection(result, KanjiruWoPattern(), "$1を感じる", "感覚表現", corrections, enableLogging);
        result = ApplyCorrection(result, IcshunDakePattern(), "一瞬だけ$1", "瞬間表現", corrections, enableLogging);

        return result;
    }

    /// <summary>
    /// 個別修正パターンを適用
    /// </summary>
    private static string ApplyCorrection(string text, Regex pattern, string replacement, string description, List<string> corrections, bool enableLogging)
    {
        var beforeMatch = text;
        var result = pattern.Replace(text, replacement);

        if (beforeMatch != result && enableLogging)
        {
            corrections.Add($"修正適用: {description}");
        }

        return result;
    }

    /// <summary>
    /// 特定の誤認識パターンに対する信頼度を評価
    /// </summary>
    /// <param name="originalText">元のOCRテキスト</param>
    /// <param name="correctedText">修正後のテキスト</param>
    /// <returns>修正の信頼度（0.0-1.0）</returns>
    public static double EvaluateCorrectionConfidence(string originalText, string correctedText)
    {
        if (originalText == correctedText)
            return 1.0; // 修正なし = 高信頼度

        // 修正された文字数の比率
        var editDistance = CalculateEditDistance(originalText, correctedText);
        var maxLength = Math.Max(originalText.Length, correctedText.Length);
        
        if (maxLength == 0)
            return 1.0;

        // 修正率が低いほど信頼度が高い
        var correctionRatio = (double)editDistance / maxLength;
        var confidence = Math.Max(0.0, 1.0 - correctionRatio * 2); // 50%以上修正で信頼度0

        // 既知の高確率修正パターンにボーナス
        if (correctedText.Contains("一瞬だけ") && originalText.Contains("瞬だけ"))
        {
            confidence += 0.2;
        }
        
        if (correctedText.Contains("も感じる") && originalText.Contains("さ感じる"))
        {
            confidence += 0.15;
        }

        return Math.Min(1.0, confidence);
    }

    /// <summary>
    /// レーベンシュタイン距離を計算
    /// </summary>
    private static int CalculateEditDistance(string s1, string s2)
    {
        var len1 = s1.Length;
        var len2 = s2.Length;
        var matrix = new int[len1 + 1, len2 + 1];

        for (int i = 0; i <= len1; i++)
            matrix[i, 0] = i;
        for (int j = 0; j <= len2; j++)
            matrix[0, j] = j;

        for (int i = 1; i <= len1; i++)
        {
            for (int j = 1; j <= len2; j++)
            {
                var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                matrix[i, j] = Math.Min(
                    Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                    matrix[i - 1, j - 1] + cost);
            }
        }

        return matrix[len1, len2];
    }

    /// <summary>
    /// 修正パターンの統計情報を取得
    /// </summary>
    /// <returns>パターン統計</returns>
    public static (int TotalPatterns, Dictionary<string, int> CategoryCounts) GetPatternStatistics()
    {
        var categoryStats = new Dictionary<string, int>
        {
            ["漢字類似性"] = 13,
            ["かな類似性"] = 4,
            ["記号"] = 6,
            ["ゲーム特有"] = 4,
            ["文脈"] = 6
        };

        var total = categoryStats.Values.Sum();
        return (total, categoryStats);
    }
}