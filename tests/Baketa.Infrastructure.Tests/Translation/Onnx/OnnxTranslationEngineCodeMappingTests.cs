using System.Diagnostics.CodeAnalysis;
using Baketa.Infrastructure.Translation.Onnx;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Onnx;

[SuppressMessage("Style", "CA1707:識別子にアンダースコアを含めることはできません", Justification = "xUnit規約に準拠するテストメソッド名")]
public class OnnxTranslationEngineCodeMappingTests
{
    [Theory]
    [InlineData("en", "eng_Latn")]
    [InlineData("ja", "jpn_Jpan")]
    [InlineData("zh-CN", "zho_Hans")]
    [InlineData("zh-TW", "zho_Hant")]
    [InlineData("ko", "kor_Hang")]
    [InlineData("fr", "fra_Latn")]
    [InlineData("de", "deu_Latn")]
    [InlineData("it", "ita_Latn")]
    [InlineData("es", "spa_Latn")]
    [InlineData("pt", "por_Latn")]
    [InlineData("ru", "rus_Cyrl")]
    [InlineData("ar", "arb_Arab")]
    [InlineData("nl", "nld_Latn")]
    [InlineData("pl", "pol_Latn")]
    [InlineData("tr", "tur_Latn")]
    [InlineData("vi", "vie_Latn")]
    [InlineData("th", "tha_Thai")]
    [InlineData("id", "ind_Latn")]
    [InlineData("hi", "hin_Deva")]
    public void ToNllbCode_WithBaketaCode_ReturnsCorrectNllbCode(string baketaCode, string expectedNllb)
    {
        // Act
        var result = OnnxTranslationEngine.ToNllbCode(baketaCode);

        // Assert
        Assert.Equal(expectedNllb, result);
    }

    [Theory]
    [InlineData("eng_Latn")]
    [InlineData("jpn_Jpan")]
    [InlineData("zho_Hans")]
    public void ToNllbCode_WithNllbFormat_ReturnsSameCode(string nllbCode)
    {
        // Act
        var result = OnnxTranslationEngine.ToNllbCode(nllbCode);

        // Assert
        Assert.Equal(nllbCode, result);
    }

    [Fact]
    public void ToNllbCode_WithUnsupportedCode_ReturnsNull()
    {
        // Act
        var result = OnnxTranslationEngine.ToNllbCode("xx");

        // Assert
        Assert.Null(result);
    }

    [Theory]
    [InlineData("ZH-CN", "zho_Hans")]
    [InlineData("Zh-Tw", "zho_Hant")]
    [InlineData("EN", "eng_Latn")]
    [InlineData("JA", "jpn_Jpan")]
    public void ToNllbCode_IsCaseInsensitive(string baketaCode, string expectedNllb)
    {
        // Act
        var result = OnnxTranslationEngine.ToNllbCode(baketaCode);

        // Assert
        Assert.Equal(expectedNllb, result);
    }
}
