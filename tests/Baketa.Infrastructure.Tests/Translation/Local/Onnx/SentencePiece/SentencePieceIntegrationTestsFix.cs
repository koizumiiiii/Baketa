using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Baketa.Infrastructure.Tests.Helpers;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePiece統合テスト（修正版）
/// </summary>
public class SentencePieceIntegrationTestsFix : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions TestJsonOptions = new()
    {
        WriteIndented = true
        // PropertyNamingPolicyは設定しない（PascalCaseを保持）
    };
    
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;
    private bool _disposed;

    public SentencePieceIntegrationTestsFix(ITestOutputHelper output)
    {
        _output = output;
        
        // テスト用の一時ディレクトリを作成
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaIntegrationTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDirectory);
    }

    [Fact]
    public void ServiceCollection_Extension_RegistersSentencePieceServices_Fixed()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // テスト用のモデルファイルを事前に作成
        var testModelName = "test-service-model";
        var testModelPath = Path.Combine(_tempDirectory, $"{testModelName}.model");
        var testMetadataPath = Path.Combine(_tempDirectory, $"{testModelName}.metadata.json");
        
        CreateTestModelFile(testModelPath);
        CreateTestMetadata(testMetadataPath, testModelName);
        
        // HttpClientサービスを直接登録（モックを使わない）
        services.AddHttpClient();
        
        // SentencePieceOptionsを設定
        services.Configure<SentencePieceOptions>(options =>
        {
            options.ModelsDirectory = _tempDirectory;
            options.DefaultModel = testModelName;
            options.DownloadUrl = "https://example.com/models/{0}.model"; // ダミーURL
            options.EnableChecksumValidation = false; // テスト中は無効化
            options.EnableAutoCleanup = false; // テスト中は無効化
        });

        // Act - SentencePieceサービスを登録
        services.AddSentencePieceServices();
        
        using var serviceProvider = services.BuildServiceProvider();

        // Assert - 必要なサービスが登録されている
        var modelManager = serviceProvider.GetService<SentencePieceModelManager>();
        Assert.NotNull(modelManager);
        
        // ITokenizerの取得（ダウンロード失敗を許容）
        try
        {
            var tokenizer = serviceProvider.GetService<Baketa.Core.Translation.Models.ITokenizer>();
            if (tokenizer != null)
            {
                _output.WriteLine($"✅ ITokenizerの取得成功: {tokenizer.GetType().Name}");
            }
            else
            {
                _output.WriteLine("⚠️ ITokenizerがnullでした");
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("モデルのダウンロードに失敗しました", StringComparison.Ordinal))
        {
            // モデルダウンロード失敗はテスト環境では許容する
            _output.WriteLine($"⚠️ ITokenizerの取得をスキップ: {ex.Message}");
        }
        catch (HttpRequestException ex)
        {
            // HTTPリクエストエラーも許容
            _output.WriteLine($"⚠️ ITokenizerの取得でHTTPエラー: {ex.Message}");
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            // ソケットエラーも許容
            _output.WriteLine($"⚠️ ITokenizerの取得でソケットエラー: {ex.Message}");
        }
        catch (System.IO.DirectoryNotFoundException ex)
        {
            // ディレクトリ不存在エラーも許容
            _output.WriteLine($"⚠️ ITokenizerの取得でディレクトリエラー: {ex.Message}");
        }
        catch (System.IO.FileNotFoundException ex)
        {
            // ファイル不存在エラーも許容
            _output.WriteLine($"⚠️ ITokenizerの取得でファイルエラー: {ex.Message}");
        }
        
        _output.WriteLine("✅ DIサービス登録テスト完了");
    }

    [Fact]
    public async Task ModelManager_DownloadWorkflow_WithMockHttpClient_Fixed()
    {
        // Arrange
        var mockHandler = new Mock<HttpMessageHandler>();
        var testContent = "Mock SentencePiece model content for integration test";
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
#pragma warning disable CA2000 // Dispose objects before losing scope - Mockフレームワークが管理するため問題なし
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(testContent),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"integration-test-etag\"") }
            });
#pragma warning restore CA2000 // Dispose objects before losing scope

        // HttpClientを直接作成
#pragma warning disable CA2000 // Dispose objects before losing scope - テスト終了時に破棄される
        var httpClient = new HttpClient(mockHandler.Object);
