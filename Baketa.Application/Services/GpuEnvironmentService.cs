using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services;

/// <summary>
/// GPU環境チェック・セットアップサービス実装
/// Issue #193: Python GPU環境の自動セットアップ
///
/// 開発版（#if !IS_DISTRIBUTION）でのみGPU環境の動的セットアップを行う。
/// 配布版はPyInstaller exeのためビルド時にGPU環境が固定される。
/// </summary>
public class GpuEnvironmentService : IGpuEnvironmentService
{
    private readonly ILogger<GpuEnvironmentService> _logger;
    private readonly string _venvPath;
    private readonly string _gpuFlagPath;

    /// <inheritdoc/>
    public event EventHandler<GpuSetupProgressEventArgs>? ProgressChanged;

    public GpuEnvironmentService(ILogger<GpuEnvironmentService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // .venvパスを解決（プロジェクトルートから）
        var projectRoot = FindProjectRoot(AppContext.BaseDirectory);
        _venvPath = Path.Combine(projectRoot ?? AppContext.BaseDirectory, ".venv");
        _gpuFlagPath = Path.Combine(_venvPath, ".gpu_ok");

        _logger.LogDebug("[GPU] venvPath: {VenvPath}", _venvPath);
        _logger.LogDebug("[GPU] gpuFlagPath: {GpuFlagPath}", _gpuFlagPath);
    }

