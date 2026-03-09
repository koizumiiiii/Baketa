using System.Diagnostics.CodeAnalysis;
using Baketa.Application.EventHandlers.Translation;
using Xunit;

namespace Baketa.Application.Tests.EventHandlers.Translation;

[SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
public class OverlayFontFamilyTests
{
    [Theory]
    [InlineData("ja", "Yu Gothic UI")]
    [InlineData("zh-cn", "Microsoft YaHei UI")]
    [InlineData("zh-tw", "Microsoft JhengHei UI")]
    [InlineData("ko", "Malgun Gothic")]
    [InlineData("en", "Segoe UI")]
    [InlineData("fr", "Segoe UI")]
    [InlineData("de", "Segoe UI")]
    [InlineData("it", "Segoe UI")]
    [InlineData("es", "Segoe UI")]
    [InlineData("pt", "Segoe UI")]
    public void GetOverlayFontFamily_WithTargetLanguage_ReturnsCorrectFont(string lang, string expectedFont)
    {
        // Act
        var result = AggregatedChunksReadyEventHandler.GetOverlayFontFamily(lang);

        // Assert
        Assert.Equal(expectedFont, result);
    }

    [Theory]
    [InlineData("zho_hans", "Microsoft YaHei UI")]
    [InlineData("zho_hant", "Microsoft JhengHei UI")]
    [InlineData("kor_hang", "Malgun Gothic")]
    public void GetOverlayFontFamily_WithNllbCode_ReturnsCorrectFont(string nllbCode, string expectedFont)
    {
        // Act
        var result = AggregatedChunksReadyEventHandler.GetOverlayFontFamily(nllbCode);

        // Assert
        Assert.Equal(expectedFont, result);
    }

    [Theory]
    [InlineData("xx")]
    [InlineData("unknown")]
    [InlineData("")]
    public void GetOverlayFontFamily_WithUnknownLanguage_ReturnsFallback(string lang)
    {
        // Act
        var result = AggregatedChunksReadyEventHandler.GetOverlayFontFamily(lang);

        // Assert
        Assert.Equal("Segoe UI", result);
    }
}
