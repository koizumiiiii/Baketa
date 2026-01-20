using System.Diagnostics;
using System.IO;
using System.Text;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Server;
using Baketa.Core.Events;
using Baketa.Translation.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Issue #292: OCR+翻訳統合AIサーバー管理
/// OCRと翻訳を単一プロセスで実行することでVRAMを削減
/// </summary>
public sealed class UnifiedServerManager : IUnifiedAIServerManager
{
    // [Gemini Review Fix] 設定の外部化 - マジックナンバーを定数化
    /// <summary>サーバー起動タイムアウト（秒）- OCR+翻訳両方のモデルロード時間</summary>
    private const int StartupTimeoutSeconds = 300;

    /// <summary>サーバー停止タイムアウト（秒）</summary>
    private const int StopTimeoutSeconds = 10;

    /// <summary>gRPCヘルスチェックタイムアウト（秒）</summary>
    private const int HealthCheckTimeoutSeconds = 5;

    /// <summary>孤立プロセスKillタイムアウト（秒）</summary>
    private const int ProcessKillTimeoutSeconds = 5;

    private readonly ILogger<UnifiedServerManager> _logger;
    private readonly IEventAggregator? _eventAggregator;
    private readonly int _port;
    private Process? _serverProcess;
    private ProcessJobObject? _jobObject;
    private bool _isReady;
    private bool _disposed;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    /// <summary>
    /// サーバーが準備完了かどうか
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// サーバーポート
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// 統合サーバーが利用可能かどうか（exe/Pythonスクリプトが存在するか）
    /// </summary>
    public bool IsAvailable => ResolveServerExecutable().executablePath != null;

    public UnifiedServerManager(
        int port,
        ILogger<UnifiedServerManager> logger,
        IEventAggregator? eventAggregator = null)
    {
        _port = port;
        _logger = logger;
        _eventAggregator = eventAggregator;

        // Issue #189: Job Object初期化 - ゾンビプロセス対策
        _jobObject = new ProcessJobObject(logger);
        _logger.LogDebug("[UnifiedServer] Job Object初期化: IsValid={IsValid}", _jobObject.IsValid);
    }

