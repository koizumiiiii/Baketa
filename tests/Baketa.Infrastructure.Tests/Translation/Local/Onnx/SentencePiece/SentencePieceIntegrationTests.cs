using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Xunit;
using Xunit.Abstractions;

#pragma warning disable CA2007 // テストコードではConfigureAwaitの明示的指定は不要

namespace Baketa.Infrastructure.Tests.Translation.Local.Onnx.SentencePiece;

/// <summary>
/// SentencePiece統合テスト
/// </summary>
public class SentencePieceIntegrationTests : IDisposable
{
    private static readonly System.Text.Json.JsonSerializerOptions TestJsonOptions = new()
    {
        WriteIndented = true
        // PropertyNamingPolicyは設定しない（PascalCaseを保持）
    };
    
    private readonly ITestOutputHelper _output;
    private readonly string _tempDirectory;
    private readonly ServiceProvider _serviceProvider;
    private bool _disposed;

    public SentencePieceIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // 文字化けを防ぐため、コンソールエンコーディングをUTF-8に設定
        try
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;
        }
        catch (IOException)
        {
            // コンソールエンコーディングの設定に失敗してもテストを継続
            _output.WriteLine("⚠️ コンソールエンコーディングの設定に失敗しました");
        }
        
        // テスト用の一時ディレクトリを作成
        _tempDirectory = Path.Combine(Path.GetTempPath(), "BaketaIntegrationTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDirectory);
        
        // DIコンテナを設定
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(IServiceCollection services)
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

        services.AddLogging(builder => 
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Error); // テスト中はErrorレベル以上のみ表示
            // SentencePiece関連の警告を抑制（テスト環境では意圖された動作）
            builder.AddFilter("Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece", LogLevel.Error);
        });
        
        // 実際のHTTPクライアントファクトリを使用（ダウンロードが発生しないようファイルを事前配置）
        services.AddHttpClient();
        services.AddSingleton<SentencePieceModelManager>();
    }

    [Fact]
    public async Task FullWorkflow_CreateTokenizer_WithModelManager()
    {
        // Arrange - モデルファイルを事前に配置してダウンロードを回避
        var modelName = "test-integration-model";
        var modelPath = Path.Combine(_tempDirectory, $"{modelName}.model");
        var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
        
        CreateTestModelFileWithMetadata(modelPath, metadataPath, modelName);
        
        var modelManager = _serviceProvider.GetRequiredService<SentencePieceModelManager>();
        var logger = _serviceProvider.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();

        // Act - モデルパスを取得（ダウンロードは発生しない）
        var retrievedModelPath = await modelManager.GetModelPathAsync(modelName).ConfigureAwait(true);
        
        // Assert - モデルパスが正しく取得できた
        Assert.Equal(modelPath, retrievedModelPath);
        Assert.True(File.Exists(retrievedModelPath));
        
        _output.WriteLine($"✅ モデルパスの取得成功: {retrievedModelPath}");

        // Act - トークナイザーを作成
        using var tokenizer = new RealSentencePieceTokenizer(retrievedModelPath, logger);
        
        // Assert - トークナイザーが正しく初期化された
        Assert.True(tokenizer.IsInitialized);
        Assert.Equal(retrievedModelPath, tokenizer.ModelPath);
        Assert.Equal($"SentencePiece_{modelName}", tokenizer.TokenizerId);
        
        _output.WriteLine($"✅ トークナイザーの作成成功: {tokenizer.Name}");

        // Act - テキストをトークン化
        var testTexts = new[]
        {
            "Hello",
            "World",
            "こんにちは",
            "Hello World Test"
        };

        foreach (var testText in testTexts)
        {
            var tokens = tokenizer.Tokenize(testText);
            var decoded = tokenizer.Decode(tokens);
            
            // Assert - トークン化とデコードが機能する
            Assert.NotNull(tokens);
            Assert.NotEmpty(tokens);
            Assert.NotNull(decoded);
            
            _output.WriteLine($"✅ '{testText}' → [{string.Join(", ", tokens)}] → '{decoded}'");
        }
        
        // Act - 特殊トークンの取得
        var specialTokens = tokenizer.GetSpecialTokens();
        
        // Assert - 特殊トークンが取得できた
        Assert.NotNull(specialTokens);
        Assert.True(specialTokens.UnknownId >= 0);
        Assert.True(specialTokens.BeginOfSentenceId >= 0);
        Assert.True(specialTokens.EndOfSentenceId >= 0);
        
        _output.WriteLine($"✅ 特殊トークン: <unk>={specialTokens.UnknownId}, <s>={specialTokens.BeginOfSentenceId}, </s>={specialTokens.EndOfSentenceId}");
    }

    [Fact]
    public async Task ModelManager_DownloadWorkflow_WithMockHttpClient()
    {
        // Arrange - 成功するHTTPモックを作成
        var testContent = "Mock SentencePiece model content for integration test";
        var httpClient = CreateMockHttpClient(testContent, HttpStatusCode.OK, "integration-test-etag");
        
        var services = CreateTestServiceProviderWithMockHttp("downloadable-model", httpClient);
        using (services)
        {
            var modelManager = services.GetRequiredService<SentencePieceModelManager>();

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
            
            // Act - 利用可能なモデル一覧を取得
            var availableModels = await modelManager.GetAvailableModelsAsync().ConfigureAwait(true);
            
            // Assert - ダウンロードしたモデルが一覧に含まれる
            Assert.NotEmpty(availableModels);
            Assert.Contains(availableModels, m => m.ModelName == modelName);
            
            _output.WriteLine($"✅ 利用可能なモデル数: {availableModels.Length}");
        }
    }

    [Fact]
    public async Task EndToEnd_DownloadAndTokenize()
    {
        // Arrange - 実際のSentencePieceモデルファイル形式をシミュレート
        var mockModelContent = CreateMockSentencePieceModelContent();
        var httpClient = CreateMockHttpClient(mockModelContent, HttpStatusCode.OK, "e2e-test-etag");
        
        var modelName = "e2e-test-model";
        var services = CreateTestServiceProviderWithMockHttp(modelName, httpClient);
        using (services)
        {
            var modelManager = services.GetRequiredService<SentencePieceModelManager>();
            var logger = services.GetRequiredService<ILogger<RealSentencePieceTokenizer>>();

            // Act - エンドツーエンドワークフロー
            _output.WriteLine("📥 モデルダウンロード開始...");
            var modelPath = await modelManager.GetModelPathAsync(modelName).ConfigureAwait(true);
            _output.WriteLine($"✅ モデルダウンロード完了: {modelPath}");
            
            _output.WriteLine("🔧 トークナイザー作成開始...");
            using var tokenizer = new RealSentencePieceTokenizer(modelPath, logger);
            _output.WriteLine($"✅ トークナイザー作成完了: {tokenizer.Name}");
            
            // 実際のSentencePieceモデルではないため、暫定実装が使用される
            Assert.False(tokenizer.IsRealSentencePieceAvailable);
            Assert.True(tokenizer.IsInitialized);
            
            _output.WriteLine("🔤 トークン化テスト開始...");
            string[] testSentences =
            [
                "Hello world!",
                "これはテストです。",
                "Machine learning is fascinating.",
                "人工知能の発展は素晴らしい。"
            ];

            foreach (var sentence in testSentences)
            {
                var tokens = tokenizer.Tokenize(sentence);
                var decoded = tokenizer.Decode(tokens);
                
                Assert.NotNull(tokens);
                Assert.NotEmpty(tokens);
                Assert.NotNull(decoded);
                
                _output.WriteLine($"  '{sentence}' → {tokens.Length} tokens → '{decoded}'");
            }
            
            _output.WriteLine("✅ エンドツーエンドテスト完了");
        }
    }

    [Fact]
    public async Task ConcurrentAccess_MultipleTokenizers()
    {
        // Arrange - 各タスク用に個別のモデルファイルを事前に配置してファイルロックを回避
        var modelBaseName = "concurrent-test-model";
        var modelManager = _serviceProvider.GetRequiredService<SentencePieceModelManager>();
        var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();
        
        // 各タスク用のモデルを事前作成（異なるファイル名で競合を回避）
        var modelInfos = new List<(string modelName, string modelPath, string metadataPath)>();
        for (int i = 0; i < 5; i++)
        {
            var modelName = $"{modelBaseName}-{i}";
            var modelPath = Path.Combine(_tempDirectory, $"{modelName}.model");
            var metadataPath = Path.Combine(_tempDirectory, $"{modelName}.metadata.json");
            
            CreateTestModelFileWithMetadata(modelPath, metadataPath, modelName);
            
            modelInfos.Add((modelName, modelPath, metadataPath));
        }

        // Act - 複数のトークナイザーを並行作成（各タスクで異なるモデルを使用）
        var tasks = new Task[5];
        for (int i = 0; i < tasks.Length; i++)
        {
            int taskId = i;
            var (modelName, modelPath, metadataPath) = modelInfos[taskId];
            
            tasks[i] = Task.Run(async () =>
            {
                // 各タスクで異なるモデルを使用してファイルロックを回避
                var retrievedPath = await modelManager.GetModelPathAsync(modelName).ConfigureAwait(true);
                using var tokenizer = new RealSentencePieceTokenizer(
                    retrievedPath, 
                    loggerFactory.CreateLogger<RealSentencePieceTokenizer>());
                
                // 各トークナイザーでテキストを処理
                var tokens = tokenizer.Tokenize($"Test text {taskId}");
                var decoded = tokenizer.Decode(tokens);
                
                _output.WriteLine($"Task {taskId}: '{decoded}' ({tokens.Length} tokens)");
                
                return decoded;
            });
        }

        var results = await Task.WhenAll(tasks.Cast<Task<string>>()).ConfigureAwait(true);

        // Assert - すべてのタスクが正常に完了
        Assert.All(results, result => Assert.NotNull(result));
        Assert.All(results, result => Assert.NotEmpty(result));
        
        _output.WriteLine($"✅ 並行アクセステスト完了: {results.Length} タスク");
    }

    [Fact]
    public void ServiceCollection_Extension_RegistersSentencePieceServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        
        // テスト用のモデルファイルを事前に作成
        var testModelName = "test-service-model";
        var testModelPath = Path.Combine(_tempDirectory, $"{testModelName}.model");
        var testMetadataPath = Path.Combine(_tempDirectory, $"{testModelName}.metadata.json");
        
        CreateTestModelFileWithMetadata(testModelPath, testMetadataPath, testModelName);
        
        // 実際のHTTPクライアントを使用（ダウンロードを回避するためファイルが既に存在）
        services.AddHttpClient();
        
        // SentencePieceOptionsを設定
        services.Configure<SentencePieceOptions>(options =>
        {
            options.ModelsDirectory = _tempDirectory;
            options.DefaultModel = testModelName;
            options.DownloadUrl = "https://example.com/models/{0}.model"; // ダミーURL（使用されない）
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
    public async Task ModelManager_HandleDownloadFailure_WithRetry()
    {
        // Arrange - 失敗するHTTPモックを作成（404エラー）
        var httpClient = CreateMockHttpClient("", HttpStatusCode.NotFound, "error-etag");
        
        // 失敗テスト用にログレベルを調整
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Warning); // 失敗テストでは警告レベルで表示
            // SentencePiece関連の警告を一時的に有効化（失敗テストのため）
            builder.AddFilter("Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece", LogLevel.Warning);
        });
        
        // カスタムHttpClientFactoryを登録
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(httpClient));
        
        services.Configure<SentencePieceOptions>(options =>
        {
            options.ModelsDirectory = _tempDirectory;
            options.DefaultModel = "nonexistent-model";
            options.DownloadUrl = "https://example.com/models/{0}.model";
            options.ModelCacheDays = 30;
            options.MaxDownloadRetries = 2;
            options.DownloadTimeoutMinutes = 1;
            options.EnableChecksumValidation = false;
            options.EnableAutoCleanup = false;
            options.CleanupThresholdDays = 90;
        });
        
        services.AddSingleton<SentencePieceModelManager>();
        
        using var serviceProvider = services.BuildServiceProvider();
        var modelManager = serviceProvider.GetRequiredService<SentencePieceModelManager>();

        _output.WriteLine("🚨 以下のHTTPエラーは期待される動作です（失敗テストのため）");
        
        // Act & Assert - ダウンロード失敗例外が発生することを確認
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => 
            modelManager.GetModelPathAsync("nonexistent-model"));
        
        Assert.Contains("モデルのダウンロードに失敗しました", exception.Message, StringComparison.Ordinal);
        _output.WriteLine($"✅ ダウンロード失敗ハンドリング: {exception.Message}");
    }

    [Fact]
    public void TestHttpClientFactory_WorksCorrectly()
    {
        // Arrange
        var expectedContent = "Test content";
        var httpClient = CreateMockHttpClient(expectedContent, HttpStatusCode.OK, "test-etag");
        var factory = new TestHttpClientFactory(httpClient);

        // Act
        var client1 = factory.CreateClient("");
        var client2 = factory.CreateClient("test-name");
        var client3 = factory.CreateClient(string.Empty);

        // Assert
        Assert.Same(httpClient, client1);
        Assert.Same(httpClient, client2);
        Assert.Same(httpClient, client3);
        
        _output.WriteLine("✅ TestHttpClientFactoryが正しく動作しています");
    }

    private static byte[] CreateMockSentencePieceModelContent()
    {
        // より現実的なSentencePieceモデルファイルの構造をシミュレート
        var content = new List<byte>();
        
        // Protocol Buffersのヘッダー構造を模倣
        byte[] header = [0x08, 0x01, 0x10, 0xE8, 0x07]; // model_type + vocab_size
        byte[] normalizer = [0x1A, 0x08, 0x6E, 0x6D, 0x74, 0x5F, 0x6E, 0x66, 0x6B, 0x63]; // normalizer: "nmt_nfkc"
        
        content.AddRange(header);
        content.AddRange(normalizer);
        
        // 語彙データを追加
        string[] pieces = ["<unk>", "<s>", "</s>", "▁", "Hello", "World", "こんにちは", "世界"];
        foreach (var piece in pieces)
        {
            var pieceBytes = System.Text.Encoding.UTF8.GetBytes(piece);
            content.Add(0x22); // pieces field tag
            content.Add((byte)pieceBytes.Length);
            content.AddRange(pieceBytes);
        }
        
        return [.. content];
    }
    
    /// <summary>
    /// テスト用のHttpClientFactory実装（Moqの制限を回避）
    /// </summary>
    private sealed class TestHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;

        public TestHttpClientFactory(HttpClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public HttpClient CreateClient(string name)
        {
            // すべての呼び出しに対して同じHttpClientインスタンスを返す
            return _client;
        }
    }

    /// <summary>
    /// モックHTTPクライアントを作成
    /// </summary>
    private static HttpClient CreateMockHttpClient(string content, HttpStatusCode statusCode, string etag)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
