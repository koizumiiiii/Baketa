using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePieceModelManagerのテスト
/// </summary>
public class SentencePieceModelManagerTests : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions TestJsonOptions = new()
    {
        WriteIndented = true
        // PropertyNamingPolicyは設定しない（PascalCaseを保持）
    };
    
    private readonly ILogger<SentencePieceModelManager> _logger;
    private readonly string _tempDirectory;
    private readonly SentencePieceOptions _options;
    private bool _disposed;

    public SentencePieceModelManagerTests()
    {
        _logger = NullLogger<SentencePieceModelManager>.Instance;
        
        // テスト用の一時ディレクトリを作成
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDirectory);
        
        _options = new SentencePieceOptions
        {
            ModelsDirectory = _tempDirectory,
            DefaultModel = "test-model",
            DownloadUrl = "https://example.com/models/{0}.model",
            ModelCacheDays = 30,
            MaxDownloadRetries = 2,
            DownloadTimeoutMinutes = 1,
            EnableChecksumValidation = true,
            EnableAutoCleanup = true,
            CleanupThresholdDays = 90
        };
    }

    [Fact]
    public void Constructor_ValidOptions_InitializesSuccessfully()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();

        // Act
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);

        // Assert
        Assert.True(Directory.Exists(_options.ModelsDirectory));
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        // Arrange
        var mockHttpClientFactory = CreateMockHttpClientFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SentencePieceModelManager(null!, mockHttpClientFactory, _logger));
    }

    [Fact]
    public void Constructor_NullHttpClientFactory_ThrowsArgumentNullException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SentencePieceModelManager(optionsWrapper, null!, _logger));
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, null!));
    }

    [Fact]
    public async Task GetModelPathAsync_ExistingValidModel_ReturnsPath()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        var modelName = "existing-model";
        var modelPath = Path.Combine(_tempDirectory, $"{modelName}.model");
        var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
        
        // 有効なモデルファイルとメタデータを作成
        CreateTestModelFile(modelPath);
        CreateTestMetadata(metadataPath, modelName);

        // Act
        var result = await manager.GetModelPathAsync(modelName).ConfigureAwait(true);

        // Assert
        Assert.Equal(modelPath, result);
        Assert.True(File.Exists(result));
    }

    [Fact]
    public async Task GetModelPathAsync_NonExistentModel_DownloadsModel()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactoryWithDownload();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        var modelName = "new-model";
        var expectedPath = Path.Combine(_tempDirectory, $"{modelName}.model");

        // Act
        var result = await manager.GetModelPathAsync(modelName).ConfigureAwait(true);

        // Assert
        Assert.Equal(expectedPath, result);
        Assert.True(File.Exists(result));
        
        // メタデータファイルも作成されていることを確認
        var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
        Assert.True(File.Exists(metadataPath));
    }

    [Fact]
    public async Task GetModelPathAsync_NullModelName_ThrowsArgumentNullException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);

        // Act & Assert
        // ArgumentException.ThrowIfNullOrEmpty は null の場合 ArgumentNullException を投げる
        await Assert.ThrowsAsync<ArgumentNullException>(() => 
            manager.GetModelPathAsync(null!)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetModelPathAsync_EmptyModelName_ThrowsArgumentException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);

        // Act & Assert
        // ArgumentException.ThrowIfNullOrEmpty は空文字の場合 ArgumentException を投げる
        await Assert.ThrowsAsync<ArgumentException>(() => 
            manager.GetModelPathAsync(string.Empty)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetModelPathAsync_DownloadFailure_ThrowsInvalidOperationException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactoryWithError();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        var modelName = "failing-model";

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => 
            manager.GetModelPathAsync(modelName)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetModelPathAsync_CancellationRequested_ThrowsTaskCanceledException()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactoryWithDelay();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        var modelName = "slow-model";
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => 
            manager.GetModelPathAsync(modelName, cts.Token)).ConfigureAwait(true);
    }

    [Fact]
    public async Task GetAvailableModelsAsync_WithExistingModels_ReturnsModelList()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        // テスト用のモデルを作成（メタデータ作成前にモデルファイルを作成）
        var modelNames = new[] { "model1", "model2", "model3" };
        foreach (var modelName in modelNames)
        {
            var modelPath = Path.Combine(_tempDirectory, $"{modelName}.model");
            var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
            
            // モデルファイルを先に作成（サイズ計算のため）
            CreateTestModelFile(modelPath);
            // その後メタデータを作成（実際のサイズを反映）
            CreateTestMetadata(metadataPath, modelName);
            
            // メタデータファイルが正しく作成されたことを確認
            Assert.True(File.Exists(metadataPath), $"メタデータファイルが作成されていません: {metadataPath}");
            
            // メタデータの内容を検証
            var jsonContent = await File.ReadAllTextAsync(metadataPath).ConfigureAwait(true);
            Assert.Contains(modelName, jsonContent, StringComparison.Ordinal);
        }

        // Act
        var models = await manager.GetAvailableModelsAsync().ConfigureAwait(true);

        // Assert
        Assert.Equal(modelNames.Length, models.Length);
        
        // より詳細なアサーション - ModelNameが正しく設定されているかチェック
        foreach (var model in models)
        {
            Assert.False(string.IsNullOrEmpty(model.ModelName), 
                $"モデル名が空です: {model}");
            Assert.True(modelNames.Contains(model.ModelName), 
                $"予期しないモデル名: {model.ModelName}");
        }
    }

    [Fact]
    public async Task GetAvailableModelsAsync_EmptyDirectory_ReturnsEmptyArray()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);

        // Act
        var models = await manager.GetAvailableModelsAsync().ConfigureAwait(true);

        // Assert
        Assert.Empty(models);
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactory();
        var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);

        // Act & Assert
        manager.Dispose();
        manager.Dispose(); // 2回目の呼び出し
        
        // 例外が発生しないことを確認
    }

    [Fact]
    public async Task GetModelPathAsync_ConcurrentAccess_HandlesSafely()
    {
        // Arrange
        var optionsWrapper = Options.Create(_options);
        var mockHttpClientFactory = CreateMockHttpClientFactoryWithDownload();
        using var manager = new SentencePieceModelManager(optionsWrapper, mockHttpClientFactory, _logger);
        
        var modelName = "concurrent-model";
        var tasks = new Task<string>[5];

        // Act - 複数のタスクが同時に同じモデルを要求
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = manager.GetModelPathAsync(modelName);
        }
        
        var results = await Task.WhenAll(tasks).ConfigureAwait(true);

        // Assert
        Assert.All(results, result => Assert.Equal(results[0], result));
        Assert.True(File.Exists(results[0]));
    }

    private static TestHttpClientFactory CreateMockHttpClientFactory()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        // デフォルトのレスポンスを設定
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("Default mock content")
            });
