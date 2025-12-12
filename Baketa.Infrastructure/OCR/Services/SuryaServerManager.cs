using System.Diagnostics;
using System.IO;
using System.Text;
using Baketa.Infrastructure.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Services;

/// <summary>
/// Surya OCR gRPCサーバー管理
/// Issue #189: PythonServerManagerパターンを参考に実装
/// Issue #189: ゾンビプロセス対策 - Job Object統合
/// </summary>
public sealed class SuryaServerManager : IAsyncDisposable
{
    private readonly ILogger<SuryaServerManager> _logger;
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

    public SuryaServerManager(int port, ILogger<SuryaServerManager> logger)
    {
        _port = port;
        _logger = logger;

        // Issue #189: Job Object初期化 - ゾンビプロセス対策
        _jobObject = new ProcessJobObject(logger);
        _logger.LogDebug("[Surya] Job Object初期化: IsValid={IsValid}", _jobObject.IsValid);
    }

    /// <summary>
    /// Suryaサーバーを起動し、準備完了まで待機
    /// Issue #197: モデルダウンロード完了待機ロジック追加
    /// </summary>
    public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
    {
        await _startLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isReady && _serverProcess is { HasExited: false })
            {
                _logger.LogInformation("♻️ [Surya] 既存サーバー再利用: Port {Port}", _port);
                return true;
            }

            // Issue #197: モデルダウンロード完了を待機
            // ComponentDownloaderがモデルをダウンロード中の場合、完了まで待つ
            var modelReady = await WaitForSuryaModelAsync(cancellationToken).ConfigureAwait(false);
            if (!modelReady)
            {
                _logger.LogError("❌ [Surya] モデルファイルが見つかりません。ComponentDownloaderでのダウンロードをお待ちください。");
                return false;
            }

            // 孤立プロセスの強制終了
            await KillOrphanedProcessAsync().ConfigureAwait(false);

            // Issue #197: exe版とPython版の両対応
            // 優先順位: 1. exe版（配布用） 2. Pythonスクリプト（開発用）
            var (executablePath, arguments, workingDir, isExeMode) = ResolveServerExecutable();

            if (string.IsNullOrEmpty(executablePath))
            {
                _logger.LogError("❌ [Surya] サーバー実行ファイルが見つかりません（exe/Pythonいずれも）");
                return false;
            }

            _logger.LogInformation("🚀 [Surya] サーバー起動開始: {Executable} {Args}", executablePath, arguments);
            _logger.LogInformation("🔧 [Surya] 実行モード: {Mode}", isExeMode ? "exe（配布版）" : "Python（開発版）");
            _logger.LogInformation("🔧 [Surya] WorkingDir: {Dir}", workingDir);

            var startInfo = new ProcessStartInfo
            {
                FileName = executablePath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workingDir,
                // UTF-8エンコーディング明示設定（日本語Windows対応）
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Python版の場合のみ環境変数設定
            if (!isExeMode)
            {
                startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
                startInfo.Environment["PYTHONUNBUFFERED"] = "1";
            }

            // Issue #198: Surya 0.17.0はHuggingFaceから自動ダウンロードするため
            // BAKETA_SURYA_MODEL_DIR環境変数は設定しない
            // 以前のコードでは XDG_DATA_HOME上書きによりDetectionモデルのパスが壊れていた
            _logger.LogInformation("ℹ️ [Surya] Surya 0.17.0はHuggingFaceからモデルを自動ダウンロードします");

            _serverProcess = new Process { StartInfo = startInfo };

            var readyTcs = new TaskCompletionSource<bool>();
            var errorOutput = new List<string>();

            _serverProcess.OutputDataReceived += (_, e) =>
            {
                if (e.Data == null) return;

                _logger.LogDebug("[Surya-stdout] {Data}", e.Data);

                // gRPCサーバー起動完了を検出
                CheckForReadyMessage(e.Data, readyTcs);
            };

            _serverProcess.ErrorDataReceived += (_, e) =>
            {
                if (e.Data == null) return;

                // PyTorch/CUDA警告はDEBUGレベル
                if (e.Data.Contains("UserWarning") || e.Data.Contains("FutureWarning"))
                {
                    _logger.LogDebug("[Surya-stderr] {Data}", e.Data);
                }
                else
                {
                    _logger.LogDebug("[Surya-stderr] {Data}", e.Data);

                    // stderr からも準備完了を検出（Pythonのloggingはstderrに出力）
                    CheckForReadyMessage(e.Data, readyTcs);

                    // 致命的エラーのみ記録（一般的な出力は除外）
                    if ((e.Data.Contains("Error:") || e.Data.Contains("Exception:") ||
                         e.Data.Contains("Traceback") || e.Data.Contains("ModuleNotFoundError")) &&
                        !e.Data.Contains("WARNING") && !e.Data.Contains("INFO"))
                    {
                        errorOutput.Add(e.Data);
                    }
                }
            };

            _serverProcess.Start();
            _serverProcess.BeginOutputReadLine();
            _serverProcess.BeginErrorReadLine();

            _logger.LogInformation("✅ [Surya] プロセス起動完了 (PID: {PID})", _serverProcess.Id);

            // Issue #189: プロセスをJob Objectに関連付け（ゾンビプロセス対策）
            if (_jobObject?.AssignProcess(_serverProcess) == true)
            {
                _logger.LogInformation("✅ [Surya] Job Object関連付け成功: PID={PID}", _serverProcess.Id);
            }

            // 準備完了を待機（タイムアウト: 300秒 - 初回モデルダウンロード＋ロードに時間がかかる）
            // Issue #189: 120秒 → 300秒に延長（ユーザー報告: 4-5分かかるケースあり）
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(300));

