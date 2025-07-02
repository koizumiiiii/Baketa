using System;
using Baketa.Core.Translation.Models;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// ChineseVariant列挙型とその拡張メソッドのテスト
/// </summary>
public class ChineseVariantTests
{
    [Theory]
    [InlineData(ChineseVariant.Auto, "")]
    [InlineData(ChineseVariant.Simplified, ">>cmn_Hans<<")]
    [InlineData(ChineseVariant.Traditional, ">>cmn_Hant<<")]
    [InlineData(ChineseVariant.Cantonese, ">>yue<<")]
    public void GetOpusPrefix_ShouldReturnCorrectPrefix(ChineseVariant variant, string expectedPrefix)
    {
        // Act
        var result = variant.GetOpusPrefix();

        // Assert
        Assert.Equal(expectedPrefix, result);
    }

    [Theory]
    [InlineData(ChineseVariant.Auto, "中国語（自動）")]
    [InlineData(ChineseVariant.Simplified, "中国語（簡体字）")]
    [InlineData(ChineseVariant.Traditional, "中国語（繁体字）")]
    [InlineData(ChineseVariant.Cantonese, "広東語")]
    public void GetDisplayName_ShouldReturnCorrectDisplayName(ChineseVariant variant, string expectedName)
    {
        // Act
        var result = variant.GetDisplayName();

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData(ChineseVariant.Auto, "Chinese (Auto)")]
    [InlineData(ChineseVariant.Simplified, "Chinese (Simplified)")]
    [InlineData(ChineseVariant.Traditional, "Chinese (Traditional)")]
    [InlineData(ChineseVariant.Cantonese, "Cantonese")]
    public void GetEnglishDisplayName_ShouldReturnCorrectEnglishName(ChineseVariant variant, string expectedName)
    {
        // Act
        var result = variant.GetEnglishDisplayName();

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData(ChineseVariant.Auto, "中文（自动）")]
    [InlineData(ChineseVariant.Simplified, "中文（简体）")]
    [InlineData(ChineseVariant.Traditional, "中文（繁體）")]
    [InlineData(ChineseVariant.Cantonese, "粵語")]
    public void GetNativeDisplayName_ShouldReturnCorrectNativeName(ChineseVariant variant, string expectedName)
    {
        // Act
        var result = variant.GetNativeDisplayName();

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData("zh-hans", ChineseVariant.Simplified)]
    [InlineData("zh-cn", ChineseVariant.Simplified)]
    [InlineData("zh-chs", ChineseVariant.Simplified)]
    [InlineData("cmn_hans", ChineseVariant.Simplified)]
    [InlineData("zh-hant", ChineseVariant.Traditional)]
    [InlineData("zh-tw", ChineseVariant.Traditional)]
    [InlineData("zh-hk", ChineseVariant.Traditional)]
    [InlineData("zh-mo", ChineseVariant.Traditional)]
    [InlineData("zh-cht", ChineseVariant.Traditional)]
    [InlineData("cmn_hant", ChineseVariant.Traditional)]
    [InlineData("yue", ChineseVariant.Cantonese)]
    [InlineData("yue-hk", ChineseVariant.Cantonese)]
    [InlineData("yue-cn", ChineseVariant.Cantonese)]
    [InlineData("zh", ChineseVariant.Auto)]
    [InlineData("cmn", ChineseVariant.Auto)]
    [InlineData("zho", ChineseVariant.Auto)]
    [InlineData("unknown", ChineseVariant.Auto)]
    [InlineData("", ChineseVariant.Auto)]
    [InlineData(null, ChineseVariant.Auto)]
    public void FromLanguageCode_ShouldReturnCorrectVariant(string languageCode, ChineseVariant expectedVariant)
    {
        // Act
        var result = ChineseVariantExtensions.FromLanguageCode(languageCode);

        // Assert
        Assert.Equal(expectedVariant, result);
    }

    [Theory]
    [InlineData(ChineseVariant.Simplified, "zh-Hans")]
    [InlineData(ChineseVariant.Traditional, "zh-Hant")]
    [InlineData(ChineseVariant.Cantonese, "yue")]
    [InlineData(ChineseVariant.Auto, "zh")]
    public void ToLanguageCode_ShouldReturnCorrectLanguageCode(ChineseVariant variant, string expectedCode)
    {
        // Act
        var result = variant.ToLanguageCode();

        // Assert
        Assert.Equal(expectedCode, result);
    }

    [Theory]
    [InlineData(ChineseVariant.Auto)]
    [InlineData(ChineseVariant.Simplified)]
    [InlineData(ChineseVariant.Traditional)]
    [InlineData(ChineseVariant.Cantonese)]
    public void IsValid_ShouldReturnTrueForValidVariants(ChineseVariant variant)
    {
        // Act
        var result = variant.IsValid();

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void IsValid_ShouldReturnFalseForInvalidVariant()
    {
        // Arrange
        var invalidVariant = (ChineseVariant)999;

        // Act
        var result = invalidVariant.IsValid();

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("ZH-HANS", ChineseVariant.Simplified)] // 大文字
    [InlineData("  zh-hant  ", ChineseVariant.Traditional)] // 前後空白
    [InlineData("Zh-Cn", ChineseVariant.Simplified)] // 混合大小文字
    public void FromLanguageCode_ShouldHandleCaseAndWhitespace(string languageCode, ChineseVariant expectedVariant)
    {
        // Act
        var result = ChineseVariantExtensions.FromLanguageCode(languageCode);

        // Assert
        Assert.Equal(expectedVariant, result);
    }
}
