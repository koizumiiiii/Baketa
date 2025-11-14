using System.Drawing;
using Baketa.Infrastructure.OCR.PaddleOCR.Results;
using Sdcb.PaddleOCR;
using Xunit;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// OCR結果処理の単体テスト
/// Phase 4: テストと検証 - OCR結果処理テスト
/// </summary>
public class OcrResultTests
{
    #region OcrResult単体テスト

    [Fact]
    public void Constructor_ValidParameters_CreatesInstance()
    {
        // Arrange
        const string text = "Hello World";
        const float confidence = 0.95f;
        var boundingBox = new Rectangle(10, 20, 100, 30);

        // Act
        var result = new OcrResult(text, confidence, boundingBox);

        // Assert
        Assert.Equal(text, result.Text);
        Assert.Equal(confidence, result.Confidence);
        Assert.Equal(boundingBox, result.BoundingBox);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_EmptyOrWhitespaceText_CreatesInstanceWithEmptyText(string text)
    {
        // Arrange
        const float confidence = 0.8f;
        var boundingBox = new Rectangle(0, 0, 50, 25);

        // Act
        var result = new OcrResult(text, confidence, boundingBox);

        // Assert
        Assert.Equal(text, result.Text);
        Assert.Equal(confidence, result.Confidence);
        Assert.Equal(boundingBox, result.BoundingBox);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void Constructor_InvalidConfidence_ThrowsArgumentOutOfRangeException(float confidence)
    {
        // Arrange
        const string text = "Test";
        var boundingBox = new Rectangle(0, 0, 50, 25);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OcrResult(text, confidence, boundingBox));
    }

    [Fact]
    public void Constructor_NegativeBoundingBox_ThrowsArgumentException()
    {
        // Arrange
        const string text = "Test";
        const float confidence = 0.8f;
        var invalidBoundingBox = new Rectangle(-10, -20, -30, -40);

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => new OcrResult(text, confidence, invalidBoundingBox));
    }

    [Fact]
    public void ToString_ReturnsExpectedFormat()
    {
        // Arrange
        const string text = "Sample Text";
        const float confidence = 0.87f;
        var boundingBox = new Rectangle(5, 10, 80, 20);
        var result = new OcrResult(text, confidence, boundingBox);

        // Act
        var stringResult = result.ToString();

        // Assert - 期待されるフォーマット: "Text: 'Sample Text', Confidence: 87.0%, BoundingBox: {X=5,Y=10,Width=80,Height=20}"
        var expectedFormat = "Text: 'Sample Text', Confidence: 87.0%, BoundingBox: {X=5,Y=10,Width=80,Height=20}";
        Assert.Equal(expectedFormat, stringResult);

        // 追加の部分文字列チェック
        Assert.Contains(text, stringResult, StringComparison.Ordinal);
        Assert.Contains("87.0%", stringResult, StringComparison.Ordinal);
        Assert.Contains("Text:", stringResult, StringComparison.Ordinal);
        Assert.Contains("Confidence:", stringResult, StringComparison.Ordinal);
        Assert.Contains("BoundingBox:", stringResult, StringComparison.Ordinal);
    }

    #endregion

    #region PaddleOcrResultSet単体テスト

    [Fact]
    public void PaddleOcrResultSet_EmptyResults_ReturnsCorrectProperties()
    {
        // Arrange
        var emptyResults = Array.Empty<OcrResult>();
        var processingTime = TimeSpan.FromMilliseconds(100);
        const string language = "eng";
        var imageSize = new Size(640, 480);

        // Act
        var collection = new PaddleOcrResultSet(emptyResults, processingTime, language, imageSize);

        // Assert
        Assert.Empty(collection.Results);
        Assert.Equal(processingTime, collection.ProcessingTime);
        Assert.Equal(language, collection.Language);
        Assert.Equal(imageSize, collection.ImageSize);
        Assert.Equal(string.Empty, collection.CombinedText);
        Assert.Equal(0, collection.ResultCount);
        Assert.Equal(0.0f, collection.AverageConfidence);
    }

    [Fact]
    public void PaddleOcrResultSet_MultipleResults_CalculatesCorrectStatistics()
    {
        // Arrange
        var results = new[]
        {
            new OcrResult("Hello", 0.9f, new Rectangle(0, 0, 50, 20)),
            new OcrResult("World", 0.8f, new Rectangle(60, 0, 50, 20)),
            new OcrResult("Test", 0.7f, new Rectangle(0, 30, 40, 20))
        };
        var processingTime = TimeSpan.FromMilliseconds(250);
        const string language = "eng";
        var imageSize = new Size(640, 480);

        // Act
        var collection = new PaddleOcrResultSet(results, processingTime, language, imageSize);

        // Assert
        Assert.Equal(3, collection.ResultCount);
        Assert.Equal("Hello World Test", collection.CombinedText);
        Assert.Equal(0.8f, collection.AverageConfidence, 2); // (0.9 + 0.8 + 0.7) / 3 = 0.8
    }

