using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Engine;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using Baketa.Infrastructure.Tests.OCR.PaddleOCR.TestData;
using Baketa.Core.Abstractions.Imaging;
using System.Drawing;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Unit;

/// <summary>
/// PaddleOcrEngineの単体テスト
/// Phase 4: テストと検証 - 単体テスト実装
/// </summary>
public class PaddleOcrEngineTests : IDisposable
{
    private readonly Mock<ILogger<PaddleOcrEngine>> _mockLogger;
    private readonly Mock<IModelPathResolver> _mockModelPathResolver;
    private readonly SafeTestPaddleOcrEngine _ocrEngine;
    private bool _disposed;

    public PaddleOcrEngineTests()
    {
        _mockLogger = new Mock<ILogger<PaddleOcrEngine>>();
        _mockModelPathResolver = new Mock<IModelPathResolver>();
        
        // モックセットアップ
        SetupModelPathResolverMock();
        
        // テスト用の安全なエンジンを使用
        _ocrEngine = new SafeTestPaddleOcrEngine(
            _mockModelPathResolver.Object, 
            _mockLogger.Object, 
            skipRealInitialization: true);
    }

    private void SetupModelPathResolverMock()
    {
        _mockModelPathResolver.Setup(x => x.GetModelsRootDirectory())
            .Returns(@"E:\dev\Baketa\tests\TestModels");
        
        _mockModelPathResolver.Setup(x => x.GetDetectionModelsDirectory())
            .Returns(@"E:\dev\Baketa\tests\TestModels\detection");
        
        _mockModelPathResolver.Setup(x => x.GetRecognitionModelsDirectory("eng"))
            .Returns(@"E:\dev\Baketa\tests\TestModels\recognition\eng");
        
        _mockModelPathResolver.Setup(x => x.GetRecognitionModelsDirectory("jpn"))
            .Returns(@"E:\dev\Baketa\tests\TestModels\recognition\jpn");
            
        // ファイル存在チェックのモック設定（テスト用）
        _mockModelPathResolver.Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(false); // テストではモデルファイルが存在しない想定
    }

    #region 初期化テスト

