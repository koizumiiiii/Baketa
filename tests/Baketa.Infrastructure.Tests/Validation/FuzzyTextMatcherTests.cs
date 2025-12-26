using Baketa.Core.Abstractions.Validation;
using Baketa.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Validation;

/// <summary>
/// FuzzyTextMatcherのユニットテスト
/// Issue #78 Phase 3: ファジーマッチングロジック検証
/// </summary>
public sealed class FuzzyTextMatcherTests
{
    private readonly Mock<ILogger<FuzzyTextMatcher>> _loggerMock;
    private readonly FuzzyTextMatcher _sut;

    public FuzzyTextMatcherTests()
    {
        _loggerMock = new Mock<ILogger<FuzzyTextMatcher>>();
        _sut = new FuzzyTextMatcher(_loggerMock.Object);
    }

    #region CalculateSimilarity Tests

    [Fact]
    public void CalculateSimilarity_IdenticalStrings_ReturnsOne()
    {
        // Arrange
        var text = "Hello World";

        // Act
        var similarity = _sut.CalculateSimilarity(text, text);

        // Assert
        Assert.Equal(1.0f, similarity);
    }

    [Fact]
    public void CalculateSimilarity_CompletelyDifferent_ReturnsLowSimilarity()
    {
        // Arrange
        var text1 = "abc";
        var text2 = "xyz";

        // Act
        var similarity = _sut.CalculateSimilarity(text1, text2);

        // Assert
        Assert.True(similarity < 0.5f);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData(null, null)]
    public void CalculateSimilarity_BothEmpty_ReturnsOne(string? text1, string? text2)
    {
        // Act
        var similarity = _sut.CalculateSimilarity(text1!, text2!);

        // Assert
        Assert.Equal(1.0f, similarity);
    }

    [Theory]
    [InlineData("hello", null)]
    [InlineData(null, "hello")]
    [InlineData("hello", "")]
    [InlineData("", "hello")]
    public void CalculateSimilarity_OneEmpty_ReturnsZero(string? text1, string? text2)
    {
        // Act
        var similarity = _sut.CalculateSimilarity(text1!, text2!);

        // Assert
        Assert.Equal(0.0f, similarity);
    }

    [Theory]
    [InlineData("hello", "helo", 0.8f)] // 1文字欠損
    [InlineData("test", "tset", 0.5f)]  // 2文字入れ替え
    [InlineData("abcde", "abcdf", 0.8f)] // 1文字置換
    public void CalculateSimilarity_SimilarStrings_ReturnsExpectedRange(
        string text1, string text2, float minExpected)
    {
        // Act
        var similarity = _sut.CalculateSimilarity(text1, text2);

        // Assert
        Assert.True(similarity >= minExpected,
            $"Expected similarity >= {minExpected}, but got {similarity}");
    }

    [Fact]
    public void CalculateSimilarity_JapaneseText_CalculatesCorrectly()
    {
        // Arrange
        var text1 = "こんにちは";
        var text2 = "こんにちわ"; // 最後の文字が違う

        // Act
        var similarity = _sut.CalculateSimilarity(text1, text2);

        // Assert
        // 5文字中1文字違い → 類似度 80%
        Assert.Equal(0.8f, similarity, 2);
    }

    #endregion

    #region IsMatch Tests (Default Threshold)

    [Theory]
    [InlineData("AB", "AB", true)]       // 短いテキスト、完全一致 (100%) >= 90%
    [InlineData("ABC", "AB", false)]     // 短いテキスト、1文字欠損 (66%) < 90%
    [InlineData("ABCDE", "ABCDF", false)] // 短いテキスト、1文字置換 (80%) < 90%
    public void IsMatch_ShortText_Uses90PercentThreshold(
        string text1, string text2, bool expectedMatch)
    {
        // Act
        var result = _sut.IsMatch(text1, text2);

        // Assert
        Assert.Equal(expectedMatch, result.IsMatch);
    }

    [Theory]
    [InlineData("ABCDEFGH", "ABCDEFGH", true)]  // 完全一致
    [InlineData("ABCDEFGH", "ABCDEFGX", true)]  // 1文字置換 (87.5%) >= 85%
    public void IsMatch_MediumText_Uses85PercentThreshold(
        string text1, string text2, bool expectedMatch)
    {
        // Act
        var result = _sut.IsMatch(text1, text2);

        // Assert
        Assert.Equal(expectedMatch, result.IsMatch);
    }

    [Theory]
    [InlineData("ABCDEFGHIJ", "ABCDEFGHIJ", true)] // 完全一致
    [InlineData("ABCDEFGHIJ", "ABCDEFGHXX", true)] // 2文字置換 (80%) >= 80%
    public void IsMatch_LongText_Uses80PercentThreshold(
        string text1, string text2, bool expectedMatch)
    {
        // Act
        var result = _sut.IsMatch(text1, text2);

        // Assert
        Assert.Equal(expectedMatch, result.IsMatch);
    }

    #endregion

    #region IsMatch Tests (Custom Threshold)

    [Theory]
    [InlineData("hello", "helo", 0.7f, true)]   // 80% >= 70%
    [InlineData("hello", "helo", 0.9f, false)]  // 80% < 90%
    [InlineData("hello", "hello", 0.99f, true)] // 100% >= 99%
    public void IsMatch_CustomThreshold_RespectsThreshold(
        string text1, string text2, float threshold, bool expectedMatch)
    {
        // Act
        var result = _sut.IsMatch(text1, text2, threshold);

        // Assert
        Assert.Equal(expectedMatch, result.IsMatch);
    }

    [Fact]
    public void IsMatch_ReturnsResultWithCorrectProperties()
    {
        // Arrange
        var text1 = "hello";
        var text2 = "helo";

        // Act
        var result = _sut.IsMatch(text1, text2, 0.7f);

        // Assert
        Assert.True(result.IsMatch);
        Assert.Equal(0.8f, result.Similarity, 2);
        Assert.Equal(0.7f, result.AppliedThreshold, 2);
        Assert.Equal(1, result.EditDistance); // 1文字欠損
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CalculateSimilarity_VeryDifferentLengths_EarlyReturns()
    {
        // Arrange: 長さの差が80%以上
        var text1 = "A";
        var text2 = "ABCDEFGHIJ"; // 10文字

        // Act
        var similarity = _sut.CalculateSimilarity(text1, text2);

        // Assert: 早期リターンで最大長が返る → 類似度 = 1 - 9/10 = 0.1
        Assert.True(similarity <= 0.2f);
    }

    [Fact]
    public void IsMatch_PreservesOriginalTexts()
    {
        // Arrange
        var text1 = "original";
        var text2 = "orignal";

        // Act
        var result = _sut.IsMatch(text1, text2);

        // Assert
        Assert.Equal(text1, result.Text1);
        Assert.Equal(text2, result.Text2);
    }

    #endregion
}
