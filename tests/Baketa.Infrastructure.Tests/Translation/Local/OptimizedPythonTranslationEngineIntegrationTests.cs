using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Core.Translation.Models;
using Baketa.Infrastructure.Translation.Local;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local;

/// <summary>
/// OptimizedPythonTranslationEngine統合テストクラス（Issue #147）
/// 接続プール統合、DI統合、パフォーマンス測定を含む包括的テスト
/// </summary>
[Collection("PythonServer")]
public class OptimizedPythonTranslationEngineConnectionPoolIntegrationTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private ServiceProvider? _serviceProvider;

    public OptimizedPythonTranslationEngineConnectionPoolIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task DI_Integration_ShouldCreateEngineWithConnectionPool()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Assert
        Assert.NotNull(engine);
        Assert.NotNull(connectionPool);
        Assert.Equal("OptimizedPythonTranslation", engine.Name);
        Assert.Equal("高速化されたPython翻訳エンジン（500ms目標）", engine.Description);
        Assert.False(engine.RequiresNetwork);

        var metrics = connectionPool.GetMetrics();
        _output.WriteLine($"接続プール統計 - Max: {metrics.MaxConnections}, Min: {metrics.MinConnections}");
    }

    [Fact]
    public async Task Configuration_Integration_ShouldUseCustomSettings()
    {
        // Arrange
        var configurationData = new Dictionary<string, string>
        {
            ["Translation:MaxConnections"] = "3",
            ["Translation:MinConnections"] = "1",
            ["Translation:ConnectionTimeoutMs"] = "20000",
            ["Translation:HealthCheckIntervalMs"] = "45000"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = CreateServiceCollection(configuration);
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var settings = _serviceProvider.GetRequiredService<IOptions<TranslationSettings>>().Value;

        // Assert
        Assert.Equal(3, settings.MaxConnections);
        Assert.Equal(1, settings.MinConnections);
        Assert.Equal(20000, settings.ConnectionTimeoutMs);
        Assert.Equal(45000, settings.HealthCheckIntervalMs);

        var metrics = connectionPool.GetMetrics();
        Assert.Equal(3, metrics.MaxConnections);
        Assert.Equal(1, metrics.MinConnections);

        _output.WriteLine($"カスタム設定適用確認 - MaxConnections: {metrics.MaxConnections}");
    }

    [Fact]
    public async Task GetSupportedLanguagePairs_ShouldReturnExpectedPairs()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        // Act
        var languagePairs = await engine.GetSupportedLanguagePairsAsync();

        // Assert
        Assert.NotNull(languagePairs);
        Assert.Equal(2, languagePairs.Count);

        var pairsList = languagePairs.ToList();
        
        // ja -> en
        var jaToEn = pairsList.FirstOrDefault(p => p.SourceLanguage.Code == "ja" && p.TargetLanguage.Code == "en");
        Assert.NotNull(jaToEn);
        
        // en -> ja
        var enToJa = pairsList.FirstOrDefault(p => p.SourceLanguage.Code == "en" && p.TargetLanguage.Code == "ja");
        Assert.NotNull(enToJa);

        _output.WriteLine($"サポート言語ペア数: {languagePairs.Count}");
    }

    [Theory]
    [InlineData("ja", "en", true)]
    [InlineData("en", "ja", true)]
    [InlineData("zh", "en", false)]
    [InlineData("ko", "ja", false)]
    public async Task SupportsLanguagePair_ShouldReturnCorrectResult(string sourceCode, string targetCode, bool expected)
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        var languagePair = new LanguagePair
        {
            SourceLanguage = new Language { Code = sourceCode, DisplayName = $"Language {sourceCode}" },
            TargetLanguage = new Language { Code = targetCode, DisplayName = $"Language {targetCode}" }
        };

        // Act
        var result = await engine.SupportsLanguagePairAsync(languagePair);

        // Assert
        Assert.Equal(expected, result);
        _output.WriteLine($"言語ペア {sourceCode}->{targetCode}: {(result ? "サポート" : "非サポート")}");
    }

    [Fact]
    public async Task IsReady_WithoutServer_ShouldReturnFalse()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        // Act
        var isReady = await engine.IsReadyAsync();

        // Assert
        Assert.False(isReady); // Pythonサーバーが動作していないため
        _output.WriteLine($"エンジン準備状態: {(isReady ? "準備完了" : "準備未完了")}");
    }

    [Fact]
    public async Task TranslateAsync_WithoutServer_ShouldReturnErrorResponse()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        var request = TranslationRequest.Create(
            "Hello, World!",
            new Language { Code = "en", DisplayName = "English" },
            new Language { Code = "ja", DisplayName = "Japanese" }
        );

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await engine.TranslateAsync(request);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(response);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(request.SourceText, response.SourceText);
        Assert.False(response.IsSuccess); // サーバーなしでは失敗
        Assert.Equal("翻訳エラーが発生しました", response.TranslatedText);
        Assert.Equal(0.0f, response.ConfidenceScore);

        _output.WriteLine($"翻訳レスポンス時間: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"レスポンス: IsSuccess={response.IsSuccess}, Text='{response.TranslatedText}'");
    }

    [Fact]
    public async Task TranslateBatchAsync_WithoutServer_ShouldHandleMultipleRequests()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        var requests = new List<TranslationRequest>
        {
            TranslationRequest.Create(
                "Hello",
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }
            ),
            TranslationRequest.Create(
                "World",
                new Language { Code = "en", DisplayName = "English" },
                new Language { Code = "ja", DisplayName = "Japanese" }
            )
        };

        // Act
        var stopwatch = Stopwatch.StartNew();
        var responses = await engine.TranslateBatchAsync(requests);
        stopwatch.Stop();

        // Assert
        Assert.NotNull(responses);
        Assert.Equal(2, responses.Count);

        foreach (var response in responses)
        {
            Assert.False(response.IsSuccess); // サーバーなしでは失敗
            Assert.Equal("翻訳エラーが発生しました", response.TranslatedText);
        }

        _output.WriteLine($"バッチ翻訳時間: {stopwatch.ElapsedMilliseconds}ms ({requests.Count}件)");
        _output.WriteLine($"平均処理時間: {stopwatch.ElapsedMilliseconds / requests.Count}ms/件");
    }

    [Fact]
    public async Task ConnectionPool_Metrics_ShouldBeAccessibleAfterEngineCreation()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Act
        var initialMetrics = connectionPool.GetMetrics();

        // 初期化を少し待つ（バックグラウンド初期化のため）
        await Task.Delay(1000);

        var metricsAfterDelay = connectionPool.GetMetrics();

        // Assert
        Assert.Equal(initialMetrics.MaxConnections, metricsAfterDelay.MaxConnections);
        Assert.Equal(initialMetrics.MinConnections, metricsAfterDelay.MinConnections);

        _output.WriteLine($"初期メトリクス - Active: {initialMetrics.ActiveConnections}, Available: {initialMetrics.AvailableConnections}");
        _output.WriteLine($"遅延後メトリクス - Active: {metricsAfterDelay.ActiveConnections}, Available: {metricsAfterDelay.AvailableConnections}");
    }

    [Fact]
    public async Task Dispose_ShouldCleanupResourcesCorrectly()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Act
        engine.Dispose();
        await connectionPool.DisposeAsync();

        // Assert - 破棄後もメトリクス取得は例外をスローしない
        var metrics = connectionPool.GetMetrics();
        Assert.NotNull(metrics);

        _output.WriteLine("リソース破棄完了");
    }

    [Fact(Skip = "Pythonサーバーが必要なため統合テスト環境でのみ実行")]
    public async Task FullIntegration_WithPythonServer_ShouldMeetPerformanceTargets()
    {
        // このテストは実際のPythonサーバーが動作している環境でのみ実行される
        // CI/CDパイプラインや統合テスト環境で利用

        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        // サーバー起動待機
        await Task.Delay(3000);

        var request = TranslationRequest.Create(
            "こんにちは、世界！",
            new Language { Code = "ja", DisplayName = "Japanese" },
            new Language { Code = "en", DisplayName = "English" }
        );

        // Act
        var stopwatch = Stopwatch.StartNew();
        var response = await engine.TranslateAsync(request);
        stopwatch.Stop();

        // Assert
        Assert.True(response.IsSuccess);
        Assert.True(stopwatch.ElapsedMilliseconds < 500); // 500ms目標
        Assert.True(response.ConfidenceScore > 0.5f);
        Assert.NotEmpty(response.TranslatedText);
        Assert.NotEqual("翻訳エラーが発生しました", response.TranslatedText);

        _output.WriteLine($"✅ パフォーマンステスト成功");
        _output.WriteLine($"処理時間: {stopwatch.ElapsedMilliseconds}ms < 500ms");
        _output.WriteLine($"翻訳結果: '{response.TranslatedText}'");
        _output.WriteLine($"信頼度: {response.ConfidenceScore:P1}");
    }

    [Fact]
    public async Task ConnectionPool_Performance_ShouldReduceLockContention()
    {
        // このテストは実際のPythonサーバーが動作している環境でのみ実行される
        // Issue #147の接続ロック競合問題解決を検証

        // Arrange
        var configurationData = new Dictionary<string, string>
        {
            ["Translation:MaxConnections"] = "4", // 複数接続でテスト
            ["Translation:MinConnections"] = "2"
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationData)
            .Build();

        var services = CreateServiceCollection(configuration);
        _serviceProvider = services.BuildServiceProvider();
        var engine = _serviceProvider.GetRequiredService<OptimizedPythonTranslationEngine>();

        // 複数の並列リクエストを作成
        var requests = Enumerable.Range(1, 10).Select(i => TranslationRequest.Create(
            $"テストテキスト {i}",
            new Language { Code = "ja", DisplayName = "Japanese" },
            new Language { Code = "en", DisplayName = "English" }
        )).ToList();

        // Act
        var stopwatch = Stopwatch.StartNew();
        var tasks = requests.Select(request => engine.TranslateAsync(request)).ToArray();
        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var averageTime = stopwatch.ElapsedMilliseconds / requests.Count;
        var successCount = responses.Count(r => r.IsSuccess);

        Assert.True(successCount > 5); // 最低限の成功数
        Assert.True(averageTime < 1000); // 平均処理時間 < 1秒（接続プールの効果）

        _output.WriteLine($"✅ 接続プール並列処理テスト成功");
        _output.WriteLine($"総処理時間: {stopwatch.ElapsedMilliseconds}ms (10件)");
        _output.WriteLine($"平均処理時間: {averageTime}ms/件");
        _output.WriteLine($"成功率: {successCount}/10件 ({successCount * 10}%)");
    }

    /// <summary>
    /// テスト用のServiceCollectionを作成
    /// </summary>
    private static ServiceCollection CreateServiceCollection(IConfiguration? configuration = null)
    {
        var services = new ServiceCollection();

        // ロギング設定
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        // 設定管理
        if (configuration != null)
        {
            services.AddSingleton(configuration);
        }
        else
        {
            // デフォルト設定
            var defaultConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string>
                {
                    ["Translation:MaxConnections"] = "2",
                    ["Translation:MinConnections"] = "1",
                    ["Translation:OptimalChunksPerConnection"] = "4",
                    ["Translation:ConnectionTimeoutMs"] = "30000",
                    ["Translation:HealthCheckIntervalMs"] = "30000"
                })
                .Build();
            services.AddSingleton<IConfiguration>(defaultConfig);
        }

        // TranslationSettings設定
        services.Configure<TranslationSettings>(options =>
        {
            var config = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
            var maxConnectionsStr = config["Translation:MaxConnections"];

            options.MaxConnections = string.IsNullOrEmpty(maxConnectionsStr) ? null : int.Parse(maxConnectionsStr);
            options.MinConnections = int.Parse(config["Translation:MinConnections"] ?? "1");
            options.OptimalChunksPerConnection = int.Parse(config["Translation:OptimalChunksPerConnection"] ?? "4");
            options.ConnectionTimeoutMs = int.Parse(config["Translation:ConnectionTimeoutMs"] ?? "30000");
            options.HealthCheckIntervalMs = int.Parse(config["Translation:HealthCheckIntervalMs"] ?? "30000");
        });

        // Issue #147: 接続プールとエンジンの登録
        services.AddSingleton<FixedSizeConnectionPool>();
        services.AddSingleton<OptimizedPythonTranslationEngine>();

        return services;
    }

    public async ValueTask DisposeAsync()
    {
        if (_serviceProvider != null)
        {
            await _serviceProvider.DisposeAsync();
        }
    }
}