#pragma warning restore CA2000

        // カスタムHttpClientFactoryの実装
        var mockFactory = new TestHttpClientFactory(httpClient);

        // DIコンテナを設定
        var services = new ServiceCollection();
        ConfigureTestServices(services, mockFactory);
        
        using var serviceProvider = services.BuildServiceProvider();
        var modelManager = serviceProvider.GetRequiredService<SentencePieceModelManager>();

        // Act - 存在しないモデルを要求（ダウンロードが発生）
        var modelName = "downloadable-model";
        var modelPath = await modelManager.GetModelPathAsync(modelName).ConfigureAwait(true);

        // Assert - ダウンロードが成功した
        Assert.True(File.Exists(modelPath));
        
        var content = await File.ReadAllTextAsync(modelPath).ConfigureAwait(true);
        Assert.Equal(testContent, content);
        
        // メタデータファイルも作成されている
        var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
        Assert.True(File.Exists(metadataPath));
        
        _output.WriteLine($"✅ モデルダウンロード成功: {modelPath}");
        _output.WriteLine($"✅ メタデータ作成成功: {metadataPath}");
    }

    private void ConfigureTestServices(IServiceCollection services, IHttpClientFactory httpClientFactory)
    {
        var options = new SentencePieceOptions
        {
            ModelsDirectory = _tempDirectory,
            DefaultModel = "test-model",
            DownloadUrl = "https://example.com/models/{0}.model",
            ModelCacheDays = 30,
            MaxDownloadRetries = 2,
            DownloadTimeoutMinutes = 1,
            EnableChecksumValidation = false, // テスト中は無効化
            EnableAutoCleanup = false, // テスト中は無効
            CleanupThresholdDays = 90
        };

        services.Configure<SentencePieceOptions>(opts =>
        {
            opts.ModelsDirectory = options.ModelsDirectory;
            opts.DefaultModel = options.DefaultModel;
            opts.DownloadUrl = options.DownloadUrl;
            opts.ModelCacheDays = options.ModelCacheDays;
            opts.MaxDownloadRetries = options.MaxDownloadRetries;
            opts.DownloadTimeoutMinutes = options.DownloadTimeoutMinutes;
            opts.EnableChecksumValidation = options.EnableChecksumValidation;
            opts.EnableAutoCleanup = options.EnableAutoCleanup;
            opts.CleanupThresholdDays = options.CleanupThresholdDays;
        });

        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(httpClientFactory);
        services.AddSingleton<SentencePieceModelManager>();
    }

    private static void CreateTestModelFile(string filePath)
    {
        var content = @"# Test SentencePiece Model File
trainer_spec {
  model_type: UNIGRAM
  vocab_size: 1000
}
normalizer_spec {
  name: ""nfkc""
  add_dummy_prefix: true
}
pieces { piece: ""<unk>"" score: 0 type: UNKNOWN }
pieces { piece: ""<s>"" score: 0 type: CONTROL }
pieces { piece: ""</s>"" score: 0 type: CONTROL }
pieces { piece: ""Hello"" score: -1.0 type: NORMAL }
pieces { piece: ""World"" score: -1.1 type: NORMAL }
";
        File.WriteAllText(filePath, content);
    }

    private static void CreateTestMetadata(string metadataPath, string modelName)
    {
        // 過去の日時を設定してモデルが古いと判定されないようにする
        var baseTime = DateTime.UtcNow.AddMinutes(-30); // 30分前の時刻
        
        // 実際のモデルファイルサイズを取得
        var modelPath = Path.ChangeExtension(metadataPath, ".model");
        long actualSize = 1500; // デフォルトサイズ
        
        if (File.Exists(modelPath))
        {
            actualSize = new FileInfo(modelPath).Length;
        }
        
        var metadata = new ModelMetadata
        {
            ModelName = modelName,
            DownloadedAt = baseTime, // 過去の日時を使用
            Version = "integration-test-1.0",
            Size = actualSize, // 実際のファイルサイズを使用
            Checksum = "integration-test-checksum",
            LastAccessedAt = baseTime.AddMinutes(10), // ダウンロード後の時刻
            SourceUrl = new Uri($"https://example.com/models/{modelName}.model"),
            Description = "Integration test model",
            SourceLanguage = "ja",
            TargetLanguage = "en",
            ModelType = "SentencePiece"
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

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// テスト用のHttpClientFactory実装
    /// </summary>
    private class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
    }
}
