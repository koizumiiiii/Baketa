using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.Server;
using Baketa.Core.Events;
using Baketa.Core.Settings;
using Baketa.Ocr.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// Issue #292: Surya OCR AIサーバー管理
/// [Issue #458] 翻訳はC# OnnxTranslationEngineに移行済み。OCRのみを管理。
/// </summary>
public sealed class UnifiedServerManager : IUnifiedAIServerManager
{
    // [Phase 3 Review Fix] 定数を削除し、UnifiedServerSettingsからの設定値を使用
    /// <summary>孤立プロセスKillタイムアウト（秒） - 設定に含めるほど重要でないため定数として残す</summary>
    private const int ProcessKillTimeoutSeconds = 5;

    private readonly UnifiedServerSettings _settings;
    private readonly ILogger<UnifiedServerManager> _logger;
    private readonly IEventAggregator? _eventAggregator;
    private Process? _serverProcess;
    private ProcessJobObject? _jobObject;
    private bool _isReady;
    private bool _disposed;
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private string? _serverVersion;

    /// <summary>
    /// サーバーが準備完了かどうか
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// [Issue #366] サーバーバージョン（gRPCで取得）
    /// </summary>
    public string? ServerVersion => _serverVersion;

    /// <summary>
    /// [Issue #366] アプリバージョンとサーバーバージョンが不一致かどうか
    /// 不一致の場合、コンポーネントの再ダウンロードが必要
    /// </summary>
    public bool IsVersionMismatch
    {
        get
        {
            if (string.IsNullOrEmpty(_serverVersion)) return false;
            var appVersion = GetAppVersion();
            return !string.Equals(appVersion, _serverVersion, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// サーバーポート
    /// </summary>
    public int Port => _settings.Port;

    /// <summary>
    /// 統合サーバーが利用可能かどうか（exe/Pythonスクリプトが存在するか）
    /// </summary>
    public bool IsAvailable => ResolveServerExecutable().executablePath != null;

    /// <summary>
    /// 統合サーバーマネージャーを初期化
    /// </summary>
    /// <param name="settings">統合サーバー設定</param>
    /// <param name="logger">ロガー</param>
    /// <param name="eventAggregator">イベントアグリゲーター（任意）</param>
    public UnifiedServerManager(
        UnifiedServerSettings settings,
        ILogger<UnifiedServerManager> logger,
        IEventAggregator? eventAggregator = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _settings = settings;
        _logger = logger;
        _eventAggregator = eventAggregator;

        // Issue #189: Job Object初期化 - ゾンビプロセス対策
        _jobObject = new ProcessJobObject(logger);
        _logger.LogDebug("[UnifiedServer] Job Object初期化: IsValid={IsValid}, Port={Port}, StartupTimeout={StartupTimeout}s",
            _jobObject.IsValid, _settings.Port, _settings.StartupTimeoutSeconds);
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
                _logger.LogInformation("♻️ [UnifiedServer] 既存サーバー再利用: Port {Port}", _settings.Port);
                return true;
            }

            // 孤立プロセスの強制終了
            await KillOrphanedProcessAsync().ConfigureAwait(false);

            // 実行ファイルを解決（初回ダウンロード中は待機）
            var (executablePath, arguments, workingDir, isExeMode) = await ResolveServerExecutableWithRetryAsync(cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogError("❌ [UnifiedServer] サーバー実行ファイルが見つかりません（待機タイムアウト）");
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

            // 準備完了を待機（タイムアウト: 設定値秒 - OCR+翻訳両方のモデルロード）
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_settings.StartupTimeoutSeconds));

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
                    _logger.LogInformation("✅ [UnifiedServer] 統合AIサーバー準備完了: Port {Port}", _settings.Port);
                    return true;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("❌ [UnifiedServer] サーバー起動タイムアウト（{Timeout}秒）", _settings.StartupTimeoutSeconds);
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
            await _serverProcess.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(_settings.StopTimeoutSeconds)).ConfigureAwait(false);
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
            var serverAddress = $"http://127.0.0.1:{_settings.Port}";

            using var channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
            {
                HttpHandler = new System.Net.Http.SocketsHttpHandler
                {
                    ConnectTimeout = TimeSpan.FromSeconds(_settings.HealthCheckTimeoutSeconds)
                }
            });

