namespace Baketa.Core.Models.Translation;

/// <summary>
/// [Issue #520] 型安全な言語表現（統一Language型）
/// 言語コードと表示名を統一管理し、将来の言語追加に対応
/// 旧 Baketa.Core.Translation.Models.Language を統合
/// </summary>
public sealed record Language(string Code, string DisplayName)
{
    /// <summary>
    /// 言語名（英語表記）
    /// 旧Language classとの互換性のため保持
    /// </summary>
    public string Name { get; init; } = DisplayName;

    /// <summary>
    /// 言語名（現地語表記）
    /// 例: "日本語", "한국어", "Español"
    /// </summary>
    public string? NativeName { get; init; }

    /// <summary>
    /// 地域コード（ISO 3166-1）
    /// 例: "JP", "US", "CN", "TW"
    /// </summary>
    public string? RegionCode { get; init; }

    /// <summary>
    /// 自動検出言語かどうか
    /// </summary>
    public bool IsAutoDetect { get; init; }

    /// <summary>
    /// RTL（右から左）言語かどうか
    /// </summary>
    public bool IsRightToLeft { get; init; }

    /// <summary>
    /// 日本語
    /// </summary>
    public static Language Japanese => new("ja", "Japanese")
    {
        NativeName = "日本語",
        RegionCode = "JP"
    };

    /// <summary>
    /// 英語
    /// </summary>
    public static Language English => new("en", "English")
    {
        NativeName = "English",
        RegionCode = "US"
    };

    /// <summary>
    /// 簡体字中国語
    /// </summary>
    public static Language ChineseSimplified => new("zh-cn", "Chinese (Simplified)")
    {
        NativeName = "中文（简体）",
        RegionCode = "CN"
    };

    /// <summary>
    /// 繁体字中国語
    /// </summary>
    public static Language ChineseTraditional => new("zh-tw", "Chinese (Traditional)")
    {
        NativeName = "中文（繁體）",
        RegionCode = "TW"
    };

    /// <summary>
    /// 韓国語
    /// </summary>
    public static Language Korean => new("ko", "Korean")
    {
        NativeName = "한국어",
        RegionCode = "KR"
    };

    /// <summary>
    /// スペイン語
    /// </summary>
    public static Language Spanish => new("es", "Spanish")
    {
        NativeName = "Español",
        RegionCode = "ES"
    };

    /// <summary>
    /// フランス語
    /// </summary>
    public static Language French => new("fr", "French")
    {
        NativeName = "Français",
        RegionCode = "FR"
    };

    /// <summary>
    /// ドイツ語
    /// </summary>
    public static Language German => new("de", "German")
    {
        NativeName = "Deutsch",
        RegionCode = "DE"
    };

    /// <summary>
    /// イタリア語
    /// </summary>
    public static Language Italian => new("it", "Italian")
    {
        NativeName = "Italiano",
        RegionCode = "IT"
    };

    /// <summary>
    /// ポルトガル語
    /// </summary>
    public static Language Portuguese => new("pt", "Portuguese")
    {
        NativeName = "Português",
        RegionCode = "BR"
    };

    /// <summary>
    /// ロシア語
    /// </summary>
    public static Language Russian => new("ru", "Russian")
    {
        NativeName = "Русский"
    };

    /// <summary>
    /// アラビア語
    /// </summary>
    public static Language Arabic => new("ar", "Arabic")
    {
        NativeName = "العربية",
        IsRightToLeft = true
    };

    /// <summary>
    /// 自動検出
    /// </summary>
    public static Language Auto => new("auto", "Auto Detect")
    {
        IsAutoDetect = true
    };

    /// <summary>
    /// 旧名前空間との互換性エイリアス
    /// </summary>
    public static Language AutoDetect => Auto;

    /// <summary>
    /// 言語コードから言語オブジェクトを取得
    /// </summary>
    /// <param name="code">言語コード</param>
    /// <returns>対応する言語オブジェクト</returns>
    /// <exception cref="ArgumentException">サポートされていない言語コードの場合</exception>
    public static Language FromCode(string code) => code?.ToLowerInvariant() switch
    {
        "ja" or "ja-jp" or "jpn_jpan" or "japanese" => Japanese,
        "en" or "en-us" or "eng_latn" or "english" => English,
        "zh-cn" => ChineseSimplified,
        "zh-tw" => ChineseTraditional,
        "ko" => Korean,
        "es" => Spanish,
        "fr" => French,
        "de" => German,
        "it" => Italian,
        "pt" => Portuguese,
        "ru" => Russian,
        "ar" => Arabic,
        "auto" => Auto,
        _ => throw new ArgumentException($"Unsupported language code: {code}")
    };

    /// <summary>
    /// 言語コードからLanguageを取得（未知コードはそのまま使用、例外なし）
    /// 旧Language classのFromCode()との互換性: 未知コードでも例外を投げない
    /// </summary>
    public static Language FromCodeOrDefault(string code)
    {
        try
        {
            return FromCode(code);
        }
        catch (ArgumentException)
        {
            return new Language(code, code) { Name = code };
        }
    }

    /// <summary>
    /// 表示名から言語オブジェクトを取得
    /// </summary>
    /// <param name="displayName">表示名</param>
    /// <returns>対応する言語オブジェクト</returns>
    /// <exception cref="ArgumentException">サポートされていない表示名の場合</exception>
    public static Language FromDisplayName(string displayName) => displayName?.ToLowerInvariant() switch
    {
        "japanese" or "日本語" => Japanese,
        "english" or "英語" => English,
        "chinese (simplified)" or "简体中文" => ChineseSimplified,
        "chinese (traditional)" or "繁體中文" => ChineseTraditional,
        "korean" or "한국어" => Korean,
        "spanish" or "español" => Spanish,
        "french" or "français" => French,
        "german" or "deutsch" => German,
        "italian" or "italiano" => Italian,
        "portuguese" or "português" => Portuguese,
        "russian" or "русский" => Russian,
        "arabic" or "العربية" => Arabic,
        "auto" or "auto detect" or "自動検出" => Auto,
        _ => throw new ArgumentException($"Unsupported display name: {displayName}")
    };

    /// <summary>
    /// サポートされている全言語を取得
    /// </summary>
    public static IReadOnlyList<Language> SupportedLanguages =>
        [Japanese, English, ChineseSimplified, ChineseTraditional, Korean,
         Spanish, French, German, Italian, Portuguese, Russian, Arabic];

    /// <summary>
    /// 有効な翻訳用言語かどうか（自動検出以外）
    /// </summary>
    public bool IsValidForTranslation => !IsAutoDetect && Code != "auto";

    /// <summary>
    /// 文字列表現
    /// </summary>
    public override string ToString()
    {
        if (!string.IsNullOrEmpty(RegionCode))
            return $"{DisplayName} ({Code}-{RegionCode})";

        return $"{DisplayName} ({Code})";
    }
}
