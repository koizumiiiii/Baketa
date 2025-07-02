using System;
using System.Linq;
using Baketa.Core.Translation.Configuration;
using Baketa.Core.Translation.Models;
using Xunit;

namespace Baketa.Core.Tests.Translation.Configuration;

/// <summary>
/// LanguageConfigurationのテスト
/// </summary>
public class LanguageConfigurationTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Act
        var config = new LanguageConfiguration();

        // Assert
        Assert.NotNull(config.SupportedLanguages);
        Assert.Empty(config.SupportedLanguages);
        Assert.Equal("auto", config.DefaultSourceLanguage);
        Assert.Equal("ja", config.DefaultTargetLanguage);
        Assert.True(config.EnableChineseVariantAutoDetection);
        Assert.True(config.EnableLanguageDetection);
    }

    [Fact]
    public void Default_ShouldReturnConfigWithSupportedLanguages()
    {
        // Act
        var config = LanguageConfiguration.Default;

        // Assert
        Assert.NotNull(config.SupportedLanguages);
        Assert.NotEmpty(config.SupportedLanguages);
        
        // 主要言語が含まれていることを確認
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "auto");
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "en");
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "ja");
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "zh");
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "zh-Hans");
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "zh-Hant");
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ja", "日本語")]
    [InlineData("zh-Hans", "中国語（簡体字）")]
    [InlineData("zh-Hant", "中国語（繁体字）")]
    [InlineData("auto", "自動検出")]
    public void GetLanguageInfo_WithValidCode_ShouldReturnCorrectInfo(string code, string expectedName)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.GetLanguageInfoInstance(code);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(code, result.Code);
        Assert.Equal(expectedName, result.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("nonexistent")]
    public void GetLanguageInfo_WithInvalidCode_ShouldReturnNull(string? code)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.GetLanguageInfoInstance(code!);

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("zh", ChineseVariant.Auto)]
    [InlineData("zh-Hans", ChineseVariant.Simplified)]
    [InlineData("zh-Hant", ChineseVariant.Traditional)]
    [InlineData("yue", ChineseVariant.Cantonese)]
    [InlineData("en", ChineseVariant.Auto)] // 非中国語の場合はAuto
    public void GetChineseVariant_ShouldReturnCorrectVariant(string languageCode, ChineseVariant expectedVariant)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.GetChineseVariantForLanguageCode(languageCode);

        // Assert
        Assert.Equal(expectedVariant, result);
    }

    [Fact]
    public void GetChineseLanguages_ShouldReturnOnlyChineseLanguages()
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var chineseLanguages = config.GetChineseLanguages().ToList();

        // Assert
        Assert.NotEmpty(chineseLanguages);
        Assert.All(chineseLanguages, lang => Assert.True(lang.IsChinese()));
    }

    [Theory]
    [InlineData("auto", "ja", true)] // 自動検出は常にサポート
    [InlineData("en", "ja", true)]
    [InlineData("ja", "en", true)]
    [InlineData("zh-Hans", "en", true)]
    [InlineData("en", "zh-Hant", true)]
    [InlineData("en", "en", false)] // 同じ言語への翻訳は不要
    [InlineData("nonexistent", "ja", false)] // 存在しない言語
    [InlineData("en", "nonexistent", false)] // 存在しない言語
    public void IsTranslationPairSupported_ShouldReturnCorrectResult(
        string sourceLang, 
        string targetLang, 
        bool expectedSupported)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.IsTranslationPairSupported(sourceLang, targetLang);

        // Assert
        Assert.Equal(expectedSupported, result);
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ja", "日本語")]
    [InlineData("nonexistent", "nonexistent")]
    public void GetDisplayName_ShouldReturnCorrectName(string languageCode, string expectedName)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.GetDisplayName(languageCode);

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Theory]
    [InlineData("en", "English")]
    [InlineData("ja", "日本語")]
    [InlineData("nonexistent", "nonexistent")]
    public void GetNativeName_ShouldReturnCorrectName(string languageCode, string expectedName)
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var result = config.GetNativeName(languageCode);

        // Assert
        Assert.Equal(expectedName, result);
    }

    [Fact]
    public void AddLanguage_WithNewLanguage_ShouldAddToCollection()
    {
        // Arrange
        var config = new LanguageConfiguration();
        var newLang = new LanguageInfo
        {
            Code = "test",
            Name = "Test Language"
        };

        // Act
        config.AddLanguage(newLang);

        // Assert
        Assert.Contains(config.SupportedLanguages, lang => lang.Code == "test");
    }

    [Fact]
    public void AddLanguage_WithExistingLanguage_ShouldUpdateExisting()
    {
        // Arrange
        var config = LanguageConfiguration.Default;
        var originalCount = config.SupportedLanguages.Count;
        var updatedLang = new LanguageInfo
        {
            Code = "en",
            Name = "Updated English"
        };

        // Act
        config.AddLanguage(updatedLang);

        // Assert
        Assert.Equal(originalCount, config.SupportedLanguages.Count); // 数は変わらない
        var englishLang = config.GetLanguageInfoInstance("en");
        Assert.NotNull(englishLang);
        Assert.Equal("Updated English", englishLang.Name);
    }

    [Fact]
    public void AddLanguage_WithNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var config = new LanguageConfiguration();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => config.AddLanguage(null!));
    }

    [Fact]
    public void RemoveLanguage_WithExistingLanguage_ShouldRemoveAndReturnTrue()
    {
        // Arrange
        var config = LanguageConfiguration.Default;
        var originalCount = config.SupportedLanguages.Count;

        // Act
        var result = config.RemoveLanguage("en");

        // Assert
        Assert.True(result);
        Assert.Equal(originalCount - 1, config.SupportedLanguages.Count);
        Assert.Null(config.GetLanguageInfoInstance("en"));
    }

    [Theory]
    [InlineData("nonexistent")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RemoveLanguage_WithInvalidLanguage_ShouldReturnFalse(string? languageCode)
    {
        // Arrange
        var config = LanguageConfiguration.Default;
        var originalCount = config.SupportedLanguages.Count;

        // Act
        var result = config.RemoveLanguage(languageCode!);

        // Assert
        Assert.False(result);
        Assert.Equal(originalCount, config.SupportedLanguages.Count);
    }

    [Fact]
    public void Validate_WithValidConfiguration_ShouldReturnNoErrors()
    {
        // Arrange
        var config = LanguageConfiguration.Default;

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_WithEmptyLanguages_ShouldReturnError()
    {
        // Arrange
        var config = new LanguageConfiguration();

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("サポートされている言語が設定されていません"));
    }

    [Fact]
    public void Validate_WithEmptyDefaultSource_ShouldReturnError()
    {
        // Arrange
        var config = LanguageConfiguration.Default;
        config.DefaultSourceLanguage = "";

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("デフォルトのソース言語が設定されていません"));
    }

    [Fact]
    public void Validate_WithEmptyDefaultTarget_ShouldReturnError()
    {
        // Arrange
        var config = LanguageConfiguration.Default;
        config.DefaultTargetLanguage = "";

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("デフォルトのターゲット言語が設定されていません"));
    }

    [Fact]
    public void Validate_WithDuplicateLanguages_ShouldReturnError()
    {
        // Arrange
        var config = new LanguageConfiguration();
        config.SupportedLanguages.Add(new LanguageInfo
        {
            Code = "en",
            Name = "English 1"
        });
        config.SupportedLanguages.Add(new LanguageInfo
        {
            Code = "EN",
            Name = "English 2"
        }); // 大文字小文字違い

        // Act
        var errors = config.Validate();

        // Assert
        Assert.Contains(errors, e => e.Contains("重複している言語コード"));
    }

    [Fact]
    public void ResetToDefault_ShouldRestoreDefaultSettings()
    {
        // Arrange
        var config = new LanguageConfiguration();
        config.SupportedLanguages.Clear();
        config.DefaultSourceLanguage = "modified";
        config.DefaultTargetLanguage = "modified";
        config.EnableChineseVariantAutoDetection = false;
        config.EnableLanguageDetection = false;

        // Act
        config.ResetToDefault();

        // Assert
        Assert.NotEmpty(config.SupportedLanguages);
        Assert.Equal("auto", config.DefaultSourceLanguage);
        Assert.Equal("ja", config.DefaultTargetLanguage);
        Assert.True(config.EnableChineseVariantAutoDetection);
        Assert.True(config.EnableLanguageDetection);
    }

    [Fact]
    public void Clone_ShouldCreateIdenticalCopy()
    {
        // Arrange
        var original = LanguageConfiguration.Default;
        original.DefaultSourceLanguage = "modified";
        original.DefaultTargetLanguage = "modified";

        // Act
        var clone = original.Clone();

        // Assert
        Assert.NotSame(original, clone);
        Assert.Equal(original.SupportedLanguages.Count, clone.SupportedLanguages.Count);
        Assert.Equal(original.DefaultSourceLanguage, clone.DefaultSourceLanguage);
        Assert.Equal(original.DefaultTargetLanguage, clone.DefaultTargetLanguage);
        Assert.Equal(original.EnableChineseVariantAutoDetection, clone.EnableChineseVariantAutoDetection);
        Assert.Equal(original.EnableLanguageDetection, clone.EnableLanguageDetection);

        // 深いコピーであることを確認
        original.SupportedLanguages.Clear();
        Assert.NotEmpty(clone.SupportedLanguages);
    }
}
