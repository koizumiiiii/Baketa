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
        "auto" or "自動検出" => Auto,
        _ => throw new ArgumentException($"Unsupported display name: {displayName}")
    };

    /// <summary>
    /// サポートされている全言語を取得
    /// </summary>
    public static IReadOnlyList<Language> SupportedLanguages => [Japanese, English];

    /// <summary>
    /// 有効な翻訳用言語かどうか（自動検出以外）
    /// </summary>
    public bool IsValidForTranslation => !Equals(Auto);
}