            try
            {
                var readyTask = readyTcs.Task;
                var completedTask = await Task.WhenAny(
                    readyTask,
                    Task.Delay(Timeout.Infinite, timeoutCts.Token)
                ).ConfigureAwait(false);

                if (completedTask == readyTask && await readyTask.ConfigureAwait(false))
                {
                    _logger.LogInformation("✅ [Surya] サーバー準備完了: Port {Port}", _port);
                    return true;
                }
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogError("❌ [Surya] サーバー起動タイムアウト（300秒）");
            }

            // タイムアウトまたは失敗
            if (errorOutput.Count > 0)
            {
                _logger.LogError("❌ [Surya] サーバー起動エラー: {Errors}", string.Join("; ", errorOutput.Take(5)));
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
        if (_serverProcess == null) return;

        try
        {
            if (!_serverProcess.HasExited)
            {
                _logger.LogInformation("🛑 [Surya] サーバー停止中 (PID: {PID})", _serverProcess.Id);
                _serverProcess.Kill(entireProcessTree: true);
                await _serverProcess.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "⚠️ [Surya] サーバー停止時のエラー");
        }
        finally
        {
            _serverProcess.Dispose();
            _serverProcess = null;
            _isReady = false;
        }
    }

    /// <summary>
    /// サーバー準備完了メッセージを検出
    /// </summary>
    private void CheckForReadyMessage(string data, TaskCompletionSource<bool> readyTcs)
    {
        // 既に検出済みの場合はスキップ
        if (_isReady) return;

        // gRPCサーバー起動完了を検出（複数パターン対応）
        // 日本語パターンとASCIIパターンの両方をサポート
        var isReady =
            data.Contains("gRPCサーバー起動") ||        // 日本語ログ
            data.Contains("gRPC server started") ||     // 英語ログ
            data.Contains("Server started") ||          // 汎用
            data.Contains($"listening on [::]:{_port}") || // gRPC標準形式
            data.Contains($"listening on 0.0.0.0:{_port}") ||
            data.Contains($"(port: {_port})") ||        // Suryaサーバー形式
            data.Contains($"port={_port}");             // 代替形式

        if (isReady)
        {
            _logger.LogInformation("🎉 [Surya] サーバー準備完了検出: {Message}", data);
            _isReady = true;
            readyTcs.TrySetResult(true);
        }
    }

    /// <summary>
    /// Issue #197: サーバー実行ファイル解決（exe優先、Pythonフォールバック）
    /// </summary>
    /// <returns>(実行ファイルパス, 引数, 作業ディレクトリ, exeモードか)</returns>
    private (string? executablePath, string arguments, string workingDir, bool isExeMode) ResolveServerExecutable()
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;

        // 1. exe版を優先検索（配布用）
        var exePath = ResolveExePath(projectRoot);
        if (!string.IsNullOrEmpty(exePath))
        {
            var exeDir = Path.GetDirectoryName(exePath) ?? projectRoot;
            var arguments = $"--port {_port}";
            _logger.LogInformation("[Surya] exe版検出: {Path}", exePath);
            return (exePath, arguments, exeDir, true);
        }