    /// <summary>
    /// 統合サーバーを起動し、準備完了まで待機
    /// </summary>
    public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isReady && _serverProcess is { HasExited: false })
            {
                _logger.LogInformation("♻️ [UnifiedServer] 既存サーバー再利用: Port {Port}", _port);
                return true;
            }

            // 孤立プロセスの強制終了
            await KillOrphanedProcessAsync().ConfigureAwait(false);

            var (executablePath, arguments, workingDir, isExeMode) = ResolveServerExecutable();

            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogError("❌ [UnifiedServer] サーバー実行ファイルが見つかりません");
                return false;
            }

            _logger.LogInformation("🚀 [UnifiedServer] 統合AIサーバー起動開始");
            _logger.LogInformation("  実行ファイル: {Executable}", executablePath);
            _logger.LogInformation("  引数: {Args}", arguments);
            _logger.LogInformation("  モード: {Mode}", isExeMode ? "exe（配布版）" : "Python（開発版）");
            _logger.LogInformation("  WorkingDir: {Dir}", workingDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Python版の場合のみ環境変数設定
            if (!isExeMode)
            {
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";
                startInfo.Environment["TOKENIZERS_PARALLELISM"] = "false";
            }

            _serverProcess = new Process { StartInfo = startInfo };

            var readyTcs = new TaskCompletionSource<bool>();
            var errorOutput = new List<string>();

            _serverProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;

                _logger.LogDebug("[UnifiedServer-stdout] {Data}", e.Data);

                // 準備完了検出
                CheckForReadyMessage(e.Data, readyTcs);
            };

            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;

                // [Issue #264] メモリエラー検出
                DetectAndPublishServerError(e.Data);

                // [SERVER_START]検出
                if (e.Data.Contains("[SERVER_START]"))
                {
                    _logger.LogInformation("✅ [UnifiedServer] [SERVER_START]検出");
                    if (!readyTcs.Task.IsCompleted)
                    {
                        readyTcs.TrySetResult(true);
                    }
                }

                // ログレベル分類
                if (e.Data.Contains("Error") || e.Data.Contains("Exception") || e.Data.Contains("Traceback"))
                {
                    _logger.LogError("[UnifiedServer-stderr] {Data}", e.Data);
                    errorOutput.Add(e.Data);
                }
                else if (e.Data.Contains("Warning") || e.Data.Contains("WARN"))
                {
                    _logger.LogWarning("[UnifiedServer-stderr] {Data}", e.Data);
                }
                else
                {
                    _logger.LogDebug("[UnifiedServer-stderr] {Data}", e.Data);
                }
            };

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _logger.LogInformation("✅ [UnifiedServer] プロセス起動完了 (PID: {PID})", _serverProcess.Id);

            // Issue #189: プロセスをJob Objectに関連付け
            if (_jobObject?.AssignProcess(_serverProcess) == true)
            {
                _logger.LogInformation("✅ [UnifiedServer] Job Object関連付け成功: PID={PID}", _serverProcess.Id);
            }

            // 準備完了を待機（タイムアウト: 300秒 - OCR+翻訳両方のモデルロード）
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(StartupTimeoutSeconds));

            try
            {
                var readyTask = readyTcs.Task;
                var completedTask = await Task.WhenAny(
                    readyTask,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)
                ).ConfigureAwait(false);

                if (completedTask == readyTask && await readyTask.ConfigureAwait(false))
                {
                    _isReady = true;
                    _logger.LogInformation("✅ [UnifiedServer] 統合AIサーバー準備完了: Port {Port}", _port);
                    return true;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("❌ [UnifiedServer] サーバー起動タイムアウト（{Timeout}秒）", StartupTimeoutSeconds);
            }

            // タイムアウトまたは失敗
            if (errorOutput.Count > 0)
            {
                _logger.LogError("❌ [UnifiedServer] 起動エラー: {Errors}", string.Join(Environment.NewLine, errorOutput));
            }

            await StopServerAsync().ConfigureAwait(false);
            return false;
        }
        finally
        {
            _startLock.Release();
        }
    }

    /// <summary>
    /// サーバー停止
    /// </summary>
    public async Task StopServerAsync()
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            _logger.LogDebug("[UnifiedServer] 停止対象プロセスなし");
            return;
        }

        _logger.LogInformation("🛑 [UnifiedServer] サーバー停止開始: PID {PID}", _serverProcess.Id);

        try
        {
            _serverProcess.Kill(entireProcessTree: true);
            await _serverProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(StopTimeoutSeconds)).ConfigureAwait(false);
            _logger.LogInformation("✅ [UnifiedServer] サーバー停止完了");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [UnifiedServer] サーバー停止エラー");
        }
        finally
        {
            _serverProcess?.Dispose();
            _serverProcess = null;
            _isReady = false;
        }
    }

    /// <summary>
    /// [Gemini Review Fix] gRPCでサーバーの準備状態を確認
    /// TCP接続チェックではなく、実際のgRPC IsReady RPCを使用
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>サーバーが準備完了の場合true</returns>
    public async Task<bool> CheckServerHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_serverProcess == null || _serverProcess.HasExited)
        {
            _logger.LogDebug("[UnifiedServer] ヘルスチェック: プロセスが存在しません");
            return false;
        }

        try
        {
            var serverAddress = $"http://127.0.0.1:{_port}";

            using var channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
            {
                HttpHandler = new System.Net.Http.SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(HealthCheckTimeoutSeconds)
                }
            });

            var client = new TranslationService.TranslationServiceClient(channel);

            // gRPC IsReady RPCを呼び出し
            var response = await client.IsReadyAsync(
                new IsReadyRequest(),
                deadline: DateTime.UtcNow.AddSeconds(HealthCheckTimeoutSeconds),
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            _logger.LogDebug(
                "[UnifiedServer] gRPCヘルスチェック結果: IsReady={IsReady}, Status={Status}",
                response.IsReady,
                response.Status);

            return response.IsReady;
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogWarning(ex, "[UnifiedServer] gRPCヘルスチェック失敗: StatusCode={StatusCode}", ex.StatusCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[UnifiedServer] ヘルスチェックエラー");
            return false;
        }
    }

    /// <summary>
    /// 準備完了メッセージ検出
    /// </summary>
    private void CheckForReadyMessage(string line, TaskCompletionSource<bool> tcs)
    {
        if (tcs.Task.IsCompleted) return;

        // "is running on" または "[SERVER_START]" で準備完了判定
        if (line.Contains("is running on") || line.Contains("[SERVER_START]"))
        {
            _logger.LogDebug("[UnifiedServer] 準備完了メッセージ検出: {Line}", line);
            tcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// サーバー実行ファイルを解決
    /// </summary>
    private (string? executablePath, string arguments, string workingDir, bool isExeMode) ResolveServerExecutable()
    {
        // 優先順位: 1. exe版（配布用） 2. Pythonスクリプト（開発用）

        // exe版チェック
        var exePath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaUnifiedServer", "BaketaUnifiedServer.exe");
        if (File.Exists(exePath))
        {
            _logger.LogInformation("✅ [UnifiedServer] exe版使用: {Path}", exePath);
            return (exePath, $"--port {_port}", Path.GetDirectoryName(exePath)!, true);
        }

        // Python版チェック（開発時）
        var grpcServerDir = Path.Combine(AppContext.BaseDirectory, "grpc_server");
        var scriptPath = Path.Combine(grpcServerDir, "unified_server.py");

        // grpc_serverディレクトリが見つからない場合はプロジェクトルートから探索
        if (!File.Exists(scriptPath))
        {
            var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
            if (projectRoot != null)
            {
                grpcServerDir = Path.Combine(projectRoot, "grpc_server");
                scriptPath = Path.Combine(grpcServerDir, "unified_server.py");
            }
        }

        if (File.Exists(scriptPath))
        {
            // Python実行ファイルを探索
            var pythonPath = FindPythonExecutable();
            if (pythonPath != null)
            {
                _logger.LogInformation("✅ [UnifiedServer] Python版使用: {Script} (Python: {Python})", scriptPath, pythonPath);
                return (pythonPath, $"\"{scriptPath}\" --port {_port}", grpcServerDir, false);
            }
        }

        _logger.LogWarning("⚠️ [UnifiedServer] 実行ファイルが見つかりません (exe: {ExePath}, script: {ScriptPath})", exePath, scriptPath);
        return (null, "", "", false);
    }

    /// <summary>
    /// Python実行ファイルを探索
    /// </summary>
    private string? FindPythonExecutable()
    {
        // .venv環境をチェック
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        if (projectRoot != null)
        {
            var venvPython = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
            if (File.Exists(venvPython))
            {
                return venvPython;
            }
        }

        // vendor Python
        var vendorPython = Path.Combine(AppContext.BaseDirectory, "vendor", "python", "python.exe");
        if (File.Exists(vendorPython))
        {
            return vendorPython;
        }

        // pyenv Python
        var pyenvPython = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pyenv", "pyenv-win", "versions", "3.10.9", "python.exe");
        if (File.Exists(pyenvPython))
        {
            return pyenvPython;
        }

        // システムPython
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where",
                Arguments = "python",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadLine();
                if (!string.IsNullOrEmpty(output) && File.Exists(output))
                {
                    return output;
                }
            }
        }
        catch { /* ignore */ }

        return null;
    }

    /// <summary>
    /// プロジェクトルートディレクトリを.slnファイルを基点に探索
    /// </summary>
    private static string? FindProjectRoot(string startPath)
    {
        var directory = new DirectoryInfo(startPath);
        while (directory != null && !directory.GetFiles("*.sln").Any())
        {
            directory = directory.Parent;
        }
        return directory?.FullName;
    }

    /// <summary>
    /// 孤立プロセスを強制終了
    /// [Gemini Review Fix] 競合状態対策: プロセス取得とKillの間でプロセスが終了する可能性に対応
    /// </summary>
    private async Task KillOrphanedProcessAsync()
    {
        try
        {
            _logger.LogDebug("[UnifiedServer] 孤立プロセスチェック: Port {Port}", _port);

            var netstatProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netstat",
                    Arguments = "-ano",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            netstatProcess.Start();
            var output = await netstatProcess.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await netstatProcess.WaitForExitAsync().ConfigureAwait(false);

            var lines = output.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains($":{_port}") && line.Contains("LISTENING"))
                {
                    var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length > 0 && int.TryParse(parts[^1], out var pid))
                    {
                        _logger.LogWarning("⚠️ [UnifiedServer] 孤立プロセス検出: PID {Pid}", pid);
                        await TryKillProcessAsync(pid).ConfigureAwait(false);
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [UnifiedServer] 孤立プロセスチェックエラー");
        }
    }

    /// <summary>
    /// [Gemini Review Fix] プロセス終了を安全に試行
    /// 競合状態に対応: プロセス取得からKillまでの間に終了する可能性を考慮
    /// </summary>
    private async Task TryKillProcessAsync(int pid)
    {
        // 許可されたプロセス名リスト
        string[] allowedProcessNames =
        [
            "python",
            "BaketaUnifiedServer",
            "BaketaTranslationServer",
            "BaketaSuryaOcrServer"
        ];

        try
        {
            // プロセス取得
            Process orphanProcess;
            try
            {
                orphanProcess = Process.GetProcessById(pid);
            }
            catch (ArgumentException)
            {
                // プロセスが既に終了している
                _logger.LogDebug("[UnifiedServer] PID {Pid} は既に終了しています", pid);
                return;
            }

            // プロセス名を取得（HasExitedチェック付き）
            string processName;
            try
            {
                if (orphanProcess.HasExited)
                {
                    _logger.LogDebug("[UnifiedServer] PID {Pid} は既に終了しています", pid);
                    return;
                }
                processName = orphanProcess.ProcessName;
            }
            catch (InvalidOperationException)
            {
                // プロセスが終了した
                _logger.LogDebug("[UnifiedServer] PID {Pid} は取得中に終了しました", pid);
                return;
            }

            // 許可されたプロセスのみ終了
            var isAllowed = allowedProcessNames.Any(name =>
                processName.Contains(name, StringComparison.OrdinalIgnoreCase));

            if (!isAllowed)
            {
                _logger.LogDebug("[UnifiedServer] PID {Pid} ({Name}) は許可リスト外のためスキップ", pid, processName);
                return;
            }

            _logger.LogInformation("🔥 [UnifiedServer] 孤立プロセス強制終了: PID {Pid}, Name {Name}", pid, processName);

            try
            {
                orphanProcess.Kill(entireProcessTree: true);
                await orphanProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(ProcessKillTimeoutSeconds)).ConfigureAwait(false);
                _logger.LogInformation("✅ [UnifiedServer] 孤立プロセス終了完了: PID {Pid}", pid);
            }
            catch (InvalidOperationException)
            {
                // プロセスが既に終了
                _logger.LogDebug("[UnifiedServer] PID {Pid} はKill中に終了しました", pid);
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("[UnifiedServer] PID {Pid} の終了待機がタイムアウト", pid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [UnifiedServer] プロセス終了失敗: PID {Pid}", pid);
        }
    }

    /// <summary>
    /// メモリエラー等を検出してイベント発行
    /// </summary>
    private void DetectAndPublishServerError(string line)
    {
        ServerErrorDetector.DetectAndPublish(
            line,
            ServerErrorSources.UnifiedServer,
            $"Port:{_port}",
            _eventAggregator,
            _logger);
    }

    /// <summary>
    /// 非同期破棄
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogInformation("🛑 [UnifiedServer] 非同期破棄開始");

        await StopServerAsync().ConfigureAwait(false);

        try
        {
            _startLock?.Dispose();
        }
        catch { /* ignore */ }

        try
        {
            _jobObject?.Dispose();
            _jobObject = null;
        }
        catch { /* ignore */ }

        _logger.LogInformation("✅ [UnifiedServer] 非同期破棄完了");

        GC.SuppressFinalize(this);
    }
}
