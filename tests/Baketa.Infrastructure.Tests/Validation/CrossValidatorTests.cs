using System.Drawing;
using Baketa.Core.Abstractions.OCR.Results;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Abstractions.Validation;
using Baketa.Core.Models.Validation;
using Baketa.Core.Translation.Abstractions;
using Baketa.Infrastructure.Validation;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Validation;

/// <summary>
/// CrossValidatorのユニットテスト
/// Issue #78 Phase 3: 相互検証ロジック検証
/// </summary>
public sealed class CrossValidatorTests
{
    private readonly Mock<ILogger<CrossValidator>> _loggerMock;
    private readonly Mock<IFuzzyTextMatcher> _fuzzyMatcherMock;
    private readonly Mock<IConfidenceRescuer> _rescuerMock;
    private readonly CrossValidator _sut;

    public CrossValidatorTests()
    {
        _loggerMock = new Mock<ILogger<CrossValidator>>();
        _fuzzyMatcherMock = new Mock<IFuzzyTextMatcher>();
        _rescuerMock = new Mock<IConfidenceRescuer>();
        _sut = new CrossValidator(_fuzzyMatcherMock.Object, _rescuerMock.Object, _loggerMock.Object);
    }

    #region Basic Flow Tests

    [Fact]
    public async Task ValidateAsync_EmptyChunks_ReturnsEmptyResult()
    {
        // Arrange
        var chunks = Array.Empty<TextChunk>();
        var cloudResponse = CreateCloudResponse("detected", "translated");

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(0, result.Statistics.TotalLocalChunks);
    }

    [Fact]
    public async Task ValidateAsync_FailedCloudResponse_ReturnsEmptyResult()
    {
        // Arrange
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateFailedCloudResponse();

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Empty(result.ValidatedChunks);
    }

    #endregion

    #region Confidence Filtering Tests

    [Fact]
    public async Task ValidateAsync_LowConfidence_FiltersOut()
    {
        // Arrange: 信頼度 < 0.30
        var chunks = new[] { CreateChunk("hello", 0.25f) };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
    }

    #endregion

    #region Cross-Validation Tests

