using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Settings;
using Baketa.Core.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバーのヘルスチェック・自動再起動サービス
/// Geminiフィードバック反映: C#側でPythonプロセスを監視・管理
/// </summary>
public class PythonServerHealthMonitor : IHostedService, IDisposable
{
    private readonly ILogger<PythonServerHealthMonitor> _logger;
    private readonly ISettingsService _settingsService;
    private System.Threading.Timer? _healthCheckTimer;
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    
    private int _consecutiveFailures = 0;
    private bool _isRestartInProgress = false;
    private bool _disposed = false;
    private Process? _managedServerProcess;
    private int _currentServerPort = 5556;
    
    // 動的に取得した設定を保持
    private TranslationSettings? _cachedSettings;
    
    // ヘルスチェック統計
    private long _totalHealthChecks = 0;
    private long _totalFailures = 0;
    private DateTime _lastSuccessfulCheck = DateTime.UtcNow;
    private DateTime _lastRestartAttempt = DateTime.MinValue;

    public PythonServerHealthMonitor(
        ILogger<PythonServerHealthMonitor> logger,
        ISettingsService settingsService)
    {
        Console.WriteLine("🔍 [HEALTH_MONITOR] コンストラクタ開始");
        
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        
        Console.WriteLine($"🔍 [HEALTH_MONITOR] settingsService パラメータ: {settingsService?.GetType().Name ?? "null"}");
        
        // 設定の遅延取得（StartAsync時に実際に取得）
        Console.WriteLine("✅ [HEALTH_MONITOR] コンストラクタ完了 - 設定は StartAsync で取得");
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("✅ PythonServerHealthMonitor開始");
        
        // 設定を動的に取得
        var settings = await _settingsService.GetAsync<TranslationSettings>().ConfigureAwait(false);
        if (settings == null)
        {
            _logger.LogWarning("⚠️ TranslationSettings が取得できません - デフォルト設定で動作");
            Console.WriteLine("⚠️ [HEALTH_MONITOR] TranslationSettings が取得できません");
            return;
        }
        
        // 設定をキャッシュ
        _cachedSettings = settings;
        
        Console.WriteLine($"🔍 [HEALTH_MONITOR] 取得した設定: EnableServerAutoRestart={settings.EnableServerAutoRestart}");
        Console.WriteLine($"🔍 [HEALTH_MONITOR] HealthCheckIntervalMs: {settings.HealthCheckIntervalMs}ms");
        
        if (settings.EnableServerAutoRestart)
        {
            // ヘルスチェックタイマーを開始
            var interval = TimeSpan.FromMilliseconds(settings.HealthCheckIntervalMs);
            _healthCheckTimer = new System.Threading.Timer(PerformHealthCheckCallback, null, interval, interval);
            
            _logger.LogInformation("🔍 ヘルスチェック開始 - 間隔: {IntervalMs}ms", settings.HealthCheckIntervalMs);
            Console.WriteLine("✅ [HEALTH_MONITOR] ヘルスチェック有効 - 自動監視開始");
        }
        else
        {
            _logger.LogWarning("⚠️ サーバー自動再起動は無効化されています");
            Console.WriteLine("⚠️ [HEALTH_MONITOR] サーバー自動再起動は無効化されています");
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 PythonServerHealthMonitor停止開始");
        
        _healthCheckTimer?.Change(Timeout.Infinite, 0);
        
        // 管理しているサーバープロセスがあれば停止
        if (_managedServerProcess != null && !_managedServerProcess.HasExited)
        {
            try
            {
                _managedServerProcess.Kill();
                _managedServerProcess.WaitForExit(5000);
                _logger.LogInformation("🔄 管理サーバープロセス停止完了");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ サーバープロセス停止時にエラー");
            }
        }
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// ヘルスチェックタイマーコールバック
    /// </summary>
    private async void PerformHealthCheckCallback(object? state)
    {
        Console.WriteLine($"🔍 [HEALTH_MONITOR] ヘルスチェック実行開始 - {DateTime.Now:HH:mm:ss.fff}");
        
        if (_disposed || _cachedSettings == null || !_cachedSettings.EnableServerAutoRestart)
        {
            Console.WriteLine($"⚠️ [HEALTH_MONITOR] スキップ - disposed:{_disposed}, enabled:{_cachedSettings?.EnableServerAutoRestart ?? false}");
            return;
        }

        try
        {
            var isHealthy = await PerformHealthCheckAsync();
            
            Interlocked.Increment(ref _totalHealthChecks);
            
            Console.WriteLine($"🔍 [HEALTH_MONITOR] ヘルスチェック結果: {(isHealthy ? "✅ 正常" : "❌ 異常")} - Port: {_currentServerPort}");
            
            if (isHealthy)
            {
                // 成功時はカウンターリセット
                if (_consecutiveFailures > 0)
                {
                    _logger.LogInformation("✅ サーバー復旧確認 - 連続失敗回数リセット ({PrevFailures} → 0)",
                        _consecutiveFailures);
                    Console.WriteLine($"✅ [HEALTH_MONITOR] サーバー復旧確認 - 連続失敗回数リセット ({_consecutiveFailures} → 0)");
                }
                
                _consecutiveFailures = 0;
                _lastSuccessfulCheck = DateTime.UtcNow;
            }
            else
            {
                _consecutiveFailures++;
                Interlocked.Increment(ref _totalFailures);
                
                _logger.LogWarning("🚨 サーバーヘルスチェック失敗 ({Current}/{Max}) - Port: {Port}",
                    _consecutiveFailures, _cachedSettings.MaxConsecutiveFailures, _currentServerPort);
                Console.WriteLine($"🚨 [HEALTH_MONITOR] サーバーヘルスチェック失敗 ({_consecutiveFailures}/{_cachedSettings.MaxConsecutiveFailures}) - Port: {_currentServerPort}");
                
                // 最大失敗回数に達したら再起動
                if (_consecutiveFailures >= _cachedSettings.MaxConsecutiveFailures)
                {
                    Console.WriteLine($"🔄 [HEALTH_MONITOR] 最大失敗回数到達 - 自動再起動開始");
                    _ = Task.Run(async () => await HandleServerFailureAsync());
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ ヘルスチェック実行エラー");
            Console.WriteLine($"❌ [HEALTH_MONITOR] ヘルスチェック実行エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// サーバーヘルスチェックの実行
    /// </summary>
    private async Task<bool> PerformHealthCheckAsync()
    {
        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", _currentServerPort);
            
            // 短時間での接続テスト
            if (await Task.WhenAny(connectTask, Task.Delay(2000)) == connectTask)
            {
                if (client.Connected)
                {
                    // 簡単なping翻訳リクエスト
                    var testRequest = new { text = "test", source = "en", target = "ja" };
                    var requestJson = JsonSerializer.Serialize(testRequest);
                    var requestBytes = Encoding.UTF8.GetBytes(requestJson + "\n");
                    
                    var stream = client.GetStream();
                    await stream.WriteAsync(requestBytes);
                    await stream.FlushAsync();
                    
                    // レスポンス読み取り（タイムアウト付き）
                    var buffer = new byte[1024];
                    var readTask = stream.ReadAsync(buffer, 0, buffer.Length);
                    
                    if (await Task.WhenAny(readTask, Task.Delay(3000)) == readTask)
                    {
                        var bytesRead = await readTask;
                        if (bytesRead > 0)
                        {
                            var response = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                            // JSONレスポンスがあれば成功とみなす
                            return response.Contains("success") || response.Contains("translation");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ヘルスチェック接続失敗 (Port {Port}): {Error}", _currentServerPort, ex.Message);
        }
        
        return false;
    }

    /// <summary>
    /// サーバー失敗時の自動再起動処理
    /// </summary>
    private async Task HandleServerFailureAsync()
    {
        if (_isRestartInProgress)
        {
            _logger.LogDebug("再起動処理が既に進行中です");
            return;
        }

        await _restartLock.WaitAsync();
        try
        {
            if (_isRestartInProgress) return;
            
            _isRestartInProgress = true;
            _lastRestartAttempt = DateTime.UtcNow;
            
            _logger.LogError("🚨 サーバー自動再起動開始 - 連続失敗: {Failures}回, Port: {Port}",
                _consecutiveFailures, _currentServerPort);
            
            // 既存プロセスの強制終了
            await TerminateExistingServerAsync();
            
            // バックオフ待機
            await Task.Delay(_cachedSettings?.RestartBackoffMs ?? 5000);
            
            // 新しいサーバー起動
            var restartSuccess = await StartNewServerAsync();
            
            if (restartSuccess)
            {
                _logger.LogInformation("✅ サーバー自動再起動成功 - Port: {Port}", _currentServerPort);
                _consecutiveFailures = 0; // 成功時はカウンターリセット
            }
            else
            {
                _logger.LogError("❌ サーバー自動再起動失敗 - Port: {Port}", _currentServerPort);
            }
        }
        finally
        {
            _isRestartInProgress = false;
            _restartLock.Release();
        }
    }

    /// <summary>
    /// 既存サーバープロセスの終了
    /// </summary>
    private async Task TerminateExistingServerAsync()
    {
        try
        {
            if (_managedServerProcess != null && !_managedServerProcess.HasExited)
            {
                _managedServerProcess.Kill();
                _managedServerProcess.WaitForExit(3000);
                _logger.LogInformation("🔄 既存サーバープロセス終了完了");
            }
            
            // ポートを使用している可能性のあるプロセスの確認・終了
            var processName = "python";
            var processes = Process.GetProcessesByName(processName);
            
            foreach (var process in processes)
            {
                try
                {
                    var commandLine = process.MainModule?.FileName ?? "";
                    if (commandLine.Contains("opus_mt_persistent_server") || 
                        commandLine.Contains("optimized_translation_server"))
                    {
                        process.Kill();
                        process.WaitForExit(2000);
                        _logger.LogInformation("🔄 翻訳サーバープロセス終了: PID {ProcessId}", process.Id);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("プロセス終了時にエラー (PID {ProcessId}): {Error}", process.Id, ex.Message);
                }
            }
            
            await Task.Delay(1000); // プロセス終了後の安定化待機
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 既存プロセス終了時にエラー");
        }
    }

    /// <summary>
    /// 新しいサーバーの起動
    /// </summary>
    private async Task<bool> StartNewServerAsync()
    {
        try
        {
            var pythonPath = "py"; // Windows Python Launcher使用
            var serverScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                @"..\..\..\..\scripts\opus_mt_persistent_server.py");
            
            if (!File.Exists(serverScriptPath))
            {
                serverScriptPath = @"scripts\opus_mt_persistent_server.py";
            }
            
            var processInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"\"{serverScriptPath}\" --port {_currentServerPort}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            
            _managedServerProcess = new Process { StartInfo = processInfo };
            _managedServerProcess.Start();
            
            _logger.LogInformation("🚀 新しいサーバー起動開始 - PID: {ProcessId}, Port: {Port}",
                _managedServerProcess.Id, _currentServerPort);
            
            // 起動完了待機（タイムアウト付き）
            var startupTask = WaitForServerStartupAsync();
            var timeoutTask = Task.Delay(_cachedSettings?.ServerStartupTimeoutMs ?? 30000);
            
            var completedTask = await Task.WhenAny(startupTask, timeoutTask);
            
            if (completedTask == startupTask)
            {
                return await startupTask;
            }
            else
            {
                _logger.LogError("❌ サーバー起動タイムアウト ({TimeoutMs}ms)", _cachedSettings?.ServerStartupTimeoutMs ?? 30000);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ サーバー起動エラー");
            return false;
        }
    }

    /// <summary>
    /// サーバー起動完了の待機
    /// </summary>
    private async Task<bool> WaitForServerStartupAsync()
    {
        var maxAttempts = 30;
        var attemptDelay = 1000;
        
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (await PerformHealthCheckAsync())
            {
                _logger.LogInformation("✅ サーバー起動確認完了 - 試行回数: {Attempt}/{Max}", attempt, maxAttempts);
                return true;
            }
            
            await Task.Delay(attemptDelay);
        }
        
        return false;
    }

    /// <summary>
    /// 現在のヘルスチェック統計を取得
    /// </summary>
    public HealthMonitorStats GetStats()
    {
        return new HealthMonitorStats
        {
            TotalHealthChecks = _totalHealthChecks,
            TotalFailures = _totalFailures,
            ConsecutiveFailures = _consecutiveFailures,
            LastSuccessfulCheck = _lastSuccessfulCheck,
            LastRestartAttempt = _lastRestartAttempt,
            IsRestartInProgress = _isRestartInProgress,
            CurrentServerPort = _currentServerPort
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _healthCheckTimer?.Dispose();
        _restartLock?.Dispose();
        
        if (_managedServerProcess != null && !_managedServerProcess.HasExited)
        {
            try
            {
                _managedServerProcess.Kill();
                _managedServerProcess.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Dispose時のプロセス終了エラー");
            }
            finally
            {
                _managedServerProcess?.Dispose();
            }
        }
    }
}

/// <summary>
/// ヘルスモニター統計情報
/// </summary>
public record HealthMonitorStats
{
    public long TotalHealthChecks { get; init; }
    public long TotalFailures { get; init; }
    public int ConsecutiveFailures { get; init; }
    public DateTime LastSuccessfulCheck { get; init; }
    public DateTime LastRestartAttempt { get; init; }
    public bool IsRestartInProgress { get; init; }
    public int CurrentServerPort { get; init; }
    
    public double FailureRate => TotalHealthChecks > 0 ? (double)TotalFailures / TotalHealthChecks : 0.0;
    public TimeSpan TimeSinceLastSuccess => DateTime.UtcNow - LastSuccessfulCheck;
}