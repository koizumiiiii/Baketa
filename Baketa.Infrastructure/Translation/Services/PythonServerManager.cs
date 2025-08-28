using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Baketa.Core.Abstractions.Translation;
using Baketa.Infrastructure.Translation.Models;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバー管理実装
/// Issue #147 Phase 5: ヘルスチェック機能付きプロセス管理
/// Gemini改善提案反映: 自動監視・復旧機能
/// Step 1統合: PythonEnvironmentResolver活用
/// </summary>
public class PythonServerManager(
    IPortManagementService portManager,
    PythonEnvironmentResolver pythonResolver,
    ILogger<PythonServerManager> logger) : IPythonServerManager
{
    private readonly ConcurrentDictionary<string, PythonServerInstance> _activeServers = [];
    private System.Threading.Timer? _healthCheckTimer;
    private readonly object _healthCheckLock = new();
    private bool _disposed;

    /// <summary>
    /// Initialize health check timer
    /// </summary>
    public void InitializeHealthCheckTimer()
    {
        _healthCheckTimer ??= new System.Threading.Timer(HealthCheckTimerCallback, null, 
            System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _healthCheckTimer.Change(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
        logger.LogInformation("🩺 PythonServerManager初期化完了（ヘルスチェック30秒間隔）");
    }
    
    /// <summary>
    /// ヘルスチェックタイマーコールバック
    /// </summary>
    private void HealthCheckTimerCallback(object? state)
    {
        _ = Task.Run(async () => await PerformHealthCheckInternalAsync().ConfigureAwait(false));
    }

    /// <inheritdoc />
    public async Task<IPythonServerInfo> StartServerAsync(string languagePair)
    {
        logger.LogInformation("🚀 Python翻訳サーバー起動開始: {LanguagePair}", languagePair);
        
        // 既存サーバーチェック
        if (_activeServers.TryGetValue(languagePair, out var existing) && existing.IsHealthy)
        {
            logger.LogInformation("♻️ 既存サーバーを再利用: {LanguagePair} → Port {Port}", languagePair, existing.Port);
            return existing;
        }
        
        // 既存が不健全な場合は停止
        if (existing != null)
        {
            logger.LogWarning("🔄 不健全なサーバーを停止して再起動: {LanguagePair}", languagePair);
            await StopServerInternalAsync(languagePair).ConfigureAwait(false);
        }
        
        var port = await portManager.AcquireAvailablePortAsync().ConfigureAwait(false);
        
        try
        {
            var process = await StartPythonProcessAsync(port, languagePair).ConfigureAwait(false);
            
            // サーバー準備完了まで待機
            await WaitForServerReadyAsync(port).ConfigureAwait(false);
            
            var instance = new PythonServerInstance(port, languagePair, process);
            instance.UpdateStatus(ServerStatus.Running);
            _activeServers[languagePair] = instance;
            
            // ポートレジストリにサーバー情報登録
            await RegisterServerInPortRegistryAsync(instance).ConfigureAwait(false);
            
            logger.LogInformation("✅ Python翻訳サーバー起動完了: {LanguagePair} → Port {Port}, PID {PID}", 
                languagePair, port, process.Id);
            
            return instance;
        }
        catch (Exception ex)
        {
            // ポート解放
            await portManager.ReleasePortAsync(port).ConfigureAwait(false);
            logger.LogError(ex, "❌ Python翻訳サーバー起動失敗: {LanguagePair}, Port {Port}", languagePair, port);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task StopServerAsync(int port)
    {
        var server = _activeServers.Values.FirstOrDefault(s => s.Port == port);
        if (server != null)
        {
            await StopServerInternalAsync(server.LanguagePair).ConfigureAwait(false);
        }
        else
        {
            logger.LogWarning("⚠️ 停止対象サーバーが見つかりません: Port {Port}", port);
        }
    }

    /// <inheritdoc />
    public async Task StopServerAsync(string languagePair)
    {
        await StopServerInternalAsync(languagePair).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<IPythonServerInfo>> GetActiveServersAsync()
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        return _activeServers.Values.Cast<IPythonServerInfo>().ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<IPythonServerInfo?> GetServerAsync(string languagePair)
    {
        await Task.CompletedTask; // 非同期メソッドの一貫性のため
        return _activeServers.TryGetValue(languagePair, out var server) ? server : null;
    }

    /// <inheritdoc />
    public async Task PerformHealthCheckAsync()
    {
        await PerformHealthCheckInternalAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Pythonプロセス起動
    /// </summary>
    private async Task<Process> StartPythonProcessAsync(int port, string languagePair)
    {
        var scriptPath = Path.Combine(Environment.CurrentDirectory, "scripts", "dynamic_port_translation_server.py");
        
        // スクリプトファイル存在確認
        if (!File.Exists(scriptPath))
        {
            // フォールバック: 既存のoptimized_translation_server.pyを使用
            scriptPath = Path.Combine(Environment.CurrentDirectory, "scripts", "optimized_translation_server.py");
            
            if (!File.Exists(scriptPath))
            {
                throw new FileNotFoundException($"Python翻訳サーバースクリプトが見つかりません: {scriptPath}");
            }
            
            logger.LogWarning("⚠️ dynamic_port_translation_server.pyが見つかりません。既存スクリプトを使用: {Script}", scriptPath);
        }
        
        // Step 1統合: PythonEnvironmentResolver使用（py.exe優先戦略）
        string pythonExecutable;
        try
        {
            pythonExecutable = await pythonResolver.ResolvePythonExecutableAsync();
            logger.LogInformation("✅ Python実行環境解決: {PythonPath}", pythonExecutable);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogError("❌ Python実行環境解決失敗: {Error}", ex.Message);
            throw new InvalidOperationException($"Python実行環境が見つかりません。Python 3.10以上をインストールしてください。詳細: {ex.Message}", ex);
        }
        
        var startInfo = new ProcessStartInfo
        {
            FileName = pythonExecutable, // Step 1: py.exe優先戦略適用
            Arguments = $"\"{scriptPath}\" --port {port} --language-pair {languagePair}",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Environment.CurrentDirectory
        };
        
        var process = Process.Start(startInfo) ?? 
            throw new InvalidOperationException($"Python翻訳サーバープロセス起動失敗: {languagePair}");
        
        logger.LogDebug("🐍 Pythonプロセス起動: PID {PID}, Python: {Python}, Args: {Args}", 
            process.Id, pythonExecutable, startInfo.Arguments);
        
        // 非同期でログ出力監視（デバッグ用）
        _ = Task.Run(async () =>
        {
            try
            {
                while (!process.StandardOutput.EndOfStream)
                {
                    var line = await process.StandardOutput.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrEmpty(line))
                    {
                        logger.LogDebug("🐍 [Python-{LanguagePair}-{Port}] {Output}", languagePair, port, line);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogDebug("Python標準出力監視エラー: {Error}", ex.Message);
            }
        });
        
        return process;
    }

    /// <summary>
    /// サーバー準備完了まで待機
    /// </summary>
    private async Task WaitForServerReadyAsync(int port)
    {
        var maxRetries = 30; // 30秒
        var retryDelay = TimeSpan.FromSeconds(1);
        
        logger.LogDebug("⏳ サーバー準備完了を待機中: Port {Port}", port);
        
        for (var i = 0; i < maxRetries; i++)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
                
                logger.LogDebug("✅ サーバー準備完了確認: Port {Port} ({Retry}/{MaxRetries})", port, i + 1, maxRetries);
                return; // 接続成功
            }
            catch (Exception ex)
            {
                logger.LogDebug("🔄 サーバー準備待機中: Port {Port}, Retry {Retry}/{MaxRetries}, Error: {Error}", 
                    port, i + 1, maxRetries, ex.Message);
                
                if (i < maxRetries - 1)
                {
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
            }
        }
        
        throw new TimeoutException($"Python翻訳サーバー(Port {port})の起動がタイムアウトしました（{maxRetries}秒）");
    }

    /// <summary>
    /// ポートレジストリにサーバー情報を登録
    /// </summary>
    private async Task RegisterServerInPortRegistryAsync(PythonServerInstance instance)
    {
        // 将来的にはPortManagementServiceにサーバー情報登録メソッドを追加予定
        // 現在は基本的なポート管理のみ実装
        logger.LogDebug("📝 サーバー情報をレジストリに登録: {LanguagePair} → Port {Port}", 
            instance.LanguagePair, instance.Port);
    }

    /// <summary>
    /// 内部サーバー停止処理
    /// </summary>
    private async Task StopServerInternalAsync(string languagePair)
    {
        if (!_activeServers.TryRemove(languagePair, out var server))
        {
            logger.LogDebug("ℹ️ 停止対象サーバーが見つかりません: {LanguagePair}", languagePair);
            return;
        }
        
        logger.LogInformation("🛑 Python翻訳サーバー停止開始: {LanguagePair}, Port {Port}", 
            languagePair, server.Port);
        
        try
        {
            await server.DisposeAsync().ConfigureAwait(false);
            await portManager.ReleasePortAsync(server.Port).ConfigureAwait(false);
            
            logger.LogInformation("✅ Python翻訳サーバー停止完了: {LanguagePair}, Port {Port}", 
                languagePair, server.Port);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "❌ Python翻訳サーバー停止エラー: {LanguagePair}, Port {Port}", 
                languagePair, server.Port);
        }
    }

    /// <summary>
    /// ヘルスチェックコールバック（Timer用）
    /// </summary>
    private async void PerformHealthCheckCallback(object? state)
    {
        await PerformHealthCheckInternalAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// タイマーコールバック（TimerCallback用）
    /// </summary>
    private void OnHealthCheckTimer(object? state)
    {
        _ = Task.Run(async () => await PerformHealthCheckInternalAsync().ConfigureAwait(false));
    }

    /// <summary>
    /// 内部ヘルスチェック処理
    /// </summary>
    private async Task PerformHealthCheckInternalAsync()
    {
        if (_disposed) return;
        
        lock (_healthCheckLock)
        {
            if (_activeServers.IsEmpty)
            {
                logger.LogDebug("🩺 ヘルスチェック: アクティブサーバーなし");
                return;
            }
        }
        
        logger.LogDebug("🩺 ヘルスチェック開始: {Count}サーバー", _activeServers.Count);
        
        var unhealthyServers = new List<string>();
        var healthCheckTasks = _activeServers.ToList().Select(async kvp =>
        {
            var (languagePair, server) = kvp;
            var isHealthy = await CheckServerHealthAsync(server).ConfigureAwait(false);
            
            server.RecordHealthCheck(isHealthy);
            
            if (!isHealthy || !server.IsHealthy)
            {
                logger.LogWarning("❌ 異常サーバー検出: {Server}", server);
                lock (_healthCheckLock)
                {
                    unhealthyServers.Add(languagePair);
                }
            }
            else
            {
                logger.LogDebug("✅ ヘルスチェック正常: {Server}", server);
            }
        });
        
        await Task.WhenAll(healthCheckTasks).ConfigureAwait(false);
        
        // 異常サーバーの処理
        foreach (var languagePair in unhealthyServers)
        {
            logger.LogWarning("🔄 異常サーバーを停止: {LanguagePair}", languagePair);
            await StopServerInternalAsync(languagePair).ConfigureAwait(false);
            
            // 自動再起動（オプション - 設定で制御可能にする予定）
            // await StartServerAsync(languagePair);
        }
        
        if (unhealthyServers.Count > 0)
        {
            logger.LogWarning("🩺 ヘルスチェック完了: {Unhealthy}/{Total}サーバーが異常", 
                unhealthyServers.Count, _activeServers.Count + unhealthyServers.Count);
        }
        else
        {
            logger.LogDebug("🩺 ヘルスチェック完了: 全{Total}サーバー正常", _activeServers.Count);
        }
    }

    /// <summary>
    /// 個別サーバーのヘルスチェック
    /// </summary>
    private async Task<bool> CheckServerHealthAsync(PythonServerInstance server)
    {
        try
        {
            // プロセス存在確認
            if (server.Process.HasExited)
            {
                logger.LogDebug("❌ プロセス終了検出: {Server}", server);
                return false;
            }
            
            // TCP接続確認
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, server.Port)
                .WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            
            // 簡易ping送信（将来的には翻訳テストリクエストも検討）
            // 現在はTCP接続確認のみ
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogDebug("❌ ヘルスチェック失敗: {Server}, Error: {Error}", server, ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        logger.LogInformation("🛑 PythonServerManager破棄開始");
        
        _disposed = true;
        
        try
        {
            _healthCheckTimer?.Dispose();
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ ヘルスチェックタイマー破棄エラー: {Error}", ex.Message);
        }
        
        // 全サーバー停止
        var stopTasks = _activeServers.Keys.ToList().Select(StopServerInternalAsync);
        
        try
        {
            Task.WaitAll([..stopTasks], TimeSpan.FromSeconds(10));
        }
        catch (Exception ex)
        {
            logger.LogWarning("⚠️ サーバー一括停止エラー: {Error}", ex.Message);
        }
        
        logger.LogInformation("✅ PythonServerManager破棄完了");
    }
}