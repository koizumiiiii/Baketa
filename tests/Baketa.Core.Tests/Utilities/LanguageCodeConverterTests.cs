using Baketa.Core.Translation.Models;
using Baketa.Core.Utilities;
using Xunit;

namespace Baketa.Core.Tests.Utilities;

/// <summary>
/// LanguageCodeConverterの多言語対応テスト
/// </summary>
public class LanguageCodeConverterTests
{
    // ========================================
    // ToLanguageCode: 表示名 → 言語コード
    // ========================================

    [Theory]
    [InlineData("Japanese", "ja")]
    [InlineData("English", "en")]
    [InlineData("Korean", "ko")]
    [InlineData("French", "fr")]
    [InlineData("German", "de")]
    [InlineData("Italian", "it")]
    [InlineData("Spanish", "es")]
    [InlineData("Portuguese", "pt")]
    [InlineData("Chinese (Simplified)", "zh-CN")]
    [InlineData("Chinese (Traditional)", "zh-TW")]
    public void ToLanguageCode_WithEnglishDisplayName_ReturnsCorrectCode(string displayName, string expectedCode)
    {
        var result = LanguageCodeConverter.ToLanguageCode(displayName);
        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData("日本語", "ja")]
    [InlineData("英語", "en")]
    [InlineData("韓国語", "ko")]
    [InlineData("フランス語", "fr")]
    [InlineData("ドイツ語", "de")]
    [InlineData("イタリア語", "it")]
    [InlineData("スペイン語", "es")]
    [InlineData("ポルトガル語", "pt")]
    [InlineData("簡体字中国語", "zh-CN")]
    [InlineData("繁体字中国語", "zh-TW")]
    public void ToLanguageCode_WithJapaneseDisplayName_ReturnsCorrectCode(string displayName, string expectedCode)
    {
        var result = LanguageCodeConverter.ToLanguageCode(displayName);
        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData("한국어", "ko")]
    [InlineData("Français", "fr")]
    [InlineData("Deutsch", "de")]
    [InlineData("Italiano", "it")]
    [InlineData("Español", "es")]
    [InlineData("Português", "pt")]
    [InlineData("简体中文", "zh-CN")]
    [InlineData("繁體中文", "zh-TW")]
    public void ToLanguageCode_WithNativeDisplayName_ReturnsCorrectCode(string displayName, string expectedCode)
    {
        var result = LanguageCodeConverter.ToLanguageCode(displayName);
        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData("", "en")]
    [InlineData(null, "en")]
    [InlineData("Unknown", "en")]
    public void ToLanguageCode_WithInvalidInput_ReturnsDefault(string? displayName, string expectedCode)
    {
        var result = LanguageCodeConverter.ToLanguageCode(displayName!);
        Assert.Equal(expectedCode, result);
    }

    // ========================================
    // ToDisplayName: 言語コード → 英語表示名
    // ========================================

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ja", "Japanese")]
    [InlineData("ko", "Korean")]
    [InlineData("fr", "French")]
    [InlineData("de", "German")]
    [InlineData("it", "Italian")]
    [InlineData("es", "Spanish")]
    [InlineData("pt", "Portuguese")]
    [InlineData("zh-cn", "Chinese (Simplified)")]
    [InlineData("zh-tw", "Chinese (Traditional)")]
    public void ToDisplayName_WithValidCode_ReturnsEnglishName(string code, string expectedName)
    {
        var result = LanguageCodeConverter.ToDisplayName(code);
        Assert.Equal(expectedName, result);
    }

    // ========================================
    // ToJapaneseDisplayName: 言語コード → 日本語表示名
    // ========================================

    [Theory]
    [InlineData("en", "英語")]
    [InlineData("ja", "日本語")]
    [InlineData("ko", "韓国語")]
    [InlineData("fr", "フランス語")]
    [InlineData("de", "ドイツ語")]
    [InlineData("it", "イタリア語")]
    [InlineData("es", "スペイン語")]
    [InlineData("pt", "ポルトガル語")]
    [InlineData("zh-cn", "簡体字中国語")]
    [InlineData("zh-tw", "繁体字中国語")]
    public void ToJapaneseDisplayName_WithValidCode_ReturnsJapaneseName(string code, string expectedName)
    {
        var result = LanguageCodeConverter.ToJapaneseDisplayName(code);
        Assert.Equal(expectedName, result);
    }

    // ========================================
    // FromLanguageObject: Language → 言語コード
    // ========================================

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("es")]
    [InlineData("pt")]
    [InlineData("zh-TW")]
    public void FromLanguageObject_WithKnownLanguage_ReturnsCorrectCode(string expectedCode)
    {
        var language = Language.FromCode(expectedCode);
        var result = LanguageCodeConverter.FromLanguageObject(language);
        Assert.Equal(expectedCode, result, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void FromLanguageObject_WithNull_ReturnsEn()
    {
        var result = LanguageCodeConverter.FromLanguageObject(null!);
        Assert.Equal("en", result);
    }

    // ========================================
    // ToLanguageEnum: 言語コード → Language
    // ========================================

    [Theory]
    [InlineData("en", "en")]
    [InlineData("ja", "ja")]
    [InlineData("ko", "ko")]
    [InlineData("fr", "fr")]
    [InlineData("de", "de")]
    [InlineData("it", "it")]
    [InlineData("es", "es")]
    [InlineData("pt", "pt")]
    [InlineData("zh-cn", "zh-CN")]
    [InlineData("zh-tw", "zh-TW")]
    public void ToLanguageEnum_WithValidCode_ReturnsCorrectLanguage(string inputCode, string expectedCode)
    {
        var result = LanguageCodeConverter.ToLanguageEnum(inputCode);
        Assert.Equal(expectedCode, result.Code);
    }

    [Fact]
    public void ToLanguageEnum_WithUnknownCode_ReturnsFallback()
    {
        var result = LanguageCodeConverter.ToLanguageEnum("xx");
        Assert.Equal("en", result.Code); // default fallback
    }

    // ========================================
    // IsSupportedLanguageCode
    // ========================================

    [Theory]
    [InlineData("en", true)]
    [InlineData("ja", true)]
    [InlineData("ko", true)]
    [InlineData("fr", true)]
    [InlineData("de", true)]
    [InlineData("it", true)]
    [InlineData("es", true)]
    [InlineData("pt", true)]
    [InlineData("zh-cn", true)]
    [InlineData("zh-tw", true)]
    [InlineData("xx", false)]
    [InlineData("", false)]
    public void IsSupportedLanguageCode_ReturnsExpectedResult(string code, bool expected)
    {
        var result = LanguageCodeConverter.IsSupportedLanguageCode(code);
        Assert.Equal(expected, result);
    }

    // ========================================
    // ラウンドトリップテスト
    // ========================================

    [Theory]
    [InlineData("ko")]
    [InlineData("fr")]
    [InlineData("de")]
    [InlineData("it")]
    [InlineData("es")]
    [InlineData("pt")]
    public void RoundTrip_CodeToDisplayNameAndBack_ReturnsOriginalCode(string code)
    {
        var displayName = LanguageCodeConverter.ToDisplayName(code);
        var resultCode = LanguageCodeConverter.ToLanguageCode(displayName);
        Assert.Equal(code, resultCode);
    }
}
