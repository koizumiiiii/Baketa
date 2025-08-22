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
using Baketa.Core.Abstractions.Patterns;
using Baketa.Core.Translation.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python翻訳サーバーのヘルスチェック・自動再起動サービス
/// Geminiフィードバック反映: C#側でPythonプロセスを監視・管理
/// 🔧 [GEMINI_REVIEW] IAsyncDisposableパターン適用によるデッドロック防止
/// </summary>
public class PythonServerHealthMonitor : IHostedService, IAsyncDisposable
{
    private readonly ILogger<PythonServerHealthMonitor> _logger;
    private readonly ISettingsService _settingsService;
    private readonly ICircuitBreaker<TranslationResponse>? _circuitBreaker; // Phase2: サーキットブレーカー連携
    private System.Threading.Timer? _healthCheckTimer;
    private readonly SemaphoreSlim _restartLock = new(1, 1);
    
    private int _consecutiveFailures = 0;
    private bool _isRestartInProgress = false;
    private bool _disposed = false;
    private Process? _managedServerProcess;
    private int _currentServerPort = 5556; // デフォルト（OPUS-MT）、NLLB-200は5556
    
    // 🔧 [PROCESS_DUPLICATION_PREVENTION] プロセス重複防止システム
    private static readonly string PidFilePath = Path.Combine(Path.GetTempPath(), "baketa_translation_server.pid");
    private static readonly string LockFilePath = Path.Combine(Path.GetTempPath(), "baketa_translation_server.lock");
    
    // 動的に取得した設定を保持
    private TranslationSettings? _cachedSettings;
    
    // ヘルスチェック統計
    private long _totalHealthChecks = 0;
    private long _totalFailures = 0;
    private DateTime _lastSuccessfulCheck = DateTime.UtcNow;
    private DateTime _lastRestartAttempt = DateTime.MinValue;

