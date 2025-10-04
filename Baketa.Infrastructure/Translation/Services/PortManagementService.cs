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
/// 🔧 [ULTRATHINK_HYBRID_DESIGN] ハイブリッド方式実装
/// Gemini推奨: 単一共有ファイル + Named Mutex + Heartbeat機構
/// </summary>
public class PortManagementService : IPortManagementService
{
    // 🔧 [GEMINI_FEEDBACK] GUID付きMutex名で衝突回避
    private const string MutexName = @"Global\Baketa-PortManager-Mutex-7F3E4A2B-8C91-4D5F-B1A9-3E7D5F8C2A1E";
    private const string GlobalRegistryFile = "translation_ports_global.json";
    private const int HeartbeatIntervalSeconds = 30;
    private const int StaleEntryThresholdSeconds = 90; // 🔧 [GEMINI_FEEDBACK] 60秒→90秒に拡大
    
    private readonly ILogger<PortManagementService> logger;
    private readonly string _globalRegistryPath = Path.Combine(Environment.CurrentDirectory, GlobalRegistryFile);
    private readonly Mutex _globalMutex;
    private readonly System.Threading.Timer _heartbeatTimer;
    private readonly int _currentProcessId = Environment.ProcessId;
    private readonly HashSet<int> _acquiredPorts = new();
    private readonly TimeSpan _mutexTimeout = TimeSpan.FromSeconds(10);
    private bool _disposed;

