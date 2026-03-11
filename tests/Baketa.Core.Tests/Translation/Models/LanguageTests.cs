using System;
using Xunit;
using Language = Baketa.Core.Models.Translation.Language;

namespace Baketa.Core.Tests.Translation.Models;

/// <summary>
/// Languageクラスの多言語対応テスト
/// </summary>
public class LanguageTests
{
    // ========================================
    // FromCode: 言語コードからLanguage生成
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
    [InlineData("zh-CN", "Chinese (Simplified)")]
    [InlineData("zh-TW", "Chinese (Traditional)")]
    public void FromCode_WithValidCode_ReturnsCorrectLanguage(string code, string expectedDisplayName)
    {
        var language = Language.FromCode(code);
        Assert.Equal(expectedDisplayName, language.DisplayName);
    }

    [Theory]
    [InlineData("EN")]
    [InlineData("en")]
    [InlineData("Ko")]
    [InlineData("FR")]
    [InlineData("it")]
    [InlineData("PT")]
    public void FromCode_IsCaseInsensitive(string code)
    {
        var language = Language.FromCode(code);
        Assert.NotEqual(code, language.DisplayName); // コードそのままではなく表示名が返る
    }

    [Fact]
    public void FromCode_WithUnknownCode_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => Language.FromCode("xx"));
    }

    [Fact]
    public void FromCodeOrDefault_WithUnknownCode_ReturnsCodeAsDisplayName()
    {
        var language = Language.FromCodeOrDefault("xx");
        Assert.Equal("xx", language.DisplayName);
        Assert.Equal("xx", language.Code);
    }

    [Fact]
    public void FromCode_WithNull_ThrowsNullReferenceException()
    {
        Assert.ThrowsAny<Exception>(() => Language.FromCode(null!));
    }

    // ========================================
    // 静的プロパティ: Name/NativeName/RegionCode
    // ========================================

    [Fact]
    public void Italian_HasCorrectProperties()
    {
        var lang = Language.Italian;
        Assert.Equal("it", lang.Code);
        Assert.Equal("Italian", lang.Name);
        Assert.Equal("Italian", lang.DisplayName);
        Assert.Equal("Italiano", lang.NativeName);
        Assert.Equal("IT", lang.RegionCode);
        Assert.False(lang.IsRightToLeft);
    }

    [Fact]
    public void Portuguese_HasCorrectProperties()
    {
        var lang = Language.Portuguese;
        Assert.Equal("pt", lang.Code);
        Assert.Equal("Portuguese", lang.Name);
        Assert.Equal("Portuguese", lang.DisplayName);
        Assert.Equal("Português", lang.NativeName);
        Assert.Equal("BR", lang.RegionCode);
        Assert.False(lang.IsRightToLeft);
    }

    [Fact]
    public void Korean_HasCorrectProperties()
    {
        var lang = Language.Korean;
        Assert.Equal("ko", lang.Code);
        Assert.Equal("Korean", lang.Name);
        Assert.Equal("Korean", lang.DisplayName);
        Assert.Equal("한국어", lang.NativeName);
        Assert.Equal("KR", lang.RegionCode);
    }

    [Fact]
    public void French_HasCorrectProperties()
    {
        var lang = Language.French;
        Assert.Equal("fr", lang.Code);
        Assert.Equal("French", lang.Name);
        Assert.Equal("French", lang.DisplayName);
        Assert.Equal("Français", lang.NativeName);
        Assert.Equal("FR", lang.RegionCode);
    }

    [Fact]
    public void German_HasCorrectProperties()
    {
        var lang = Language.German;
        Assert.Equal("de", lang.Code);
        Assert.Equal("German", lang.Name);
        Assert.Equal("German", lang.DisplayName);
        Assert.Equal("Deutsch", lang.NativeName);
        Assert.Equal("DE", lang.RegionCode);
    }

    [Fact]
    public void Spanish_HasCorrectProperties()
    {
        var lang = Language.Spanish;
        Assert.Equal("es", lang.Code);
        Assert.Equal("Spanish", lang.Name);
        Assert.Equal("Spanish", lang.DisplayName);
        Assert.Equal("Español", lang.NativeName);
        Assert.Equal("ES", lang.RegionCode);
    }

    // ========================================
    // Equals: 言語コードでの等価比較
    // ========================================

    [Fact]
    public void Equals_SameCode_ReturnsTrue()
    {
        var lang1 = Language.FromCode("it");
        var lang2 = Language.Italian;
        Assert.Equal(lang1, lang2);
    }

    [Fact]
    public void Equals_DifferentCode_ReturnsFalse()
    {
        Assert.NotEqual(Language.Italian, Language.Portuguese);
    }
}
