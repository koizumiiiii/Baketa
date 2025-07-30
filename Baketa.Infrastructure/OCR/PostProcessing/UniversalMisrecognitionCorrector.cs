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
            return textChunks ?? [];

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

        var currentText = originalText;

        // 1. 基本的な文字レベル修正
        var basicCorrected = ApplyBasicCorrections(currentText, out int basicCount);
        correctionCount += basicCount;
        currentText = basicCorrected;

        // 2. 文脈ベース修正
        var contextCorrected = ApplyContextualCorrections(currentText, out int contextCount);
        correctionCount += contextCount;
        currentText = contextCorrected;

        // 3. パターンベース修正
        var patternCorrected = ApplyPatternCorrections(currentText, out int patternCount);
        correctionCount += patternCount;
        currentText = patternCorrected;

        // 4. 言語固有修正
        var finalCorrected = ApplyLanguageSpecificCorrections(currentText, originalChunk.DetectedLanguage, out int languageCount);
        correctionCount += languageCount;

        // 【Phase 2ログ強化】修正処理の詳細ログ記録
        try
        {
            System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 [DIRECT] 修正処理詳細 - ChunkId={originalChunk.ChunkId}: '{originalText}' → '{finalCorrected}' | 修正数={correctionCount}{Environment.NewLine}");
            
            if (correctionCount > 0)
            {
                System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}   └─ 修正ステップ: Basic={basicCount}, Context={contextCount}, Pattern={patternCount}, Language={languageCount}{Environment.NewLine}");
                
                // 各段階の変化をログ出力
                if (basicCount > 0)
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}     🔸 Basic修正: '{originalText}' → '{basicCorrected}'{Environment.NewLine}");
                }
                if (contextCount > 0)
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}     🔸 Context修正: '{basicCorrected}' → '{contextCorrected}'{Environment.NewLine}");
                }
                if (patternCount > 0)
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}     🔸 Pattern修正: '{contextCorrected}' → '{patternCorrected}'{Environment.NewLine}");
                }
                if (languageCount > 0)
                {
                    System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}     🔸 Language修正: '{patternCorrected}' → '{finalCorrected}'{Environment.NewLine}");
                }
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
    private static bool ShouldCorrectInChineseContext(string _, string __, string ___)
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
            { "rn", "m" }, { "cl", "d" }, { "vv", "w" },
            
            // 【Phase 2拡充】中国語→日本語文字修正（ゲーム頻出パターン）
            // 実際のログから確認された誤認識パターン
            { "开", "開" },     // ひらく - ログ確認済み
            { "过", "過" },     // すぎる - ログ確認済み  
            { "个", "個" },     // こ - ログ確認済み
            { "间", "間" },     // あいだ
            { "时", "時" },     // とき
            { "长", "長" },     // ながい
            { "门", "門" },     // もん
            { "车", "車" },     // くるま
            { "马", "馬" },     // うま
            { "鸟", "鳥" },     // とり
            { "龙", "龍" },     // りゅう
            { "岛", "島" },     // しま
            { "国", "國" },     // くに（旧字体対応）
            { "东", "東" },     // ひがし
            { "西", "西" },     // にし（同じ字体）
            { "南", "南" },     // みなみ（同じ字体）
            { "北", "北" },     // きた（同じ字体）
            { "风", "風" },     // かぜ
            { "雨", "雨" },     // あめ（同じ字体）
            { "雪", "雪" },     // ゆき（同じ字体）
            { "山", "山" },     // やま（同じ字体）
            { "水", "水" },     // みず（同じ字体）
            { "火", "火" },     // ひ（同じ字体）
            { "土", "土" },     // つち（同じ字体）
            { "木", "木" },     // き（同じ字体）
            { "金", "金" },     // きん（同じ字体）
            { "银", "銀" },     // ぎん
            { "铜", "銅" },     // どう
            { "铁", "鐵" },     // てつ
            { "钢", "鋼" },     // はがね
            { "宝", "寶" },     // たから
            { "书", "書" },     // しょ
            { "画", "畫" },     // が
            { "乐", "樂" },     // らく
            { "药", "藥" },     // やく
            { "医", "醫" },     // い
            { "农", "農" },     // のう
            { "工", "工" },     // こう（同じ字体）
            { "商", "商" },     // しょう（同じ字体）
            { "学", "學" },     // がく
            { "教", "教" },     // きょう（同じ字体）
            { "师", "師" },     // し
            { "生", "生" },     // せい（同じ字体）
            { "死", "死" },     // し（同じ字体）
            { "活", "活" },     // かつ（同じ字体）
            { "动", "動" },     // どう
            { "静", "靜" },     // せい
            { "快", "快" },     // かい（同じ字体）
            { "慢", "慢" },     // まん（同じ字体）
            { "强", "強" },     // きょう
            { "弱", "弱" },     // じゃく（同じ字体）
            { "高", "高" },     // こう（同じ字体）
            { "低", "低" },     // てい（同じ字体）
            { "大", "大" },     // だい（同じ字体）
            { "小", "小" },     // しょう（同じ字体）
            { "多", "多" },     // た（同じ字体）
            { "少", "少" },     // しょう（同じ字体）
            { "新", "新" },     // しん（同じ字体）
            { "旧", "舊" },     // きゅう
            { "老", "老" },     // ろう（同じ字体）
            { "年", "年" },     // ねん（同じ字体）
            { "月", "月" },     // げつ（同じ字体）
            { "日", "日" },     // にち（同じ字体）
            { "星", "星" },     // せい（同じ字体）
            { "天", "天" },     // てん（同じ字体）
            { "地", "地" },     // ち（同じ字体）
            { "人", "人" },     // じん（同じ字体）
            { "男", "男" },     // だん（同じ字体）
            { "女", "女" },     // じょ（同じ字体）
            { "子", "子" },     // し（同じ字体）
            { "父", "父" },     // ふ（同じ字体）
            { "母", "母" },     // ぼ（同じ字体）
            { "兄", "兄" },     // けい（同じ字体）
            { "弟", "弟" },     // てい（同じ字体）
            { "姐", "姉" },     // し
            { "妹", "妹" },     // まい（同じ字体）
            { "友", "友" },     // ゆう（同じ字体）
            { "敌", "敵" },     // てき
            { "战", "戰" },     // せん
            { "胜", "勝" },     // しょう
            { "败", "敗" },     // はい
            { "输", "輸" },     // ゆ
            { "赢", "贏" },     // えい
            { "买", "買" },     // ばい
            { "卖", "賣" },     // ばい
            { "钱", "錢" },     // せん
            { "富", "富" },     // ふ（同じ字体）
            { "穷", "窮" },     // きゅう
            { "饿", "餓" },     // が
            { "饱", "飽" },     // ほう
            { "吃", "吃" },     // きつ（同じ字体）
            { "喝", "喝" },     // かつ（同じ字体）
            { "睡", "睡" },     // すい（同じ字体）
            { "醒", "醒" },     // せい（同じ字体）
            { "走", "走" },     // そう（同じ字体）
            { "跑", "跑" },     // ほう（同じ字体）
            { "飞", "飛" },     // ひ
            { "游", "游" },     // ゆう（同じ字体）
            { "潜", "潛" },     // せん
            { "爬", "爬" },     // は（同じ字体）
            { "跳", "跳" },     // ちょう（同じ字体）
            { "运", "運" },     // うん
            { "动作", "動作" }, // どうさ
            { "动物", "動物" }, // どうぶつ
            { "植物", "植物" }, // しょくぶつ（同じ字体）
            { "动画", "動畫" }, // どうが
            { "电影", "電影" }, // でんえい
            { "游戏", "遊戯" }, // ゆうぎ
            { "运动", "運動" }, // うんどう
            { "体育", "體育" }, // たいいく
            { "练习", "練習" }, // れんしゅう
            { "训练", "訓練" }, // くんれん
            { "准备", "準備" }, // じゅんび
            { "开始", "開始" }, // かいし
            { "结束", "結束" }, // けっそく
            { "完成", "完成" }, // かんせい（同じ字体）
            { "失败", "失敗" }, // しっぱい
            { "成功", "成功" }, // せいこう（同じ字体）
            { "进攻", "進攻" }, // しんこう
            { "防御", "防禦" }, // ぼうぎょ
            { "攻击", "攻擊" }, // こうげき
            { "防守", "防守" }, // ぼうしゅ（同じ字体）
            { "侵略", "侵略" }, // しんりゃく（同じ字体）
            { "占领", "占領" }, // せんりょう
            { "统治", "統治" }, // とうち
            { "管理", "管理" }, // かんり（同じ字体）
            { "控制", "控制" }, // こうせい（同じ字体）
            { "操作", "操作" }, // そうさ（同じ字体）
            { "选择", "選擇" }, // せんたく
            { "决定", "決定" }, // けってい
            { "判断", "判斷" }, // はんだん
            { "思考", "思考" }, // しこう（同じ字体）  
            { "计划", "計畫" }, // けいかく
            { "策略", "策略" }, // さくりゃく（同じ字体）
            { "战术", "戰術" }, // せんじゅつ
            { "技术", "技術" }, // ぎじゅつ
            { "技能", "技能" }, // ぎのう（同じ字体）
            { "能力", "能力" }, // のうりょく（同じ字体）
            { "力量", "力量" }, // りきりょう（同じ字体）
            { "实力", "實力" }, // じつりょく
            { "潜力", "潛力" }, // せんりょく
            { "经验", "經驗" }, // けいけん
            { "知识", "知識" }, // ちしき
            { "智慧", "智慧" }, // ちえ（同じ字体）
            { "学习", "學習" }, // がくしゅう
            // { "练习", "練習" }, // れんしゅう - 重複のため削除（610行で定義済み）
            { "掌握", "掌握" }, // しょうあく（同じ字体）
            { "理解", "理解" }, // りかい（同じ字体）
            { "明白", "明白" }, // めいはく（同じ字体）
            { "清楚", "清楚" }, // せいそ（同じ字体）
            { "糊涂", "糊塗" }, // こと
            { "困惑", "困惑" }, // こんわく（同じ字体）
            { "迷惑", "迷惑" }, // めいわく（同じ字体）
            { "烦恼", "煩惱" }, // はんのう
            { "担心", "擔心" }, // たんしん
            { "害怕", "害怕" }, // がいは（同じ字体）
            { "恐惧", "恐懼" }, // きょうく
            { "勇敢", "勇敢" }, // ゆうかん（同じ字体）
            { "勇气", "勇氣" }, // ゆうき
            { "信心", "信心" }, // しんしん（同じ字体）
            { "信任", "信任" }, // しんにん（同じ字体）
            { "相信", "相信" }, // そうしん（同じ字体）
            { "怀疑", "懷疑" }  // かいぎ
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
            new { Pattern = @"([a-z])[0O]([a-z])", Replacement = "$1o$2", Description = "Letter-number-letter pattern" },
            
            // 【Phase 2拡充】中国語→日本語の文脈ベース修正
            // 複合語パターンの修正
            new { Pattern = @"侵略への备", Replacement = "侵略への備え", Description = "Chinese character in Japanese context: 备→備え" },
            new { Pattern = @"过方", Replacement = "過方", Description = "Chinese to Japanese compound: 过方→過方" },
            new { Pattern = @"开始", Replacement = "開始", Description = "Chinese to Japanese compound: 开始→開始" },
            new { Pattern = @"结束", Replacement = "結束", Description = "Chinese to Japanese compound: 结束→結束" },
            new { Pattern = @"进攻", Replacement = "進攻", Description = "Chinese to Japanese compound: 进攻→進攻" },
            new { Pattern = @"防御", Replacement = "防禦", Description = "Chinese to Japanese compound: 防御→防禦" },
            new { Pattern = @"攻击", Replacement = "攻擊", Description = "Chinese to Japanese compound: 攻击→攻擊" },
            new { Pattern = @"训练", Replacement = "訓練", Description = "Chinese to Japanese compound: 训练→訓練" },
            new { Pattern = @"练习", Replacement = "練習", Description = "Chinese to Japanese compound: 练习→練習" },
            new { Pattern = @"准备", Replacement = "準備", Description = "Chinese to Japanese compound: 准备→準備" },
            new { Pattern = @"选择", Replacement = "選擇", Description = "Chinese to Japanese compound: 选择→選擇" },
            new { Pattern = @"决定", Replacement = "決定", Description = "Chinese to Japanese compound: 决定→決定" },
            new { Pattern = @"计划", Replacement = "計畫", Description = "Chinese to Japanese compound: 计划→計畫" },
            new { Pattern = @"战术", Replacement = "戰術", Description = "Chinese to Japanese compound: 战术→戰術" },
            new { Pattern = @"技术", Replacement = "技術", Description = "Chinese to Japanese compound: 技术→技術" },
            new { Pattern = @"经验", Replacement = "經驗", Description = "Chinese to Japanese compound: 经验→經驗" },
            new { Pattern = @"学习", Replacement = "學習", Description = "Chinese to Japanese compound: 学习→學習" },
            new { Pattern = @"实力", Replacement = "實力", Description = "Chinese to Japanese compound: 实力→實力" },
            new { Pattern = @"潜力", Replacement = "潛力", Description = "Chinese to Japanese compound: 潜力→潛力" },
            new { Pattern = @"动作", Replacement = "動作", Description = "Chinese to Japanese compound: 动作→動作" },
            new { Pattern = @"运动", Replacement = "運動", Description = "Chinese to Japanese compound: 运动→運動" },
            new { Pattern = @"体育", Replacement = "體育", Description = "Chinese to Japanese compound: 体育→體育" },
            new { Pattern = @"动画", Replacement = "動畫", Description = "Chinese to Japanese compound: 动画→動畫" },
            new { Pattern = @"电影", Replacement = "電影", Description = "Chinese to Japanese compound: 电影→電影" },
            new { Pattern = @"游戏", Replacement = "遊戯", Description = "Chinese to Japanese compound: 游戏→遊戯" },
            
            // 単字の連続パターン修正
            new { Pattern = @"个个", Replacement = "個個", Description = "Chinese repetition: 个个→個個" },
            new { Pattern = @"时时", Replacement = "時時", Description = "Chinese repetition: 时时→時時" },
            new { Pattern = @"处处", Replacement = "處處", Description = "Chinese repetition: 处处→處處" },
            new { Pattern = @"间间", Replacement = "間間", Description = "Chinese repetition: 间间→間間" },
            
            // 数字との組み合わせパターン
            new { Pattern = @"(\d+)个", Replacement = "$1個", Description = "Number + Chinese counter: 个→個" },
            new { Pattern = @"(\d+)时", Replacement = "$1時", Description = "Number + Chinese time: 时→時" },
            new { Pattern = @"(\d+)门", Replacement = "$1門", Description = "Number + Chinese counter: 门→門" },
            new { Pattern = @"(\d+)间", Replacement = "$1間", Description = "Number + Chinese counter: 间→間" },
            
            // ひらがなとの組み合わせパターン
            new { Pattern = @"([あ-ん])个([あ-ん])", Replacement = "$1個$2", Description = "Hiragana + Chinese character + Hiragana: 个→個" },
            new { Pattern = @"([あ-ん])时([あ-ん])", Replacement = "$1時$2", Description = "Hiragana + Chinese character + Hiragana: 时→時" },
            new { Pattern = @"([あ-ん])开([あ-ん])", Replacement = "$1開$2", Description = "Hiragana + Chinese character + Hiragana: 开→開" },
            new { Pattern = @"([あ-ん])过([あ-ん])", Replacement = "$1過$2", Description = "Hiragana + Chinese character + Hiragana: 过→過" },
            
            // カタカナとの組み合わせパターン
            new { Pattern = @"([ア-ン])个([ア-ン])", Replacement = "$1個$2", Description = "Katakana + Chinese character + Katakana: 个→個" },
            new { Pattern = @"([ア-ン])开([ア-ン])", Replacement = "$1開$2", Description = "Katakana + Chinese character + Katakana: 开→開" },
            new { Pattern = @"([ア-ン])过([ア-ン])", Replacement = "$1過$2", Description = "Katakana + Chinese character + Katakana: 过→過" }
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