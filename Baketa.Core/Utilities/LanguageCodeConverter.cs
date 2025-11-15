using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Utilities;

/// <summary>
/// 言語コードと表示名の変換を行うユーティリティクラス
/// 重複したコード変換ロジックを統一し、将来の多言語対応を容易にする
/// </summary>
public static class LanguageCodeConverter
{
    /// <summary>
    /// サポートされている言語の映射テーブル
    /// 表示名 → 言語コードのマッピング
    /// </summary>
    private static readonly Dictionary<string, string> DisplayNameToCodeMap = new()
    {
        // 英語
        { "English", "en" },
        { "英語", "en" },
        
        // 日本語
        { "Japanese", "ja" },
        { "日本語", "ja" },
        
        // 中国語
        { "Chinese", "zh" },
        { "中国語", "zh" },
        
        // 自動検出
        { "Auto", "auto" },
        { "自動", "auto" },
        { "Automatic", "auto" }
    };

    /// <summary>
    /// 言語コード → 表示名のマッピング（英語表示）
    /// </summary>
    private static readonly Dictionary<string, string> CodeToDisplayNameMap = new()
    {
        { "en", "English" },
        { "ja", "Japanese" },
        { "zh", "Chinese" },
        { "auto", "Auto" }
    };

    /// <summary>
    /// 言語コード → 日本語表示名のマッピング
    /// </summary>
    private static readonly Dictionary<string, string> CodeToJapaneseDisplayNameMap = new()
    {
        { "en", "英語" },
        { "ja", "日本語" },
        { "zh", "中国語" },
        { "auto", "自動" }
    };

    /// <summary>
    /// 表示名から言語コードに変換する
    /// </summary>
    /// <param name="displayName">言語の表示名（English, Japanese等）</param>
    /// <param name="defaultCode">変換に失敗した場合のデフォルト言語コード</param>
    /// <returns>言語コード（en, ja, zh, auto等）</returns>
    public static string ToLanguageCode(string displayName, string defaultCode = "en")
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return defaultCode;