        // 2. Pythonスクリプト版（開発用）
        var scriptPath = ResolveScriptPath();
        if (!string.IsNullOrEmpty(scriptPath))
        {
            var pythonPath = ResolvePythonPath();
            if (!string.IsNullOrEmpty(pythonPath))
            {
                var workingDir = Path.GetDirectoryName(scriptPath) ?? projectRoot;
                var arguments = $"-u \"{scriptPath}\" --port {_port}";
                _logger.LogInformation("[Surya] Python版使用: {Script}", scriptPath);
                return (pythonPath, arguments, workingDir, false);
            }
        }

        return (null, "", "", false);
    }

    /// <summary>
    /// Issue #197: exe版パス解決
    /// PyInstallerでビルドしたBaketaSuryaOcrServer.exeを検索
    /// </summary>
    private string? ResolveExePath(string projectRoot)
    {
        // 検索候補パス（優先順）
        var searchPaths = new[]
        {
            // 1. アプリ配布時: grpc_server/BaketaSuryaOcrServer/BaketaSuryaOcrServer.exe
            Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"),
            // 2. 開発時ビルド: grpc_server/dist/BaketaSuryaOcrServer/BaketaSuryaOcrServer.exe
            Path.Combine(projectRoot, "grpc_server", "dist", "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"),
            // 3. AppContext.BaseDirectory直下
            Path.Combine(AppContext.BaseDirectory, "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"),
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("[Surya] exe検出: {Path}", path);
                return path;
            }
        }

        _logger.LogDebug("[Surya] exe版なし - Python版にフォールバック");
        return null;
    }

    /// <summary>
    /// スクリプトパス解決
    /// </summary>
    private string? ResolveScriptPath()
    {
        // プロジェクトルート検索
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        if (string.IsNullOrEmpty(projectRoot))
        {
            projectRoot = Environment.CurrentDirectory;
        }

        var scriptPath = Path.Combine(projectRoot, "grpc_server", "ocr_server_surya.py");

        if (File.Exists(scriptPath))
        {
            _logger.LogDebug("[Surya] スクリプトパス: {Path}", scriptPath);
            return scriptPath;
        }

        // AppContext.BaseDirectoryからの相対パスも試行
        scriptPath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "ocr_server_surya.py");
        if (File.Exists(scriptPath))
        {
            return scriptPath;
        }

        return null;
    }

    /// <summary>
    /// Python実行ファイルパス解決
    /// </summary>
    private string? ResolvePythonPath()
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;

        // 1. .venv環境（最優先）
        var venvPython = Path.Combine(projectRoot, ".venv", "Scripts", "python.exe");
        if (File.Exists(venvPython))
        {
            _logger.LogInformation("[Surya] Python(.venv): {Path}", venvPython);
            return venvPython;
        }

        // 2. vendor環境
        var vendorPython = Path.Combine(AppContext.BaseDirectory, "vendor", "python", "python.exe");
        if (File.Exists(vendorPython))
        {
            _logger.LogInformation("[Surya] Python(vendor): {Path}", vendorPython);
            return vendorPython;
        }

        // 3. pyenv-win環境（Windowsでよく使われる）
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var pyenvPython = Path.Combine(userProfile, ".pyenv", "pyenv-win", "shims", "python.bat");
        if (File.Exists(pyenvPython))
        {
            // pyenv shimsはバッチファイルなので、直接pythonを見つける
            var pyenvVersionsDir = Path.Combine(userProfile, ".pyenv", "pyenv-win", "versions");
            if (Directory.Exists(pyenvVersionsDir))
            {
                var versions = Directory.GetDirectories(pyenvVersionsDir);
                foreach (var ver in versions.OrderByDescending(v => v))
                {
                    var pythonExe = Path.Combine(ver, "python.exe");
                    if (File.Exists(pythonExe))
                    {
                        _logger.LogInformation("[Surya] Python(pyenv): {Path}", pythonExe);
                        return pythonExe;
                    }
                }
            }
        }

        // 4. miniconda/anaconda環境
        var minicondaPython = Path.Combine(userProfile, "miniconda3", "python.exe");
        if (File.Exists(minicondaPython))
        {
            _logger.LogInformation("[Surya] Python(miniconda): {Path}", minicondaPython);
            return minicondaPython;
        }

        var anacondaPython = Path.Combine(userProfile, "anaconda3", "python.exe");
        if (File.Exists(anacondaPython))
        {
            _logger.LogInformation("[Surya] Python(anaconda): {Path}", anacondaPython);
            return anacondaPython;
        }

        // 5. PATHからpython.exeを検索
        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var pathDir in pathEnv.Split(Path.PathSeparator))
        {
            var pythonInPath = Path.Combine(pathDir, "python.exe");
            if (File.Exists(pythonInPath))
            {
                _logger.LogInformation("[Surya] Python(PATH): {Path}", pythonInPath);
                return pythonInPath;
            }
        }

        _logger.LogWarning("[Surya] Python not found in any standard location");
        return null;
    }


    /// <summary>
    /// Issue #197: Suryaモデル準備確認
    /// Issue #198: Suryaは初回起動時にHuggingFaceからモデルを自動ダウンロードする設計
    /// ComponentDownloaderによる事前ダウンロードは任意（オフライン環境向け）
    /// </summary>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>常にtrue（Suryaが自動ダウンロードするため）</returns>
    private Task<bool> WaitForSuryaModelAsync(CancellationToken cancellationToken)
    {
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory) ?? Environment.CurrentDirectory;

        // 事前配布モデルのパス候補（ComponentDownloaderでダウンロードされた場合）
        var preloadedPaths = new[]
        {
            // appsettings.jsonの設定パス: Models/surya-quantized/surya_rec_quantized.pth
            Path.Combine(projectRoot, "Models", "surya-quantized", "surya_rec_quantized.pth"),
            Path.Combine(AppContext.BaseDirectory, "Models", "surya-quantized", "surya_rec_quantized.pth"),
            // Detection ONNX: Models/surya-onnx/detection/model_int8.onnx
            Path.Combine(projectRoot, "Models", "surya-onnx", "detection", "model_int8.onnx"),
            Path.Combine(AppContext.BaseDirectory, "Models", "surya-onnx", "detection", "model_int8.onnx"),
        };

        // 事前配布モデルが存在するか確認
        foreach (var modelPath in preloadedPaths)
        {
            if (File.Exists(modelPath))
            {
                _logger.LogInformation("✅ [Surya] 事前配布モデル検出: {Path}", modelPath);
                return Task.FromResult(true);
            }
        }

        // Issue #198: Suryaは初回起動時にHuggingFaceからモデルを自動ダウンロードする
        // 事前配布モデルがなくても、Pythonサーバー起動を許可（Suryaが自動取得）
        _logger.LogInformation("ℹ️ [Surya] 事前配布モデルなし - Suryaが初回起動時にHuggingFaceからダウンロードします");
        _logger.LogInformation("ℹ️ [Surya] 初回起動は数分かかる場合があります（モデルサイズ: 約1GB）");

        return Task.FromResult(true);
    }

    /// <summary>
    /// プロジェクトルート検索（.slnファイルベース）
    /// </summary>
    private static string? FindProjectRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current != null)
        {
            if (current.GetFiles("*.sln").Length > 0)
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        return null;
    }

    /// <summary>
    /// 孤立プロセス強制終了
    /// Issue #197: exe版とPython版の両方を検索
    /// </summary>
    private async Task KillOrphanedProcessAsync()
    {
        try
        {
            // Python版の孤立プロセス検索
            var pythonProcesses = Process.GetProcessesByName("python")
                .Where(p =>
                {
                    try
                    {
                        return p.MainModule?.FileName?.Contains("ocr_server_surya") == true ||
                               p.StartInfo.Arguments?.Contains("ocr_server_surya") == true;
                    }
                    catch
                    {
                        return false;
                    }
                })
                .ToList();

            // exe版の孤立プロセス検索
            var exeProcesses = Process.GetProcessesByName("BaketaSuryaOcrServer").ToList();

            var allProcesses = pythonProcesses.Concat(exeProcesses).ToList();

            foreach (var proc in allProcesses)
            {
                _logger.LogWarning("🔥 [Surya] 孤立プロセス強制終了: PID {PID} ({Name})", proc.Id, proc.ProcessName);
                proc.Kill(entireProcessTree: true);
                await proc.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[Surya] 孤立プロセス検索中のエラー（無視）");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await StopServerAsync().ConfigureAwait(false);

        // Issue #189: Job Object破棄（ゾンビプロセス対策）
        _jobObject?.Dispose();
        _jobObject = null;

        _startLock.Dispose();
    }
}
