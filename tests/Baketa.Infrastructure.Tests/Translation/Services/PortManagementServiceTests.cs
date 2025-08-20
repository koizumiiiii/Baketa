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
[Collection("PortManagement")]
public class PortManagementServiceTests : IDisposable
{
    private readonly Mock<ILogger<PortManagementService>> _mockLogger;
    private readonly PortManagementService _service;
    
    // テスト専用ポート範囲（他のテストと衝突回避）
    private readonly int _testPortStart = Random.Shared.Next(6000, 8000);
    private readonly int _testPortEnd;

    public PortManagementServiceTests()
    {
        _mockLogger = new Mock<ILogger<PortManagementService>>();
        _service = new PortManagementService(_mockLogger.Object);
        _testPortEnd = _testPortStart + 20; // 20ポートの範囲を確保
    }

    [Fact]
    public async Task AcquireAvailablePortAsync_Should_ReturnValidPort()
    {
        // Act
        var port = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);

        // Assert
        Assert.True(port >= _testPortStart && port <= _testPortEnd, $"取得ポート {port} が期待範囲外");
        
        // ポートが実際に利用可能かテスト（レジストリに登録されるが、実際のポートは空いている）
        var isAvailable = await _service.IsPortAvailableAsync(port);
        Assert.True(isAvailable); // PortManagementServiceは論理的な管理のみで、実際のポート占有はしない
    }

    [Fact]
    public async Task AcquireAvailablePortAsync_Multiple_Should_ReturnDifferentPorts()
    {
        // Act
        var port1 = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);
        var port2 = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);

        // Assert
        Assert.NotEqual(port1, port2);
        Assert.True(port1 >= _testPortStart && port1 <= _testPortEnd);
        Assert.True(port2 >= _testPortStart && port2 <= _testPortEnd);
    }

    [Fact]
    public async Task ReleasePortAsync_Should_MakePortAvailable()
    {
        // Arrange
        var port = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);

        // Act
        await _service.ReleasePortAsync(port);

        // Assert
        var isAvailable = await _service.IsPortAvailableAsync(port);
        Assert.True(isAvailable);
    }

    [Fact]
    public async Task IsPortAvailableAsync_Should_DetectUsedPort()
    {
        // Arrange - テスト用ポートを手動で占有
        var testPort = _testPortStart;
        using var listener = new TcpListener(IPAddress.Loopback, testPort);
        listener.Start();

        // Act
        var isAvailable = await _service.IsPortAvailableAsync(testPort);

        // Assert
        Assert.False(isAvailable);
    }

    [Fact]
    public async Task GetActivePortsAsync_Should_ReturnAcquiredPorts()
    {
        // Arrange
        var port1 = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);
        var port2 = await _service.AcquireAvailablePortAsync(_testPortStart, _testPortEnd);

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
    [InlineData(10)] // 範囲の先頭から10ポート進んだ位置
    [InlineData(15)] // 範囲の中央付近
    public async Task AcquireAvailablePortAsync_WithCustomRange_Should_ReturnPortInRange(int offset)
    {
        // Arrange - テスト専用範囲内でカスタム範囲を設定
        var customStart = _testPortStart + offset;
        var customEnd = Math.Min(customStart + 5, _testPortEnd); // 5ポートの範囲
        
        // Act
        var port = await _service.AcquireAvailablePortAsync(customStart, customEnd);

        // Assert
        Assert.True(port >= customStart && port <= customEnd, 
            $"取得ポート {port} がカスタム範囲 {customStart}-{customEnd} 外");
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
[Collection("PortManagement")]
public class PortManagementServiceIntegrationTests : IDisposable
{
    private readonly Mock<ILogger<PortManagementService>> _mockLogger;
    private readonly List<PortManagementService> _services;
    
    // 統合テスト専用ポート範囲
    private readonly int _integrationPortStart = Random.Shared.Next(8000, 9000);
    private readonly int _integrationPortEnd;

    public PortManagementServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<PortManagementService>>();
        _services = [];
        _integrationPortEnd = _integrationPortStart + 30; // 30ポートの範囲を確保
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
            service1.AcquireAvailablePortAsync(_integrationPortStart, _integrationPortEnd),
            service2.AcquireAvailablePortAsync(_integrationPortStart, _integrationPortEnd)
        };

        var ports = await Task.WhenAll(tasks);

        // Assert
        Assert.NotEqual(ports[0], ports[1]);
        Assert.All(ports, port => Assert.True(port >= _integrationPortStart && port <= _integrationPortEnd));
    }

    [Fact]
    public async Task Concurrent_PortAcquisition_Should_BeThreadSafe()
    {
        // Arrange
        var service = new PortManagementService(_mockLogger.Object);
        _services.Add(service);

        // Act - 大量の並行ポート取得（統合テスト用範囲を使用）
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => service.AcquireAvailablePortAsync(_integrationPortStart, _integrationPortEnd))
            .ToArray();

        var ports = await Task.WhenAll(tasks);

        // Assert - 全て異なるポートが取得されることを確認
        var uniquePorts = ports.Distinct().ToArray();
        var expectedMaxPorts = Math.Min(_integrationPortEnd - _integrationPortStart + 1, ports.Length);
        Assert.Equal(Math.Min(expectedMaxPorts, ports.Length), uniquePorts.Length);
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