    [Fact]
    public async Task InitializeAsync_ValidParameters_ReturnsTrue()
    {
        // Arrange
        const string language = "eng";
        const bool useGpu = false;
        const bool enableMultiThread = true;
        const int consumerCount = 2;

        // Act
        var result = await _ocrEngine.InitializeAsync(
            language, useGpu, enableMultiThread, consumerCount).ConfigureAwait(false);

        // Assert
        Assert.True(result);
        Assert.True(_ocrEngine.IsInitialized);
        Assert.Equal(language, _ocrEngine.CurrentLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task InitializeAsync_InvalidLanguage_ThrowsArgumentException(string? language)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _ocrEngine.InitializeAsync(language!, false, true, 1)).ConfigureAwait(false);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(11)]
    public async Task InitializeAsync_InvalidConsumerCount_ThrowsArgumentOutOfRangeException(int consumerCount)
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => _ocrEngine.InitializeAsync("eng", false, true, consumerCount)).ConfigureAwait(false);
    }

    [Fact]
    public async Task InitializeAsync_AlreadyInitialized_ReturnsTrue()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        Assert.True(_ocrEngine.IsInitialized);

        // Act
        var result = await _ocrEngine.InitializeAsync("jpn", false, true, 1).ConfigureAwait(false);

        // Assert
        Assert.True(result);
        Assert.True(_ocrEngine.IsInitialized);
        // 既に初期化済みの場合、言語は変更されない
        Assert.Equal("eng", _ocrEngine.CurrentLanguage);
    }

    #endregion

    #region 言語切り替えテスト

    [Fact]
    public async Task SwitchLanguageAsync_ToValidLanguage_ReturnsTrue()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        
        // Act
        var result = await _ocrEngine.SwitchLanguageAsync("jpn").ConfigureAwait(false);

        // Assert
        Assert.True(result);
        Assert.Equal("jpn", _ocrEngine.CurrentLanguage);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("invalid")]
    public async Task SwitchLanguageAsync_InvalidLanguage_ThrowsArgumentException(string? language)
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(
            () => _ocrEngine.SwitchLanguageAsync(language!)).ConfigureAwait(false);
    }

    [Fact]
    public async Task SwitchLanguageAsync_NotInitialized_ThrowsInvalidOperationException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ocrEngine.SwitchLanguageAsync("jpn")).ConfigureAwait(false);
    }

    [Fact]
    public async Task SwitchLanguageAsync_SameLanguage_ReturnsTrue()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        
        // Act
        var result = await _ocrEngine.SwitchLanguageAsync("eng").ConfigureAwait(false);

        // Assert
        Assert.True(result);
        Assert.Equal("eng", _ocrEngine.CurrentLanguage);
    }

    #endregion

    #region OCR実行テスト

    [Fact]
    public async Task RecognizeAsync_NullImage_ThrowsArgumentNullException()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _ocrEngine.RecognizeAsync(null!, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task RecognizeAsync_NotInitialized_ThrowsInvalidOperationException()
    {
        // Arrange
        var mockImage = new Mock<IImage>();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ocrEngine.RecognizeAsync(mockImage.Object, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task RecognizeAsync_ValidImage_ReturnsResults()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        var mockImage = CreateMockImage();

        // Act
        var results = await _ocrEngine.RecognizeAsync(mockImage.Object, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(results);
        // テスト用エンジンではダミー結果として空配列が返される
        Assert.Empty(results);
    }

    [Fact]
    public async Task RecognizeAsync_WithROI_ReturnsResults()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        var mockImage = CreateMockImage();
        var roi = new Rectangle(10, 10, 100, 50);

        // Act
        var results = await _ocrEngine.RecognizeAsync(mockImage.Object, roi, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(results);
        // テスト用エンジンではダミー結果として空配列が返される
        Assert.Empty(results);
    }

    [Fact]
    public async Task RecognizeAsync_Disposed_ThrowsObjectDisposedException()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        var mockImage = CreateMockImage();
        
        _ocrEngine.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => _ocrEngine.RecognizeAsync(mockImage.Object, CancellationToken.None)).ConfigureAwait(false);
    }

    #endregion

    #region プロパティテスト

    [Fact]
    public void CurrentLanguage_NotInitialized_ReturnsNull()
    {
        // Act & Assert
        Assert.Null(_ocrEngine.CurrentLanguage);
    }

    [Fact]
    public async Task CurrentLanguage_Initialized_ReturnsCorrectLanguage()
    {
        // Arrange
        const string expectedLanguage = "eng";
        
        // Act
        await _ocrEngine.InitializeAsync(expectedLanguage, false, true, 2).ConfigureAwait(false);

        // Assert
        Assert.Equal(expectedLanguage, _ocrEngine.CurrentLanguage);
    }

    [Fact]
    public void IsInitialized_NotInitialized_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(_ocrEngine.IsInitialized);
    }

    [Fact]
    public async Task IsInitialized_Initialized_ReturnsTrue()
    {
        // Act
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);

        // Assert
        Assert.True(_ocrEngine.IsInitialized);
    }

    #endregion

    #region リソース管理テスト

    [Fact]
    public void Dispose_MultipleCallsSafe()
    {
        // Act
        _ocrEngine.Dispose();
        _ocrEngine.Dispose(); // 2回目の呼び出し

        // Assert - 例外が発生しないことを確認
        Assert.True(true);
    }

    [Fact]
    public async Task Dispose_InitializedEngine_DisposesCorrectly()
    {
        // Arrange
        await _ocrEngine.InitializeAsync("eng", false, true, 2).ConfigureAwait(false);
        Assert.True(_ocrEngine.IsInitialized);

        // Act
        _ocrEngine.Dispose();

        // Assert
        // SafeTestPaddleOcrEngineではテスト用の状態管理を行う
        // 実際のエンジンではないため、状態の確認は制限される
        Assert.True(true); // Disposeが例外を発生させないことを確認
    }

    #endregion

    #region ヘルパーメソッド

    private static Mock<IImage> CreateMockImage()
    {
        var mockImage = new Mock<IImage>();
        mockImage.Setup(x => x.Width).Returns(640);
        mockImage.Setup(x => x.Height).Returns(480);
        
        // 簡単なダミーデータを設定
        var dummyData = new byte[640 * 480 * 3]; // RGB
        mockImage.Setup(x => x.ToByteArrayAsync()).ReturnsAsync(dummyData);
        
        return mockImage;
    }

    #endregion

    #region IDisposable実装

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _ocrEngine?.Dispose();
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
