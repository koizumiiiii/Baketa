namespace Baketa.Core.Translation.Models;

/// <summary>
/// 中国語変種の列挙型
/// </summary>
public enum ChineseVariant
{
    /// <summary>
    /// 自動判定（デフォルト）
    /// </summary>
    Auto,

    /// <summary>
    /// 簡体字
    /// </summary>
    Simplified,

    /// <summary>
    /// 繁体字
    /// </summary>
    Traditional,

    /// <summary>
    /// 広東語
    /// </summary>
    Cantonese
}

/// <summary>
/// ChineseVariant拡張メソッド
/// </summary>
public static class ChineseVariantExtensions
{
    /// <summary>
    /// 中国語変種を言語コードに変換
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>言語コード</returns>
    public static string ToLanguageCode(this ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => "zh-Hans",
            ChineseVariant.Traditional => "zh-Hant",
            ChineseVariant.Cantonese => "yue",
            ChineseVariant.Auto => "zh",
            _ => "zh"
        };
    }

    /// <summary>
    /// 中国語変種の表示名を取得
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>表示名</returns>
    public static string GetDisplayName(this ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => "中国語（簡体字）",
            ChineseVariant.Traditional => "中国語（繁体字）",
            ChineseVariant.Cantonese => "広東語",
            ChineseVariant.Auto => "中国語（自動）",
            _ => "中国語"
        };
    }

    /// <summary>
    /// 中国語変種の英語表示名を取得
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>英語表示名</returns>
    public static string GetEnglishDisplayName(this ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => "Chinese (Simplified)",
            ChineseVariant.Traditional => "Chinese (Traditional)",
            ChineseVariant.Cantonese => "Cantonese",
            ChineseVariant.Auto => "Chinese (Auto)",
            _ => "Chinese"
        };
    }

    /// <summary>
    /// 中国語変種のネイティブ表示名を取得
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>ネイティブ表示名</returns>
    public static string GetNativeDisplayName(this ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => "中文（简体）",
            ChineseVariant.Traditional => "中文（繁體）",
            ChineseVariant.Cantonese => "粵語",
            ChineseVariant.Auto => "中文（自动）",
            _ => "中文"
        };
    }

    /// <summary>
    /// 中国語変種のOPUS-MTプレフィックスを取得
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>OPUS-MTプレフィックス</returns>
    public static string GetOpusPrefix(this ChineseVariant variant)
    {
        return variant switch
        {
            ChineseVariant.Simplified => ">>cmn_Hans<<",
            ChineseVariant.Traditional => ">>cmn_Hant<<",
            ChineseVariant.Cantonese => ">>yue<<",
            ChineseVariant.Auto => "", // 自動の場合はプレフィックスなし
            _ => ""
        };
    }

    /// <summary>
    /// 言語コードから中国語変種を取得
    /// </summary>
    /// <param name="languageCode">言語コード</param>
    /// <returns>中国語変種</returns>
    public static ChineseVariant FromLanguageCode(string languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return ChineseVariant.Auto;
        }

        var normalizedCode = languageCode.Trim().ToUpperInvariant();
        return normalizedCode switch
        {
            "ZH-CN" or "ZH-HANS" or "ZH-CHS" or "CMN_HANS" => ChineseVariant.Simplified,
            "ZH-TW" or "ZH-HK" or "ZH-MO" or "ZH-HANT" or "ZH-CHT" or "CMN_HANT" => ChineseVariant.Traditional,
            "YUE" or "YUE-HK" or "YUE-CN" => ChineseVariant.Cantonese,
            "ZH" or "ZHO" or "CMN" => ChineseVariant.Auto,
            _ => ChineseVariant.Auto
        };
    }

    /// <summary>
    /// 中国語変種が有効かどうかを判定
    /// </summary>
    /// <param name="variant">中国語変種</param>
    /// <returns>有効な場合はtrue</returns>
    public static bool IsValid(this ChineseVariant variant)
    {
        return Enum.IsDefined(typeof(ChineseVariant), variant);
    }
}
