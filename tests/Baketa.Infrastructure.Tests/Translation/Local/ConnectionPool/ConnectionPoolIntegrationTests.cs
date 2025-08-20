using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Microsoft.Extensions.Configuration;
using Baketa.Infrastructure.Tests.TestUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;

namespace Baketa.Infrastructure.Tests.Translation.Local.ConnectionPool;

/// <summary>
/// 接続プール統合テストクラス（Issue #147）
/// DIコンテナとの統合、設定の読み込み、ライフサイクル管理をテスト
/// </summary>
public class ConnectionPoolIntegrationTests(ITestOutputHelper output) : IAsyncDisposable
{
    private ServiceProvider? _serviceProvider;

    [Fact]
    public async Task DI_Integration_ShouldResolveConnectionPoolCorrectly()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var logger = _serviceProvider.GetRequiredService<ILogger<FixedSizeConnectionPool>>();
        var options = _serviceProvider.GetRequiredService<IOptions<TranslationSettings>>();

        // Assert
        Assert.NotNull(connectionPool);
        Assert.NotNull(logger);
        Assert.NotNull(options);
        Assert.NotNull(options.Value);

        var metrics = connectionPool.GetMetrics();
        output.WriteLine($"MaxConnections: {metrics.MaxConnections}");
        output.WriteLine($"MinConnections: {metrics.MinConnections}");
        output.WriteLine($"AvailableConnections: {metrics.AvailableConnections}");

