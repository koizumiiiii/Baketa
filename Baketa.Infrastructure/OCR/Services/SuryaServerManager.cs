using System.Diagnostics;
using System.IO;
using System.Text;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Abstractions.OCR;
using Baketa.Core.Events;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Services;
using Baketa.Infrastructure.Translation.Services;
using Baketa.Ocr.V1;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.OCR.Services;

/// <summary>
/// Surya OCR gRPCサーバー管理
/// Issue #189: PythonServerManagerパターンを参考に実装
/// Issue #189: ゾンビプロセス対策 - Job Object統合
/// Issue #292: IOcrServerManager実装で統合サーバーとの互換性確保
/// </summary>
public sealed class SuryaServerManager : IOcrServerManager
{
    private readonly ILogger<SuryaServerManager> _logger;
    private readonly IEventAggregator? _eventAggregator;
    private readonly GrpcPortProvider? _grpcPortProvider; // [Issue #292] 統合サーバーポート待機用
    private readonly UnifiedServerSettings? _unifiedServerSettings; // [Issue #292] 統合サーバー設定
    private readonly int _port;
    private Process? _serverProcess;
    private ProcessJobObject? _jobObject;
    private bool _isReady;
    private bool _disposed;
    private readonly SemaphoreSlim _startLock = new(1, 1);

    /// <summary>
    /// [Issue #292] 統合サーバーモードフラグ
    /// trueの場合、StartServerAsyncはサーバー起動をスキップし、統合サーバーの準備完了を待機
    /// </summary>
    private bool _isUnifiedMode;

    /// <summary>
    /// サーバーが準備完了かどうか
    /// </summary>
    public bool IsReady => _isReady;

    /// <summary>
    /// サーバーポート
    /// </summary>
    public int Port => _port;

    /// <summary>
    /// [Issue #292] 統合サーバーモードかどうか
    /// </summary>
    public bool IsUnifiedMode => _isUnifiedMode;

    public SuryaServerManager(
        int port,
        ILogger<SuryaServerManager> logger,
        IEventAggregator? eventAggregator = null,
        GrpcPortProvider? grpcPortProvider = null,
        UnifiedServerSettings? unifiedServerSettings = null)
    {
        _port = port;
        _logger = logger;
        _eventAggregator = eventAggregator;
        _grpcPortProvider = grpcPortProvider;
        _unifiedServerSettings = unifiedServerSettings;

        // Issue #189: Job Object初期化 - ゾンビプロセス対策
        _jobObject = new ProcessJobObject(logger);
        _logger.LogDebug("[Surya] Job Object初期化: IsValid={IsValid}", _jobObject.IsValid);
    }

    /// <summary>
    /// [Issue #292] 統合サーバーモードを設定
    /// </summary>
    /// <param name="isUnifiedMode">統合サーバーモードかどうか</param>
    /// <remarks>
    /// [Fix] _isReady = true は StartServerAsync() 内で統合サーバーの準備完了を確認後に設定
    /// 即座に設定するとサーバー起動前に IsReady=true となりエラーが発生する
    /// </remarks>
    public void SetUnifiedMode(bool isUnifiedMode)
    {
        _isUnifiedMode = isUnifiedMode;
        if (isUnifiedMode)
        {
            // [Fix] _isReady は StartServerAsync() で統合サーバーの準備完了後に設定
            // ここでは _isReady = true を設定しない（統合サーバーがまだ起動していない可能性があるため）
            _logger.LogInformation("[Issue #292] [Surya] 統合サーバーモード設定 - StartServerAsync()で準備完了を待機します, Port {Port}", _port);
        }
    }