    /// <inheritdoc/>
    public async Task<bool> IsNvidiaGpuAvailableAsync()
    {
        try
        {
            _logger.LogDebug("[GPU] nvidia-smi実行開始");

            var startInfo = new ProcessStartInfo
            {
                FileName = "nvidia-smi",
                Arguments = "--query-gpu=name --format=csv,noheader",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("[GPU] nvidia-smiプロセス起動失敗");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                var gpuName = output.Trim().Split('\n').FirstOrDefault()?.Trim();
                _logger.LogInformation("[GPU] NVIDIA GPU検出: {GpuName}", gpuName);
                return true;
            }

            _logger.LogInformation("[GPU] NVIDIA GPU未検出（ExitCode: {ExitCode}）", process.ExitCode);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GPU] nvidia-smi実行エラー - NVIDIA GPU未検出として扱います");
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<bool> IsCudaAvailableInPythonAsync()
    {
        try
        {
            _logger.LogDebug("[GPU] torch.cuda.is_available()チェック開始");

            var pythonPath = GetPythonExecutable();
            if (string.IsNullOrEmpty(pythonPath))
            {
                _logger.LogWarning("[GPU] Python実行環境が見つかりません");
                return false;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-c \"import torch; print('CUDA_AVAILABLE' if torch.cuda.is_available() else 'CUDA_NOT_AVAILABLE')\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("[GPU] Pythonプロセス起動失敗");
                return false;
            }

            var output = await process.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
            var error = await process.StandardError.ReadToEndAsync().ConfigureAwait(false);
            await process.WaitForExitAsync().ConfigureAwait(false);

            if (process.ExitCode == 0 && output.Contains("CUDA_AVAILABLE"))
            {
                _logger.LogInformation("[GPU] Python環境でCUDA利用可能");
                return true;
            }

            if (!string.IsNullOrEmpty(error) && error.Contains("ModuleNotFoundError"))
            {
                _logger.LogInformation("[GPU] PyTorch未インストール");
            }
            else
            {
                _logger.LogInformation("[GPU] Python環境でCUDA利用不可（CPU版PyTorchの可能性）");
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GPU] CUDA利用可能チェックエラー");
            return false;
        }
    }

    /// <inheritdoc/>
    public bool IsGpuEnvironmentSetup()
    {
        var exists = File.Exists(_gpuFlagPath);
        _logger.LogDebug("[GPU] .gpu_okフラグ存在: {Exists}", exists);
        return exists;
    }

    /// <inheritdoc/>
    public void MarkGpuEnvironmentSetup()
    {
        try
        {
            var directory = Path.GetDirectoryName(_gpuFlagPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(_gpuFlagPath, $"GPU environment setup completed at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logger.LogInformation("[GPU] .gpu_okフラグ作成完了: {Path}", _gpuFlagPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GPU] .gpu_okフラグ作成失敗");
        }
    }

    /// <inheritdoc/>
    public async Task<GpuSetupResult> InstallGpuPackagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var pythonPath = GetPythonExecutable();
            if (string.IsNullOrEmpty(pythonPath))
            {
                _logger.LogError("[GPU] Python実行環境が見つかりません");
                return GpuSetupResult.InstallationFailed;
            }

            // Step 1: CPU版パッケージのアンインストール
            ReportProgress("uninstall", "CPU版パッケージをアンインストール中...", 10);
            await RunPipCommandAsync(pythonPath, "uninstall onnxruntime torch torchvision torchaudio -y", cancellationToken).ConfigureAwait(false);

            // Step 2: onnxruntime-gpuのインストール
            ReportProgress("install_onnx", "onnxruntime-gpuをインストール中...", 30);
            var onnxResult = await RunPipCommandAsync(pythonPath, "install onnxruntime-gpu", cancellationToken).ConfigureAwait(false);
            if (!onnxResult)
            {
                _logger.LogWarning("[GPU] onnxruntime-gpuインストール失敗");
            }

            // Step 3: CUDA版PyTorchのインストール
            ReportProgress("install_torch", "CUDA版PyTorchをインストール中（約2GB）...", 50);
            var torchResult = await RunPipCommandAsync(
                pythonPath,
                "install torch torchvision torchaudio --index-url https://download.pytorch.org/whl/cu121",
                cancellationToken,
                timeoutMinutes: 10).ConfigureAwait(false);

            if (!torchResult)
            {
                _logger.LogError("[GPU] CUDA版PyTorchインストール失敗");
                return GpuSetupResult.InstallationFailed;
            }

            // Step 4: インストール検証
            ReportProgress("verify", "インストールを検証中...", 90);
            var cudaAvailable = await IsCudaAvailableInPythonAsync().ConfigureAwait(false);

            if (cudaAvailable)
            {
                ReportProgress("complete", "GPU環境セットアップ完了", 100, isCompleted: true);
                _logger.LogInformation("[GPU] GPU環境セットアップ成功");
                return GpuSetupResult.Success;
            }
            else
            {
                _logger.LogWarning("[GPU] インストール後もCUDA利用不可 - ドライバ/CUDA Toolkitの問題の可能性");
                return GpuSetupResult.InstallationFailed;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[GPU] GPU環境セットアップがキャンセルされました");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GPU] GPU環境セットアップ中にエラー発生");
            return GpuSetupResult.InstallationFailed;
        }
    }

    /// <inheritdoc/>
    public async Task<GpuSetupResult> EnsureGpuEnvironmentAsync(CancellationToken cancellationToken = default)
    {
#if IS_DISTRIBUTION
        _logger.LogInformation("[GPU] 配布版のためGPU環境自動セットアップをスキップ");
        return GpuSetupResult.SkippedDistribution;
#else
        try
        {
            // Step 1: 既にセットアップ済みかチェック
            if (IsGpuEnvironmentSetup())
            {
                _logger.LogInformation("[GPU] GPU環境は既にセットアップ済み");
                return GpuSetupResult.AlreadySetup;
            }

            // Step 2: NVIDIA GPU存在チェック
            ReportProgress("check_gpu", "NVIDIA GPUをチェック中...", 5);
            var hasNvidiaGpu = await IsNvidiaGpuAvailableAsync().ConfigureAwait(false);

            if (!hasNvidiaGpu)
            {
                _logger.LogInformation("[GPU] NVIDIA GPU未検出 - CPUモードで続行");
                return GpuSetupResult.NoNvidiaGpu;
            }

            // Step 3: 既にCUDA利用可能かチェック
            ReportProgress("check_cuda", "CUDA環境をチェック中...", 10);
            var cudaAvailable = await IsCudaAvailableInPythonAsync().ConfigureAwait(false);

            if (cudaAvailable)
            {
                _logger.LogInformation("[GPU] CUDA既に利用可能 - セットアップ完了としてマーク");
                MarkGpuEnvironmentSetup();
                return GpuSetupResult.AlreadySetup;
            }

            // Step 4: GPU版パッケージを自動インストール
            _logger.LogInformation("[GPU] GPU版パッケージの自動インストールを開始");
            var result = await InstallGpuPackagesAsync(cancellationToken).ConfigureAwait(false);

            if (result == GpuSetupResult.Success)
            {
                MarkGpuEnvironmentSetup();
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("[GPU] GPU環境セットアップがキャンセルされました");
            return GpuSetupResult.Skipped;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GPU] GPU環境セットアップエラー - CPUモードで続行");
            return GpuSetupResult.InstallationFailed;
        }
#endif
    }