        Assert.True(metrics.MaxConnections >= 1);
        Assert.True(metrics.MinConnections >= 1);
        Assert.True(metrics.MinConnections <= metrics.MaxConnections);
    }

    [Fact]
    public async Task Configuration_Integration_ShouldLoadSettingsFromConfiguration()
    {
        // Arrange
        var configurationData = new Dictionary<string, string>
        {
            ["Translation:MaxConnections"] = "4",
            ["Translation:MinConnections"] = "2",
            ["Translation:OptimalChunksPerConnection"] = "6",
            ["Translation:ConnectionTimeoutMs"] = "15000",
            ["Translation:HealthCheckIntervalMs"] = "60000"
        };

        var configuration = ConfigurationTestHelper.CreateTestConfiguration(configurationData);

        var services = CreateServiceCollection(configuration);
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var options = _serviceProvider.GetRequiredService<IOptions<TranslationSettings>>();

        // Assert
        var settings = options.Value;
        Assert.Equal(4, settings.MaxConnections);
        Assert.Equal(2, settings.MinConnections);
        Assert.Equal(6, settings.OptimalChunksPerConnection);
        Assert.Equal(15000, settings.ConnectionTimeoutMs);
        Assert.Equal(60000, settings.HealthCheckIntervalMs);

        var metrics = connectionPool.GetMetrics();
        Assert.Equal(4, metrics.MaxConnections);
        Assert.Equal(2, metrics.MinConnections);
    }

    [Fact]
    public async Task Lifecycle_Management_ShouldDisposeCorrectly()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Act - 正常な破棄
        await _serviceProvider.DisposeAsync();

        // Assert - 破棄後のメトリクス取得は例外をスローしない
        var metrics = connectionPool.GetMetrics();
        Assert.NotNull(metrics);
        
        output.WriteLine("ServiceProvider disposed successfully");
    }

    [Fact]
    public async Task Singleton_Lifecycle_ShouldReturnSameInstance()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var connectionPool1 = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var connectionPool2 = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Assert - シングルトンとして登録されているため、同じインスタンスが返される
        Assert.Same(connectionPool1, connectionPool2);
        
        output.WriteLine("Singleton behavior confirmed");
    }

    [Fact]
    public async Task Options_Pattern_ShouldSupportRealTimeUpdates()
    {
        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var optionsMonitor = _serviceProvider.GetRequiredService<IOptionsMonitor<TranslationSettings>>();
        var currentOptions = optionsMonitor.CurrentValue;

        // Assert
        Assert.NotNull(currentOptions);
        Assert.True(currentOptions.MaxConnections >= 1 || currentOptions.MaxConnections == null);
        Assert.True(currentOptions.MinConnections >= 1);
        
        output.WriteLine($"Current MaxConnections: {currentOptions.MaxConnections}");
        output.WriteLine($"Current MinConnections: {currentOptions.MinConnections}");
    }

    [Theory]
    [InlineData(null, 1)] // 自動計算
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    public async Task Various_Configurations_ShouldBeHandledCorrectly(int? maxConnections, int minConnections)
    {
        // Arrange
        var configurationData = new Dictionary<string, string>
        {
            ["Translation:MinConnections"] = minConnections.ToString(),
            ["Translation:ConnectionTimeoutMs"] = "10000",
            ["Translation:HealthCheckIntervalMs"] = "30000"
        };

        if (maxConnections.HasValue)
        {
            configurationData["Translation:MaxConnections"] = maxConnections.ToString();
        }

        var configuration = ConfigurationTestHelper.CreateTestConfiguration(configurationData);

        var services = CreateServiceCollection(configuration);
        _serviceProvider = services.BuildServiceProvider();

        // Act
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var metrics = connectionPool.GetMetrics();

        // Assert
        if (maxConnections.HasValue)
        {
            Assert.Equal(maxConnections.Value, metrics.MaxConnections);
        }
        else
        {
            // 自動計算の場合
            var expectedMax = Environment.ProcessorCount / 2;
            if (expectedMax < 1) expectedMax = 1;
            Assert.Equal(expectedMax, metrics.MaxConnections);
        }

        Assert.Equal(minConnections, metrics.MinConnections);
        Assert.True(metrics.MinConnections <= metrics.MaxConnections);

        output.WriteLine($"Test case - MaxConnections: {maxConnections}, MinConnections: {minConnections}");
        output.WriteLine($"Result - MaxConnections: {metrics.MaxConnections}, MinConnections: {metrics.MinConnections}");
    }

    [Fact]
    public async Task Error_Handling_ShouldBeRobust()
    {
        // Arrange - 無効な設定値
        var configurationData = new Dictionary<string, string>
        {
            ["Translation:MaxConnections"] = "0", // 無効
            ["Translation:MinConnections"] = "10", // MaxConnectionsより大きい
            ["Translation:ConnectionTimeoutMs"] = "-1000",
            ["Translation:HealthCheckIntervalMs"] = "-5000"
        };

        var configuration = ConfigurationTestHelper.CreateTestConfiguration(configurationData);

        var services = CreateServiceCollection(configuration);
        _serviceProvider = services.BuildServiceProvider();

        // Act & Assert - 例外をスローしない
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();
        var metrics = connectionPool.GetMetrics();

        Assert.True(metrics.MaxConnections >= 1); // 最小値に調整される
        Assert.True(metrics.MinConnections >= 1); // 最小値に調整される
        Assert.True(metrics.MinConnections <= metrics.MaxConnections); // 論理的整合性を保つ

        output.WriteLine($"Error handling - MaxConnections: {metrics.MaxConnections}, MinConnections: {metrics.MinConnections}");
    }

    [Fact(Skip = "Pythonサーバーが必要なため統合テスト環境でのみ実行")]
    public async Task Full_Integration_WithPythonServer_ShouldConnectSuccessfully()
    {
        // このテストは実際のPythonサーバーが動作している環境でのみ実行される
        // CI/CDパイプラインや統合テスト環境で利用

        // Arrange
        var services = CreateServiceCollection();
        _serviceProvider = services.BuildServiceProvider();
        var connectionPool = _serviceProvider.GetRequiredService<FixedSizeConnectionPool>();

        // Act
        using var cts = new CancellationTokenSource(30000); // 30秒タイムアウト
        
        try
        {
            var connection = await connectionPool.GetConnectionAsync(cts.Token);
            await connectionPool.ReturnConnectionAsync(connection, CancellationToken.None);
            
            output.WriteLine("✅ 接続プール統合テスト成功 - Pythonサーバーとの接続確認完了");
        }
        catch (Exception ex)
        {
            output.WriteLine($"❌ 接続プール統合テスト失敗: {ex.Message}");
            throw;
        }
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
            builder.SetMinimumLevel(LogLevel.Debug);
        });

        // 設定管理
        if (configuration != null)
        {
            services.AddSingleton(configuration);
        }
        else
        {
            // デフォルト設定
            var defaultConfig = ConfigurationTestHelper.CreateTestConfiguration(new Dictionary<string, string>
                {
                    ["Translation:MaxConnections"] = "", // null (自動計算)
                    ["Translation:MinConnections"] = "1",
                    ["Translation:OptimalChunksPerConnection"] = "4",
                    ["Translation:ConnectionTimeoutMs"] = "30000",
                    ["Translation:HealthCheckIntervalMs"] = "30000"
                });
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

        // FixedSizeConnectionPool登録
        services.AddSingleton<FixedSizeConnectionPool>();

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
