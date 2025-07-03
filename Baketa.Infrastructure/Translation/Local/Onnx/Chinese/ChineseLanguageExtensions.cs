using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Translation.Models;

namespace Baketa.Infrastructure.Translation.Local.Onnx.Chinese;

/// <summary>
/// 中国語翻訳のための拡張メソッド
/// </summary>
public static class ChineseLanguageExtensions
{
    /// <summary>
    /// 中国語系の言語かどうかを判定
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>中国語系の場合はtrue</returns>
    public static bool IsChinese(this Language language)
    {
        if (language == null)
        {
            return false;
        }

        var code = language.Code?.ToUpperInvariant();
        return code != null && (
            code.StartsWith("ZH", StringComparison.Ordinal) ||
            code.StartsWith("CMN", StringComparison.Ordinal) ||
            code.StartsWith("YUE", StringComparison.Ordinal) ||
            code == "ZHO");
    }
    
    /// <summary>
    /// 中国語系の言語かどうかを判定（LanguageInfo用）
    /// </summary>
    /// <param name="languageInfo">言語情報オブジェクト</param>
    /// <returns>中国語系の場合はtrue</returns>
    public static bool IsChinese(this LanguageInfo languageInfo)
    {
        if (languageInfo == null)
        {
            return false;
        }

        var code = languageInfo.Code?.ToUpperInvariant();
        return code != null && (
            code.StartsWith("ZH", StringComparison.Ordinal) ||
            code.StartsWith("CMN", StringComparison.Ordinal) ||
            code.StartsWith("YUE", StringComparison.Ordinal) ||
            code == "ZHO");
    }

    /// <summary>
    /// 簡体字系の言語かどうかを判定
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>簡体字系の場合はtrue</returns>
    public static bool IsSimplifiedChinese(this Language language)
    {
        if (language == null)
        {
            return false;
        }

        var code = language.Code?.ToUpperInvariant();
        return code != null && (
            code == "ZH-CN" ||
            code == "ZH-HANS" ||
            code == "ZH-CHS" ||
            code == "CMN_HANS" ||
            code == "ZHO_HANS" ||
            (code == "ZH" && language.RegionCode?.ToUpperInvariant() == "CN"));
    }

    /// <summary>
    /// 繁体字系の言語かどうかを判定
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>繁体字系の場合はtrue</returns>
    public static bool IsTraditionalChinese(this Language language)
    {
        if (language == null)
        {
            return false;
        }

        var code = language.Code?.ToUpperInvariant();
        var region = language.RegionCode?.ToUpperInvariant();
        
        return code != null && (
            code == "ZH-TW" ||
            code == "ZH-HK" ||
            code == "ZH-MO" ||
            code == "ZH-HANT" ||
            code == "ZH-CHT" ||
            code == "CMN_HANT" ||
            code == "ZHO_HANT" ||
            (code == "ZH" && (region == "TW" || region == "HK" || region == "MO")));
    }

    /// <summary>
    /// 広東語系の言語かどうかを判定
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>広東語系の場合はtrue</returns>
    public static bool IsCantonese(this Language language)
    {
        if (language == null)
        {
            return false;
        }

        var code = language.Code?.ToUpperInvariant();
        return code != null && code.StartsWith("YUE", StringComparison.Ordinal);
    }

    /// <summary>
    /// 中国語の文字体系を説明するテキストを取得
    /// </summary>
    /// <param name="language">言語オブジェクト</param>
    /// <returns>文字体系の説明</returns>
    public static string GetChineseScriptDescription(this Language language)
    {
        if (language == null || !language.IsChinese())
        {
            return "非中国語";
        }

        if (language.IsSimplifiedChinese())
        {
            return "簡体字";
        }

        if (language.IsTraditionalChinese())
        {
            return "繁体字";
        }

        if (language.IsCantonese())
        {
            return "広東語";
        }

        return "中国語（種別不明）";
    }

