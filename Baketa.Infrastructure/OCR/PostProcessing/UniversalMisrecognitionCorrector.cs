using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.PostProcessing;

/// <summary>
/// 普遍的誤認識修正辞書システム
/// OCR精度向上ロードマップ Phase 1 - 高優先度実装
/// </summary>
public sealed class UniversalMisrecognitionCorrector
{
    private readonly ILogger<UniversalMisrecognitionCorrector> _logger;
    private readonly MisrecognitionCorrectionSettings _settings;
    private readonly Dictionary<string, CorrectionRule> _correctionRules;
    private readonly Dictionary<string, CorrectionRule> _contextualRules;

    public UniversalMisrecognitionCorrector(
        ILogger<UniversalMisrecognitionCorrector> logger,
        MisrecognitionCorrectionSettings? settings = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings ?? MisrecognitionCorrectionSettings.Default;
        
        (_correctionRules, _contextualRules) = InitializeCorrectionRules();
        
        _logger.LogInformation("普遍的誤認識修正辞書初期化完了: 基本ルール={BasicRules}個, 文脈ルール={ContextRules}個", 
            _correctionRules.Count, _contextualRules.Count);
    }

    /// <summary>
    /// TextChunkリストの誤認識を修正
    /// </summary>
    /// <param name="textChunks">修正対象のTextChunkリスト</param>
    /// <returns>修正後のTextChunkリスト</returns>
    public IReadOnlyList<TextChunk> CorrectMisrecognitions(IReadOnlyList<TextChunk> textChunks)
    {
        if (textChunks == null || textChunks.Count == 0)
            return textChunks;

        _logger.LogDebug("誤認識修正開始: {ChunkCount}個のチャンクを処理", textChunks.Count);
        
        // 直接ファイル書き込みで誤認識修正開始を記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DIRECT] UniversalMisrecognitionCorrector - 誤認識修正開始: {textChunks.Count}個のチャンク処理{Environment.NewLine}");
            
            // 処理前の各チャンクの詳細ログ出力
            for (int i = 0; i < textChunks.Count; i++)
            {
                var chunk = textChunks[i];
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📝 [DIRECT] 修正前チャンク[{i}]: Text='{chunk.CombinedText}' | ChunkId={chunk.ChunkId} | Language={chunk.DetectedLanguage ?? "unknown"}{Environment.NewLine}");
            }
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"UniversalMisrecognitionCorrector ファイル書き込みエラー: {fileEx.Message}");
        }

        var correctedChunks = new List<TextChunk>();
        var totalCorrections = 0;

        foreach (var chunk in textChunks)
        {
            var correctedChunk = CorrectSingleChunk(chunk, out int correctionCount);
            correctedChunks.Add(correctedChunk);
            totalCorrections += correctionCount;

            if (correctionCount > 0)
            {
                _logger.LogDebug("チャンク#{ChunkId}で{Count}件の修正: '{Original}' → '{Corrected}'", 
                    chunk.ChunkId, correctionCount, chunk.CombinedText, correctedChunk.CombinedText);
            }
        }

        _logger.LogInformation("誤認識修正完了: 総修正数={TotalCorrections}件", totalCorrections);
        
        // 修正完了結果をファイルログに記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ [DIRECT] UniversalMisrecognitionCorrector - 誤認識修正完了: 総修正数={totalCorrections}件{Environment.NewLine}");
            
            // 修正後の各チャンクの詳細ログ出力
            for (int i = 0; i < correctedChunks.Count; i++)
            {
                var chunk = correctedChunks[i];
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📝 [DIRECT] 修正後チャンク[{i}]: Text='{chunk.CombinedText}' | ChunkId={chunk.ChunkId}{Environment.NewLine}");
            }
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"UniversalMisrecognitionCorrector 完了ログ書き込みエラー: {fileEx.Message}");
        }
        
        return correctedChunks.AsReadOnly();
    }

    /// <summary>
    /// 単一のTextChunkを修正
    /// </summary>
    private TextChunk CorrectSingleChunk(TextChunk originalChunk, out int correctionCount)
    {
        correctionCount = 0;
        var originalText = originalChunk.CombinedText;
        
        if (string.IsNullOrWhiteSpace(originalText))
            return originalChunk;

        // 1. 基本的な文字レベル修正
        var basicCorrected = ApplyBasicCorrections(originalText, out int basicCount);
        correctionCount += basicCount;

        // 2. 文脈ベース修正
        var contextCorrected = ApplyContextualCorrections(basicCorrected, out int contextCount);
        correctionCount += contextCount;

        // 3. パターンベース修正
        var patternCorrected = ApplyPatternCorrections(contextCorrected, out int patternCount);
        correctionCount += patternCount;

        // 4. 言語固有修正
        var finalCorrected = ApplyLanguageSpecificCorrections(patternCorrected, originalChunk.DetectedLanguage, out int languageCount);
        correctionCount += languageCount;

        // 修正結果をファイルログに記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DIRECT] 修正処理詳細 - ChunkId={originalChunk.ChunkId}: '{originalText}' → '{finalCorrected}' | 修正数={correctionCount}{Environment.NewLine}");
            
            if (correctionCount > 0)
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}   └─ 修正ステップ: Basic={basicCount}, Context={contextCount}, Pattern={patternCount}, Language={languageCount}{Environment.NewLine}");
            }
        }
        catch (Exception fileEx)
        {
            System.Diagnostics.Debug.WriteLine($"UniversalMisrecognitionCorrector 修正結果ログ書き込みエラー: {fileEx.Message}");
        }

        // 修正があった場合のみ新しいチャンクを作成
        if (correctionCount == 0)
            return originalChunk;

        return CreateCorrectedChunk(originalChunk, finalCorrected);
    }

    /// <summary>
    /// 基本的な文字レベル修正を適用
    /// </summary>
    private string ApplyBasicCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        foreach (var rule in _correctionRules.Values.Where(r => r.RuleType == CorrectionRuleType.Basic))
        {
            var beforeLength = corrected.Length;
            corrected = rule.Pattern.Replace(corrected, rule.Replacement);
            
            if (corrected.Length != beforeLength || !corrected.Equals(text, StringComparison.Ordinal))
            {
                correctionCount++;
                _logger.LogTrace("基本修正適用: '{Pattern}' → '{Replacement}' in '{Text}'", 
                    rule.OriginalPattern, rule.Replacement, text);
            }
        }

        return corrected;
    }

    /// <summary>
    /// 文脈ベース修正を適用
    /// </summary>
    private string ApplyContextualCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        foreach (var rule in _contextualRules.Values.Where(r => r.RuleType == CorrectionRuleType.Contextual))
        {
            if (rule.Pattern.IsMatch(corrected))
            {
                var newCorrected = rule.Pattern.Replace(corrected, rule.Replacement);
                if (!newCorrected.Equals(corrected, StringComparison.Ordinal))
                {
                    corrected = newCorrected;
                    correctionCount++;
                    _logger.LogTrace("文脈修正適用: '{Pattern}' → '{Replacement}' in '{Text}'", 
                        rule.OriginalPattern, rule.Replacement, text);
                }
            }
        }

        return corrected;
    }

    /// <summary>
    /// パターンベース修正を適用
    /// </summary>
    private string ApplyPatternCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        // 数字と文字の混在パターン
        var numberLetterPattern = new Regex(@"(\d)[Il1](\d)", RegexOptions.Compiled);
        if (numberLetterPattern.IsMatch(corrected))
        {
            corrected = numberLetterPattern.Replace(corrected, "$1$2");
            correctionCount++;
        }

        // 連続する類似文字の修正（例: "lll" → "111" または "III"）
        var consecutivePattern = new Regex(@"([Il1]){3,}", RegexOptions.Compiled);
        var matches = consecutivePattern.Matches(corrected);
        foreach (Match match in matches)
        {
            var replacement = DetermineConsecutiveReplacement(match.Value, text);
            if (!string.IsNullOrEmpty(replacement))
            {
                corrected = corrected.Replace(match.Value, replacement);
                correctionCount++;
            }
        }

        return corrected;
    }

    /// <summary>
    /// 言語固有修正を適用
    /// </summary>
    private string ApplyLanguageSpecificCorrections(string text, string? languageCode, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        switch (languageCode?.ToLowerInvariant())
        {
            case "ja" or "jp":
                corrected = ApplyJapaneseCorrections(corrected, out correctionCount);
                break;
            case "en":
                corrected = ApplyEnglishCorrections(corrected, out correctionCount);
                break;
            case "zh" or "cn":
                corrected = ApplyChineseCorrections(corrected, out correctionCount);
                break;
            default:
                // 言語不明の場合は一般的な修正のみ
                break;
        }

        return corrected;
    }

    /// <summary>
    /// 日本語固有の修正
    /// </summary>
    private string ApplyJapaneseCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        // 日本語特有の誤認識パターン
        var japaneseCorrections = new Dictionary<string, string>
        {
            { "加", "か" },     // ユーザー報告の問題
            { "力", "カ" },     { "夕", "タ" },     { "卜", "ト" },
            { "工", "エ" },     { "人", "入" },     { "二", "ニ" },
            { "八", "ハ" },     { "木", "本" },     { "日", "目" },
            { "月", "用" },     { "石", "右" },     { "白", "自" },
            { "立", "位" },     { "古", "吉" },     { "土", "士" },
            { "千", "干" },     { "万", "方" },     { "五", "王" },
            { "ロ", "口" },     { "へ", "ヘ" },     { "ぺ", "ペ" },
            { "べ", "ベ" },     { "ゲ", "ゲ" },     { "パ", "バ" }
        };

        foreach (var (wrong, correct) in japaneseCorrections)
        {
            if (corrected.Contains(wrong))
            {
                corrected = corrected.Replace(wrong, correct);
                correctionCount++;
            }
        }

        return corrected;
    }

    /// <summary>
    /// 英語固有の修正
    /// </summary>
    private string ApplyEnglishCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        // 英語特有の誤認識パターン
        var englishCorrections = new Dictionary<string, string>
        {
            { "rn", "m" },      { "cl", "d" },      { "vv", "w" },
            { "O0", "O" },      { "0O", "O" },      { "Il", "Il" },
            { "1I", "II" },     { "I1", "II" },     { "l1", "II" },
            { "S5", "S" },      { "5S", "S" },      { "G6", "G" },
            { "6G", "G" },      { "B8", "B" },      { "8B", "B" }
        };

        foreach (var (wrong, correct) in englishCorrections)
        {
            if (corrected.Contains(wrong))
            {
                corrected = corrected.Replace(wrong, correct);
                correctionCount++;
            }
        }

        // 英語単語の修正（よくある誤認識）
        var wordCorrections = new Dictionary<string, string>
        {
            { "tlie", "the" },   { "arid", "and" },   { "witli", "with" },
            { "frorn", "from" }, { "liave", "have" }, { "tliis", "this" },
            { "tliat", "that" }, { "wlien", "when" }, { "wliere", "where" }
        };

        foreach (var (wrong, correct) in wordCorrections)
        {
            var wordPattern = new Regex($@"\b{Regex.Escape(wrong)}\b", RegexOptions.IgnoreCase);
            if (wordPattern.IsMatch(corrected))
            {
                corrected = wordPattern.Replace(corrected, correct);
                correctionCount++;
            }
        }

        return corrected;
    }

    /// <summary>
    /// 中国語固有の修正
    /// </summary>
    private string ApplyChineseCorrections(string text, out int correctionCount)
    {
        correctionCount = 0;
        var corrected = text;

        // 中国語特有の誤認識パターン（簡体字・繁体字共通）
        var chineseCorrections = new Dictionary<string, string>
        {
            { "人", "入" },     { "入", "人" },     { "木", "本" },
            { "日", "目" },     { "月", "用" },     { "石", "右" },
            { "白", "自" },     { "立", "位" },     { "古", "吉" },
            { "土", "士" },     { "千", "干" },     { "万", "方" }
        };

        foreach (var (wrong, correct) in chineseCorrections)
        {
            // 文脈を考慮した修正（簡易版）
            if (corrected.Contains(wrong) && ShouldCorrectInChineseContext(corrected, wrong, correct))
            {
                corrected = corrected.Replace(wrong, correct);
                correctionCount++;
            }
        }

        return corrected;
    }

    /// <summary>
    /// 修正後のTextChunkを作成
    /// </summary>
    private static TextChunk CreateCorrectedChunk(TextChunk originalChunk, string correctedText)
    {
        // TextResultsも更新
        var correctedResults = originalChunk.TextResults.Select(result => new Core.Abstractions.OCR.Results.PositionedTextResult
        {
            Text = result.Text == originalChunk.CombinedText ? correctedText : result.Text,
            BoundingBox = result.BoundingBox,
            Confidence = result.Confidence,
            ChunkId = result.ChunkId,
            ProcessingTime = result.ProcessingTime,
            DetectedLanguage = result.DetectedLanguage
        }).ToList();

        return new TextChunk
        {
            ChunkId = originalChunk.ChunkId,
            TextResults = correctedResults,
            CombinedBounds = originalChunk.CombinedBounds,
            CombinedText = correctedText,
            SourceWindowHandle = originalChunk.SourceWindowHandle,
            DetectedLanguage = originalChunk.DetectedLanguage,
            TranslatedText = originalChunk.TranslatedText
        };
    }

    /// <summary>
    /// 連続文字の置換を決定
    /// </summary>
    private static string DetermineConsecutiveReplacement(string consecutiveChars, string fullText)
    {
        // 数字が多い文脈では数字に変換
        if (ContainsMoreDigits(fullText))
            return new string('1', consecutiveChars.Length);

        // アルファベットが多い文脈ではアルファベットに変換
        if (ContainsMoreLetters(fullText))
            return new string('I', consecutiveChars.Length);

        // デフォルトは数字
        return new string('1', consecutiveChars.Length);
    }

    /// <summary>
    /// 中国語文脈での修正が適切かどうかを判定
    /// </summary>
    private static bool ShouldCorrectInChineseContext(string text, string wrong, string correct)
    {
        // 簡易的な文脈判定（実際の実装ではより高度な判定が必要）
        return true;
    }

    /// <summary>
    /// テキストに数字が多く含まれているかチェック
    /// </summary>
    private static bool ContainsMoreDigits(string text)
    {
        var digitCount = text.Count(char.IsDigit);
        var letterCount = text.Count(char.IsLetter);
        return digitCount > letterCount;
    }

    /// <summary>
    /// テキストに文字が多く含まれているかチェック
    /// </summary>
    private static bool ContainsMoreLetters(string text)
    {
        var letterCount = text.Count(char.IsLetter);
        var digitCount = text.Count(char.IsDigit);
        return letterCount > digitCount;
    }

    /// <summary>
    /// 修正ルールを初期化
    /// </summary>
    private static (Dictionary<string, CorrectionRule> basicRules, Dictionary<string, CorrectionRule> contextualRules) InitializeCorrectionRules()
    {
        var basicRules = new Dictionary<string, CorrectionRule>();
        var contextualRules = new Dictionary<string, CorrectionRule>();

        // 基本的な1:1文字修正
        var basicCorrections = new Dictionary<string, string>
        {
            // 数字と文字の混同
            { "0", "O" }, { "O", "0" }, { "1", "l" }, { "l", "1" }, { "I", "1" },
            { "5", "S" }, { "S", "5" }, { "6", "G" }, { "G", "6" }, { "8", "B" }, { "B", "8" },
            
            // よくある記号の混同
            { "rn", "m" }, { "cl", "d" }, { "vv", "w" }
        };

        foreach (var (wrong, correct) in basicCorrections)
        {
            var rule = new CorrectionRule
            {
                RuleType = CorrectionRuleType.Basic,
                OriginalPattern = wrong,
                Replacement = correct,
                Pattern = new Regex(Regex.Escape(wrong), RegexOptions.Compiled),
                Confidence = 0.8f,
                Description = $"Basic substitution: {wrong} → {correct}"
            };
            basicRules[wrong] = rule;
        }

        // 文脈依存の修正ルール
        var contextualPatterns = new[]
        {
            new { Pattern = @"\b(\d+)[Il](\d+)\b", Replacement = "$1$2", Description = "Numbers with letter insertion" },
            new { Pattern = @"\b[Il]{2,}\b", Replacement = "II", Description = "Multiple I/l/1 sequence" },
            new { Pattern = @"([a-z])[0O]([a-z])", Replacement = "$1o$2", Description = "Letter-number-letter pattern" }
        };

        foreach (var pattern in contextualPatterns)
        {
            var rule = new CorrectionRule
            {
                RuleType = CorrectionRuleType.Contextual,
                OriginalPattern = pattern.Pattern,
                Replacement = pattern.Replacement,
                Pattern = new Regex(pattern.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase),
                Confidence = 0.7f,
                Description = pattern.Description
            };
            contextualRules[pattern.Pattern] = rule;
        }

        return (basicRules, contextualRules);
    }
}