#pragma warning restore CA2000
        
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        var httpClient = new HttpClient(mockHandler.Object);
#pragma warning restore CA2000
        return new TestHttpClientFactory(httpClient);
    }

    private static TestHttpClientFactory CreateMockHttpClientFactoryWithDownload()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        var testContent = "Test model content";
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(testContent),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"test-etag\"") }
            });
#pragma warning restore CA2000

#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        var httpClient = new HttpClient(mockHandler.Object);
#pragma warning restore CA2000
        return new TestHttpClientFactory(httpClient);
    }

    private static TestHttpClientFactory CreateMockHttpClientFactoryWithError()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.NotFound
            });
#pragma warning restore CA2000

#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        var httpClient = new HttpClient(mockHandler.Object);
#pragma warning restore CA2000
        return new TestHttpClientFactory(httpClient);
    }

    private static TestHttpClientFactory CreateMockHttpClientFactoryWithDelay()
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                await Task.Delay(5000, ct).ConfigureAwait(false); // 5秒の遅延
#pragma warning disable CA2000 // Dispose objects before losing scope - HttpResponseMessageはMock経由で管理される
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("Delayed content")
                };
#pragma warning restore CA2000
            });

#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        var httpClient = new HttpClient(mockHandler.Object);
#pragma warning restore CA2000
        return new TestHttpClientFactory(httpClient);
    }

    private static void CreateTestModelFile(string filePath)
    {
        File.WriteAllText(filePath, "Test SentencePiece model content");
    }

    private static void CreateTestMetadata(string metadataPath, string modelName)
    {
        // メタデータのサイズを実際のモデルファイルサイズに合わせる
        var modelPath = Path.ChangeExtension(metadataPath, ".model");
        long actualSize = 1000; // デフォルトサイズ
        
        if (File.Exists(modelPath))
        {
            actualSize = new FileInfo(modelPath).Length;
        }
        
        var metadata = new ModelMetadata
        {
            ModelName = modelName,
            DownloadedAt = DateTime.UtcNow,
            Version = "test-version",
            Size = actualSize, // 実際のファイルサイズを使用
            Checksum = "test-checksum",
            LastAccessedAt = DateTime.UtcNow,
            SourceUrl = new Uri($"https://example.com/models/{modelName}.model")
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata, TestJsonOptions);
        
        File.WriteAllText(metadataPath, json);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                // テスト用ディレクトリを削除
                try
                {
                    if (Directory.Exists(_tempDirectory))
                    {
                        Directory.Delete(_tempDirectory, true);
                    }
                }
                catch (IOException)
                {
                    // ファイル削除の失敗は無視
                }
                catch (UnauthorizedAccessException)
                {
                    // アクセス権限の問題も無視
                }
            }
            _disposed = true;
        }
    }

    /// <summary>
    /// テスト用のHttpClientFactory実装（Moqの制限を回避）
    /// </summary>
    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
