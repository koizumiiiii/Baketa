using Baketa.Infrastructure.Translation.Local.ConnectionPool;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Local.ConnectionPool;

/// <summary>
/// ConnectionPoolMetricsのテストクラス（Issue #147）
/// 接続プールメトリクスクラスの機能テスト
/// </summary>
public class ConnectionPoolMetricsTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaultValues()
    {
        // Act
        var metrics = new ConnectionPoolMetrics();

        // Assert
        Assert.Equal(0, metrics.ActiveConnections);
        Assert.Equal(0, metrics.QueuedConnections);
        Assert.Equal(0, metrics.TotalConnectionsCreated);
        Assert.Equal(0, metrics.MaxConnections);
        Assert.Equal(0, metrics.MinConnections);
        Assert.Equal(0.0, metrics.ConnectionUtilization);
        Assert.Equal(0, metrics.AvailableConnections);
    }

    [Fact]
    public void Properties_ShouldBeSettableAndGettable()
    {
        // Arrange
        var metrics = new ConnectionPoolMetrics();

        // Act
        metrics.ActiveConnections = 5;
        metrics.QueuedConnections = 3;
        metrics.TotalConnectionsCreated = 10;
        metrics.MaxConnections = 8;
        metrics.MinConnections = 2;
        metrics.ConnectionUtilization = 0.625; // 5/8
        metrics.AvailableConnections = 3;

        // Assert
        Assert.Equal(5, metrics.ActiveConnections);
        Assert.Equal(3, metrics.QueuedConnections);
        Assert.Equal(10, metrics.TotalConnectionsCreated);
        Assert.Equal(8, metrics.MaxConnections);
        Assert.Equal(2, metrics.MinConnections);
        Assert.Equal(0.625, metrics.ConnectionUtilization);
        Assert.Equal(3, metrics.AvailableConnections);
    }

    [Theory]
    [InlineData(0, 10, 0.0)]
    [InlineData(5, 10, 0.5)]
    [InlineData(10, 10, 1.0)]
    [InlineData(3, 8, 0.375)]
    public void ConnectionUtilization_ShouldCalculateCorrectly(int activeConnections, int maxConnections, double expectedUtilization)
    {
        // Arrange
        var metrics = new ConnectionPoolMetrics
        {
            ActiveConnections = activeConnections,
            MaxConnections = maxConnections,
            ConnectionUtilization = (double)activeConnections / maxConnections
        };

        // Assert
        Assert.Equal(expectedUtilization, metrics.ConnectionUtilization, 3); // 小数点以下3桁まで比較
    }

    [Fact]
    public void Properties_ShouldSupportNegativeValues()
    {
        // Arrange
        var metrics = new ConnectionPoolMetrics();

        // Act - 理論的には負の値は発生しないが、プロパティとしては設定可能
        metrics.ActiveConnections = -1;
        metrics.QueuedConnections = -2;
        metrics.TotalConnectionsCreated = -3;
        metrics.MaxConnections = -4;
        metrics.MinConnections = -5;
        metrics.ConnectionUtilization = -0.5;
        metrics.AvailableConnections = -6;

        // Assert
        Assert.Equal(-1, metrics.ActiveConnections);
        Assert.Equal(-2, metrics.QueuedConnections);
        Assert.Equal(-3, metrics.TotalConnectionsCreated);
        Assert.Equal(-4, metrics.MaxConnections);
        Assert.Equal(-5, metrics.MinConnections);
        Assert.Equal(-0.5, metrics.ConnectionUtilization);
        Assert.Equal(-6, metrics.AvailableConnections);
    }

    [Fact]
    public void Properties_ShouldSupportLargeValues()
    {
        // Arrange
        var metrics = new ConnectionPoolMetrics();

        // Act
        metrics.ActiveConnections = int.MaxValue;
        metrics.QueuedConnections = int.MaxValue - 1;
        metrics.TotalConnectionsCreated = int.MaxValue - 2;
        metrics.MaxConnections = int.MaxValue - 3;
        metrics.MinConnections = int.MaxValue - 4;
        metrics.ConnectionUtilization = double.MaxValue;
        metrics.AvailableConnections = int.MaxValue - 5;

        // Assert
        Assert.Equal(int.MaxValue, metrics.ActiveConnections);
        Assert.Equal(int.MaxValue - 1, metrics.QueuedConnections);
        Assert.Equal(int.MaxValue - 2, metrics.TotalConnectionsCreated);
        Assert.Equal(int.MaxValue - 3, metrics.MaxConnections);
        Assert.Equal(int.MaxValue - 4, metrics.MinConnections);
        Assert.Equal(double.MaxValue, metrics.ConnectionUtilization);
        Assert.Equal(int.MaxValue - 5, metrics.AvailableConnections);
    }

    [Fact]
    public void ConnectionUtilization_ShouldSupportSpecialDoubleValues()
    {
        // Arrange
        var metrics = new ConnectionPoolMetrics();

        // Act & Assert - 特殊な double 値のサポート
        metrics.ConnectionUtilization = double.NaN;
        Assert.True(double.IsNaN(metrics.ConnectionUtilization));

        metrics.ConnectionUtilization = double.PositiveInfinity;
        Assert.True(double.IsPositiveInfinity(metrics.ConnectionUtilization));

        metrics.ConnectionUtilization = double.NegativeInfinity;
        Assert.True(double.IsNegativeInfinity(metrics.ConnectionUtilization));

        metrics.ConnectionUtilization = 0.0;
        Assert.Equal(0.0, metrics.ConnectionUtilization);

        metrics.ConnectionUtilization = 1.0;
        Assert.Equal(1.0, metrics.ConnectionUtilization);
    }

    [Fact]
    public void Metrics_ShouldBeIndependentInstances()
    {
        // Arrange
        var metrics1 = new ConnectionPoolMetrics { ActiveConnections = 5 };
        var metrics2 = new ConnectionPoolMetrics { ActiveConnections = 10 };

        // Act
        metrics1.QueuedConnections = 3;
        metrics2.QueuedConnections = 7;

        // Assert - インスタンスが独立していることを確認
        Assert.Equal(5, metrics1.ActiveConnections);
        Assert.Equal(10, metrics2.ActiveConnections);
        Assert.Equal(3, metrics1.QueuedConnections);
        Assert.Equal(7, metrics2.QueuedConnections);
    }

    [Theory]
    [InlineData(0, 0, 0, 0, 0, 0.0, 0)]
    [InlineData(1, 2, 3, 4, 5, 0.25, 6)]
    [InlineData(100, 50, 200, 120, 10, 0.833333, 20)]
    public void AllProperties_ShouldBeSetCorrectlyTogether(
        int activeConnections,
        int queuedConnections,
        int totalConnectionsCreated,
        int maxConnections,
        int minConnections,
        double connectionUtilization,
        int availableConnections)
    {
        // Arrange & Act
        var metrics = new ConnectionPoolMetrics
        {
            ActiveConnections = activeConnections,
            QueuedConnections = queuedConnections,
            TotalConnectionsCreated = totalConnectionsCreated,
            MaxConnections = maxConnections,
            MinConnections = minConnections,
            ConnectionUtilization = connectionUtilization,
            AvailableConnections = availableConnections
        };

        // Assert
        Assert.Equal(activeConnections, metrics.ActiveConnections);
        Assert.Equal(queuedConnections, metrics.QueuedConnections);
        Assert.Equal(totalConnectionsCreated, metrics.TotalConnectionsCreated);
        Assert.Equal(maxConnections, metrics.MaxConnections);
        Assert.Equal(minConnections, metrics.MinConnections);
        Assert.Equal(connectionUtilization, metrics.ConnectionUtilization, 6); // 小数点以下6桁まで比較
        Assert.Equal(availableConnections, metrics.AvailableConnections);
    }
}