    /// <summary>
    /// Suryaサーバーを起動し、準備完了まで待機
    /// Issue #197: モデルダウンロード完了待機ロジック追加
    /// [Issue #292] 統合サーバーモードではサーバー起動をスキップ
    /// </summary>
    public async Task<bool> StartServerAsync(CancellationToken cancellationToken = default)
    {
        // [Issue #292] 統合サーバーモードではサーバー起動をスキップし、gRPCで直接サーバーの準備完了を確認
        // [Fix] GrpcPortProvider.GetPortAsync()は使用しない（循環依存によるデッドロック回避）
        // ApplicationInitializer → SuryaOcrEngine → SuryaServerManager → GetPortAsync() → SetPort() →
        // ServerManagerHostedService → SignalCompletion() → ApplicationInitializer の循環を防止
        if (_isUnifiedMode)
        {
            var timeoutSeconds = _unifiedServerSettings?.StartupTimeoutSeconds ?? 300;

            // [Issue #422] 統合サーバーexe未検出時はダウンロード/展開中と判断しタイムアウトを延長
            var unifiedExePath = Path.Combine(AppContext.BaseDirectory,
                "grpc_server", "BaketaUnifiedServer", "BaketaUnifiedServer.exe");
            if (!File.Exists(unifiedExePath))
            {
                var extendedTimeout = Math.Max(timeoutSeconds, 600);
                _logger.LogInformation("⏳ [Issue #422] [Surya] 統合サーバーexe未検出 → ダウンロード/展開中と判断、タイムアウトを{Original}秒→{Extended}秒に延長",
                    timeoutSeconds, extendedTimeout);
                timeoutSeconds = extendedTimeout;
            }

            _logger.LogInformation("🔄 [Issue #292] [Surya] 統合サーバーモード - gRPCポーリングで準備完了を待機中... Port {Port}, Timeout {Timeout}秒",
                _port, timeoutSeconds);

            // gRPCで直接サーバーの準備完了をポーリング（循環依存回避）
            var isReady = await WaitForUnifiedServerReadyAsync(_port, timeoutSeconds, cancellationToken).ConfigureAwait(false);

            if (isReady)
            {
                _logger.LogInformation("✅ [Issue #292] [Surya] 統合サーバー準備完了: Port {Port}", _port);
                _isReady = true;
                return true;
            }
            else
            {
                _logger.LogError("❌ [Issue #292] [Surya] 統合サーバー待機タイムアウト（{Timeout}秒）- サーバーが起動していません", timeoutSeconds);
                _isReady = false;
                return false;
            }
        }

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

            // Issue #199: exe版のダウンロード完了を待機（配布版で必須）
            // ComponentDownloadServiceがSurya OCRサーバーexeをダウンロード中の場合、完了まで待つ
            await WaitForExeDownloadAsync(cancellationToken).ConfigureAwait(false);

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

                // [Issue #264] メモリエラー検出 & ユーザー通知
                DetectAndPublishServerError(e.Data);

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
    /// Issue #229: 配布版パスを確実に優先するよう修正
    /// </summary>
    private string? ResolveExePath(string projectRoot)
    {
        // Issue #229: 配布版判定 - AppContext.BaseDirectory直下に.slnがなければ配布版
        var isDistribution = Directory.GetFiles(AppContext.BaseDirectory, "*.sln").Length == 0;

        // 検索候補パス（優先順）
        var searchPaths = new List<string>();

        // 1. アプリ配布時: grpc_server/BaketaSuryaOcrServer/BaketaSuryaOcrServer.exe（常に最優先）
        searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"));

        // 2. 開発環境でのみ: grpc_server/dist/BaketaSuryaOcrServer/BaketaSuryaOcrServer.exe
        if (!isDistribution)
        {
            searchPaths.Add(Path.Combine(projectRoot, "grpc_server", "dist", "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"));
        }

        // 3. AppContext.BaseDirectory直下
        searchPaths.Add(Path.Combine(AppContext.BaseDirectory, "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe"));

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                _logger.LogDebug("[Surya] exe検出: {Path} (配布版={IsDistribution})", path, isDistribution);
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
    /// Issue #199: exe版のダウンロード完了を待機
    /// ComponentDownloadServiceがBaketaSuryaOcrServer.exeをダウンロード中の場合、完了まで待つ
    /// Issue #229: 開発環境判定を修正 - AppContext.BaseDirectory直下に.slnがある場合のみ開発環境
    /// </summary>
    private async Task WaitForExeDownloadAsync(CancellationToken cancellationToken)
    {
        // Issue #229: 開発環境判定を修正
        // AppContext.BaseDirectory直下に.slnがある場合のみ開発環境とみなす
        // 配布版フォルダ（Baketa-beta-x.x.x）からの実行時は親ディレクトリの.slnを無視
        var isDevEnvironment = Directory.GetFiles(AppContext.BaseDirectory, "*.sln").Length > 0;
        if (isDevEnvironment)
        {
            _logger.LogDebug("[Surya] 開発環境検出（直下に.sln） - exe待機スキップ");
            return;
        }

        // exe版の期待パス
        var exePath = Path.Combine(AppContext.BaseDirectory, "grpc_server", "BaketaSuryaOcrServer", "BaketaSuryaOcrServer.exe");

        // 既に存在する場合は即座に戻る
        if (File.Exists(exePath))
        {
            _logger.LogInformation("✅ [Surya] exe版検出済み: {Path}", exePath);
            return;
        }

        // [Issue #199] 最大10分待機（CUDA版は約2.4GB、ネットワーク状況により7分以上かかる場合がある）
        var maxWaitTime = TimeSpan.FromMinutes(10);
        var pollInterval = TimeSpan.FromSeconds(2);
        var progressLogInterval = TimeSpan.FromSeconds(30); // 30秒ごとに進捗ログ
        var startTime = DateTime.UtcNow;
        var lastProgressLog = DateTime.UtcNow;

        _logger.LogInformation("⏳ [Surya] exe版ダウンロード待機開始（最大{MaxWait:F0}分）...", maxWaitTime.TotalMinutes);

        while (DateTime.UtcNow - startTime < maxWaitTime)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (File.Exists(exePath))
            {
                var elapsed = DateTime.UtcNow - startTime;
                _logger.LogInformation("✅ [Surya] exe版ダウンロード完了検出: {Path} (待機時間: {Elapsed:mm\\:ss})", exePath, elapsed);
                return;
            }

            // 30秒ごとに進捗ログを出力
            if (DateTime.UtcNow - lastProgressLog >= progressLogInterval)
            {
                var elapsed = DateTime.UtcNow - startTime;
                var remaining = maxWaitTime - elapsed;
                _logger.LogInformation("⏳ [Surya] exe版ダウンロード待機中... (経過: {Elapsed:mm\\:ss} / 残り: {Remaining:mm\\:ss})",
                    elapsed, remaining);
                lastProgressLog = DateTime.UtcNow;
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }

        // タイムアウトしてもエラーにしない（Python版にフォールバック可能な場合があるため）
        // ただしRelease版ではPython版がないため、実質的にはOCRが使用不可となる
        _logger.LogWarning("⚠️ [Surya] exe版ダウンロード待機タイムアウト（{MaxWait:F0}分） - 続行します", maxWaitTime.TotalMinutes);
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

    /// <summary>
    /// [Issue #264] stderrからメモリエラー等を検出してServerErrorEventを発行
    /// ServerErrorDetectorヘルパークラスに共通ロジックを委譲
    /// </summary>
    private void DetectAndPublishServerError(string line)
    {
        var context = $"Port:{_port}";
        Infrastructure.Services.ServerErrorDetector.DetectAndPublish(
            line,
            ServerErrorSources.OcrServer,
            context,
            _eventAggregator,
            _logger);
    }

    /// <summary>
    /// [Issue #292] 統合サーバーの準備完了をgRPCで直接ポーリング
    /// GrpcPortProviderを使用せず、循環依存によるデッドロックを回避
    /// </summary>
    /// <param name="port">統合サーバーのポート番号</param>
    /// <param name="timeoutSeconds">タイムアウト秒数</param>
    /// <param name="cancellationToken">キャンセルトークン</param>
    /// <returns>サーバーが準備完了の場合true</returns>
    private async Task<bool> WaitForUnifiedServerReadyAsync(int port, int timeoutSeconds, CancellationToken cancellationToken)
    {
        var serverAddress = $"http://127.0.0.1:{port}";
        var stopwatch = Stopwatch.StartNew();
        var retryInterval = TimeSpan.FromSeconds(2);
        var timeout = TimeSpan.FromSeconds(timeoutSeconds);

        _logger.LogDebug("[Issue #292] [Surya] 統合サーバー準備完了ポーリング開始: {Address}", serverAddress);

        while (stopwatch.Elapsed < timeout && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var channel = GrpcChannel.ForAddress(serverAddress, new GrpcChannelOptions
                {
                    HttpHandler = new System.Net.Http.SocketsHttpHandler
                    {
                        ConnectTimeout = TimeSpan.FromSeconds(5)
                    }
                });

                var client = new OcrService.OcrServiceClient(channel);

                // IsReady RPCを呼び出し
                var response = await client.IsReadyAsync(
                    new OcrIsReadyRequest(),
                    deadline: DateTime.UtcNow.AddSeconds(10),
                    cancellationToken: cancellationToken
                ).ConfigureAwait(false);

                if (response.IsReady)
                {
                    _logger.LogDebug("[Issue #292] [Surya] 統合サーバー準備完了確認: {ElapsedMs}ms", stopwatch.ElapsedMilliseconds);
                    return true;
                }

                _logger.LogDebug("[Issue #292] [Surya] 統合サーバー未準備: Status={Status}, 再試行中...", response.Status);
            }
            catch (Grpc.Core.RpcException ex) when (ex.StatusCode == Grpc.Core.StatusCode.Unavailable)
            {
                // サーバーが起動していない - 再試行
                _logger.LogDebug("[Issue #292] [Surya] 統合サーバー未起動 ({ElapsedSec}秒経過) - 再試行中...",
                    stopwatch.Elapsed.TotalSeconds.ToString("F1"));
            }
            catch (Grpc.Core.RpcException ex)
            {
                _logger.LogWarning("[Issue #292] [Surya] gRPCエラー: {StatusCode} - 再試行中...", ex.StatusCode);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "[Issue #292] [Surya] 統合サーバー接続エラー - 再試行中...");
            }

            // 再試行間隔
            try
            {
                await Task.Delay(retryInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogWarning("[Issue #292] [Surya] 統合サーバー準備完了待機タイムアウト: {ElapsedSec}秒", stopwatch.Elapsed.TotalSeconds);
        return false;
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