    [Fact]
    public void GetHighConfidenceResults_FiltersByThreshold()
    {
        // Arrange
        var results = new[]
        {
            new OcrResult("High1", 0.95f, new Rectangle(0, 0, 50, 20)),
            new OcrResult("Low", 0.6f, new Rectangle(60, 0, 50, 20)),
            new OcrResult("High2", 0.85f, new Rectangle(0, 30, 40, 20)),
            new OcrResult("VeryLow", 0.3f, new Rectangle(120, 0, 30, 20))
        };
        var collection = new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), "eng", new Size(640, 480));

        // Act
        var highConfidenceResults = collection.GetHighConfidenceResults(0.8f);

        // Assert
        Assert.Equal(2, highConfidenceResults.Count());
        Assert.Contains(highConfidenceResults, r => r.Text == "High1");
        Assert.Contains(highConfidenceResults, r => r.Text == "High2");
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    public void GetHighConfidenceResults_InvalidThreshold_ThrowsArgumentOutOfRangeException(float threshold)
    {
        // Arrange
        var results = new[] { new OcrResult("Test", 0.8f, new Rectangle(0, 0, 50, 20)) };
        var collection = new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), "eng", new Size(640, 480));

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => collection.GetHighConfidenceResults(threshold));
    }

    [Fact]
    public void GetResultsInRegion_FiltersByBoundingBox()
    {
        // Arrange
        var results = new[]
        {
            new OcrResult("Inside1", 0.9f, new Rectangle(20, 20, 30, 20)),
            new OcrResult("Outside", 0.8f, new Rectangle(200, 200, 50, 20)),
            new OcrResult("Inside2", 0.85f, new Rectangle(40, 30, 25, 15)),
            new OcrResult("PartialOverlap", 0.7f, new Rectangle(80, 40, 50, 20))
        };
        var collection = new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), "eng", new Size(640, 480));
        var searchRegion = new Rectangle(10, 10, 60, 50); // covers Inside1 and Inside2

        // Act
        var resultsInRegion = collection.GetResultsInRegion(searchRegion);

        // Assert
        Assert.Equal(2, resultsInRegion.Count());
        Assert.Contains(resultsInRegion, r => r.Text == "Inside1");
        Assert.Contains(resultsInRegion, r => r.Text == "Inside2");
    }

    [Fact]
    public void GetResultsInRegion_EmptyRegion_ReturnsEmpty()
    {
        // Arrange
        var results = new[]
        {
            new OcrResult("Test1", 0.9f, new Rectangle(10, 10, 30, 20)),
            new OcrResult("Test2", 0.8f, new Rectangle(50, 50, 40, 25))
        };
        var collection = new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), "eng", new Size(640, 480));
        var emptyRegion = new Rectangle(100, 100, 0, 0);

        // Act
        var resultsInRegion = collection.GetResultsInRegion(emptyRegion);

        // Assert
        Assert.Empty(resultsInRegion);
    }

    #endregion

    #region FromPaddleResults静的メソッドテスト

    [Fact]
    public void FromPaddleResults_NullResults_ReturnsEmptyArray()
    {
        // Act
        var results = OcrResult.FromPaddleResults(null);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    [Fact]
    public void FromPaddleResults_EmptyResults_ReturnsEmptyArray()
    {
        // Arrange
        var emptyResults = Array.Empty<PaddleOcrResult>();

        // Act
        var results = OcrResult.FromPaddleResults(emptyResults);

        // Assert
        Assert.NotNull(results);
        Assert.Empty(results);
    }

    #endregion

    #region エッジケーステスト

    [Fact]
    public void PaddleOcrResultSet_NullResults_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(
            () => new PaddleOcrResultSet(null!, TimeSpan.FromMilliseconds(100), "eng", new Size(640, 480)));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void PaddleOcrResultSet_InvalidLanguage_ThrowsArgumentException(string? language)
    {
        // Arrange
        var results = Array.Empty<OcrResult>();

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), language!, new Size(640, 480)));
    }

    [Fact]
    public void PaddleOcrResultSet_NegativeProcessingTime_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var results = Array.Empty<OcrResult>();
        var negativeTime = TimeSpan.FromMilliseconds(-100);

        // Act & Assert
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PaddleOcrResultSet(results, negativeTime, "eng", new Size(640, 480)));
    }

    [Fact]
    public void PaddleOcrResultSet_ZeroImageSize_ThrowsArgumentException()
    {
        // Arrange
        var results = Array.Empty<OcrResult>();
        var zeroSize = new Size(0, 0);

        // Act & Assert
        Assert.Throws<ArgumentException>(
            () => new PaddleOcrResultSet(results, TimeSpan.FromMilliseconds(100), "eng", zeroSize));
    }

    #endregion
}