    /// <summary>
    /// サポートされている中国語言語のリストを取得
    /// </summary>
    /// <returns>中国語言語のリスト</returns>
    public static IReadOnlyList<Language> GetSupportedChineseLanguages()
    {
        return new List<Language>
        {
            Language.ChineseSimplified,
            Language.ChineseTraditional,
            new() { Code = "zh", DisplayName = "中国語（自動判別）", NativeName = "中文" },
            new() { Code = "zh-Hans", DisplayName = "中国語（簡体字）", NativeName = "中文（简体）" },
            new() { Code = "zh-Hant", DisplayName = "中国語（繁体字）", NativeName = "中文（繁體）" },
            new() { Code = "yue", DisplayName = "広東語", NativeName = "粵語" },
            new() { Code = "yue-HK", DisplayName = "広東語（香港）", NativeName = "粵語（香港）", RegionCode = "HK" },
            new() { Code = "cmn", DisplayName = "標準中国語", NativeName = "國語/普通話" },
            new() { Code = "cmn_Hans", DisplayName = "標準中国語（簡体字）", NativeName = "國語/普通話（简体）" },
            new() { Code = "cmn_Hant", DisplayName = "標準中国語（繁体字）", NativeName = "國語/普通話（繁體）" }
        }.AsReadOnly();
    }

    /// <summary>
    /// 言語コードから最適な中国語言語オブジェクトを取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>対応する言語オブジェクト（見つからない場合はnull）</returns>
    public static Language? GetChineseLanguageByCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var supportedLanguages = GetSupportedChineseLanguages();
        var normalizedCode = languageCode.Trim();

        // 完全一致を最初に試行
        var exactMatch = supportedLanguages.FirstOrDefault(lang => 
            string.Equals(lang.Code, normalizedCode, StringComparison.OrdinalIgnoreCase));
        
        if (exactMatch != null)
        {
            return exactMatch;
        }

        // 部分一致を試行
        var partialMatch = supportedLanguages.FirstOrDefault(lang => 
            lang.Code.StartsWith(normalizedCode, StringComparison.OrdinalIgnoreCase) ||
            normalizedCode.StartsWith(lang.Code, StringComparison.OrdinalIgnoreCase));

        return partialMatch;
    }

    /// <summary>
    /// 中国語テキストから推奨言語を判定
    /// </summary>
    /// <param name="text">中国語テキスト</param>
    /// <returns>推奨される言語オブジェクト</returns>
    public static Language GetRecommendedChineseLanguage(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Language.ChineseSimplified; // デフォルト
        }

        var simplifiedCount = 0;
        var traditionalCount = 0;
        var chineseCharCount = 0;

        foreach (var character in text)
        {
            if (ChineseLanguageProcessor.IsChineseCharacter(character))
            {
                chineseCharCount++;
                
                // 簡体字専用文字の検出（基本的な例）
                if (IsSimplifiedOnlyCharacter(character))
                {
                    simplifiedCount += 2; // 重み付け
                }
                // 繁体字専用文字の検出（基本的な例）
                else if (IsTraditionalOnlyCharacter(character))
                {
                    traditionalCount += 2; // 重み付け
                }
            }
        }

        if (chineseCharCount == 0)
        {
            return Language.ChineseSimplified; // 中国語文字がない場合はデフォルト
        }

        // 繁体字専用文字が多い場合
        if (traditionalCount > simplifiedCount && traditionalCount > chineseCharCount * 0.1)
        {
            return Language.ChineseTraditional;
        }

        // デフォルトは簡体字
        return Language.ChineseSimplified;
    }

    /// <summary>
    /// 簡体字専用文字かどうかを判定（基本的な実装）
    /// </summary>
    /// <param name="character">文字</param>
    /// <returns>簡体字専用の場合はtrue</returns>
    private static bool IsSimplifiedOnlyCharacter(char character)
    {
        // 基本的な簡体字専用文字の例
        // 実際の実装では、より包括的な辞書が必要
        return character switch
        {
            '国' or '对' or '会' or '学' or '说' or '时' or '过' or '现' or '开' or '门' => true,
            '内' or '间' or '年' or '进' or '实' or '问' or '变' or '还' or '发' or '应' => true,
            _ => false
        };
    }

    /// <summary>
    /// 繁体字専用文字かどうかを判定（基本的な実装）
    /// </summary>
    /// <param name="character">文字</param>
    /// <returns>繁体字専用の場合はtrue</returns>
    private static bool IsTraditionalOnlyCharacter(char character)
    {
        // 基本的な繁体字専用文字の例
        // 実際の実装では、より包括的な辞書が必要
        return character switch
        {
            '國' or '對' or '會' or '學' or '說' or '時' or '過' or '現' or '開' or '門' => true,
            '內' or '間' or '進' or '實' or '問' or '變' or '還' or '發' or '應' or '標' => true,
            _ => false
        };
    }
}
