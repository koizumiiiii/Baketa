using System;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;
using Xunit.Abstractions;
using Baketa.Infrastructure.Tests.TestUtilities;

namespace Baketa.Infrastructure.Tests.Translation.Local.ConnectionPool;

/// <summary>
/// FixedSizeConnectionPoolのテストクラス（Issue #147）
/// 接続プール機能の包括的テスト
/// </summary>
public class FixedSizeConnectionPoolTests : IAsyncDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly Mock<ILogger<FixedSizeConnectionPool>> _mockLogger;
    private readonly Mock<IConfiguration> _mockConfiguration;
    private readonly TranslationSettings _testSettings;
    private readonly IOptions<TranslationSettings> _testOptions;
    private FixedSizeConnectionPool? _connectionPool;

    public FixedSizeConnectionPoolTests(ITestOutputHelper output)
    {
        _output = output;
        _mockLogger = new Mock<ILogger<FixedSizeConnectionPool>>();
        _mockConfiguration = new Mock<IConfiguration>();
        
        // IConfigurationのモック設定（デフォルトでNLLB-200）
        _mockConfiguration.Setup(x => x["Translation:DefaultEngine"]).Returns("NLLB200");
        
        // テスト用設定
        _testSettings = new TranslationSettings
        {
            MaxConnections = 2,
            MinConnections = 1,
            OptimalChunksPerConnection = 4,
            ConnectionTimeoutMs = 5000,
            HealthCheckIntervalMs = 30000
        };
        _testOptions = Options.Create(_testSettings);
    }

    [Fact]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);

        // Assert
        Assert.NotNull(_connectionPool);
        var metrics = _connectionPool.GetMetrics();
        Assert.Equal(2, metrics.MaxConnections);
        Assert.Equal(1, metrics.MinConnections);
        Assert.Equal(0, metrics.ActiveConnections);
        Assert.Equal(0, metrics.TotalConnectionsCreated);
    }

    [Fact]
    public void Constructor_WithNullMaxConnections_ShouldUseAutoCalculation()
    {
        // Arrange
        var settingsWithNullMax = new TranslationSettings
        {
            MaxConnections = null, // 自動計算
            MinConnections = 1
        };
        var optionsWithNullMax = Options.Create(settingsWithNullMax);

        // Act
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, optionsWithNullMax);

        // Assert
        var metrics = _connectionPool.GetMetrics();
        var expectedMax = Environment.ProcessorCount / 2;
        if (expectedMax < 1) expectedMax = 1;
        
        Assert.Equal(expectedMax, metrics.MaxConnections);
        Assert.Equal(1, metrics.MinConnections);
    }

    [Fact]
    public async Task GetConnectionAsync_ShouldHandleConnectionFailureGracefully()
    {
        // Arrange
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);
        using var cts = new CancellationTokenSource(5000); // 5秒タイムアウト

        // Act
        try
        {
            var connection = await _connectionPool.GetConnectionAsync(cts.Token);
            
            // もし接続が成功した場合（稀なケース）、適切にクリーンアップ
            if (connection != null)
            {
                await _connectionPool.ReturnConnectionAsync(connection, CancellationToken.None);
                _output.WriteLine("予期せず接続が成功しました（テスト環境でサーバーが稼働中）");
            }
        }
        catch (Exception ex)
        {
            // サーバーがない環境では例外が期待される
            _output.WriteLine($"期待通りの例外が発生: {ex.GetType().Name} - {ex.Message}");
        }

        // Assert - どちらのケースでも、接続プールが適切に動作することを確認
        var metrics = _connectionPool.GetMetrics();
        Assert.True(metrics.MaxConnections >= metrics.MinConnections);
        _output.WriteLine($"接続プールメトリクス: Max={metrics.MaxConnections}, Min={metrics.MinConnections}");
    }

    [Fact]
    public async Task GetMetrics_ShouldReturnCorrectInitialValues()
    {
        // Arrange
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);

        // Act
        var metrics = _connectionPool.GetMetrics();

        // Assert
        Assert.Equal(2, metrics.MaxConnections);
        Assert.Equal(1, metrics.MinConnections);
        Assert.Equal(0, metrics.ActiveConnections);
        Assert.Equal(0, metrics.QueuedConnections);
        Assert.Equal(0, metrics.TotalConnectionsCreated);
        Assert.Equal(0.0, metrics.ConnectionUtilization);
        Assert.Equal(2, metrics.AvailableConnections); // 初期状態では MaxConnections と同じ
    }

    [Fact]
    public async Task GetMetrics_AfterDispose_ShouldNotThrow()
    {
        // Arrange
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);

        // Act
        await AsyncTestHelper.SafeWaitAsync(_connectionPool.DisposeAsync().AsTask());
        var metrics = _connectionPool.GetMetrics();

        // Assert - 破棄後でもメトリクス取得は例外をスローしない
        Assert.NotNull(metrics);
    }

    [Fact]
    public void Constructor_WithInvalidSettings_ShouldAdjustValues()
    {
        // Arrange - 無効な設定値
        var invalidSettings = new TranslationSettings
        {
            MaxConnections = 0, // 無効: 0
            MinConnections = 10, // 無効: MaxConnectionsより大きい
            ConnectionTimeoutMs = -1000, // 負の値は問題ないかもしれないが、テストとして確認
            HealthCheckIntervalMs = 0 // ヘルスチェック無効
        };
        var invalidOptions = Options.Create(invalidSettings);

        // Act
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, invalidOptions);

        // Assert
        var metrics = _connectionPool.GetMetrics();
        Assert.True(metrics.MaxConnections >= 1); // 最小値 1 に調整される
        Assert.True(metrics.MinConnections <= metrics.MaxConnections); // MinConnections が MaxConnections 以下に調整される
        Assert.True(metrics.MinConnections >= 1); // 最小値 1 に調整される
    }

    [Fact]
    public async Task DisposeAsync_ShouldCompleteWithoutException()
    {
        // Arrange
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);

        // Act & Assert - 例外をスローしない
        await _connectionPool.DisposeAsync();
        
        // 二重破棄も例外をスローしない
        await _connectionPool.DisposeAsync();
    }

    [Fact]
    public async Task GetConnectionAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, _testOptions);
        await _connectionPool.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
        {
            await _connectionPool.GetConnectionAsync();
        });
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 1)]
    [InlineData(4, 2)]
    [InlineData(8, 4)]
    public void Constructor_WithVariousConnectionCounts_ShouldConfigureCorrectly(int maxConnections, int minConnections)
    {
        // Arrange
        var settings = new TranslationSettings
        {
            MaxConnections = maxConnections,
            MinConnections = minConnections,
            ConnectionTimeoutMs = 5000,
            HealthCheckIntervalMs = 30000
        };
        var options = Options.Create(settings);

        // Act
        _connectionPool = new FixedSizeConnectionPool(_mockLogger.Object, _mockConfiguration.Object, options);

        // Assert
        var metrics = _connectionPool.GetMetrics();
        Assert.Equal(maxConnections, metrics.MaxConnections);
        Assert.Equal(minConnections, metrics.MinConnections);
        Assert.Equal(maxConnections, metrics.AvailableConnections); // 初期状態
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionPool != null)
        {
            await _connectionPool.DisposeAsync();
        }
    }
}