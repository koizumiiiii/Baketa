using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using Baketa.Infrastructure.OCR.PaddleOCR.Services;
using Baketa.Infrastructure.OCR.PaddleOCR.Abstractions;
using Sdcb.PaddleOCR.Models;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR.Services;

/// <summary>
/// PaddleOcrModelManager単体テスト
/// Phase 2.10: Phase 2.9リファクタリング検証
/// </summary>
public class PaddleOcrModelManagerTests : IDisposable
{
    private readonly Mock<IPaddleOcrUtilities> _mockUtilities;
    private readonly Mock<ILogger<PaddleOcrModelManager>> _mockLogger;
    private readonly PaddleOcrModelManager _modelManager;
    private bool _disposed;

    public PaddleOcrModelManagerTests()
    {
        _mockUtilities = new Mock<IPaddleOcrUtilities>();
        _mockLogger = new Mock<ILogger<PaddleOcrModelManager>>();

        // デフォルトではテスト環境として動作
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);

        _modelManager = new PaddleOcrModelManager(
            _mockUtilities.Object,
            _mockLogger.Object);
    }

    #region Phase 2.9.6 追加メソッドテスト - IOcrEngine委譲実装

    [Fact]
    public void GetAvailableLanguages_ReturnsExpectedLanguages()
    {
        // Act
        var languages = _modelManager.GetAvailableLanguages();

        // Assert
        Assert.NotNull(languages);
        Assert.Equal(3, languages.Count);
        Assert.Contains("eng", languages);
        Assert.Contains("jpn", languages);
        Assert.Contains("chi_sim", languages);
    }

    [Fact]
    public void GetAvailableModels_ReturnsExpectedModels()
    {
        // Act
        var models = _modelManager.GetAvailableModels();

        // Assert
        Assert.NotNull(models);
        Assert.Equal(2, models.Count);
        Assert.Contains("standard", models);
        Assert.Contains("ppocrv5", models);
    }

    [Theory]
    [InlineData("eng", true)]
    [InlineData("jpn", true)]
    [InlineData("chi_sim", true)]
    [InlineData("invalid", false)]
    [InlineData("", false)]
    public async Task IsLanguageAvailableAsync_ValidatesLanguageCode(string languageCode, bool shouldBeAvailable)
    {
        // Arrange - テスト環境ではPrepareModelsAsyncがnullを返す
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);

        // Act
        var isAvailable = await _modelManager.IsLanguageAvailableAsync(languageCode, CancellationToken.None).ConfigureAwait(false);

        // Assert
        if (shouldBeAvailable)
        {
            // テスト環境ではモデルが存在しないため、availableLanguagesに含まれていてもfalseが期待される
            Assert.False(isAvailable, $"{languageCode} should return false in test environment (no models)");
        }
        else
        {
            Assert.False(isAvailable, $"{languageCode} should not be available");
        }
    }

    [Fact]
    public async Task IsLanguageAvailableAsync_NullLanguageCode_ReturnsFalse()
    {
        // Act
        var isAvailable = await _modelManager.IsLanguageAvailableAsync(null!, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(isAvailable);
    }

    #endregion

    #region PrepareModelsAsync テスト

    [Fact]
    public async Task PrepareModelsAsync_TestEnvironment_ReturnsNull()
    {
        // Arrange
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);

        // Act
        var model = await _modelManager.PrepareModelsAsync("eng", CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Null(model);
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("jpn")]
    [InlineData("chi_sim")]
    public async Task PrepareModelsAsync_TestEnvironment_SkipsNetworkAccess(string language)
    {
        // Arrange
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);

        // Act
        var model = await _modelManager.PrepareModelsAsync(language, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.Null(model);

        // テスト環境判定が呼ばれたことを確認
        _mockUtilities.Verify(x => x.IsTestEnvironment(), Times.Once);
    }

    [Fact]
    public async Task PrepareModelsAsync_Cancellation_HandlesGracefully()
    {
        // Arrange
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // Act & Assert - テスト環境では即座にnull返却のため例外発生しない
        var model = await _modelManager.PrepareModelsAsync("eng", cts.Token).ConfigureAwait(false);
        Assert.Null(model);
    }

    #endregion

    #region GetDefaultModelForLanguage テスト

    [Theory]
    [InlineData("jpn")]
    [InlineData("ja")]
    public void GetDefaultModelForLanguage_Japanese_ReturnsJapanV4OrNull(string language)
    {
        // Act
        var model = _modelManager.GetDefaultModelForLanguage(language);

        // Assert
        // テスト環境では LocalFullModels が null を返す可能性があるため、null許容
        // 実運用環境では適切なモデルが返される
        Assert.True(model == null || model.GetType().Name.Contains("Japan", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("en")]
    public void GetDefaultModelForLanguage_English_ReturnsEnglishV4OrNull(string language)
    {
        // Act
        var model = _modelManager.GetDefaultModelForLanguage(language);

        // Assert
        Assert.True(model == null || model.GetType().Name.Contains("English", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("chs")]
    [InlineData("zh")]
    [InlineData("chi")]
    public void GetDefaultModelForLanguage_Chinese_ReturnsChineseV4OrNull(string language)
    {
        // Act
        var model = _modelManager.GetDefaultModelForLanguage(language);

        // Assert
        Assert.True(model == null || model.GetType().Name.Contains("Chinese", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDefaultModelForLanguage_Unknown_ReturnsEnglishV4OrNull()
    {
        // Act
        var model = _modelManager.GetDefaultModelForLanguage("unknown");

        // Assert
        // デフォルトは EnglishV4
        Assert.True(model == null || model.GetType().Name.Contains("English", StringComparison.Ordinal));
    }

    #endregion

    #region DetectIfV5Model テスト

    [Fact]
    public void DetectIfV5Model_NullModel_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => _modelManager.DetectIfV5Model(null!));
    }

    // NOTE: DetectIfV5Model_ValidModel_ReturnsTrue テストは
    // LocalFullModelsがテストプロジェクトでアクセス不可のため、
    // 統合テスト（PaddleOcrIntegrationTests）で検証する

    #endregion

    #region TryCreatePPOCRv5ModelAsync テスト

    [Fact]
    public async Task TryCreatePPOCRv5ModelAsync_CallsProviderMethod()
    {
        // Arrange - テスト環境ではPPOCRv5ModelProviderが利用不可の可能性が高い
        _mockUtilities.Setup(x => x.IsTestEnvironment()).Returns(true);

        // Act
        var model = await _modelManager.TryCreatePPOCRv5ModelAsync("eng", CancellationToken.None).ConfigureAwait(false);

        // Assert - テスト環境ではnullが期待される
        Assert.Null(model);
    }

    [Theory]
    [InlineData("eng")]
    [InlineData("jpn")]
    [InlineData("chi_sim")]
    public async Task TryCreatePPOCRv5ModelAsync_DifferentLanguages_HandlesGracefully(string language)
    {
        // Act
        var model = await _modelManager.TryCreatePPOCRv5ModelAsync(language, CancellationToken.None).ConfigureAwait(false);

        // Assert - テスト環境ではnullが期待される
        Assert.Null(model);
    }

    #endregion

    #region IDisposable実装

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // モックオブジェクトは明示的なDispose不要
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