            var client = new OcrService.OcrServiceClient(channel);

            // [Issue #458] gRPC IsReady RPCを呼び出し（OCRサービス経由）
            var response = await client.IsReadyAsync(
                new OcrIsReadyRequest(),
                deadline: DateTime.UtcNow.AddSeconds(_settings.HealthCheckTimeoutSeconds),
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            // [Issue #366] サーバーバージョンを取得
            if (response.Details.TryGetValue("server_version", out var serverVersion))
            {
                _serverVersion = serverVersion;
                var appVersion = GetAppVersion();
                var versionMatch = string.Equals(appVersion, serverVersion, StringComparison.OrdinalIgnoreCase);

                _logger.LogDebug(
                    "[UnifiedServer] gRPCヘルスチェック結果: IsReady={IsReady}, Status={Status}, ServerVersion={ServerVersion}, AppVersion={AppVersion}, VersionMatch={VersionMatch}",
                    response.IsReady,
                    response.Status,
                    serverVersion,
                    appVersion,
                    versionMatch);

                if (!versionMatch)
                {
                    _logger.LogWarning(
                        "[UnifiedServer] バージョン不一致検出: Server={ServerVersion}, App={AppVersion}. コンポーネント再ダウンロードが必要です。",
                        serverVersion,
                        appVersion);
                }
            }
            else
            {
                _logger.LogDebug(
                    "[UnifiedServer] gRPCヘルスチェック結果: IsReady={IsReady}, Status={Status}",
                    response.IsReady,
                    response.Status);
            }

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
    /// サーバー実行ファイルを解決（初回ダウンロード中はリトライ待機）
    /// [Issue #292] 初回ダウンロード時のタイミング問題対策
    /// [Gemini Review] 例外処理・CancellationTokenチェック追加
    /// </summary>
    private async Task<(string? executablePath, string arguments, string workingDir, bool isExeMode)> ResolveServerExecutableWithRetryAsync(CancellationToken cancellationToken)
    {
        // まず即座に解決を試みる
        var result = ResolveServerExecutable();
        if (!string.IsNullOrEmpty(result.executablePath))
        {
            return result;
        }

        // exe版のパスを取得（待機対象）
        var exePath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaUnifiedServer", "BaketaUnifiedServer.exe");
        var exeDir = Path.GetDirectoryName(exePath)!;
        var parentDir = Path.GetDirectoryName(exeDir);

        // [Issue #360] ダウンロード/展開完了判定を改善
        // [Gemini Review] パスを設定から取得（一元管理）
        var metadataPath = _settings.GetMetadataFilePath();
        var metadataExists = File.Exists(metadataPath);

        // [Gemini Review] ダウンロード中の判定を堅牢に（try-catch追加）
        // [Fix] exe未検出 + grpc_serverディレクトリ存在 → メタデータ有無に関わらずダウンロード/展開中と判断
        // 理由: 旧バージョンからのアップグレード時にメタデータだけ残存し、exeが新パスに未配置のケースに対応
        var isDownloadInProgress = false;
        try
        {
            if (parentDir != null && Directory.Exists(parentDir))
            {
                // exe未検出 + grpc_serverディレクトリ存在 → ダウンロード/展開中と判断
                isDownloadInProgress = true;

                if (!metadataExists)
                {
                    _logger.LogInformation("[Fix] メタデータ未作成 + grpc_serverディレクトリ存在 → 初回ダウンロード中と判断");
                }
                else
                {
                    _logger.LogInformation("[Fix] メタデータ存在するがexe未検出 → 展開中または再インストール中と判断して待機開始");
                }
            }
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or UnauthorizedAccessException or IOException)
        {
            _logger.LogWarning(ex, "[UnifiedServer] ダウンロード状況の確認中に例外が発生しました。待機せずに続行します。");
            return (null, "", "", false);
        }

        if (!isDownloadInProgress)
        {
            _logger.LogDebug("[UnifiedServer] ダウンロード中の兆候なし - 即座にnullを返す (メタデータ: {MetadataExists})", metadataExists);
            return result;
        }

        // 初回ダウンロード待機（最大10分、5秒間隔でポーリング）
        const int maxWaitMinutes = 10;
        const int pollIntervalSeconds = 5;
        // [Gemini Review] マジックナンバーを定数化（30秒ごとにログ出力）
        const int logIntervalSeconds = 30;
        const int attemptsPerLog = logIntervalSeconds / pollIntervalSeconds;
        var maxAttempts = (maxWaitMinutes * 60) / pollIntervalSeconds;

        _logger.LogInformation("⏳ [UnifiedServer] 初回ダウンロード待機開始（最大{MaxWait}分）", maxWaitMinutes);
        _logger.LogInformation("  待機対象: {Path}", exePath);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            // [Gemini Review] ループ先頭でキャンセルチェック
            cancellationToken.ThrowIfCancellationRequested();

            await Task.Delay(TimeSpan.FromSeconds(pollIntervalSeconds), cancellationToken).ConfigureAwait(false);

            result = ResolveServerExecutable();
            if (!string.IsNullOrEmpty(result.executablePath))
            {
                _logger.LogInformation("✅ [UnifiedServer] 実行ファイル検出（{Attempt}回目のチェック、{Elapsed}秒後）",
                    attempt, attempt * pollIntervalSeconds);
                return result;
            }

            // 進捗ログ（logIntervalSecondsごと）
            if (attempt % attemptsPerLog == 0)
            {
                var elapsed = attempt * pollIntervalSeconds;
                _logger.LogInformation("⏳ [UnifiedServer] ダウンロード待機中... {Elapsed}秒経過（最大{MaxWait}分）",
                    elapsed, maxWaitMinutes);
            }
        }

        _logger.LogWarning("⚠️ [UnifiedServer] ダウンロード待機タイムアウト（{MaxWait}分）", maxWaitMinutes);
        return (null, "", "", false);
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
            return (exePath, $"--port {_settings.Port}", Path.GetDirectoryName(exePath)!, true);
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
                return (pythonPath, $"\"{scriptPath}\" --port {_settings.Port}", grpcServerDir, false);
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

        // pyenv Python（.python-versionから動的にバージョンを取得）
        var pyenvPython = FindPyenvPython(projectRoot);
        if (pyenvPython != null)
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
    /// pyenv Python実行ファイルを.python-versionから動的に探索
    /// [Gemini Review Fix] バージョン番号のハードコードを排除
    /// </summary>
    private static string? FindPyenvPython(string? projectRoot)
    {
        // .python-versionファイルからバージョンを読み取り
        string? pythonVersion = null;

        // プロジェクトルートの.python-versionを優先
        if (projectRoot != null)
        {
            var projectVersionFile = Path.Combine(projectRoot, ".python-version");
            if (File.Exists(projectVersionFile))
            {
                pythonVersion = File.ReadAllText(projectVersionFile).Trim();
            }
        }

        // ユーザーホームの.python-versionをフォールバック
        if (string.IsNullOrEmpty(pythonVersion))
        {
            var userVersionFile = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".python-version");
            if (File.Exists(userVersionFile))
            {
                pythonVersion = File.ReadAllText(userVersionFile).Trim();
            }
        }

        if (string.IsNullOrEmpty(pythonVersion))
        {
            return null;
        }

        // pyenvパスを構築
        var pyenvPython = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".pyenv", "pyenv-win", "versions", pythonVersion, "python.exe");