    /// <summary>
    /// pipコマンドを実行
    /// </summary>
    private async Task<bool> RunPipCommandAsync(
        string pythonPath,
        string pipArgs,
        CancellationToken cancellationToken,
        int timeoutMinutes = 5)
    {
        try
        {
            _logger.LogDebug("[GPU] pip {Args}", pipArgs);

            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = $"-m pip {pipArgs}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("[GPU] pipプロセス起動失敗");
                return false;
            }

            // 非同期で出力を監視（進捗表示用）
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    outputBuilder.AppendLine(e.Data);
                    _logger.LogDebug("[pip] {Output}", e.Data);

                    // ダウンロード進捗を解析
                    var match = Regex.Match(e.Data, @"Downloading.*?(\d+)%");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out var percent))
                    {
                        ReportProgress("downloading", $"ダウンロード中... {percent}%", percent);
                    }
                }
            };

            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorBuilder.AppendLine(e.Data);
                    _logger.LogDebug("[pip-err] {Error}", e.Data);
                }
            };

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromMinutes(timeoutMinutes));

            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);

            if (process.ExitCode == 0)
            {
                _logger.LogInformation("[GPU] pip {Args} 成功", pipArgs);
                return true;
            }
            else
            {
                _logger.LogWarning("[GPU] pip {Args} 失敗 (ExitCode: {ExitCode})", pipArgs, process.ExitCode);
                _logger.LogWarning("[GPU] Error output: {Error}", errorBuilder.ToString());
                return false;
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("[GPU] pip {Args} タイムアウトまたはキャンセル", pipArgs);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GPU] pip {Args} 実行エラー", pipArgs);
            return false;
        }
    }

    /// <summary>
    /// Python実行ファイルのパスを取得
    /// </summary>
    private string? GetPythonExecutable()
    {
        // 1. .venv内のPython
        var venvPython = Path.Combine(_venvPath, "Scripts", "python.exe");
        if (File.Exists(venvPython))
        {
            _logger.LogDebug("[GPU] .venv Python使用: {Path}", venvPython);
            return venvPython;
        }

        // 2. システムPython（pyランチャー）
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "py",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(5000);
                if (process.ExitCode == 0)
                {
                    _logger.LogDebug("[GPU] pyランチャー使用");
                    return "py";
                }
            }
        }
        catch { }

        // 3. python.exe直接
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                Arguments = "--version",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit(5000);
                if (process.ExitCode == 0)
                {
                    _logger.LogDebug("[GPU] システムPython使用");
                    return "python";
                }
            }
        }
        catch { }

        _logger.LogWarning("[GPU] Python実行環境が見つかりません");
        return null;
    }

    /// <summary>
    /// 進捗をレポート
    /// </summary>
    private void ReportProgress(string step, string message, int progress, bool isCompleted = false, string? errorMessage = null)
    {
        ProgressChanged?.Invoke(this, new GpuSetupProgressEventArgs
        {
            Step = step,
            Message = message,
            Progress = progress,
            IsCompleted = isCompleted,
            ErrorMessage = errorMessage
        });
    }

    /// <summary>
    /// プロジェクトルートを探索
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
}
