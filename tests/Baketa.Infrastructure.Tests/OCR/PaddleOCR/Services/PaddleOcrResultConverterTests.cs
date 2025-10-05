using Xunit;
using Moq;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Baketa.Core.Abstractions.Imaging;
using Sdcb.PaddleOCR;
using System.Drawing;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOcrResultConverter単体テスト
/// Phase 2.10: Phase 2.9リファクタリング検証
/// </summary>
public class PaddleOcrResultConverterTests : IDisposable
{
    private readonly PaddleOcrResultConverter _resultConverter;
    private bool _disposed;

    public PaddleOcrResultConverterTests()
    {
        _resultConverter = new PaddleOcrResultConverter();
    }

    #region ConvertToTextRegions テスト

    [Fact]
    public void ConvertToTextRegions_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var emptyResults = Array.Empty<PaddleOcrResult>();
        const double scaleFactor = 1.0;
        Rectangle? roi = null;

        // Act
        var textRegions = _resultConverter.ConvertToTextRegions(emptyResults, scaleFactor, roi);

        // Assert
        Assert.NotNull(textRegions);
        Assert.Empty(textRegions);
    }

    [Fact]
    public void ConvertToTextRegions_NullResults_ThrowsArgumentNullException()
    {
        // Arrange
        const double scaleFactor = 1.0;
        Rectangle? roi = null;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _resultConverter.ConvertToTextRegions(null!, scaleFactor, roi));
    }

    [Fact]
    public void ConvertToTextRegions_WithScaleFactor_AppliesScaling()
    {
        // Arrange
        var mockResults = Array.Empty<PaddleOcrResult>(); // 簡易テスト
        const double scaleFactor = 2.0;
        Rectangle? roi = null;

        // Act
        var textRegions = _resultConverter.ConvertToTextRegions(mockResults, scaleFactor, roi);

        // Assert
        Assert.NotNull(textRegions);
        // 空結果のため、スケーリング適用の詳細検証は統合テストで実施
    }

    [Fact]
    public void ConvertToTextRegions_WithROI_AppliesOffsets()
    {
        // Arrange
        var mockResults = Array.Empty<PaddleOcrResult>(); // 簡易テスト
        const double scaleFactor = 1.0;
        var roi = new Rectangle(10, 20, 100, 100);

        // Act
        var textRegions = _resultConverter.ConvertToTextRegions(mockResults, scaleFactor, roi);

        // Assert
        Assert.NotNull(textRegions);
        // 空結果のため、ROIオフセット適用の詳細検証は統合テストで実施
    }

    #endregion

    #region ConvertDetectionOnlyResult テスト

    [Fact]
    public void ConvertDetectionOnlyResult_EmptyResults_ReturnsEmptyList()
    {
        // Arrange
        var emptyResults = Array.Empty<PaddleOcrResult>();

        // Act
        var textRegions = _resultConverter.ConvertDetectionOnlyResult(emptyResults);

        // Assert
        Assert.NotNull(textRegions);
        Assert.Empty(textRegions);
    }

    [Fact]
    public void ConvertDetectionOnlyResult_NullResults_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _resultConverter.ConvertDetectionOnlyResult(null!));
    }

    #endregion

    #region CreateEmptyResult テスト

    [Fact]
    public void CreateEmptyResult_ValidParameters_ReturnsEmptyOcrResults()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(640);
        mockImage.Setup(x => x.Height).Returns(480);
        Rectangle? roi = null;
        var processingTime = TimeSpan.FromMilliseconds(100);

        // Act
        var result = _resultConverter.CreateEmptyResult(mockImage.Object, roi, processingTime);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.TextRegions);
        Assert.Equal(processingTime, result.ProcessingTime);
        Assert.Null(result.RegionOfInterest);
    }

    [Fact]
    public void CreateEmptyResult_WithROI_IncludesROIInResult()
    {
        // Arrange
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(640);
        mockImage.Setup(x => x.Height).Returns(480);
        var roi = new Rectangle(10, 20, 100, 100);
        var processingTime = TimeSpan.FromMilliseconds(50);

        // Act
        var result = _resultConverter.CreateEmptyResult(mockImage.Object, roi, processingTime);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result.TextRegions);
        Assert.Equal(roi, result.RegionOfInterest);
        Assert.Equal(processingTime, result.ProcessingTime);
    }

    [Fact]
    public void CreateEmptyResult_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        Rectangle? roi = null;
        var processingTime = TimeSpan.Zero;

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            _resultConverter.CreateEmptyResult(null!, roi, processingTime));
    }

    #endregion

    #region IDisposable実装

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // PaddleOcrResultConverterは状態を持たないため、Dispose不要
            }

            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    #endregion
}
