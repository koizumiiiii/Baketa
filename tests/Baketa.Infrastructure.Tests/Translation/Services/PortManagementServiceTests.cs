using System.Net;
using System.Net.Sockets;
using Baketa.Infrastructure.Translation.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Baketa.Infrastructure.Tests.Translation.Services;

/// <summary>
/// PortManagementServiceのユニットテスト
/// Issue #147 Phase 5: ポート競合防止機構
/// </summary>
public class PortManagementServiceTests : IDisposable
{
    private readonly Mock<ILogger<PortManagementService>> _mockLogger;
    private readonly PortManagementService _service;

    public PortManagementServiceTests()
    {
        _mockLogger = new Mock<ILogger<PortManagementService>>();
        _service = new PortManagementService(_mockLogger.Object);
    }

    [Fact]
    public async Task AcquireAvailablePortAsync_Should_ReturnValidPort()
    {
        // Act
        var port = await _service.AcquireAvailablePortAsync();

        // Assert
        Assert.True(port >= 5555 && port <= 5560, $"取得ポート {port} が期待範囲外");
        
        // ポートが実際に利用可能かテスト
        var isAvailable = await _service.IsPortAvailableAsync(port);
        Assert.False(isAvailable); // 取得後は利用不可になるべき
    }

    [Fact]
    public async Task AcquireAvailablePortAsync_Multiple_Should_ReturnDifferentPorts()
    {
        // Act
        var port1 = await _service.AcquireAvailablePortAsync();
        var port2 = await _service.AcquireAvailablePortAsync();

        // Assert
        Assert.NotEqual(port1, port2);
        Assert.True(port1 >= 5555 && port1 <= 5560);
        Assert.True(port2 >= 5555 && port2 <= 5560);
    }

    [Fact]
    public async Task ReleasePortAsync_Should_MakePortAvailable()
    {
        // Arrange
        var port = await _service.AcquireAvailablePortAsync();

        // Act
        await _service.ReleasePortAsync(port);

        // Assert
        var isAvailable = await _service.IsPortAvailableAsync(port);
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task IsPortAvailableAsync_Should_DetectUsedPort()
    {
        // Arrange - ポートを手動で占有
        using var listener = new TcpListener(IPAddress.Loopback, 5555);
        listener.Start();

        // Act
        var isAvailable = await _service.IsPortAvailableAsync(5555);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task GetActivePortsAsync_Should_ReturnAcquiredPorts()
    {
        // Arrange
        var port1 = await _service.AcquireAvailablePortAsync();
        var port2 = await _service.AcquireAvailablePortAsync();

        // Act
        var activePorts = await _service.GetActivePortsAsync();

        // Assert
        Assert.Contains(port1, activePorts);
        Assert.Contains(port2, activePorts);
        Assert.True(activePorts.Count >= 2);
    }

    [Fact]
    public async Task CleanupOrphanedProcessesAsync_Should_ExecuteWithoutErrors()
    {
        // Act & Assert - 例外が発生しないことを確認
        await _service.CleanupOrphanedProcessesAsync();
    }

    [Theory]
    [InlineData(5550, 5555)] // 開始ポートより小さい
    [InlineData(5565, 5560)] // 終了ポートより大きい
    public async Task AcquireAvailablePortAsync_WithCustomRange_Should_ReturnPortInRange(int startPort, int endPort)
    {
        // Act
        var port = await _service.AcquireAvailablePortAsync(startPort, endPort);

        // Assert
        Assert.True(port >= startPort && port <= endPort, 
            $"取得ポート {port} がカスタム範囲 {startPort}-{endPort} 外");
    }

    public void Dispose()
    {
        _service?.Dispose();
    }
}

/// <summary>
/// PortManagementService統合テスト
/// 実際のMutex動作とプロセス間連携をテスト
/// </summary>
public class PortManagementServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<PortManagementService>> _mockLogger;
    private readonly List<PortManagementService> _services;

    public PortManagementServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<PortManagementService>>();
        _services = [];
    }

    [Fact]
    public async Task MultipleServices_Should_AcquireDifferentPorts()
    {
        // Arrange - 複数のサービスインスタンス作成
        var service1 = new PortManagementService(_mockLogger.Object);
        var service2 = new PortManagementService(_mockLogger.Object);
        _services.AddRange([service1, service2]);

        // Act - 並行してポート取得
        var tasks = new[]
        {
            service1.AcquireAvailablePortAsync(),
            service2.AcquireAvailablePortAsync()
        };

        var ports = await Task.WhenAll(tasks);

        // Assert
        Assert.NotEqual(ports[0], ports[1]);
        Assert.All(ports, port => Assert.True(port >= 5555 && port <= 5560));
    }

    [Fact]
    public async Task Concurrent_PortAcquisition_Should_BeThreadSafe()
    {
        // Arrange
        var service = new PortManagementService(_mockLogger.Object);
        _services.Add(service);

        // Act - 大量の並行ポート取得
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.AcquireAvailablePortAsync())
            .ToArray();

        var ports = await Task.WhenAll(tasks);

        // Assert - 全て異なるポートが取得されることを確認
        var uniquePorts = ports.Distinct().ToArray();
        Assert.Equal(Math.Min(6, ports.Length), uniquePorts.Length); // 最大6ポート
    }

    public void Dispose()
    {
        foreach (var service in _services)
        {
            service?.Dispose();
        }
        _services.Clear();
    }
}