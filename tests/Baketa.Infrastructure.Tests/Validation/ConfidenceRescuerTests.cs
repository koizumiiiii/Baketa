using System.Drawing;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Validation;

/// <summary>
/// ConfidenceRescuerのユニットテスト
/// Issue #78 Phase 3: 低信頼度テキスト救済ロジック検証
/// </summary>
public sealed class ConfidenceRescuerTests
{
    private readonly Mock<ILogger<ConfidenceRescuer>> _loggerMock;
    private readonly Mock<IFuzzyTextMatcher> _fuzzyMatcherMock;
    private readonly ConfidenceRescuer _sut;

    public ConfidenceRescuerTests()
    {
        _loggerMock = new Mock<ILogger<ConfidenceRescuer>>();
        _fuzzyMatcherMock = new Mock<IFuzzyTextMatcher>();
        _sut = new ConfidenceRescuer(_fuzzyMatcherMock.Object, _loggerMock.Object);
    }

    #region Eligibility Tests

    [Fact]
    public void TryRescue_ConfidenceTooLow_ReturnsNotRescued()
    {
        // Arrange: 信頼度 < 0.30
        var chunk = CreateChunk("hello", confidence: 0.25f);

        // Act
        var result = _sut.TryRescue(chunk, "hello");

        // Assert
        Assert.False(result.IsRescued);
        Assert.Contains("最低閾値", result.Reason);
    }

    [Fact]
    public void TryRescue_ConfidenceTooHigh_ReturnsNotRescued()
    {
        // Arrange: 信頼度 >= 0.70（通常検証対象）
        var chunk = CreateChunk("hello", confidence: 0.75f);

        // Act
        var result = _sut.TryRescue(chunk, "hello");

        // Assert
        Assert.False(result.IsRescued);
        Assert.Contains("通常検証対象", result.Reason);
    }

    [Fact]
    public void TryRescue_TextTooShort_ReturnsNotRescued()
    {
        // Arrange: テキスト長 < 3
        var chunk = CreateChunk("AB", confidence: 0.50f);

        // Act
        var result = _sut.TryRescue(chunk, "AB");

        // Assert
        Assert.False(result.IsRescued);
        Assert.Contains("テキスト長", result.Reason);
    }

    #endregion

    #region Rescue Success Tests

    [Fact]
    public void TryRescue_HighSimilarity_ReturnsRescued()
    {
        // Arrange
        var chunk = CreateChunk("hello", confidence: 0.50f);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "hello"))
            .Returns(1.0f);

        // Act
        var result = _sut.TryRescue(chunk, "hello");

        // Assert
        Assert.True(result.IsRescued);
        Assert.Equal(1.0f, result.MatchSimilarity);
        Assert.Equal("hello", result.MatchedCloudText);
    }

    [Fact]
    public void TryRescue_SimilarityAtThreshold_ReturnsRescued()
    {
        // Arrange: 類似度 = 80%（閾値ちょうど）
        var chunk = CreateChunk("hello", confidence: 0.50f);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "helo"))
            .Returns(0.80f);

        // Act
        var result = _sut.TryRescue(chunk, "helo");

        // Assert
        Assert.True(result.IsRescued);
        Assert.Equal(0.80f, result.MatchSimilarity);
    }

    [Fact]
    public void TryRescue_MultipleCloudTexts_FindsBestMatch()
    {
        // Arrange
        var chunk = CreateChunk("hello", confidence: 0.50f);
        var cloudTexts = new[] { "hi", "helo", "hello" };

        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "hi"))
            .Returns(0.4f);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "helo"))
            .Returns(0.8f);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "hello"))
            .Returns(1.0f);

        // Act
        var result = _sut.TryRescue(chunk, cloudTexts);

        // Assert
        Assert.True(result.IsRescued);
        Assert.Equal(1.0f, result.MatchSimilarity);
        Assert.Equal("hello", result.MatchedCloudText);
    }

    #endregion

    #region Rescue Failure Tests

    [Fact]
    public void TryRescue_LowSimilarity_ReturnsNotRescued()
    {
        // Arrange: 類似度 < 80%
        var chunk = CreateChunk("hello", confidence: 0.50f);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity("hello", "world"))
            .Returns(0.20f);

        // Act
        var result = _sut.TryRescue(chunk, "world");

        // Assert
        Assert.False(result.IsRescued);
        Assert.Contains("閾値", result.Reason);
    }

    [Fact]
    public void TryRescue_EmptyCloudTexts_ReturnsNotRescued()
    {
        // Arrange
        var chunk = CreateChunk("hello", confidence: 0.50f);
        var cloudTexts = Array.Empty<string>();

        // Act
        var result = _sut.TryRescue(chunk, cloudTexts);

        // Assert
        Assert.False(result.IsRescued);
        Assert.Equal(0f, result.MatchSimilarity);
    }

    [Fact]
    public void TryRescue_AllCloudTextsEmpty_ReturnsNotRescued()
    {
        // Arrange
        var chunk = CreateChunk("hello", confidence: 0.50f);
        var cloudTexts = new[] { "", null!, "" };

        // Act
        var result = _sut.TryRescue(chunk, cloudTexts);

        // Assert
        Assert.False(result.IsRescued);
    }

    #endregion

    #region Boundary Tests

    [Theory]
    [InlineData(0.30f, true)]  // 最低閾値ちょうど → 救済対象
    [InlineData(0.29f, false)] // 最低閾値未満 → 対象外
    [InlineData(0.69f, true)]  // 上限未満 → 救済対象
    [InlineData(0.70f, false)] // 上限ちょうど → 対象外
    public void TryRescue_ConfidenceBoundary_HandlesCorrectly(
        float confidence, bool isEligible)
    {
        // Arrange
        var chunk = CreateChunk("hello", confidence);
        _fuzzyMatcherMock
            .Setup(m => m.CalculateSimilarity(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(1.0f); // 完全一致

        // Act
        var result = _sut.TryRescue(chunk, "hello");

        // Assert
        Assert.Equal(isEligible, result.IsRescued);
    }

    #endregion

    #region Helper Methods

    private static TextChunk CreateChunk(string text, float confidence)
    {
        var textResult = new PositionedTextResult
        {
            Text = text,
            BoundingBox = new Rectangle(0, 0, 100, 20),
            Confidence = confidence,
            ChunkId = 1
        };

        return new TextChunk
        {
            ChunkId = 1,
            TextResults = new[] { textResult },
            CombinedBounds = new Rectangle(0, 0, 100, 20),
            CombinedText = text,
            SourceWindowHandle = IntPtr.Zero
        };
    }

    #endregion
}
