using Baketa.Core.Abstractions.Translation;
using Xunit;

namespace Baketa.Core.Tests.Abstractions.Translation;

/// <summary>
/// LanguageCodeNormalizerの多言語対応テスト
/// </summary>
public class LanguageCodeNormalizerTests
{
    // ========================================
    // Normalize: イタリア語バリエーション
    // ========================================

    [Theory]
    [InlineData("ita", "it")]
    [InlineData("italian", "it")]
    [InlineData("italiano", "it")]
    [InlineData("ITA", "it")]
    [InlineData("Italian", "it")]
    public void Normalize_ItalianVariants_ReturnsIt(string input, string expected)
    {
        var result = LanguageCodeNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    // ========================================
    // Normalize: ポルトガル語バリエーション
    // ========================================

    [Theory]
    [InlineData("por", "pt")]
    [InlineData("portuguese", "pt")]
    [InlineData("português", "pt")]
    [InlineData("POR", "pt")]
    [InlineData("Portuguese", "pt")]
    public void Normalize_PortugueseVariants_ReturnsPt(string input, string expected)
    {
        var result = LanguageCodeNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    // ========================================
    // Normalize: 既存言語（回帰テスト）
    // ========================================

    [Theory]
    [InlineData("korean", "ko")]
    [InlineData("kor", "ko")]
    [InlineData("french", "fr")]
    [InlineData("fra", "fr")]
    [InlineData("français", "fr")]
    [InlineData("german", "de")]
    [InlineData("ger", "de")]
    [InlineData("deutsch", "de")]
    [InlineData("spanish", "es")]
    [InlineData("spa", "es")]
    [InlineData("español", "es")]
    public void Normalize_ExistingLanguageVariants_ReturnsCorrectCode(string input, string expected)
    {
        var result = LanguageCodeNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    // ========================================
    // IsKnownRegionalVariant
    // ========================================

    [Theory]
    [InlineData("it-IT", true)]
    [InlineData("pt-BR", true)]
    [InlineData("pt-PT", true)]
    [InlineData("fr-FR", true)]
    [InlineData("de-DE", true)]
    [InlineData("es-ES", true)]
    [InlineData("it-XX", false)]
    [InlineData("pt-XX", false)]
    public void IsSupported_RegionalVariants_ReturnsExpectedResult(string input, bool expected)
    {
        var result = LanguageCodeNormalizer.IsSupported(input);
        Assert.Equal(expected, result);
    }

    // ========================================
    // Normalize: 地域変種はそのまま返す
    // ========================================

    [Theory]
    [InlineData("it-IT", "it-it")]
    [InlineData("pt-BR", "pt-br")]
    [InlineData("pt-PT", "pt-pt")]
    public void Normalize_KnownRegionalVariant_ReturnsLowercased(string input, string expected)
    {
        var result = LanguageCodeNormalizer.Normalize(input);
        Assert.Equal(expected, result);
    }

    // ========================================
    // エッジケース
    // ========================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Normalize_WithEmptyOrNull_ReturnsSameValue(string? input)
    {
        var result = LanguageCodeNormalizer.Normalize(input!);
        Assert.Equal(input, result);
    }

    [Fact]
    public void Normalize_WithUnknownCode_ReturnsLowercased()
    {
        var result = LanguageCodeNormalizer.Normalize("UNKNOWN");
        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetNormalizationStats_ReturnsNonZeroCounts()
    {
        var (totalMappings, uniquePrimaryLanguages) = LanguageCodeNormalizer.GetNormalizationStats();
        Assert.True(totalMappings > 0);
        Assert.True(uniquePrimaryLanguages > 0);
    }
}