/// <summary>
/// 修正ルール
/// </summary>
public sealed class CorrectionRule
{
    public required CorrectionRuleType RuleType { get; init; }
    public required string OriginalPattern { get; init; }
    public required string Replacement { get; init; }
    public required Regex Pattern { get; init; }
    public required float Confidence { get; init; }
    public required string Description { get; init; }
}

/// <summary>
/// 修正ルールのタイプ
/// </summary>
public enum CorrectionRuleType
{
    Basic,      // 基本的な1:1置換
    Contextual, // 文脈依存置換
    Pattern,    // パターンベース置換
    Language    // 言語固有置換
}

/// <summary>
/// 誤認識修正の設定
/// </summary>
public sealed class MisrecognitionCorrectionSettings
{
    /// <summary>修正を適用する最小信頼度</summary>
    public float MinimumConfidenceForCorrection { get; init; } = 0.6f;

    /// <summary>文脈ベース修正を有効にするか</summary>
    public bool EnableContextualCorrections { get; init; } = true;

    /// <summary>言語固有修正を有効にするか</summary>
    public bool EnableLanguageSpecificCorrections { get; init; } = true;

    /// <summary>パターンベース修正を有効にするか</summary>
    public bool EnablePatternCorrections { get; init; } = true;

    /// <summary>修正対象とする最小テキスト長</summary>
    public int MinimumTextLengthForCorrection { get; init; } = 1;

    /// <summary>デフォルト設定</summary>
    public static MisrecognitionCorrectionSettings Default => new();

    /// <summary>保守的な修正設定</summary>
    public static MisrecognitionCorrectionSettings Conservative => new()
    {
        MinimumConfidenceForCorrection = 0.8f,
        EnableContextualCorrections = false,
        EnablePatternCorrections = false,
        MinimumTextLengthForCorrection = 2
    };

    /// <summary>積極的な修正設定</summary>
    public static MisrecognitionCorrectionSettings Aggressive => new()
    {
        MinimumConfidenceForCorrection = 0.4f,
        EnableContextualCorrections = true,
        EnableLanguageSpecificCorrections = true,
        EnablePatternCorrections = true,
        MinimumTextLengthForCorrection = 1
    };
}