    public PythonServerHealthMonitor(
        ILogger<PythonServerHealthMonitor> logger,
        ISettingsService settingsService,
        ICircuitBreaker<TranslationResponse>? circuitBreaker = null)
    {
        Console.WriteLine("🔍 [HEALTH_MONITOR] コンストラクタ開始");
        
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _circuitBreaker = circuitBreaker; // Phase2: サーキットブレーカー連携（オプション）
        
        Console.WriteLine($"🔍 [HEALTH_MONITOR] settingsService パラメータ: {settingsService?.GetType().Name ?? "null"}");
        Console.WriteLine($"🔧 [PHASE2] サーキットブレーカー連携: {(_circuitBreaker != null ? "有効" : "無効")}");
        
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
                
                // Phase2: サーキットブレーカー統計情報ログ
                if (_circuitBreaker != null)
                {
                    var stats = _circuitBreaker.GetStats();
                    _logger.LogDebug("🔧 [PHASE2] サーキットブレーカー統計 - State: {State}, FailureRate: {FailureRate:P2}, TotalExecutions: {TotalExecutions}", 
                        _circuitBreaker.State, stats.FailureRate, stats.TotalExecutions);
                }
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
                
                // Phase2: サーキットブレーカーのリセット
                if (_circuitBreaker != null)
                {
                    _circuitBreaker.Reset();
                    _logger.LogInformation("🔧 [PHASE2] サーキットブレーカーをリセット - サーバー復旧完了");
                }
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
            // 🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイルベースの既存プロセス終了
            await TerminateExistingServersByPidFileAsync();
            
            if (_managedServerProcess != null && !_managedServerProcess.HasExited)
            {
                _managedServerProcess.Kill();
                _managedServerProcess.WaitForExit(3000);
                _logger.LogInformation("🔄 既存サーバープロセス終了完了");
            }
            
            // 🚨 [CRITICAL_FIX] Python翻訳サーバーの完全終了（プロセス重複防止）
            await TerminateAllTranslationServerProcessesAsync();
            
            await Task.Delay(1000); // プロセス終了後の安定化待機
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 既存プロセス終了時にエラー");
        }
    }
    
    /// <summary>
    /// 🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイルベースの既存プロセス終了
    /// </summary>
    private async Task TerminateExistingServersByPidFileAsync()
    {
        try
        {
            if (File.Exists(PidFilePath))
            {
                var pidText = await File.ReadAllTextAsync(PidFilePath).ConfigureAwait(false);
                if (int.TryParse(pidText.Trim(), out var existingPid))
                {
                    try
                    {
                        var existingProcess = Process.GetProcessById(existingPid);
                        if (!existingProcess.HasExited)
                        {
                            _logger.LogWarning("🔄 [PROCESS_DUPLICATION_PREVENTION] 既存のPython翻訳サーバー終了: PID {ProcessId}", existingPid);
                            existingProcess.Kill();
                            existingProcess.WaitForExit(3000);
                        }
                    }
                    catch (ArgumentException)
                    {
                        // プロセスが既に存在しない場合は正常
                        _logger.LogDebug("PIDファイル内のプロセス (PID: {ProcessId}) は既に終了済み", existingPid);
                    }
                }
                
                // PIDファイル削除
                File.Delete(PidFilePath);
                _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイル削除完了");
            }
            
            // ロックファイルも削除
            if (File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
                _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] ロックファイル削除完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ PIDファイルベースの既存プロセス終了時にエラー");
        }
    }
    
    /// <summary>
    /// 🚨 [CRITICAL_FIX] 全てのPython翻訳サーバープロセスの完全終了
    /// 🔧 [GEMINI_REVIEW] ポート使用状況ベースの確実なプロセス特定
    /// </summary>
    private async Task TerminateAllTranslationServerProcessesAsync()
    {
        try
        {
            // 🔧 [GEMINI_REVIEW] ポート5556を使用するプロセスIDを特定
            var processIdsUsingPort = await GetProcessIdsUsingPortAsync(_currentServerPort);
            var terminatedCount = 0;
            
            foreach (var pid in processIdsUsingPort)
            {
                try
                {
                    var process = Process.GetProcessById(pid);
                    if (!process.HasExited && process.ProcessName.Equals("python", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("🔄 [PROCESS_CLEANUP] ポート{Port}使用Python翻訳サーバープロセス終了: PID {ProcessId}", 
                            _currentServerPort, process.Id);
                        process.Kill();
                        process.WaitForExit(2000);
                        terminatedCount++;
                    }
                }
                catch (ArgumentException)
                {
                    // プロセスが既に存在しない場合は正常
                    _logger.LogDebug("ポート使用プロセス (PID: {ProcessId}) は既に終了済み", pid);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("プロセス終了時にエラー (PID {ProcessId}): {Error}", pid, ex.Message);
                }
            }
            
            // 🔧 [GEMINI_REVIEW] フォールバック: プロセス名ベースの確認（非推奨だが保険）
            if (terminatedCount == 0)
            {
                await TerminateByProcessNameFallbackAsync();
            }
            
            if (terminatedCount > 0)
            {
                _logger.LogInformation("✅ [PROCESS_CLEANUP] Python翻訳サーバープロセス終了完了: {Count}個", terminatedCount);
                await Task.Delay(2000); // 複数プロセス終了後の安定化待機
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ 全Python翻訳サーバープロセス終了時にエラー");
        }
    }
    
    /// <summary>
    /// 🔧 [GEMINI_REVIEW] 指定ポートを使用するプロセスIDを取得
    /// </summary>
    private async Task<List<int>> GetProcessIdsUsingPortAsync(int port)
    {
        var processIds = new List<int>();
        
        try
        {
            // netstat -ano コマンドでポート使用状況を取得
            var startInfo = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();
                
                var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    if (line.Contains($":{port} ") && line.Contains("LISTENING"))
                    {
                        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                        {
                            processIds.Add(pid);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("ポート使用プロセス取得エラー: {Error}", ex.Message);
        }
        
        return processIds;
    }
    
    /// <summary>
    /// 🔧 [GEMINI_REVIEW] フォールバック: プロセス名ベースの終了（非推奨）
    /// </summary>
    private async Task TerminateByProcessNameFallbackAsync()
    {
        try
        {
            var processes = Process.GetProcessesByName("python");
            var terminatedCount = 0;
            
            foreach (var process in processes)
            {
                try
                {
                    // 簡易判定: Pythonプロセス全体から翻訳サーバーを推定
                    _logger.LogWarning("🔄 [FALLBACK] Python프로세ス終了 (推定翻訳サーバー): PID {ProcessId}", process.Id);
                    process.Kill();
                    process.WaitForExit(2000);
                    terminatedCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("フォールバックプロセス終了時にエラー (PID {ProcessId}): {Error}", process.Id, ex.Message);
                }
                finally
                {
                    process.Dispose();
                }
            }
            
            if (terminatedCount > 0)
            {
                _logger.LogInformation("✅ [FALLBACK] Python프로세ス終了完了: {Count}個", terminatedCount);
                await Task.Delay(2000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ フォールバックプロセス終了時にエラー");
        }
    }

    /// <summary>
    /// 新しいサーバーの起動
    /// </summary>
    private async Task<bool> StartNewServerAsync()
    {
        try
        {
            // 🔧 [PROCESS_DUPLICATION_PREVENTION] 重複起動防止チェック
            if (!await AcquireServerLockAsync())
            {
                _logger.LogWarning("⚠️ [PROCESS_DUPLICATION_PREVENTION] 他のサーバーインスタンスが既に動作中のため起動をスキップ");
                return false;
            }
            
            var pythonPath = "py"; // Windows Python Launcher使用
            
            // 🎯 [NLLB-200] モデル設定に基づくサーバースクリプト選択
            string serverScriptPath;
            var defaultEngine = _cachedSettings?.DefaultEngine ?? TranslationEngine.NLLB200;
            
            if (defaultEngine == TranslationEngine.NLLB200)
            {
                // NLLB-200サーバー使用
                serverScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                    @"..\..\..\..\scripts\nllb_translation_server.py");
                    
                if (!File.Exists(serverScriptPath))
                {
                    serverScriptPath = @"scripts\nllb_translation_server.py";
                }
                
                // NLLB-200用のポート設定
                _currentServerPort = 5556;
                
                _logger.LogInformation("🎯 [NLLB-200] NLLB-200高品質翻訳サーバーを起動: {ScriptPath} Port:{Port}", serverScriptPath, _currentServerPort);
            }
            else
            {
                // 従来のOPUS-MTサーバー使用
                serverScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, 
                    @"..\..\..\..\scripts\optimized_translation_server.py");
                    
                if (!File.Exists(serverScriptPath))
                {
                    serverScriptPath = @"scripts\optimized_translation_server.py";
                }
                
                _logger.LogInformation("🔧 [OPUS-MT] 従来の翻訳サーバーを起動: {ScriptPath}", serverScriptPath);
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
            
            // 🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイル作成
            await CreatePidFileAsync(_managedServerProcess.Id);
            
            _logger.LogInformation("🚀 [PROCESS_DUPLICATION_PREVENTION] 新しいサーバー起動開始 - PID: {ProcessId}, Port: {Port}",
                _managedServerProcess.Id, _currentServerPort);
            
            // 起動完了待機（タイムアウト付き）
            var startupTask = WaitForServerStartupAsync();
            var timeoutTask = Task.Delay(_cachedSettings?.ServerStartupTimeoutMs ?? 30000);
            
            var completedTask = await Task.WhenAny(startupTask, timeoutTask);
            
            if (completedTask == startupTask)
            {
                var success = await startupTask;
                if (success)
                {
                    _logger.LogInformation("✅ [PROCESS_DUPLICATION_PREVENTION] サーバー起動成功 - PID: {ProcessId}", _managedServerProcess.Id);
                }
                return success;
            }
            else
            {
                _logger.LogError("❌ サーバー起動タイムアウト ({TimeoutMs}ms)", _cachedSettings?.ServerStartupTimeoutMs ?? 30000);
                await CleanupPidFileAsync(); // タイムアウト時のクリーンアップ
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ サーバー起動エラー");
            await CleanupPidFileAsync(); // エラー時のクリーンアップ
            return false;
        }
    }
    
    /// <summary>
    /// 🔧 [PROCESS_DUPLICATION_PREVENTION] サーバーロック取得
    /// </summary>
    private async Task<bool> AcquireServerLockAsync()
    {
        try
        {
            var lockFileDir = Path.GetDirectoryName(LockFilePath);
            if (!Directory.Exists(lockFileDir))
            {
                Directory.CreateDirectory(lockFileDir!);
            }
            
            // ロックファイルが存在する場合は既に他のインスタンスが動作中
            if (File.Exists(LockFilePath))
            {
                // ロックファイルの内容をチェック（古いロックファイルかどうか）
                var lockContent = await File.ReadAllTextAsync(LockFilePath).ConfigureAwait(false);
                var lines = lockContent.Split('\n');
                
                if (lines.Length >= 2 && 
                    int.TryParse(lines[0], out var lockedPid) &&
                    DateTime.TryParse(lines[1], out var lockTime))
                {
                    // 1時間以上古いロックファイルは無効とみなす
                    if (DateTime.UtcNow - lockTime > TimeSpan.FromHours(1))
                    {
                        _logger.LogWarning("⚠️ [PROCESS_DUPLICATION_PREVENTION] 古いロックファイルを削除: {LockFilePath}", LockFilePath);
                        File.Delete(LockFilePath);
                    }
                    else
                    {
                        // プロセスが実際に動作中かチェック
                        try
                        {
                            var lockProcess = Process.GetProcessById(lockedPid);
                            if (!lockProcess.HasExited)
                            {
                                return false; // 他のインスタンスが動作中
                            }
                        }
                        catch (ArgumentException)
                        {
                            // プロセスが存在しない場合はロックファイル削除
                            File.Delete(LockFilePath);
                        }
                    }
                }
            }
            
            // ロックファイル作成
            var newLockContent = $"{Environment.ProcessId}\n{DateTime.UtcNow:O}";
            await File.WriteAllTextAsync(LockFilePath, newLockContent).ConfigureAwait(false);
            
            _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] サーバーロック取得成功: {LockFilePath}", LockFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ サーバーロック取得時にエラー: {LockFilePath}", LockFilePath);
            return false;
        }
    }
    
    /// <summary>
    /// 🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイル作成
    /// </summary>
    private async Task CreatePidFileAsync(int processId)
    {
        try
        {
            var pidFileDir = Path.GetDirectoryName(PidFilePath);
            if (!Directory.Exists(pidFileDir))
            {
                Directory.CreateDirectory(pidFileDir!);
            }
            
            await File.WriteAllTextAsync(PidFilePath, processId.ToString()).ConfigureAwait(false);
            _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイル作成: {PidFilePath} (PID: {ProcessId})", PidFilePath, processId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ PIDファイル作成時にエラー: {PidFilePath}", PidFilePath);
        }
    }
    
    /// <summary>
    /// 🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイルクリーンアップ
    /// </summary>
    private async Task CleanupPidFileAsync()
    {
        try
        {
            if (File.Exists(PidFilePath))
            {
                File.Delete(PidFilePath);
                _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] PIDファイルクリーンアップ完了");
            }
            
            if (File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
                _logger.LogDebug("🔧 [PROCESS_DUPLICATION_PREVENTION] ロックファイルクリーンアップ完了");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ PIDファイルクリーンアップ時にエラー");
        }
        
        await Task.CompletedTask;
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
            CurrentServerPort = _currentServerPort,
            // Phase2: サーキットブレーカー統計情報
            CircuitBreakerState = _circuitBreaker?.State,
            CircuitBreakerStats = _circuitBreaker?.GetStats()
        };
    }

    /// <summary>
    /// 🔧 [GEMINI_REVIEW] IAsyncDisposableパターンによるデッドロック防止
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        
        _disposed = true;
        _healthCheckTimer?.Dispose();
        _restartLock?.Dispose();
        
        // 🔧 [GEMINI_REVIEW] 非同期クリーンアップによるデッドロック防止
        try
        {
            await CleanupPidFileAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ DisposeAsync時のPIDファイルクリーンアップエラー");
        }
        
        if (_managedServerProcess != null && !_managedServerProcess.HasExited)
        {
            try
            {
                _managedServerProcess.Kill();
                _managedServerProcess.WaitForExit(3000);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ DisposeAsync時のプロセス終了エラー");
            }
            finally
            {
                _managedServerProcess?.Dispose();
            }
        }
        
        GC.SuppressFinalize(this);
    }
    
    /// <summary>
    /// 🔧 [GEMINI_REVIEW] 同期Disposeパターンの保持（後方互換性）
    /// </summary>
    public void Dispose()
    {
        // CA2012対応：ValueTaskを直接同期的に待機
        var disposeTask = DisposeAsync();
        if (disposeTask.IsCompleted)
        {
            // 既に完了している場合はGetAwaiter().GetResult()でOK
            disposeTask.GetAwaiter().GetResult();
        }
        else
        {
            // 未完了の場合はAsTask()でTask変換してから待機
            disposeTask.AsTask().ConfigureAwait(false).GetAwaiter().GetResult();
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
    
    // Phase2: サーキットブレーカー統計情報
    public CircuitBreakerState? CircuitBreakerState { get; init; }
    public CircuitBreakerStats? CircuitBreakerStats { get; init; }
}