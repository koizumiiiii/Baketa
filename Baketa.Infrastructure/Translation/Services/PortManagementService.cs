using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// ポート管理サービス実装
/// Issue #147 Phase 5: ポート競合防止機構（Mutex版）
/// Gemini改善提案反映: プロセス間競合防止 + 孤立プロセスクリーンアップ
/// </summary>
public class PortManagementService(ILogger<PortManagementService> logger) : IPortManagementService
{
    private readonly string _portRegistryFile = Path.Combine(Environment.CurrentDirectory, "translation_ports.json");
    private readonly SemaphoreSlim _semaphore = new(1, 1); // SemaphoreSlim使用でスレッドセーフ性確保
    private readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    /// <inheritdoc />
    public async Task<int> AcquireAvailablePortAsync(int startPort = 5555, int endPort = 5560)
    {
        logger.LogDebug("🔍 ポート取得開始: 範囲 {StartPort}-{EndPort}", startPort, endPort);
        
        if (!await _semaphore.WaitAsync(_lockTimeout).ConfigureAwait(false))
        {
            throw new TimeoutException($"ポート管理セマフォ取得がタイムアウトしました（{_lockTimeout.TotalSeconds}秒）");
        }
        
        try
        {
            // 起動時孤立プロセスクリーンアップ
            await CleanupOrphanedProcessesInternalAsync().ConfigureAwait(false);
            
            var registry = await LoadPortRegistryAsync().ConfigureAwait(false);
            
            for (int port = startPort; port <= endPort; port++)
            {
                if (await IsPortAvailableInternalAsync(port).ConfigureAwait(false) && 
                    !registry.ActivePorts.Contains(port))
                {
                    registry.ActivePorts.Add(port);
                    registry.LastUpdated = DateTime.UtcNow;
                    await SavePortRegistryAsync(registry).ConfigureAwait(false);
                    
                    logger.LogInformation("🔌 ポート {Port} を取得しました", port);
                    return port;
                }
                else
                {
                    logger.LogDebug("⚠️ ポート {Port} は利用できません（使用中またはレジストリ登録済み）", port);
                }
            }
            
            throw new InvalidOperationException($"ポート範囲 {startPort}-{endPort} に利用可能なポートがありません");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task ReleasePortAsync(int port)
    {
        logger.LogDebug("🔓 ポート {Port} の解放開始", port);
        
        if (!await _semaphore.WaitAsync(_lockTimeout).ConfigureAwait(false))
        {
            logger.LogWarning("⚠️ ポート解放時のセマフォ取得がタイムアウトしました: Port {Port}", port);
            return;
        }
        
        try
        {
            var registry = await LoadPortRegistryAsync().ConfigureAwait(false);
            
            if (registry.ActivePorts.Remove(port))
            {
                registry.Servers.Remove(port.ToString());
                registry.LastUpdated = DateTime.UtcNow;
                await SavePortRegistryAsync(registry).ConfigureAwait(false);
                
                logger.LogInformation("🔓 ポート {Port} を解放しました", port);
            }
            else
            {
                logger.LogDebug("ℹ️ ポート {Port} は既に解放されています", port);
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> IsPortAvailableAsync(int port)
    {
        return await IsPortAvailableInternalAsync(port).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetActivePortsAsync()
    {
        if (!await _semaphore.WaitAsync(_lockTimeout).ConfigureAwait(false))
        {
            logger.LogWarning("⚠️ アクティブポート取得時のセマフォ取得がタイムアウトしました");
            return Array.Empty<int>();
        }
        
        try
        {
            var registry = await LoadPortRegistryAsync().ConfigureAwait(false);
            return registry.ActivePorts.AsReadOnly();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public async Task CleanupOrphanedProcessesAsync()
    {
        if (!await _semaphore.WaitAsync(_lockTimeout).ConfigureAwait(false))
        {
            logger.LogWarning("⚠️ 孤立プロセスクリーンアップのセマフォ取得がタイムアウトしました");
            return;
        }
        
        try
        {
            await CleanupOrphanedProcessesInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    /// <summary>
    /// 内部的な孤立プロセスクリーンアップ（Mutex取得済み前提）
    /// </summary>
    private async Task CleanupOrphanedProcessesInternalAsync()
    {
        logger.LogDebug("🧹 孤立プロセスクリーンアップ開始");
        
        var registry = await LoadPortRegistryAsync().ConfigureAwait(false);
        var orphanedPorts = new List<int>();
        
        foreach (var (portStr, serverInfo) in registry.Servers)
        {
            var port = int.Parse(portStr);
            var pid = serverInfo.Pid;
            
            // プロセス存在確認
            if (!IsProcessAlive(pid))
            {
                logger.LogWarning("🧹 孤立プロセス検出: Port={Port}, PID={PID} - クリーンアップします", port, pid);
                orphanedPorts.Add(port);
                continue;
            }
            
            // TCP応答確認
            if (!await IsServerResponsiveAsync(port).ConfigureAwait(false))
            {
                logger.LogWarning("🧹 応答なしサーバー検出: Port={Port}, PID={PID} - 強制終了します", port, pid);
                KillProcess(pid);
                orphanedPorts.Add(port);
            }
            else
            {
                // ヘルスチェック時刻更新
                serverInfo.LastHealthCheck = DateTime.UtcNow;
            }
        }
        
        // 孤立ポート削除
        foreach (var port in orphanedPorts)
        {
            registry.ActivePorts.Remove(port);
            registry.Servers.Remove(port.ToString());
        }
        
        if (orphanedPorts.Count > 0)
        {
            registry.LastUpdated = DateTime.UtcNow;
            await SavePortRegistryAsync(registry).ConfigureAwait(false);
            logger.LogInformation("🧹 {Count}個の孤立プロセスをクリーンアップしました", orphanedPorts.Count);
        }
        else
        {
            logger.LogDebug("✅ 孤立プロセスは見つかりませんでした");
        }
    }

    /// <summary>
    /// ポートが利用可能かチェック（内部用）
    /// </summary>
    private static async Task<bool> IsPortAvailableInternalAsync(int port)
    {
        try
        {
            // TCPポート確認
            using var tcpListener = new TcpListener(IPAddress.Loopback, port);
            tcpListener.Start();
            tcpListener.Stop();
            
            // 念のためNetworkInformationでも確認
            var ipGlobalProperties = IPGlobalProperties.GetIPGlobalProperties();
            var tcpConnInfoArray = ipGlobalProperties.GetActiveTcpListeners();
            
            return !tcpConnInfoArray.Any(endpoint => endpoint.Port == port);
        }
        catch (SocketException)
        {
            // ポートが既に使用中
            return false;
        }
        catch (Exception)
        {
            // その他のエラーは利用不可とみなす
            return false;
        }
    }

    /// <summary>
    /// プロセスが生きているかチェック
    /// </summary>
    private static bool IsProcessAlive(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false; // プロセスが存在しない
        }
        catch (Exception)
        {
            return false; // その他のエラーは死んでいるとみなす
        }
    }

    /// <summary>
    /// サーバーが応答するかチェック
    /// </summary>
    private async Task<bool> IsServerResponsiveAsync(int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("サーバー応答チェック失敗 Port={Port}: {Error}", port, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// プロセスを強制終了
    /// </summary>
    private void KillProcess(int pid)
    {
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
                logger.LogInformation("💀 プロセス PID={PID} を強制終了しました", pid);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ プロセス終了失敗 PID={PID}: {Error}", pid, ex.Message);
        }
    }

    /// <summary>
    /// ポートレジストリファイルを読み込み
    /// </summary>
    private async Task<PortRegistry> LoadPortRegistryAsync()
    {
        try
        {
            if (!File.Exists(_portRegistryFile))
            {
                logger.LogDebug("ポートレジストリファイルが存在しません。新規作成します: {File}", _portRegistryFile);
                return new PortRegistry();
            }
            
            var json = await File.ReadAllTextAsync(_portRegistryFile).ConfigureAwait(false);
            var registry = JsonSerializer.Deserialize<PortRegistry>(json);
            
            return registry ?? new PortRegistry();
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ ポートレジストリファイル読み込みエラー: {Error}. 新規レジストリを作成します", ex.Message);
            return new PortRegistry();
        }
    }

    /// <summary>
    /// ポートレジストリファイルを保存
    /// </summary>
    private async Task SavePortRegistryAsync(PortRegistry registry)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            
            var json = JsonSerializer.Serialize(registry, options);
            await File.WriteAllTextAsync(_portRegistryFile, json).ConfigureAwait(false);
            
            logger.LogDebug("📁 ポートレジストリファイルを保存しました: {File}", _portRegistryFile);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ ポートレジストリファイル保存エラー: {File}", _portRegistryFile);
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _semaphore.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ セマフォ破棄エラー: {Error}", ex.Message);
        }
        
        _disposed = true;
    }
}