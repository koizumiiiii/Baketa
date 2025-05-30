using System;
using System.Linq;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// ChineseLanguageProcessorのテストクラス
/// </summary>
public class ChineseLanguageProcessorTests
{
    private readonly Mock<ILogger<ChineseLanguageProcessor>> _mockLogger;
    private readonly ChineseLanguageProcessor _processor;

    public ChineseLanguageProcessorTests()
    {
        _mockLogger = new Mock<ILogger<ChineseLanguageProcessor>>();
        _processor = new ChineseLanguageProcessor(_mockLogger.Object);
    }

    [Theory]
    [InlineData("zh-CN", ">>cmn_Hans<<")]
    [InlineData("zh-Hans", ">>cmn_Hans<<")]
    [InlineData("zh-TW", ">>cmn_Hant<<")]
    [InlineData("zh-Hant", ">>cmn_Hant<<")]
    [InlineData("zh", ">>cmn_Hans<<")]
    [InlineData("cmn_Hans", ">>cmn_Hans<<")]
    [InlineData("cmn_Hant", ">>cmn_Hant<<")]
    [InlineData("yue", ">>yue<<")]
    [InlineData("yue-HK", ">>yue_Hant<<")]
    [InlineData("yue-CN", ">>yue_Hans<<")]
    public void GetOpusPrefix_ValidChineseLanguageCode_ReturnsCorrectPrefix(string languageCode, string expectedPrefix)
    {
        // Act
        var result = _processor.GetOpusPrefix(languageCode);

        // Assert
        Assert.Equal(expectedPrefix, result);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    public void GetOpusPrefix_NonChineseLanguageCode_ReturnsEmptyString(string languageCode)
    {
        // Act
        var result = _processor.GetOpusPrefix(languageCode);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void GetOpusPrefix_ChineseSimplifiedLanguage_ReturnsCorrectPrefix()
    {
        // Arrange
        var language = Language.ChineseSimplified;

        // Act
        var result = _processor.GetOpusPrefix(language);

        // Assert
        Assert.Equal(">>cmn_Hans<<", result);
    }

    [Fact]
    public void GetOpusPrefix_ChineseTraditionalLanguage_ReturnsCorrectPrefix()
    {
        // Arrange
        var language = Language.ChineseTraditional;

        // Act
        var result = _processor.GetOpusPrefix(language);

        // Assert
        Assert.Equal(">>cmn_Hant<<", result);
    }

    [Fact]
    public void GetOpusPrefix_NullLanguage_ReturnsEmptyString()
    {
        // Act
        var result = _processor.GetOpusPrefix((Language)null!);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Theory]
    [InlineData("Hello world", "zh-CN", ">>cmn_Hans<< Hello world")]
    [InlineData("Hello world", "zh-TW", ">>cmn_Hant<< Hello world")]
    [InlineData("Hello world", "yue", ">>yue<< Hello world")]
    [InlineData("Hello world", "en", "Hello world")] // 非中国語の場合はプレフィックスなし
    public void AddPrefixToText_ValidInput_ReturnsCorrectResult(string text, string languageCode, string expected)
    {
        // Arrange
        var language = new Language { Code = languageCode, DisplayName = "Test" };

        // Act
        var result = _processor.AddPrefixToText(text, language);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void AddPrefixToText_TextWithExistingPrefix_ReturnsUnchanged()
    {
        // Arrange
        var text = ">>cmn_Hans<< Hello world";
        var language = Language.ChineseSimplified;

        // Act
        var result = _processor.AddPrefixToText(text, language);

        // Assert
        Assert.Equal(text, result);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void AddPrefixToText_EmptyOrNullText_ReturnsOriginalText(string text)
    {
        // Arrange
        var language = Language.ChineseSimplified;

        // Act
        var result = _processor.AddPrefixToText(text, language);

        // Assert  
        Assert.Equal(text ?? string.Empty, result);
    }

    [Theory]
    [InlineData("国家", ChineseScriptType.Simplified)]
    [InlineData("國家", ChineseScriptType.Traditional)]
    [InlineData("Hello", ChineseScriptType.Unknown)]
    [InlineData("", ChineseScriptType.Unknown)]
    [InlineData(null, ChineseScriptType.Unknown)]
    public void DetectScriptType_ValidInput_ReturnsCorrectType(string text, ChineseScriptType expected)
    {
        // Act
        var result = _processor.DetectScriptType(text);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("zh-CN", true)]
    [InlineData("zh-TW", true)]
    [InlineData("zh", true)]
    [InlineData("cmn", true)]
    [InlineData("yue", true)]
    [InlineData("en", false)]
    [InlineData("ja", false)]
    [InlineData("ko", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsChineseLanguageCode_ValidInput_ReturnsCorrectResult(string languageCode, bool expected)
    {
        // Act
        var result = _processor.IsChineseLanguageCode(languageCode);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData('中', true)]
    [InlineData('国', true)]
    [InlineData('A', false)]
    [InlineData('あ', false)]
    [InlineData('1', false)]
    public void IsChineseCharacter_ValidInput_ReturnsCorrectResult(char character, bool expected)
    {
        // Act
        var result = ChineseLanguageProcessor.IsChineseCharacter(character);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSupportedLanguageCodes_ReturnsNonEmptyList()
    {
        // Act
        var result = _processor.GetSupportedLanguageCodes();

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
        Assert.Contains("zh-CN", result);
        Assert.Contains("zh-TW", result);
        Assert.Contains("zh-Hans", result);
        Assert.Contains("zh-Hant", result);
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChineseLanguageProcessor(null!));
    }
}