        return DisplayNameToCodeMap.GetValueOrDefault(displayName.Trim(), defaultCode);
    }

    /// <summary>
    /// 言語コードから表示名（英語）に変換する
    /// </summary>
    /// <param name="languageCode">言語コード（en, ja, zh等）</param>
    /// <param name="defaultDisplayName">変換に失敗した場合のデフォルト表示名</param>
    /// <returns>英語の表示名（English, Japanese等）</returns>
    public static string ToDisplayName(string languageCode, string defaultDisplayName = "English")
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return defaultDisplayName;

        return CodeToDisplayNameMap.GetValueOrDefault(languageCode.Trim().ToLowerInvariant(), defaultDisplayName);
    }

    /// <summary>
    /// 言語コードから日本語表示名に変換する
    /// </summary>
    /// <param name="languageCode">言語コード（en, ja, zh等）</param>
    /// <param name="defaultDisplayName">変換に失敗した場合のデフォルト表示名</param>
    /// <returns>日本語の表示名（英語、日本語等）</returns>
    public static string ToJapaneseDisplayName(string languageCode, string defaultDisplayName = "英語")
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return defaultDisplayName;

        return CodeToJapaneseDisplayNameMap.GetValueOrDefault(languageCode.Trim().ToLowerInvariant(), defaultDisplayName);
    }

    /// <summary>
    /// Language staticプロパティから言語コードに変換する
    /// </summary>
    /// <param name="language">Language オブジェクト</param>
    /// <returns>対応する言語コード</returns>
    public static string FromLanguageObject(Language language)
    {
        if (language == null) return "en";
        return language.Code switch
        {
            "en" => "en",
            "ja" => "ja",
            "zh-CN" or "zh" => "zh",
            "auto" => "auto",
            _ => "en"
        };
    }

    /// <summary>
    /// 言語コードからLanguage enumに変換する
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <param name="defaultLanguage">変換に失敗した場合のデフォルト言語</param>
    /// <returns>対応するLanguage enum値</returns>
    public static Language ToLanguageEnum(string languageCode, Language? defaultLanguage = null)
    {
        var fallbackLanguage = defaultLanguage ?? Language.English;

        if (string.IsNullOrWhiteSpace(languageCode))
            return fallbackLanguage;

        return languageCode.Trim().ToLowerInvariant() switch
        {
            "en" => Language.English,
            "ja" => Language.Japanese,
            "zh" => Language.ChineseSimplified,
            "auto" => Language.Auto,
            _ => fallbackLanguage
        };
    }

    /// <summary>
    /// サポートされている全ての言語コードを取得する
    /// </summary>
    /// <returns>サポートされている言語コードのリスト</returns>
    public static IReadOnlyList<string> GetSupportedLanguageCodes()
    {
        return CodeToDisplayNameMap.Keys.ToList().AsReadOnly();
    }

    /// <summary>
    /// サポートされている全ての表示名を取得する（英語）
    /// </summary>
    /// <returns>サポートされている表示名のリスト</returns>
    public static IReadOnlyList<string> GetSupportedDisplayNames()
    {
        return CodeToDisplayNameMap.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// サポートされている全ての日本語表示名を取得する
    /// </summary>
    /// <returns>サポートされている日本語表示名のリスト</returns>
    public static IReadOnlyList<string> GetSupportedJapaneseDisplayNames()
    {
        return CodeToJapaneseDisplayNameMap.Values.ToList().AsReadOnly();
    }

    /// <summary>
    /// 指定された言語コードがサポートされているかを確認する
    /// </summary>
    /// <param name="languageCode">確認する言語コード</param>
    /// <returns>サポートされている場合true</returns>
    public static bool IsSupportedLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        return CodeToDisplayNameMap.ContainsKey(languageCode.Trim().ToLowerInvariant());
    }

    /// <summary>
    /// 指定された表示名がサポートされているかを確認する
    /// </summary>
    /// <param name="displayName">確認する表示名</param>
    /// <returns>サポートされている場合true</returns>
    public static bool IsSupportedDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return false;

        return DisplayNameToCodeMap.ContainsKey(displayName.Trim());
    }

    /// <summary>
    /// 言語の双方向ペアを取得する（翻訳方向の確認用）
    /// </summary>
    /// <param name="sourceLanguage">ソース言語コード</param>
    /// <param name="targetLanguage">ターゲット言語コード</param>
    /// <returns>正規化された言語ペア（source, target）</returns>
    public static (string source, string target) NormalizeLanguagePair(string sourceLanguage, string targetLanguage)
    {
        var normalizedSource = IsSupportedLanguageCode(sourceLanguage) ? sourceLanguage.ToLowerInvariant() : "en";
        var normalizedTarget = IsSupportedLanguageCode(targetLanguage) ? targetLanguage.ToLowerInvariant() : "ja";

        // 同一言語の場合のフォールバック
        if (normalizedSource == normalizedTarget)
        {
            normalizedTarget = normalizedSource == "en" ? "ja" : "en";
        }

        return (normalizedSource, normalizedTarget);
    }

    /// <summary>
    /// 翻訳方向の説明文を取得する（デバッグ・ログ用）
    /// </summary>
    /// <param name="sourceLanguage">ソース言語コード</param>
    /// <param name="targetLanguage">ターゲット言語コード</param>
    /// <param name="useJapanese">日本語で説明を返すかどうか</param>
    /// <returns>翻訳方向の説明文</returns>
    public static string GetTranslationDirectionDescription(string sourceLanguage, string targetLanguage, bool useJapanese = true)
    {
        if (useJapanese)
        {
            var sourceDisplay = ToJapaneseDisplayName(sourceLanguage);
            var targetDisplay = ToJapaneseDisplayName(targetLanguage);
            return $"{sourceDisplay}→{targetDisplay}";
        }
        else
        {
            var sourceDisplay = ToDisplayName(sourceLanguage);
            var targetDisplay = ToDisplayName(targetLanguage);
            return $"{sourceDisplay}→{targetDisplay}";
        }
    }
}