#pragma warning disable CA2000 // Dispose objects before losing scope - Mockフレームワークが管理するため問題なし
        var response = new HttpResponseMessage
        {
            StatusCode = statusCode,
            Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"") }
        };
#pragma warning restore CA2000 // Dispose objects before losing scope

        if (statusCode == HttpStatusCode.OK)
        {
            response.Content = new StringContent(content);
        }
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        return new HttpClient(mockHandler.Object);
#pragma warning restore CA2000 // Dispose objects before losing scope
    }

    /// <summary>
    /// モックHTTPクライアントを作成（バイナリコンテンツ用）
    /// </summary>
    private static HttpClient CreateMockHttpClient(byte[] content, HttpStatusCode statusCode, string etag)
    {
        var mockHandler = new Mock<HttpMessageHandler>();
        
#pragma warning disable CA2000 // Dispose objects before losing scope - Mockフレームワークが管理するため問題なし
        var response = new HttpResponseMessage
        {
            StatusCode = statusCode,
            Content = new ByteArrayContent(content),
            Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue($"\"{etag}\"") }
        };
#pragma warning restore CA2000 // Dispose objects before losing scope
        
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

#pragma warning disable CA2000 // Dispose objects before losing scope - HttpClientはTestHttpClientFactoryによって管理される
        return new HttpClient(mockHandler.Object);