        return File.Exists(pyenvPython) ? pyenvPython : null;
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
    /// [Gemini Review Fix] netstatコマンドからWindows API (GetExtendedTcpTable)に置換
    /// </summary>
    private async Task KillOrphanedProcessAsync()
    {
        try
        {
            _logger.LogDebug("[UnifiedServer] 孤立プロセスチェック: Port {Port}", _settings.Port);

            // Windows APIを使用してTCP接続テーブルを取得
            var pid = GetListeningProcessId(_settings.Port);

            if (pid > 0)
            {
                _logger.LogWarning("⚠️ [UnifiedServer] 孤立プロセス検出: PID {Pid}", pid);
                await TryKillProcessAsync(pid).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [UnifiedServer] 孤立プロセスチェックエラー");
        }
    }

    #region Windows API P/Invoke for TCP Table

    /// <summary>
    /// 指定ポートでLISTEN中のプロセスIDを取得
    /// [Gemini Review Fix] netstatコマンドを廃止し、Windows API直接呼び出しに置換
    /// </summary>
    private static int GetListeningProcessId(int port)
    {
        // .NET の IPGlobalProperties を使用（内部でGetExtendedTcpTableを呼び出す）
        // これによりP/Invoke宣言を最小限に抑えつつ、netstat依存を排除
        try
        {
            var properties = IPGlobalProperties.GetIPGlobalProperties();
            var connections = properties.GetActiveTcpListeners();

            // GetActiveTcpListeners はプロセスIDを返さないため、
            // GetExtendedTcpTable を直接使用する必要がある
            return GetListeningProcessIdFromExtendedTable(port);
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// GetExtendedTcpTable APIを使用してLISTEN中のプロセスIDを取得
    /// </summary>
    private static int GetListeningProcessIdFromExtendedTable(int port)
    {
        const int AF_INET = 2; // IPv4
        const int TCP_TABLE_OWNER_PID_LISTENER = 3;
        const int MIB_TCP_STATE_LISTEN = 2;

        var bufferSize = 0;
        var result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);

        var tcpTablePtr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            result = GetExtendedTcpTable(tcpTablePtr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (result != 0)
            {
                return -1;
            }

            // テーブルのエントリ数を取得
            var numEntries = Marshal.ReadInt32(tcpTablePtr);
            var rowPtr = IntPtr.Add(tcpTablePtr, 4); // dwNumEntries(4バイト)をスキップ

            for (var i = 0; i < numEntries; i++)
            {
                var row = Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr);

                // ローカルポートはネットワークバイトオーダー（ビッグエンディアン）
                var localPort = ((row.dwLocalPort & 0xFF) << 8) | ((row.dwLocalPort >> 8) & 0xFF);

                if (row.dwState == MIB_TCP_STATE_LISTEN && localPort == port)
                {
                    return (int)row.dwOwningPid;
                }

                rowPtr = IntPtr.Add(rowPtr, Marshal.SizeOf<MIB_TCPROW_OWNER_PID>());
            }

            return -1;
        }
        finally
        {
            Marshal.FreeHGlobal(tcpTablePtr);
        }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern int GetExtendedTcpTable(
        IntPtr pTcpTable,
        ref int dwOutBufLen,
        bool sort,
        int ipVersion,
        int tblClass,
        int reserved);

    [StructLayout(LayoutKind.Sequential)]
    private struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    #endregion

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
            $"Port:{_settings.Port}",
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

    /// <summary>
    /// [Issue #366] アプリケーションバージョンを取得
    /// MinVerはAssemblyInformationalVersionAttributeにバージョンを設定
    /// </summary>
    private static string GetAppVersion()
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(infoVersion))
        {
            // "+metadata" サフィックスを除去（例: "0.2.41+abc123" → "0.2.41"）
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }

        // フォールバック: AssemblyName.Version
        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }
}
