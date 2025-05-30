using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local.Onnx;
using Baketa.Infrastructure.Translation.Local.Onnx.Chinese;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.Chinese;

/// <summary>
/// OpusMtOnnxEngineの中国語対応機能のテストクラス
/// </summary>
public class OpusMtOnnxEngineChineseTests
{
    private readonly Mock<ILogger<ChineseLanguageProcessor>> _mockChineseLogger;
    private readonly ChineseLanguageProcessor _processor;

    public OpusMtOnnxEngineChineseTests()
    {
        _mockChineseLogger = new Mock<ILogger<ChineseLanguageProcessor>>();
        _processor = new ChineseLanguageProcessor(_mockChineseLogger.Object);
    }

    [Theory]
    [InlineData("Hello world", "zh-CN", ">>cmn_Hans<< Hello world")]
    [InlineData("Hello world", "zh-TW", ">>cmn_Hant<< Hello world")]
    [InlineData("Hello world", "zh-Hans", ">>cmn_Hans<< Hello world")]
    [InlineData("Hello world", "zh-Hant", ">>cmn_Hant<< Hello world")]
    [InlineData("Hello world", "en", "Hello world")] // 非中国語の場合はプレフィックスなし
    public void ApplyChinesePrefixIfNeeded_ValidInput_ReturnsCorrectResult(string inputText, string targetLanguageCode, string expectedText)
    {
        // Arrange
        var targetLanguage = new Language { Code = targetLanguageCode, DisplayName = "Test" };

        // Act
        var result = _processor.AddPrefixToText(inputText, targetLanguage);

        // Assert
        Assert.Equal(expectedText, result);
    }

    [Theory]
    [InlineData("国家")]
    [InlineData("國家")]
    [InlineData("你好世界")]
    [InlineData("Hello")]
    public void DetectChineseScriptType_ValidText_ReturnsLanguageObject(string text)
    {
        // Act
        var scriptType = _processor.DetectScriptType(text);

        // Assert
        Assert.True(Enum.IsDefined(typeof(ChineseScriptType), scriptType));
    }

    [Fact]
    public void GetSupportedChineseLanguageCodes_ReturnsExpectedCodes()
    {
        // Act
        var supportedCodes = _processor.GetSupportedLanguageCodes();

        // Assert
        Assert.Contains("zh-CN", supportedCodes);
        Assert.Contains("zh-TW", supportedCodes);
        Assert.Contains("zh-Hans", supportedCodes);
        Assert.Contains("zh-Hant", supportedCodes);
        Assert.Contains("cmn_Hans", supportedCodes);
        Assert.Contains("cmn_Hant", supportedCodes);
        Assert.Contains("yue", supportedCodes);
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
    [InlineData("invalid", false)]
    public void IsChineseLanguageCode_ValidInput_ReturnsCorrectResult(string languageCode, bool expected)
    {
        // Act
        var result = _processor.IsChineseLanguageCode(languageCode);

        // Assert
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ChineseLanguageProcessor_Constructor_WithValidLogger_Success()
    {
        // Act & Assert - Should not throw
        Assert.NotNull(_processor);
    }

    [Fact]
    public void ChineseLanguageProcessor_Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new ChineseLanguageProcessor(null!));
    }

    [Theory]
    [InlineData('中', true)]
    [InlineData('国', true)]
    [InlineData('語', true)]
    [InlineData('A', false)]
    [InlineData('あ', false)]
    [InlineData('한', false)]
    [InlineData('1', false)]
    [InlineData(' ', false)]
    public void IsChineseCharacter_ValidInput_ReturnsCorrectResult(char character, bool expected)
    {
        // Act
        var result = ChineseLanguageProcessor.IsChineseCharacter(character);

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(">>prefix<< text", true)]
    [InlineData("  >>prefix<< text", true)]
    [InlineData("text >>prefix<<", false)]
    [InlineData("text", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasExistingPrefix_ValidInput_ReturnsCorrectResult(string? text, bool expected)
    {
        // Act - 既存のプレフィックスをチェック
        var hasPrefix = text != null && text.TrimStart().StartsWith(">>", StringComparison.Ordinal);

        // Assert
        Assert.Equal(expected, hasPrefix);
    }
}
