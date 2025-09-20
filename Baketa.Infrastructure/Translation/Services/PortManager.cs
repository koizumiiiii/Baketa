using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Baketa.Core.Abstractions.Translation;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// ポート管理（Step 1: 即座の応急処置）
/// 自動代替ポート選択とポート競合回避機能
/// </summary>
public sealed class PortManager : IPortManagementService
{
    private readonly ILogger<PortManager> _logger;
    private readonly ConcurrentDictionary<int, DateTime> _acquiredPorts = new();
    private volatile bool _disposed;
    
    // Gemini推奨: 5557-5600範囲での代替ポート選択
    private const int DefaultPort = 5556;
    private const int PortRangeStart = 5556;
    private const int PortRangeEnd = 5600;
    
    public PortManager(ILogger<PortManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <inheritdoc />
    public async Task<int> AcquireAvailablePortAsync(int startPort = PortRangeStart, int endPort = PortRangeEnd)
    {
        _logger.LogInformation("🔍 利用可能ポート検索開始 - 範囲: {StartPort}-{EndPort}", startPort, endPort);
        
        // 1. 指定範囲でのポート検索
        for (int port = startPort; port <= endPort; port++)
        {
            if (await IsPortAvailableAsync(port))
            {
                _acquiredPorts[port] = DateTime.UtcNow;
                _logger.LogInformation("✅ ポート取得成功: {Port}", port);
                return port;
            }
        }
        
        // 2. 最後の手段: システムが割り当てる任意のポート
        var systemPort = FindSystemAvailablePort();
        if (systemPort > 0)
        {
            _acquiredPorts[systemPort] = DateTime.UtcNow;
            _logger.LogWarning("⚠️ システム割り当てポート使用: {Port}", systemPort);
            return systemPort;
        }
        
        throw new InvalidOperationException($"利用可能なポートが見つかりません。ポート範囲 {startPort}-{endPort} を確認してください。");
    }
    
    /// <summary>
    /// 利用可能ポートを検索（デフォルトポートから開始）
    /// </summary>
    public async Task<int> FindAvailablePortAsync(int preferredPort = DefaultPort)
    {
        return await AcquireAvailablePortAsync(preferredPort, PortRangeEnd);
    }
    
    /// <inheritdoc />
    public async Task<bool> IsPortAvailableAsync(int port)
    {
        if (port < 1 || port > 65535)
        {
            return false;
        }
        
        try
        {
            // TCP接続テスト
            var tcpAvailable = await IsPortAvailableTcpAsync(port);
            
            // UDP接続テスト（一部アプリケーションで必要）
            var udpAvailable = IsPortAvailableUdp(port);
            
            var isAvailable = tcpAvailable && udpAvailable;
            
            _logger.LogDebug("ポート {Port} 可用性: TCP={TcpAvailable}, UDP={UdpAvailable}", 
                port, tcpAvailable, udpAvailable);
            
            return isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ポート {Port} 可用性チェックエラー: {Error}", port, ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// TCP ポート可用性確認（Gemini修正版）
    /// </summary>
    private async Task<bool> IsPortAvailableTcpAsync(int port)
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            listener.Stop(); // すぐに停止できれば利用可能
            await Task.CompletedTask; // 非同期メソッドの警告回避
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            listener?.Stop();
        }
    }
    
    /// <summary>
    /// UDP ポート可用性確認
    /// </summary>
    private bool IsPortAvailableUdp(int port)
    {
        UdpClient? udpClient = null;
        try
        {
            udpClient = new UdpClient(port);
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            return false;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            udpClient?.Close();
        }
    }
    
    /// <summary>
    /// システムが自動割り当てする利用可能ポートを取得
    /// </summary>
    private int FindSystemAvailablePort()
    {
        TcpListener? listener = null;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            _logger.LogDebug("システム割り当てポート: {Port}", port);
            
            return port;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "システム割り当てポート取得エラー");
            return -1;
        }
        finally
        {
            listener?.Stop();
        }
    }
    
    /// <summary>
    /// 指定範囲内の利用可能ポートを全て取得
    /// </summary>
    public async Task<int[]> GetAvailablePortsInRangeAsync(int startPort = PortRangeStart, int endPort = PortRangeEnd)
    {
        var availablePorts = new List<int>();
        
        _logger.LogDebug("ポート範囲スキャン開始: {StartPort}-{EndPort}", startPort, endPort);
        
        var tasks = new List<Task<(int port, bool available)>>();
        
        for (int port = startPort; port <= endPort; port++)
        {
            var portToCheck = port;
            tasks.Add(Task.Run(async () => (portToCheck, await IsPortAvailableAsync(portToCheck))));
        }
        
        var results = await Task.WhenAll(tasks);
        
        foreach (var (port, available) in results)
        {
            if (available)
            {
                availablePorts.Add(port);
            }
        }
        
        _logger.LogInformation("利用可能ポート発見: {Count}個 [{Ports}]", 
            availablePorts.Count, string.Join(", ", availablePorts));
        
        return availablePorts.ToArray();
    }
    
    /// <summary>
    /// ポート使用状況の詳細情報を取得
    /// </summary>
    public async Task<PortUsageInfo> GetPortUsageInfoAsync(int port)
    {
        var info = new PortUsageInfo { Port = port };
        
        try
        {
            info.IsTcpInUse = !await IsPortAvailableTcpAsync(port);
            info.IsUdpInUse = !IsPortAvailableUdp(port);
            
            // netstat相当の情報取得
            info.ProcessInfo = await GetPortProcessInfoAsync(port);
            
            // ネットワーク統計情報
            var tcpStats = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            var udpStats = IPGlobalProperties.GetIPGlobalProperties().GetActiveUdpListeners();
            
            info.TcpConnections = tcpStats
                .Where(ep => ep.Port == port)
                .Select(ep => ep.ToString())
                .ToArray();
                
            info.UdpConnections = udpStats
                .Where(ep => ep.Port == port)
                .Select(ep => ep.ToString())
                .ToArray();
                
            _logger.LogDebug("ポート {Port} 使用状況: TCP={TcpInUse}, UDP={UdpInUse}, Process={ProcessInfo}",
                port, info.IsTcpInUse, info.IsUdpInUse, info.ProcessInfo ?? "Unknown");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ポート {Port} 使用状況取得エラー", port);
            info.Error = ex.Message;
        }
        
        return info;
    }
    
    /// <summary>
    /// ポートを使用しているプロセス情報を取得
    /// </summary>
    private async Task<string?> GetPortProcessInfoAsync(int port)
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = $"-ano | findstr :{port}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                
                foreach (var line in lines)
                {
                    if (line.Contains($":{port} "))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                        {
                            try
                            {
                                var proc = System.Diagnostics.Process.GetProcessById(pid);
                                return $"{proc.ProcessName} (PID: {pid})";
                            }
                            catch
                            {
                                return $"PID: {pid}";
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("プロセス情報取得エラー (ポート {Port}): {Error}", port, ex.Message);
        }
        
        return null;
    }
    
    /// <summary>
    /// ポート予約（将来の拡張用）
    /// </summary>
    public async Task<PortReservation?> ReservePortAsync(int port, TimeSpan reservationDuration)
    {
        if (await IsPortAvailableAsync(port))
        {
            var reservation = new PortReservation
            {
                Port = port,
                ReservedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.Add(reservationDuration),
                ReservationId = Guid.NewGuid()
            };
            
            _logger.LogInformation("ポート予約成功: {Port} (期限: {ExpiresAt})", port, reservation.ExpiresAt);
            return reservation;
        }
        
        _logger.LogWarning("ポート予約失敗（使用中）: {Port}", port);
        return null;
    }
    
    /// <inheritdoc />
    public async Task ReleasePortAsync(int port)
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        
        if (_acquiredPorts.TryRemove(port, out var acquiredTime))
        {
            _logger.LogInformation("✅ ポート解放: {Port} (使用時間: {Duration})", 
                port, DateTime.UtcNow - acquiredTime);
        }
        else
        {
            _logger.LogDebug("ℹ️ 未取得ポートの解放要求: {Port}", port);
        }
    }
    
    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetActivePortsAsync()
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        return _acquiredPorts.Keys.ToList().AsReadOnly();
    }
    
    /// <inheritdoc />
    public async Task CleanupOrphanedProcessesAsync()
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        
        var orphanedPorts = new List<int>();
        
        foreach (var (port, _) in _acquiredPorts)
        {
            var usageInfo = await GetPortUsageInfoAsync(port);
            if (!usageInfo.IsTcpInUse && !usageInfo.IsUdpInUse)
            {
                orphanedPorts.Add(port);
            }
        }
        
        foreach (var port in orphanedPorts)
        {
            await ReleasePortAsync(port);
            _logger.LogInformation("🧹 孤立ポートをクリーンアップ: {Port}", port);
        }
        
        if (orphanedPorts.Count > 0)
        {
            _logger.LogInformation("🧹 孤立プロセスクリーンアップ完了: {Count}ポート", orphanedPorts.Count);
        }
    }
    
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        _logger.LogInformation("🛑 PortManager破棄 - アクティブポート: {Count}", _acquiredPorts.Count);
        _acquiredPorts.Clear();
    }
}

/// <summary>
/// ポート使用情報
/// </summary>
public sealed class PortUsageInfo
{
    public int Port { get; set; }
    public bool IsTcpInUse { get; set; }
    public bool IsUdpInUse { get; set; }
    public string? ProcessInfo { get; set; }
    public string[] TcpConnections { get; set; } = [];
    public string[] UdpConnections { get; set; } = [];
    public string? Error { get; set; }
    
    public bool IsAvailable => !IsTcpInUse && !IsUdpInUse && string.IsNullOrEmpty(Error);
}

/// <summary>
/// ポート予約情報
/// </summary>
public sealed class PortReservation
{
    public int Port { get; set; }
    public DateTime ReservedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public Guid ReservationId { get; set; }
    
    public bool IsExpired => DateTime.UtcNow > ExpiresAt;
}