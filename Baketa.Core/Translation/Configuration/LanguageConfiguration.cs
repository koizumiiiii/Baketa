using System;
using System.Collections.Generic;
using System.Linq;
using Baketa.Core.Translation.Models;

namespace Baketa.Core.Translation.Configuration;

/// <summary>
/// 言語設定の管理クラス
/// 翻訳でサポートされる言語の詳細情報を提供
/// </summary>
public class LanguageConfiguration
{
    /// <summary>
    /// サポートされている言語のリスト
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "設定クラスでは変更可能なListが適切")]
    public List<LanguageInfo> SupportedLanguages { get; set; } = [];

    /// <summary>
    /// デフォルトのソース言語
    /// </summary>
    public string DefaultSourceLanguage { get; set; } = "auto";

    /// <summary>
    /// デフォルトのターゲット言語
    /// </summary>
    public string DefaultTargetLanguage { get; set; } = "ja";

    /// <summary>
    /// 中国語変種の自動検出を有効にするかどうか
    /// </summary>
    public bool EnableChineseVariantAutoDetection { get; set; } = true;

    /// <summary>
    /// 言語の自動検出を有効にするかどうか
    /// </summary>
    public bool EnableLanguageDetection { get; set; } = true;

    /// <summary>
    /// デフォルト設定の静的インスタンス
    /// </summary>
    public static LanguageConfiguration Default => CreateDefault();

    /// <summary>
    /// デフォルトコンストラクタ
    /// </summary>
    public LanguageConfiguration()
    {
    }

    /// <summary>
    /// デフォルト設定を作成
    /// </summary>
    /// <returns>デフォルト設定のインスタンス</returns>
    private static LanguageConfiguration CreateDefault()
    {
        var config = new LanguageConfiguration();
        config.ResetToDefault();
        return config;
    }

    /// <summary>
    /// サポートされている全言語のリストを取得
    /// </summary>
    /// <returns>言語情報のリスト</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "内部実装でのList作成")]
    private static List<LanguageInfo> GetDefaultSupportedLanguages()
    {
        return
        [
            // 自動検出
            new()
            {
                Code = "auto",
                Name = "自動検出",
                NativeName = "Auto Detect",
                OpusPrefix = null,
                Variant = null,
                IsAutoDetect = true,
                IsSupported = true
            },

            // 英語
            new()
            {
                Code = "en",
                Name = "English",
                NativeName = "English",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "US",
                IsSupported = true
            },

            // 日本語
            new()
            {
                Code = "ja",
                Name = "日本語",
                NativeName = "日本語",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "JP",
                IsSupported = true
            },

            // 中国語（自動）
            new()
            {
                Code = "zh",
                Name = "中国語（自動）",
                NativeName = "中文（自动）",
                OpusPrefix = "",
                Variant = ChineseVariant.Auto,
                IsSupported = true
            },

            // 中国語（簡体字）
            new()
            {
                Code = "zh-Hans",
                Name = "中国語（簡体字）",
                NativeName = "中文（简体）",
                OpusPrefix = ">>cmn_Hans<<",
                Variant = ChineseVariant.Simplified,
                RegionCode = "CN",
                IsSupported = true
            },

            // 中国語（繁体字）
            new()
            {
                Code = "zh-Hant",
                Name = "中国語（繁体字）",
                NativeName = "中文（繁體）",
                OpusPrefix = ">>cmn_Hant<<",
                Variant = ChineseVariant.Traditional,
                RegionCode = "TW",
                IsSupported = true
            },

            // 広東語（将来拡張用）
            new()
            {
                Code = "yue",
                Name = "広東語",
                NativeName = "粵語",
                OpusPrefix = ">>yue<<",
                Variant = ChineseVariant.Cantonese,
                RegionCode = "HK",
                IsSupported = false
            },

            // その他の言語（現在未サポート）
            new()
            {
                Code = "ko",
                Name = "韓国語",
                NativeName = "한국어",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "KR",
                IsSupported = false
            },
            new()
            {
                Code = "es",
                Name = "スペイン語",
                NativeName = "Español",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "ES",
                IsSupported = false
            },
            new()
            {
                Code = "fr",
                Name = "フランス語",
                NativeName = "Français",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "FR",
                IsSupported = false
            },
            new()
            {
                Code = "de",
                Name = "ドイツ語",
                NativeName = "Deutsch",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "DE",
                IsSupported = false
            },
            new()
            {
                Code = "ru",
                Name = "ロシア語",
                NativeName = "Русский",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "RU",
                IsSupported = false
            },
            new()
            {
                Code = "ar",
                Name = "アラビア語",
                NativeName = "العربية",
                OpusPrefix = null,
                Variant = null,
                RegionCode = "SA",
                IsRightToLeft = true,
                IsSupported = false
            }
        ];
    }

    /// <summary>
    /// 言語コードから中国語変種を取得（インスタンスメソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語変種</returns>
    public ChineseVariant GetChineseVariantForLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return ChineseVariant.Auto;
        }

        return ChineseVariantExtensions.FromLanguageCode(languageCode);
    }

    /// <summary>
    /// 中国語関連の言語コードかどうかを判定（インスタンスメソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語関連の場合はtrue</returns>
    public bool IsChineseLanguageCodeInstance(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalizedCode = languageCode.ToUpperInvariant();
        return normalizedCode.StartsWith("ZH", StringComparison.Ordinal) ||
               normalizedCode.StartsWith("CMN", StringComparison.Ordinal) ||
               normalizedCode.StartsWith("YUE", StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定された言語ペアがサポートされているかどうかを確認（インスタンスメソッド）
    /// </summary>
    /// <param name="sourceLang">ソース言語コード</param>
    /// <param name="targetLang">ターゲット言語コード</param>
    /// <returns>サポートされている場合はtrue</returns>
    public bool IsLanguagePairSupportedInstance(string sourceLang, string targetLang)
    {
        var sourceSupported = SupportedLanguages.Any(l => 
            string.Equals(l.Code, sourceLang, StringComparison.OrdinalIgnoreCase) && 
            (l.IsAutoDetect || l.IsSupported));
        var targetSupported = SupportedLanguages.Any(l => 
            string.Equals(l.Code, targetLang, StringComparison.OrdinalIgnoreCase) && 
            l.IsSupported);
        
        return sourceSupported && targetSupported;
    }

    /// <summary>
    /// 翻訳ペアがサポートされているかどうかを確認（テスト用の詳細チェック付き）
    /// </summary>
    /// <param name="sourceLang">ソース言語コード</param>
    /// <param name="targetLang">ターゲット言語コード</param>
    /// <returns>サポートされている場合はtrue</returns>
    public bool IsTranslationPairSupported(string sourceLang, string targetLang)
    {
        // 同じ言語への翻訳は不要
        if (string.Equals(sourceLang, targetLang, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // 自動検出の場合は、ターゲット言語がサポートされているかのみチェック
        if (string.Equals(sourceLang, "auto", StringComparison.OrdinalIgnoreCase))
        {
            return SupportedLanguages.Any(l => 
                string.Equals(l.Code, targetLang, StringComparison.OrdinalIgnoreCase) && 
                l.IsSupported);
        }

        // 通常の言語ペアのサポート確認
        return IsLanguagePairSupportedInstance(sourceLang, targetLang);
    }

    /// <summary>
    /// 言語コードに対応する言語情報を取得（インスタンスメソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>言語情報（見つからない場合はnull）</returns>
    public LanguageInfo? GetLanguageInfoInstance(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        return SupportedLanguages.FirstOrDefault(l => 
            string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 中国語系の言語情報を取得
    /// </summary>
    /// <returns>中国語系の言語情報のリスト</returns>
    public IEnumerable<LanguageInfo> GetChineseLanguages()
    {
        return SupportedLanguages.Where(l => l.IsChinese());
    }

    /// <summary>
    /// 言語コードの表示名を取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>表示名（見つからない場合は言語コードをそのまま返す）</returns>
    public string GetDisplayName(string languageCode)
    {
        var languageInfo = GetLanguageInfoInstance(languageCode);
        return languageInfo?.Name ?? languageCode;
    }

    /// <summary>
    /// 言語コードのネイティブ名を取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>ネイティブ名（見つからない場合は表示名、それも見つからない場合は言語コード）</returns>
    public string GetNativeName(string languageCode)
    {
        var languageInfo = GetLanguageInfoInstance(languageCode);
        return languageInfo?.NativeName ?? languageInfo?.Name ?? languageCode;
    }

    /// <summary>
    /// 言語を追加または更新
    /// </summary>
    /// <param name="language">言語情報</param>
    public void AddLanguage(LanguageInfo language)
    {
        ArgumentNullException.ThrowIfNull(language);

        var existing = SupportedLanguages.FirstOrDefault(l => 
            string.Equals(l.Code, language.Code, StringComparison.OrdinalIgnoreCase));

        if (existing != null)
        {
            // 既存の言語を更新
            var index = SupportedLanguages.IndexOf(existing);
            SupportedLanguages[index] = language;
        }
        else
        {
            // 新しい言語を追加
            SupportedLanguages.Add(language);
        }
    }

    /// <summary>
    /// 言語を削除
    /// </summary>
    /// <param name="languageCode">削除する言語コード</param>
    /// <returns>削除に成功した場合はtrue</returns>
    public bool RemoveLanguage(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var language = SupportedLanguages.FirstOrDefault(l => 
            string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));

        if (language != null)
        {
            SupportedLanguages.Remove(language);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 設定の検証
    /// </summary>
    /// <returns>エラーメッセージのリスト（エラーがない場合は空のリスト）</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "検証結果のリスト返却")]
    public List<string> Validate()
    {
        var errors = new List<string>();

        // サポートされている言語のチェック
        if (SupportedLanguages.Count == 0)
        {
            errors.Add("サポートされている言語が設定されていません。");
        }

        // デフォルトソース言語のチェック
        if (string.IsNullOrWhiteSpace(DefaultSourceLanguage))
        {
            errors.Add("デフォルトのソース言語が設定されていません。");
        }

        // デフォルトターゲット言語のチェック
        if (string.IsNullOrWhiteSpace(DefaultTargetLanguage))
        {
            errors.Add("デフォルトのターゲット言語が設定されていません。");
        }

        // 重複言語コードのチェック
        var duplicateCodes = SupportedLanguages
            .GroupBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        foreach (var code in duplicateCodes)
        {
            errors.Add($"重複している言語コードが見つかりました: {code}");
        }

        return errors;
    }

    /// <summary>
    /// デフォルト設定にリセット
    /// </summary>
    public void ResetToDefault()
    {
        SupportedLanguages.Clear();
        SupportedLanguages.AddRange(GetDefaultSupportedLanguages());
        DefaultSourceLanguage = "auto";
        DefaultTargetLanguage = "ja";
        EnableChineseVariantAutoDetection = true;
        EnableLanguageDetection = true;
    }

    /// <summary>
    /// 設定のクローンを作成
    /// </summary>
    /// <returns>複製された設定</returns>
    public LanguageConfiguration Clone()
    {
        var clone = new LanguageConfiguration
        {
            DefaultSourceLanguage = DefaultSourceLanguage,
            DefaultTargetLanguage = DefaultTargetLanguage,
            EnableChineseVariantAutoDetection = EnableChineseVariantAutoDetection,
            EnableLanguageDetection = EnableLanguageDetection
        };

        // サポートされている言語の深いコピー
        foreach (var language in SupportedLanguages)
        {
            clone.SupportedLanguages.Add(new LanguageInfo
            {
                Code = language.Code,
                Name = language.Name,
                NativeName = language.NativeName,
                OpusPrefix = language.OpusPrefix,
                Variant = language.Variant,
                RegionCode = language.RegionCode,
                IsRightToLeft = language.IsRightToLeft,
                IsAutoDetect = language.IsAutoDetect,
                IsSupported = language.IsSupported
            });
        }

        return clone;
    }

    /// <summary>
    /// 静的メソッド用の互換性メソッド - サポートされている全言語のリストを取得
    /// </summary>
    /// <returns>サポートされている全言語のリスト</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1002:Do not expose generic lists", Justification = "静的メソッドでの互換性保持")]
    public static List<LanguageInfo> GetSupportedLanguages()
    {
        return GetDefaultSupportedLanguages();
    }

    /// <summary>
    /// 言語コードから中国語変種を取得（静的メソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語変種</returns>
    public static ChineseVariant GetChineseVariant(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return ChineseVariant.Auto;
        }

        return ChineseVariantExtensions.FromLanguageCode(languageCode);
    }

    /// <summary>
    /// 中国語関連の言語コードかどうかを判定（静的メソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語関連の場合はtrue</returns>
    public static bool IsChineseLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return false;
        }

        var normalizedCode = languageCode.ToUpperInvariant();
        return normalizedCode.StartsWith("ZH", StringComparison.Ordinal) ||
               normalizedCode.StartsWith("CMN", StringComparison.Ordinal) ||
               normalizedCode.StartsWith("YUE", StringComparison.Ordinal);
    }

    /// <summary>
    /// 指定された言語ペアがサポートされているかどうかを確認（静的メソッド）
    /// </summary>
    /// <param name="sourceLang">ソース言語コード</param>
    /// <param name="targetLang">ターゲット言語コード</param>
    /// <returns>サポートされている場合はtrue</returns>
    public static bool IsLanguagePairSupported(string sourceLang, string targetLang)
    {
        var supportedLanguages = GetDefaultSupportedLanguages();
        var sourceSupported = supportedLanguages.Any(l => 
            string.Equals(l.Code, sourceLang, StringComparison.OrdinalIgnoreCase) && 
            (l.IsAutoDetect || l.IsSupported));
        var targetSupported = supportedLanguages.Any(l => 
            string.Equals(l.Code, targetLang, StringComparison.OrdinalIgnoreCase) && 
            l.IsSupported);
        
        return sourceSupported && targetSupported;
    }

    /// <summary>
    /// 言語コードに対応する言語情報を取得（静的メソッド）
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>言語情報（見つからない場合はnull）</returns>
    public static LanguageInfo? GetLanguageInfo(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var supportedLanguages = GetDefaultSupportedLanguages();
        return supportedLanguages.FirstOrDefault(l => 
            string.Equals(l.Code, languageCode, StringComparison.OrdinalIgnoreCase));
    }
}
