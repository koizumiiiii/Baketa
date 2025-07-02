using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Moq;
using Moq.Protected;
using System.Net;
using Baketa.Core.Abstractions.OCR;
using Baketa.Infrastructure.OCR.PaddleOCR.Models;
using System.Drawing;

namespace Baketa.Infrastructure.Tests.OCR.PaddleOCR;

/// <summary>
/// OcrModelManagerの単体テスト
/// </summary>
public class OcrModelManagerTests : IDisposable
{
    private readonly Mock<IModelPathResolver> _mockModelPathResolver;
    private readonly Mock<HttpMessageHandler> _mockHttpMessageHandler;
    private readonly HttpClient _httpClient;
    private readonly OcrModelManager _modelManager;
    private readonly string _tempDirectory;

    public OcrModelManagerTests()
    {
        _mockModelPathResolver = new Mock<IModelPathResolver>();
        _mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        
        // Root cause solution: Proper HTTP mock configuration to prevent 404 errors
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && req.RequestUri.ToString().Contains("nonexistent-model")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                // Return 404 only for nonexistent-model requests
                var response = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.NotFound,
                    Content = new StringContent("Model not found")
                };
                return response;
            });
            
        // Root cause solution: Provide success responses for valid model requests
        _mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req => req.RequestUri != null && !req.RequestUri.ToString().Contains("nonexistent-model")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                // Return successful response for valid model requests
                var response = new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new ByteArrayContent(new byte[] { 0x50, 0x4B, 0x03, 0x04 }) // Mock ZIP header
                };
                return response;
            });
        
        _httpClient = new HttpClient(_mockHttpMessageHandler.Object);
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaOcrModelsTest", Guid.NewGuid().ToString());
        
        // モデルパス解決のモック設定
        _mockModelPathResolver.Setup(x => x.GetModelsRootDirectory())
            .Returns("test/models");
        _mockModelPathResolver.Setup(x => x.GetDetectionModelPath(It.IsAny<string>()))
            .Returns("test/detection/model.onnx");
        _mockModelPathResolver.Setup(x => x.GetRecognitionModelPath(It.IsAny<string>(), It.IsAny<string>()))
            .Returns("test/recognition/model.onnx");
        _mockModelPathResolver.Setup(x => x.GetClassificationModelPath(It.IsAny<string>()))
            .Returns("test/classification/model.onnx");
        _mockModelPathResolver.Setup(x => x.FileExists(It.IsAny<string>()))
            .Returns(false); // テスト環境ではファイルは存在しない
        _mockModelPathResolver.Setup(x => x.EnsureDirectoryExists(It.IsAny<string>()));
        
        _modelManager = new OcrModelManager(
            _mockModelPathResolver.Object, 
            _httpClient, 
            _tempDirectory, 
            NullLogger<OcrModelManager>.Instance);
    }

    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var manager = new OcrModelManager(
            _mockModelPathResolver.Object, 
            _httpClient, 
            _tempDirectory, 
            NullLogger<OcrModelManager>.Instance);

        // Assert
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_WithNullModelPathResolver_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OcrModelManager(null!, _httpClient, _tempDirectory, NullLogger<OcrModelManager>.Instance));
    }

    [Fact]
    public void Constructor_WithNullHttpClient_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OcrModelManager(_mockModelPathResolver.Object, null!, _tempDirectory, NullLogger<OcrModelManager>.Instance));
    }

    [Fact]
    public async Task GetAvailableModelsAsync_ShouldReturnModelList()
    {
        // Act
        var models = await _modelManager.GetAvailableModelsAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(models);
        Assert.True(models.Count > 0);
        
        // 固定モデルリストの確認
        Assert.Contains(models, m => m.Id == "det_db_standard");
        Assert.Contains(models, m => m.Id == "rec_english_standard");
        Assert.Contains(models, m => m.Id == "rec_japan_standard");
    }

    [Fact]
    public async Task GetModelsForLanguageAsync_WithValidLanguage_ShouldReturnFilteredModels()
    {
        // Act
        var models = await _modelManager.GetModelsForLanguageAsync("jpn", CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(models);
        Assert.True(models.Count > 0);
        
        // 日本語または言語非依存のモデルのみが含まれることを確認
        Assert.All(models, model => 
        {
            Assert.True(string.Equals(model.LanguageCode, "jpn", StringComparison.Ordinal) || model.LanguageCode == null);
        });
    }

    [Fact]
    public async Task GetModelsForLanguageAsync_WithEmptyLanguage_ShouldThrowArgumentException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _modelManager.GetModelsForLanguageAsync("", CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task IsModelDownloadedAsync_WithNullModel_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _modelManager.IsModelDownloadedAsync(null!, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task IsModelDownloadedAsync_WithNonExistentModel_ShouldReturnFalse()
    {
        // Arrange
        var model = new OcrModelInfo(
            "test_model",
            "Test Model",
            OcrModelType.Detection,
            "test_model.onnx",
            new Uri("https://example.com/test_model.tar"),
            1024,
            "abcdef123456",
            null,
            "Test model for unit tests");

        // Act
        var isDownloaded = await _modelManager.IsModelDownloadedAsync(model, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(isDownloaded);
    }

    [Fact]
    public async Task DownloadModelAsync_WithNullModel_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _modelManager.DownloadModelAsync(null!, null, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task DownloadModelAsync_WithInvalidModel_ShouldThrowModelManagementException()
    {
        // Arrange - Root cause solution: Use nonexistent-model in URL to trigger mocked 404 response
        var invalidModel = new OcrModelInfo(
            "nonexistent-model", // モック設定に対応するID
            "Invalid Model",
            OcrModelType.Detection,
            "invalid.onnx",
            new Uri("https://localhost/nonexistent-model.tar"), // モック対応URL
            0, // 無効なファイルサイズ
            "",
            null,
            "Invalid model for testing");

        // Act & Assert
        await Assert.ThrowsAsync<ModelManagementException>(() =>
            _modelManager.DownloadModelAsync(invalidModel, null, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task DownloadModelsAsync_WithNullModelList_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _modelManager.DownloadModelsAsync(null!, null, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task DownloadModelsAsync_WithEmptyModelList_ShouldReturnTrue()
    {
        // Arrange
        var emptyList = new List<OcrModelInfo>();

        // Act
        var result = await _modelManager.DownloadModelsAsync(emptyList, null, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task GetDownloadedModelsAsync_ShouldReturnEmptyList()
    {
        // Act
        var downloadedModels = await _modelManager.GetDownloadedModelsAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(downloadedModels);
        Assert.Empty(downloadedModels); // テスト環境では何もダウンロードされていない
    }

    [Fact]
    public async Task DeleteModelAsync_WithNullModel_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _modelManager.DeleteModelAsync(null!, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task DeleteModelAsync_WithNonExistentModel_ShouldReturnTrue()
    {
        // Arrange
        var model = new OcrModelInfo(
            "test_model",
            "Test Model",
            OcrModelType.Detection,
            "test_model.onnx",
            new Uri("https://example.com/test_model.tar"),
            1024,
            "abcdef123456",
            null,
            "Test model for unit tests");

        // Act
        var result = await _modelManager.DeleteModelAsync(model, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(result); // 存在しないファイルの削除は成功とみなす
    }

    [Fact]
    public async Task IsLanguageCompleteAsync_WithEmptyLanguage_ShouldReturnFalse()
    {
        // Act
        var result = await _modelManager.IsLanguageCompleteAsync("", CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsLanguageCompleteAsync_WithValidLanguage_ShouldReturnFalse()
    {
        // Act
        var result = await _modelManager.IsLanguageCompleteAsync("jpn", CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(result); // テスト環境ではモデルがダウンロードされていない
    }

    [Fact]
    public async Task ValidateModelAsync_WithNullModel_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _modelManager.ValidateModelAsync(null!, CancellationToken.None)).ConfigureAwait(false);
    }

    [Fact]
    public async Task ValidateModelAsync_WithNonExistentModel_ShouldReturnInvalidResult()
    {
        // Arrange
        var model = new OcrModelInfo(
            "test_model",
            "Test Model",
            OcrModelType.Detection,
            "test_model.onnx",
            new Uri("https://example.com/test_model.tar"),
            1024,
            "abcdef123456",
            null,
            "Test model for unit tests");

        // Act
        var result = await _modelManager.ValidateModelAsync(model, CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.False(result.IsValid);
        Assert.False(result.FileExists);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public async Task RefreshModelMetadataAsync_ShouldReturnTrue()
    {
        // Act
        var result = await _modelManager.RefreshModelMetadataAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(result); // 現在の実装では常に成功
    }

    [Fact]
    public async Task CleanupUnusedModelsAsync_ShouldReturnZero()
    {
        // Act
        var result = await _modelManager.CleanupUnusedModelsAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.True(result >= 0); // クリーンアップされたファイル数は0以上
    }

    [Fact]
    public async Task GetStatisticsAsync_ShouldReturnValidStats()
    {
        // Act
        var stats = await _modelManager.GetStatisticsAsync(CancellationToken.None).ConfigureAwait(false);

        // Assert
        Assert.NotNull(stats);
        Assert.True(stats.TotalModels > 0);
        Assert.Equal(0, stats.DownloadedModels); // テスト環境では何もダウンロードされていない
        Assert.True(stats.TotalDownloadSize > 0);
        Assert.Equal(0, stats.UsedDiskSpace);
        Assert.True(stats.AvailableLanguages > 0);
        Assert.Equal(0, stats.CompletedLanguages);
    }

    private bool _disposed;

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _httpClient?.Dispose();
                
                // テスト用の一時ディレクトリをクリーンアップ
                if (Directory.Exists(_tempDirectory))
                {
                    try
                    {
                        Directory.Delete(_tempDirectory, true);
                    }
                    catch (IOException)
                    {
                        // テスト環境でのクリーンアップ失敗は無視
                    }
                }
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// OcrModelInfoの単体テスト
/// </summary>
public class OcrModelInfoTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange & Act
        var model = new OcrModelInfo(
            "test_id",
            "Test Model",
            OcrModelType.Detection,
            "test.onnx",
            new Uri("https://example.com/test.tar"),
            1024,
            "abcdef123456",
            "jpn",
            "Test description",
            "1.0");

        // Assert
        Assert.Equal("test_id", model.Id);
        Assert.Equal("Test Model", model.Name);
        Assert.Equal(OcrModelType.Detection, model.Type);
        Assert.Equal("test.onnx", model.FileName);
        Assert.Equal("https://example.com/test.tar", model.DownloadUrl.ToString());
        Assert.Equal(1024, model.FileSize);
        Assert.Equal("abcdef123456", model.Hash);
        Assert.Equal("jpn", model.LanguageCode);
        Assert.Equal("Test description", model.Description);
        Assert.Equal("1.0", model.Version);
    }

    [Fact]
    public void Constructor_WithNullId_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new OcrModelInfo(
                null!, "Test Model", OcrModelType.Detection, "test.onnx",
                new Uri("https://example.com/test.tar"), 1024, "abcdef123456"));
    }

    [Theory]
    [InlineData("valid_id", "Valid Model", "test.onnx", "https://example.com/test.tar", 1024, "abcdef123456", true)]
    [InlineData("", "Valid Model", "test.onnx", "https://example.com/test.tar", 1024, "abcdef123456", false)] // 空のID
    [InlineData("valid_id", "", "test.onnx", "https://example.com/test.tar", 1024, "abcdef123456", false)] // 空の名前
    [InlineData("valid_id", "Valid Model", "", "https://example.com/test.tar", 1024, "abcdef123456", false)] // 空のファイル名
    [InlineData("valid_id", "Valid Model", "test.onnx", "https://example.com/test.tar", 0, "abcdef123456", false)] // 無効なサイズ
    [InlineData("valid_id", "Valid Model", "test.onnx", "https://example.com/test.tar", 1024, "", false)] // 空のハッシュ
    [InlineData("valid_id", "Valid Model", "test.onnx", "http://example.com/test.tar", 1024, "abcdef123456", true)] // HTTPでも有効なURL
    [InlineData("valid_id", "Valid Model", "test.onnx", "ftp://example.com/test.tar", 1024, "abcdef123456", false)] // FTPは無効なURLスキーム
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1054:URI-like parameters should not be strings", Justification = "Test method needs to validate both valid and invalid URL strings")]
    public void IsValid_ShouldReturnCorrectResult(
        string id, string name, string fileName, string addressText, 
        long fileSize, string hash, bool expected)
    {
        try
        {
            // Arrange
            var downloadUrl = new Uri(addressText);
            var model = new OcrModelInfo(id, name, OcrModelType.Detection, fileName, downloadUrl, fileSize, hash);

            // Act
            var isValid = model.IsValid();

            // Assert
            Assert.Equal(expected, isValid);
        }
        catch (UriFormatException)
        {
            // 無効なURL形式の場合は無効とみなす
            Assert.False(expected, "Invalid URL format should result in false validation");
        }
    }

    [Fact]
    public void GetFullPath_WithValidBaseDirectory_ShouldReturnCombinedPath()
    {
        // Arrange
        var model = new OcrModelInfo(
            "test_id", "Test Model", OcrModelType.Detection, "test.onnx",
            new Uri("https://example.com/test.tar"), 1024, "abcdef123456");
        var baseDirectory = @"C:\Models";

        // Act
        var fullPath = model.GetFullPath(baseDirectory);

        // Assert
        var expected = Path.Combine(baseDirectory, "test.onnx");
        Assert.Equal(expected, fullPath);
    }

    [Fact]
    public void GetFullPath_WithEmptyBaseDirectory_ShouldThrowArgumentException()
    {
        // Arrange
        var model = new OcrModelInfo(
            "test_id", "Test Model", OcrModelType.Detection, "test.onnx",
            new Uri("https://example.com/test.tar"), 1024, "abcdef123456");

        // Act & Assert
        Assert.Throws<ArgumentException>(() => model.GetFullPath(""));
    }
}

/// <summary>
/// ModelDownloadProgressの単体テスト
/// </summary>
public class ModelDownloadProgressTests
{
    [Fact]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Arrange
        var modelInfo = new OcrModelInfo(
            "test_id", "Test Model", OcrModelType.Detection, "test.onnx",
            new Uri("https://example.com/test.tar"), 1024, "abcdef123456");

        // Act
        var progress = new ModelDownloadProgress(
            modelInfo, ModelDownloadStatus.Downloading, 0.5, "Downloading...",
            null, 512, 1024, 1000.0, TimeSpan.FromSeconds(1), 0);

        // Assert
        Assert.Equal(modelInfo, progress.ModelInfo);
        Assert.Equal(ModelDownloadStatus.Downloading, progress.Status);
        Assert.Equal(0.5, progress.Progress);
        Assert.Equal("Downloading...", progress.StatusMessage);
        Assert.Null(progress.ErrorMessage);
        Assert.Equal(512, progress.DownloadedBytes);
        Assert.Equal(1024, progress.TotalBytes);
        Assert.Equal(1000.0, progress.DownloadSpeedBps);
        Assert.Equal(TimeSpan.FromSeconds(1), progress.EstimatedTimeRemaining);
        Assert.Equal(0, progress.RetryCount);
    }

    [Fact]
    public void GetFormattedSpeed_ShouldReturnFormattedString()
    {
        // Arrange
        var modelInfo = new OcrModelInfo(
            "test_id", "Test Model", OcrModelType.Detection, "test.onnx",
            new Uri("https://example.com/test.tar"), 1024, "abcdef123456");
        var progress = new ModelDownloadProgress(
            modelInfo, ModelDownloadStatus.Downloading, 0.5, "Downloading...",
            downloadSpeedBps: 1048576); // 1MB/s

        // Act
        var formattedSpeed = progress.GetFormattedSpeed();

        // Assert
        Assert.Contains("MB/s", formattedSpeed, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFormattedProgress_ShouldReturnFormattedString()
    {
        // Arrange
        var modelInfo = new OcrModelInfo(
            "test_id", "Test Model", OcrModelType.Detection, "test.onnx",
            new Uri("https://example.com/test.tar"), 1024, "abcdef123456");
        var progress = new ModelDownloadProgress(
            modelInfo, ModelDownloadStatus.Downloading, 0.5, "Downloading...",
            downloadedBytes: 512, totalBytes: 1024);

        // Act
        var formattedProgress = progress.GetFormattedProgress();

        // Assert
        Assert.Contains("512", formattedProgress, StringComparison.Ordinal);
        Assert.Contains("1", formattedProgress, StringComparison.Ordinal);
        Assert.Contains("KB", formattedProgress, StringComparison.Ordinal);
    }
}