    [Fact]
    public async Task ValidateAsync_BothDetected_CrossValidated()
    {
        // Arrange
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 1.0f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);
        Assert.Equal(ValidationStatus.CrossValidated, result.ValidatedChunks[0].Status);
        Assert.Equal(1, result.Statistics.CrossValidatedCount);
    }

    [Fact]
    public async Task ValidateAsync_LocalOnlyHighConfidence_MarkedAsLocalOnly()
    {
        // Arrange: 高信頼度だがCloud AIで検出されず
        var chunks = new[] { CreateChunk("hello", 0.80f) };
        var cloudResponse = CreateCloudResponse("world", "世界");

        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.LocalOnlyCount);
    }

    #endregion

    #region Rescue Tests

    [Fact]
    public async Task ValidateAsync_LowConfidenceWithCloudMatch_Rescued()
    {
        // Arrange: 低信頼度だがCloud AIと一致
        var chunk = CreateChunk("hello", 0.50f);
        var chunks = new[] { chunk };
        var cloudResponse = CreateCloudResponse("hello", "こんにちは");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 0.85f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        Assert.Single(result.ValidatedChunks);
        Assert.Equal(ValidationStatus.Rescued, result.ValidatedChunks[0].Status);
        Assert.Equal(1, result.Statistics.RescuedCount);
    }

    [Fact]
    public async Task ValidateAsync_LowConfidenceNoMatch_AttemptsRescue()
    {
        // Arrange: 低信頼度、Cloud AIとマッチなし
        var chunk = CreateChunk("hello", 0.50f);
        var chunks = new[] { chunk };
        var cloudResponse = CreateCloudResponse("world", "世界");

        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);
        _rescuerMock
            .Setup(r => r.TryRescue(It.IsAny<TextChunk>(), It.IsAny<IReadOnlyList<string>>()))
            .Returns(RescueResult.NotRescued("No match found"));

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため結果リストに含まれない
        Assert.Empty(result.ValidatedChunks);
        Assert.Equal(1, result.Statistics.FilteredByMismatchCount);
    }

    #endregion

    #region Multiple Chunks Tests

    [Fact]
    public async Task ValidateAsync_MultipleChunks_ProcessesAll()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("hello", 0.80f),
            CreateChunk("world", 0.80f),
            CreateChunk("test", 0.25f) // 低信頼度 → 除外
        };
        var cloudResponse = CreateCloudResponse("hello\nworld", "こんにちは\n世界");

        SetupFuzzyMatch("hello", "hello", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("hello", "world", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("world", "hello", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("world", "world", isMatch: true, similarity: 1.0f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため、採用チャンクのみ結果に含まれる
        Assert.Equal(2, result.ValidatedChunks.Count); // CrossValidatedの2つのみ
        Assert.Equal(3, result.Statistics.TotalLocalChunks);
        Assert.Equal(2, result.Statistics.CrossValidatedCount);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
    }

    #endregion

    #region Statistics Tests

    [Fact]
    public async Task ValidateAsync_ReturnsCorrectStatistics()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("matched", 0.85f),   // CrossValidated
            CreateChunk("local", 0.80f),     // LocalOnly
            CreateChunk("low", 0.20f)        // Filtered
        };
        var cloudResponse = CreateCloudResponse("matched\ncloud", "マッチ\nクラウド");

        SetupFuzzyMatch("matched", "matched", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("matched", "cloud", isMatch: false, similarity: 0.10f);
        SetupFuzzyMatch("local", "matched", isMatch: false, similarity: 0.20f);
        SetupFuzzyMatch("local", "cloud", isMatch: false, similarity: 0.20f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert: Geminiレビュー対応 - 除外チャンクはnullを返すため、採用チャンクのみ結果に含まれる
        Assert.Single(result.ValidatedChunks); // CrossValidatedの1つのみ
        Assert.Equal(3, result.Statistics.TotalLocalChunks);
        Assert.Equal(2, result.Statistics.TotalCloudDetections);
        Assert.Equal(1, result.Statistics.CrossValidatedCount);
        Assert.Equal(1, result.Statistics.LocalOnlyCount);
        Assert.Equal(1, result.Statistics.FilteredByConfidenceCount);
        Assert.True(result.ProcessingTime.TotalMilliseconds > 0);
    }

    [Fact]
    public async Task ValidateAsync_CalculatesAcceptanceRate()
    {
        // Arrange
        var chunks = new[]
        {
            CreateChunk("a", 0.85f),  // CrossValidated
            CreateChunk("b", 0.50f),  // Rescued
            CreateChunk("c", 0.25f)   // Filtered
        };
        var cloudResponse = CreateCloudResponse("a\nb", "A\nB");

        SetupFuzzyMatch("a", "a", isMatch: true, similarity: 1.0f);
        SetupFuzzyMatch("a", "b", isMatch: false, similarity: 0.0f);
        SetupFuzzyMatch("b", "a", isMatch: false, similarity: 0.0f);
        SetupFuzzyMatch("b", "b", isMatch: true, similarity: 0.85f);

        // Act
        var result = await _sut.ValidateAsync(chunks, cloudResponse);

        // Assert
        // 採用率 = (CrossValidated + Rescued) / Total = 2/3 ≈ 0.67
        Assert.True(result.Statistics.AcceptanceRate > 0.6f);
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task ValidateAsync_Cancellation_ThrowsOperationCanceledException()
    {
        // Arrange
        var chunks = Enumerable.Range(0, 100)
            .Select(i => CreateChunk($"text{i}", 0.80f))
            .ToArray();
        var cloudResponse = CreateCloudResponse("text0", "テキスト0");

        // Setup generic matcher for any string comparison
        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(new FuzzyMatchResult
            {
                IsMatch = false,
                Similarity = 0.10f,
                AppliedThreshold = 0.80f,
                Text1 = "any",
                Text2 = "any",
                EditDistance = 3
            });

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            _sut.ValidateAsync(chunks, cloudResponse, cts.Token));
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

    private static ImageTranslationResponse CreateCloudResponse(
        string detectedText, string translatedText)
    {
        return ImageTranslationResponse.Success(
            requestId: "test-request",
            detectedText: detectedText,
            translatedText: translatedText,
            providerId: "gemini",
            tokenUsage: TokenUsageDetail.Empty,
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    private static ImageTranslationResponse CreateFailedCloudResponse()
    {
        return ImageTranslationResponse.Failure(
            requestId: "test-request",
            error: new TranslationErrorDetail
            {
                Code = TranslationErrorDetail.Codes.ApiError,
                Message = "Test error",
                IsRetryable = false
            },
            processingTime: TimeSpan.FromMilliseconds(100));
    }

    private void SetupFuzzyMatch(string text1, string text2, bool isMatch, float similarity)
    {
        var result = new FuzzyMatchResult
        {
            IsMatch = isMatch,
            Similarity = similarity,
            AppliedThreshold = 0.80f,
            Text1 = text1,
            Text2 = text2,
            EditDistance = (int)((1 - similarity) * Math.Max(text1.Length, text2.Length))
        };

        _fuzzyMatcherMock
            .Setup(m => m.IsMatch(text1, text2))
            .Returns(result);
    }

    #endregion
}