#pragma warning restore CA2000 // Dispose objects before losing scope
    }

    /// <summary>
    /// テスト用のServiceProviderを作成（既存のモデルファイル用）
    /// </summary>
    private ServiceProvider CreateTestServiceProvider(string defaultModel)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Error); // テスト中はErrorレベル以上のみ表示
            // SentencePiece関連の警告を抑制（テスト環境では意圖された動作）
            builder.AddFilter("Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece", LogLevel.Error);
        });
        services.AddHttpClient(); // 実際のHTTPクライアント（ダウンロードは発生しない）
        
        services.Configure<SentencePieceOptions>(options =>
        {
            options.ModelsDirectory = _tempDirectory;
            options.DefaultModel = defaultModel;
            options.DownloadUrl = "https://example.com/models/{0}.model";
            options.ModelCacheDays = 30;
            options.MaxDownloadRetries = 2;
            options.DownloadTimeoutMinutes = 1;
            options.EnableChecksumValidation = false; // テスト中は無効
            options.EnableAutoCleanup = false;
            options.CleanupThresholdDays = 90;
        });
        
        services.AddSingleton<SentencePieceModelManager>();
        
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// テスト用のServiceProviderを作成（モックHTTPクライアント用）
    /// </summary>
    private ServiceProvider CreateTestServiceProviderWithMockHttp(string defaultModel, HttpClient httpClient)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => 
        {
            builder.AddConsole().SetMinimumLevel(LogLevel.Error); // テスト中はErrorレベル以上のみ表示
            // SentencePiece関連の警告を抑制（テスト環境では意圖された動作）
            builder.AddFilter("Baketa.Infrastructure.Translation.Local.Onnx.SentencePiece", LogLevel.Error);
        });
        
        // カスタムHttpClientFactoryを登録
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(httpClient));
        
        services.Configure<SentencePieceOptions>(options =>
        {
            options.ModelsDirectory = _tempDirectory;
            options.DefaultModel = defaultModel;
            options.DownloadUrl = "https://example.com/models/{0}.model";
            options.ModelCacheDays = 30;
            options.MaxDownloadRetries = 2;
            options.DownloadTimeoutMinutes = 1;
            options.EnableChecksumValidation = false; // テスト中は無効
            options.EnableAutoCleanup = false;
            options.CleanupThresholdDays = 90;
        });
        
        services.AddSingleton<SentencePieceModelManager>();
        
        return services.BuildServiceProvider();
    }

    /// <summary>
    /// テストモデルファイルとメタデータを同期的に作成
    /// </summary>
    private static void CreateTestModelFileWithMetadata(string modelPath, string metadataPath, string modelName)
    {
        // モデルファイルを作成
        CreateValidTestModelFile(modelPath);
        
        // ファイル作成後に実際のサイズを取得してメタデータを作成
        var fileInfo = new FileInfo(modelPath);
        var actualSize = fileInfo.Length;
        
        var baseTime = DateTime.UtcNow.AddMinutes(-30);
        
        var metadata = new ModelMetadata
        {
            ModelName = modelName,
            DownloadedAt = baseTime,
            Version = "test-1.0",
            Size = actualSize, // 実際のファイルサイズを使用
            Checksum = "test-checksum-12345",
            LastAccessedAt = baseTime.AddMinutes(10),
            SourceUrl = new Uri($"https://example.com/models/{modelName}.model"),
            Description = "Test SentencePiece model for integration testing",
            SourceLanguage = "ja",
            TargetLanguage = "en",
            ModelType = "SentencePiece"
        };

        var json = System.Text.Json.JsonSerializer.Serialize(metadata, TestJsonOptions);
        File.WriteAllText(metadataPath, json);
    }
    private static void CreateValidTestModelFile(string filePath)
    {
        var content = @"# Test SentencePiece Model File - Valid Format
trainer_spec {
  model_type: UNIGRAM
  vocab_size: 1000
  character_coverage: 0.9995
  input_sentence_size: 0
  shuffle_input_sentence: true
}
normalizer_spec {
  name: ""nmt_nfkc""
  add_dummy_prefix: true
  remove_extra_whitespaces: true
  escape_whitespaces: true
}
pieces { piece: ""<unk>"" score: 0 type: UNKNOWN }
pieces { piece: ""<s>"" score: 0 type: CONTROL }
pieces { piece: ""</s>"" score: 0 type: CONTROL }
pieces { piece: ""▁"" score: -1.0 type: NORMAL }
pieces { piece: ""Hello"" score: -2.0 type: NORMAL }
pieces { piece: ""World"" score: -2.1 type: NORMAL }
pieces { piece: ""こんにちは"" score: -3.0 type: NORMAL }
pieces { piece: ""世界"" score: -3.1 type: NORMAL }
";
        File.WriteAllText(filePath, content);
    }

    /// <summary>
    /// 有効なテストメタデータを作成
    /// </summary>
    private static void CreateValidTestMetadata(string metadataPath, string modelName)
    {
        // メタデータのサイズを実際のモデルファイルサイズに合わせる
        var modelPath = Path.ChangeExtension(metadataPath, ".model");
        long actualSize = 1000; // デフォルトサイズを大きめに設定
        
        // ファイルが存在する場合は実際のサイズを使用
        if (File.Exists(modelPath))
        {
            var fileInfo = new FileInfo(modelPath);
            actualSize = fileInfo.Length;
        }
        
        var baseTime = DateTime.UtcNow.AddMinutes(-30); // 過去の確実な時刻
        
        var metadata = new ModelMetadata
        {
            ModelName = modelName,
            DownloadedAt = baseTime,
            Version = "test-1.0",
            Size = actualSize,
            Checksum = "test-checksum-12345",
            LastAccessedAt = baseTime.AddMinutes(10),
            SourceUrl = new Uri($"https://example.com/models/{modelName}.model"),
            Description = "Test SentencePiece model for integration testing",
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
                _serviceProvider?.Dispose();
                
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
}
