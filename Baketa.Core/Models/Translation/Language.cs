namespace Baketa.Core.Models.Translation;

/// <summary>
/// 型安全な言語表現
/// 言語コードと表示名を統一管理し、将来の言語追加に対応
/// </summary>
public sealed record Language(string Code, string DisplayName)
{
    /// <summary>
    /// 日本語
    /// </summary>
    public static Language Japanese => new("ja", "Japanese");

    /// <summary>
    /// 英語
    /// </summary>
    public static Language English => new("en", "English");

    /// <summary>
    /// 簡体字中国語
    /// </summary>
    public static Language ChineseSimplified => new("zh-cn", "Chinese (Simplified)");

    /// <summary>
    /// 繁体字中国語
    /// </summary>
    public static Language ChineseTraditional => new("zh-tw", "Chinese (Traditional)");

    /// <summary>
    /// 韓国語
    /// </summary>
    public static Language Korean => new("ko", "Korean");

    /// <summary>
    /// スペイン語
    /// </summary>
    public static Language Spanish => new("es", "Spanish");

    /// <summary>
    /// フランス語
    /// </summary>
    public static Language French => new("fr", "French");

    /// <summary>
    /// ドイツ語
    /// </summary>
    public static Language German => new("de", "German");

    /// <summary>
    /// イタリア語
    /// </summary>
    public static Language Italian => new("it", "Italian");

    /// <summary>
    /// ポルトガル語
    /// </summary>
    public static Language Portuguese => new("pt", "Portuguese");

    /// <summary>
    /// ロシア語
    /// </summary>
    public static Language Russian => new("ru", "Russian");

    /// <summary>
    /// アラビア語
    /// </summary>
    public static Language Arabic => new("ar", "Arabic");

    /// <summary>
    /// 自動検出
    /// </summary>
    public static Language Auto => new("auto", "Auto");

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
        "auto" or "自動検出" => Auto,
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
    public bool IsValidForTranslation => !Equals(Auto);
}
