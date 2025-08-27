namespace Baketa.Core.Abstractions.Translation;

/// <summary>
/// 言語コード正規化クラス
/// 様々な形式の言語コードを標準形式に正規化
/// </summary>
public static class LanguageCodeNormalizer
{
    /// <summary>
    /// 言語コード正規化マップ
    /// Key: 入力バリエーション, Value: 標準コード
    /// </summary>
    private static readonly Dictionary<string, string> NormalizationMap = new(StringComparer.OrdinalIgnoreCase)
    {
        // 日本語バリエーション
        { "jp", "ja" },
        { "jpn", "ja" },
        { "japan", "ja" },
        { "japanese", "ja" },
        
        // 中国語バリエーション
        { "cn", "zh" },
        { "chn", "zh" },
        { "chinese", "zh" },
        { "china", "zh" },
        { "cmn", "zh" }, // 標準中国語
        
        // 英語バリエーション
        { "en-us", "en" },
        { "en-gb", "en" },
        { "eng", "en" },
        { "english", "en" },
        
        // 韓国語バリエーション
        { "kr", "ko" },
        { "kor", "ko" },
        { "korean", "ko" },
        { "korea", "ko" },
        
        // その他主要言語
        { "ger", "de" },
        { "german", "de" },
        { "deutsch", "de" },
        { "fra", "fr" },
        { "fre", "fr" },
        { "french", "fr" },
        { "français", "fr" },
        { "spa", "es" },
        { "spanish", "es" },
        { "español", "es" },
        { "rus", "ru" },
        { "russian", "ru" },
        { "русский", "ru" },
        { "ara", "ar" },
        { "arabic", "ar" },
        { "العربية", "ar" },
        
        // 中国語方言・地域変種
        { "zh-cn", "zh-Hans" },
        { "zh-tw", "zh-Hant" },
        { "zh-hk", "zh-Hant" },
        { "zh-mo", "zh-Hant" },
        { "zh-sg", "zh-Hans" },
        { "simplified", "zh-Hans" },
        { "traditional", "zh-Hant" },
        { "mandarin", "zh" },
        { "cantonese", "yue" },
        { "guangdong", "yue" },
        
        // ISO 639-3 to ISO 639-1 mappings
        { "zho", "zh" },
        { "yue", "yue" }, // 広東語は独立コード
        { "deu", "de" }
        // 注意: cmn, jpn, kor, eng, fra, spa, rus, ara は上記で既に定義済み
    };

    /// <summary>
    /// 言語コードを標準形式に正規化
    /// </summary>
    /// <param name="languageCode">入力言語コード</param>
    /// <returns>正規化された言語コード（正規化不可能な場合は元のコード）</returns>
    public static string Normalize(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return languageCode;

        // トリムしてから正規化
        var trimmedCode = languageCode.Trim();
        
        // 完全一致チェック
        if (NormalizationMap.TryGetValue(trimmedCode, out var normalizedCode))
        {
            return normalizedCode;
        }
        
        // ハイフン区切りの場合、最初の部分のみを正規化
        if (trimmedCode.Contains('-'))
        {
            var parts = trimmedCode.Split('-');
            var primaryCode = parts[0];
            
            if (NormalizationMap.TryGetValue(primaryCode, out var normalizedPrimary))
            {
                // 既知の地域変種の場合はそのまま返す
                if (IsKnownRegionalVariant(trimmedCode))
                {
                    return trimmedCode.ToLowerInvariant();
                }
                
                // それ以外は正規化されたプライマリコードを返す
                return normalizedPrimary;
            }
        }

        // 正規化不可能な場合は小文字化して返す
        return trimmedCode.ToLowerInvariant();
    }

    /// <summary>
    /// 言語コードが正規化をサポートしているかチェック
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>正規化サポートの有無</returns>
    public static bool IsSupported(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
            return false;

        var trimmedCode = languageCode.Trim();
        return NormalizationMap.ContainsKey(trimmedCode) || 
               IsKnownRegionalVariant(trimmedCode);
    }

    /// <summary>
    /// 既知の地域変種かチェック
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>既知地域変種の場合true</returns>
    private static bool IsKnownRegionalVariant(string languageCode)
    {
        var knownVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "zh-Hans", "zh-Hant", "zh-CN", "zh-TW", "zh-HK", "zh-MO", "zh-SG",
            "en-US", "en-GB", "en-CA", "en-AU",
            "es-ES", "es-MX", "es-AR",
            "fr-FR", "fr-CA",
            "de-DE", "de-AT", "de-CH"
        };

        return knownVariants.Contains(languageCode);
    }

    /// <summary>
    /// 正規化統計情報を取得
    /// </summary>
    /// <returns>正規化マップ情報</returns>
    public static (int TotalMappings, int UniquePrimaryLanguages) GetNormalizationStats()
    {
        var uniquePrimary = NormalizationMap.Values
            .Select(code => code.Split('-')[0])
            .Distinct()
            .Count();
            
        return (NormalizationMap.Count, uniquePrimary);
    }

    /// <summary>
    /// サポートされている全言語コードを取得
    /// </summary>
    /// <returns>サポート言語コード一覧</returns>
    public static IReadOnlyList<string> GetSupportedLanguageCodes()
    {
        var supportedCodes = new HashSet<string>();
        
        // 正規化マップからプライマリコードを追加
        foreach (var value in NormalizationMap.Values)
        {
            supportedCodes.Add(value);
        }
        
        // 既知の地域変種を追加
        var knownVariants = new[]
        {
            "zh-Hans", "zh-Hant", "en-US", "en-GB", 
            "es-ES", "es-MX", "fr-FR", "fr-CA", 
            "de-DE", "de-AT"
        };
        
        foreach (var variant in knownVariants)
        {
            supportedCodes.Add(variant);
        }
        
        return supportedCodes.OrderBy(code => code).ToList();
    }
}