    public PortManagementService(ILogger<PortManagementService> logger)
    {
        this.logger = logger;
        
        try
        {
            _globalMutex = new Mutex(false, MutexName);
            
            // 起動時: 孤立ファイルと古いエントリのクリーンアップ
            CleanupLegacyFiles();
            CleanupStaleEntries();
            
            // Heartbeatタイマー開始
            _heartbeatTimer = new System.Threading.Timer(
                UpdateHeartbeat,
                null,
                TimeSpan.FromSeconds(HeartbeatIntervalSeconds),
                TimeSpan.FromSeconds(HeartbeatIntervalSeconds));
                
            logger.LogInformation("🚀 ポート管理サービス初期化完了 (PID={ProcessId})", _currentProcessId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ ポート管理サービス初期化エラー");
            throw;
        }
    }
    
    /// <inheritdoc />
    public async Task<int> AcquireAvailablePortAsync(int startPort = 5556, int endPort = 5562)
    {
        logger.LogDebug("🔍 ポート取得開始: 範囲 {StartPort}-{EndPort}", startPort, endPort);
        
        if (!_globalMutex.WaitOne(_mutexTimeout))
        {
            throw new TimeoutException($"グローバルMutex取得がタイムアウトしました（{_mutexTimeout.TotalSeconds}秒）");
        }
        
        try
        {
            var registry = LoadGlobalRegistry();
            
            // 古いエントリをクリーンアップ
            CleanupStaleEntriesInRegistry(registry);
            
            for (int port = startPort; port <= endPort; port++)
            {
                if (await IsPortAvailableInternalAsync(port).ConfigureAwait(false) && 
                    !registry.Ports.ContainsKey(port.ToString()))
                {
                    // ポートエントリ追加
                    registry.Ports[port.ToString()] = new PortEntry
                    {
                        Pid = _currentProcessId,
                        LastHeartbeat = DateTime.UtcNow
                    };
                    
                    SaveGlobalRegistryAtomic(registry);
                    _acquiredPorts.Add(port);
                    
                    logger.LogInformation("🔌 ポート {Port} を取得しました (PID={ProcessId})", port, _currentProcessId);
                    return port;
                }
                else
                {
                    logger.LogDebug("⚠️ ポート {Port} は利用できません", port);
                }
            }
            
            throw new InvalidOperationException($"ポート範囲 {startPort}-{endPort} に利用可能なポートがありません");
        }
        finally
        {
            _globalMutex.ReleaseMutex(); // 🔧 [GEMINI_FEEDBACK] 確実なMutex解放
        }
    }

    /// <inheritdoc />
    public async Task ReleasePortAsync(int port)
    {
        logger.LogDebug("🔓 ポート {Port} の解放開始", port);
        
        if (!_globalMutex.WaitOne(_mutexTimeout))
        {
            logger.LogWarning("⚠️ ポート解放時のMutex取得がタイムアウトしました: Port {Port}", port);
            return;
        }
        
        try
        {
            var registry = LoadGlobalRegistry();
            
            if (registry.Ports.Remove(port.ToString()))
            {
                SaveGlobalRegistryAtomic(registry);
                _acquiredPorts.Remove(port);
                
                logger.LogInformation("🔓 ポート {Port} を解放しました (PID={ProcessId})", port, _currentProcessId);
            }
            else
            {
                logger.LogDebug("ℹ️ ポート {Port} は既に解放されています", port);
            }
        }
        finally
        {
            _globalMutex.ReleaseMutex();
        }
        
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> IsPortAvailableAsync(int port)
    {
        return await IsPortAvailableInternalAsync(port).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<int>> GetActivePortsAsync()
    {
        if (!_globalMutex.WaitOne(_mutexTimeout))
        {
            logger.LogWarning("⚠️ アクティブポート取得時のMutex取得がタイムアウトしました");
            return [];
        }
        
        try
        {
            var registry = LoadGlobalRegistry();
            CleanupStaleEntriesInRegistry(registry);
            
            var activePorts = registry.Ports
                .Select(kvp => int.Parse(kvp.Key))
                .ToList()
                .AsReadOnly();

            return activePorts;
        }
        finally
        {
            _globalMutex.ReleaseMutex();
        }
    }

    /// <inheritdoc />
    public async Task CleanupOrphanedProcessesAsync()
    {
        // 🔧 [HYBRID_DESIGN] 新設計では起動時とHeartbeatで自動クリーンアップ
        CleanupStaleEntries();
        await Task.CompletedTask;
    }

    /// <summary>
    /// 🔧 [HYBRID_DESIGN] Heartbeat更新コールバック
    /// </summary>
    private void UpdateHeartbeat(object? state)
    {
        if (_disposed) return;
        
        if (!_globalMutex.WaitOne(TimeSpan.FromSeconds(5)))
        {
            logger.LogWarning("⚠️ Heartbeat更新時のMutex取得がタイムアウトしました");
            return;
        }
        
        try
        {
            var registry = LoadGlobalRegistry();
            var updated = false;
            
            foreach (var port in _acquiredPorts)
            {
                if (registry.Ports.TryGetValue(port.ToString(), out var entry) && entry.Pid == _currentProcessId)
                {
                    entry.LastHeartbeat = DateTime.UtcNow;
                    updated = true;
                }
            }
            
            if (updated)
            {
                SaveGlobalRegistryAtomic(registry);
                logger.LogDebug("💓 Heartbeat更新完了 (PID={ProcessId}, Ports={Ports})", _currentProcessId, string.Join(",", _acquiredPorts));
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Heartbeat更新エラー");
        }
        finally
        {
            _globalMutex.ReleaseMutex();
        }
    }
    
    /// <summary>
    /// 🔧 [HYBRID_DESIGN] レガシーファイルのクリーンアップ
    /// 旧設計のプロセス別ファイルを削除
    /// </summary>
    private void CleanupLegacyFiles()
    {
        try
        {
            var currentDirectory = Environment.CurrentDirectory;
            var registryFiles = Directory.GetFiles(currentDirectory, "translation_ports_*.json");
            var cleanupCount = 0;
            
            logger.LogInformation("🧹 レガシーファイルクリーンアップ開始: {Count}個の旧形式ファイル検出", registryFiles.Length);
            
            foreach (var filePath in registryFiles)
            {
                var fileName = Path.GetFileName(filePath);
                
                // ファイル名からプロセスID抽出: translation_ports_{PID}.json
                if (fileName.StartsWith("translation_ports_") && fileName.EndsWith(".json"))
                {
                    var pidString = fileName.Substring("translation_ports_".Length, fileName.Length - "translation_ports_".Length - ".json".Length);
                    
                    if (int.TryParse(pidString, out var pid))
                    {
                        // 現在のプロセスIDは除外
                        // 旧形式ファイルはすべて削除（新設計に移行）
                        try
                        {
                            File.Delete(filePath);
                            cleanupCount++;
                            logger.LogInformation("🧹 レガシーファイル削除: {FileName} (PID={PID})", fileName, pid);
                        }
                        catch (Exception deleteEx)
                        {
                            logger.LogWarning("⚠️ ファイル削除失敗: {FileName} - {Error}", fileName, deleteEx.Message);
                        }
                    }
                    else
                    {
                        logger.LogWarning("⚠️ 無効なファイル名形式: {FileName} - PID解析失敗", fileName);
                    }
                }
            }
            
            if (cleanupCount > 0)
            {
                logger.LogInformation("🧹 レガシーファイルクリーンアップ完了: {Count}個の旧形式ファイルを削除", cleanupCount);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ レガシーファイルクリーンアップエラー");
        }
    }

    /// <summary>
    /// 🔧 [HYBRID_DESIGN] 古いエントリのクリーンアップ
    /// </summary>
    private void CleanupStaleEntries()
    {
        if (!_globalMutex.WaitOne(TimeSpan.FromSeconds(5)))
        {
            logger.LogWarning("⚠️ 古いエントリクリーンアップ時のMutex取得がタイムアウトしました");
            return;
        }
        
        try
        {
            var registry = LoadGlobalRegistry();
            CleanupStaleEntriesInRegistry(registry);
            SaveGlobalRegistryAtomic(registry);
        }
        finally
        {
            _globalMutex.ReleaseMutex();
        }
    }
    
    /// <summary>
    /// 🔧 [HYBRID_DESIGN] レジストリ内の古いエントリをクリーンアップ
    /// </summary>
    private void CleanupStaleEntriesInRegistry(GlobalPortRegistry registry)
    {
        var now = DateTime.UtcNow;
        var staleThreshold = TimeSpan.FromSeconds(StaleEntryThresholdSeconds);
        var staleEntries = new List<string>();
        
        foreach (var (portStr, entry) in registry.Ports)
        {
            if (now - entry.LastHeartbeat > staleThreshold)
            {
                logger.LogWarning("🧹 古いエントリ検出: Port={Port}, PID={PID}, LastHeartbeat={LastHeartbeat}",
                    portStr, entry.Pid, entry.LastHeartbeat);
                staleEntries.Add(portStr);
            }
        }
        
        foreach (var portStr in staleEntries)
        {
            registry.Ports.Remove(portStr);
            logger.LogInformation("🧹 古いエントリ削除: Port={Port}", portStr);
        }
        
        if (staleEntries.Count > 0)
        {
            registry.LastUpdated = DateTime.UtcNow;
        }
    }
    
    /// <summary>
    /// 🔧 [HYBRID_DESIGN] グローバルレジストリ読み込み
    /// </summary>
    private GlobalPortRegistry LoadGlobalRegistry()
    {
        try
        {
            if (!File.Exists(_globalRegistryPath))
            {
                logger.LogDebug("グローバルレジストリファイルが存在しません。新規作成します: {File}", _globalRegistryPath);
                return new GlobalPortRegistry();
            }
            
            var json = File.ReadAllText(_globalRegistryPath);
            var registry = JsonSerializer.Deserialize<GlobalPortRegistry>(json);
            
            return registry ?? new GlobalPortRegistry();
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ グローバルレジストリファイル読み込みエラー: {Error}. 新規レジストリを作成します", ex.Message);
            return new GlobalPortRegistry();
        }
    }
    
    /// <summary>
    /// 🔧 [GEMINI_FEEDBACK] アトミックなファイル保存
    /// 一時ファイル → リネームで破損防止
    /// </summary>
    private void SaveGlobalRegistryAtomic(GlobalPortRegistry registry)
    {
        try
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            };
            
            var json = JsonSerializer.Serialize(registry, options);
            var tempFile = $"{_globalRegistryPath}.tmp";
            
            // 🔧 [GEMINI_FEEDBACK] アトミック書き込み
            File.WriteAllText(tempFile, json);
            File.Move(tempFile, _globalRegistryPath, true);
            
            logger.LogDebug("📁 グローバルレジストリファイルを保存しました: {File}", _globalRegistryPath);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ グローバルレジストリファイル保存エラー: {File}", _globalRegistryPath);
            throw;
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


    public void Dispose()
    {
        if (_disposed) return;
        
        try
        {
            _heartbeatTimer?.Dispose();
            
            // 🔧 [HYBRID_DESIGN] 獲得したポートを確実に解放
            if (_globalMutex.WaitOne(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    var registry = LoadGlobalRegistry();
                    var removedCount = 0;
                    
                    foreach (var port in _acquiredPorts)
                    {
                        if (registry.Ports.Remove(port.ToString()))
                        {
                            removedCount++;
                        }
                    }
                    
                    if (removedCount > 0)
                    {
                        SaveGlobalRegistryAtomic(registry);
                        logger.LogInformation("🔓 プロセス終了時に{Count}個のポートを解放しました (PID={ProcessId})", removedCount, _currentProcessId);
                    }
                }
                finally
                {
                    _globalMutex.ReleaseMutex();
                }
            }
            
            _globalMutex?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Disposeエラー");
        }
        
        _disposed = true;
    }
}