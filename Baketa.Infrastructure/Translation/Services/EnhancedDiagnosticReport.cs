using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// 拡張診断レポート（Step 1: 即座の応急処置）
/// Gemini推奨の包括的診断情報を提供
/// </summary>
public sealed class EnhancedDiagnosticReport
{
    private readonly ILogger<EnhancedDiagnosticReport> _logger;
    private readonly PythonEnvironmentResolver _pythonResolver;

    public EnhancedDiagnosticReport(
        ILogger<EnhancedDiagnosticReport> logger,
        PythonEnvironmentResolver pythonResolver)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pythonResolver = pythonResolver ?? throw new ArgumentNullException(nameof(pythonResolver));
    }

    /// <summary>
    /// 完全診断レポートの生成
    /// </summary>
    public async Task<ComprehensiveDiagnosticResult> GenerateReportAsync()
    {
        var result = new ComprehensiveDiagnosticResult
        {
            Timestamp = DateTime.UtcNow,
            ReportVersion = "1.0.0"
        };

        try
        {
            _logger.LogInformation("🔍 拡張診断レポート生成開始");

            // 並列実行で高速化
            var task1 = Task.Run(async () => result.PythonDiagnostics = await GetPythonDiagnosticsAsync());
            var task2 = Task.Run(async () => result.GpuDiagnostics = await GetGpuDiagnosticsAsync());
            var task3 = Task.Run(async () => result.NetworkDiagnostics = await GetNetworkDiagnosticsAsync());
            var task4 = Task.Run(async () => result.ProcessDiagnostics = await GetProcessDiagnosticsAsync());
            var task5 = Task.Run(() => result.SystemDiagnostics = GetSystemDiagnostics());

            await Task.WhenAll(task1, task2, task3, task4, task5);

            // 自動修復アクションの生成
            result.SuggestedActions = GenerateSuggestedActions(result);

            _logger.LogInformation("✅ 拡張診断レポート生成完了");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ 診断レポート生成エラー");
            result.GeneralError = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// Python環境詳細診断
    /// </summary>
    private async Task<PythonDiagnosticsResult> GetPythonDiagnosticsAsync()
    {
        var result = new PythonDiagnosticsResult();

        try
        {
            var envDiagnostics = await _pythonResolver.GetEnvironmentDiagnosticsAsync();

            result.ExecutablePath = envDiagnostics.PythonExecutablePath;
            result.Version = envDiagnostics.PythonVersion;
            result.InstalledPackages = envDiagnostics.InstalledPackages;
            result.PyenvStatus = envDiagnostics.PyenvStatus;
            result.EnvironmentVariables = envDiagnostics.EnvironmentVariables;

            // 必須パッケージの確認
            result.RequiredPackagesStatus = CheckRequiredPackages(envDiagnostics.InstalledPackages);

            // トーチのCUDA対応確認
            result.TorchCudaAvailable = await CheckTorchCudaAvailabilityAsync(envDiagnostics.PythonExecutablePath);

            result.IsHealthy = !string.IsNullOrEmpty(result.Version) &&
                             result.RequiredPackagesStatus.All(kvp => kvp.Value);
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// GPU/CUDA環境診断（Gemini推奨追加項目）
    /// </summary>
    private async Task<GpuDiagnosticsResult> GetGpuDiagnosticsAsync()
    {
        var result = new GpuDiagnosticsResult();

        try
        {
            // nvidia-smi コマンド実行
            result.NvidiaSmiOutput = await ExecuteCommandAsync("nvidia-smi", "--query-gpu=name,memory.total,memory.used,memory.free --format=csv");
            result.IsNvidiaGpuDetected = !string.IsNullOrEmpty(result.NvidiaSmiOutput) && !result.NvidiaSmiOutput.Contains("not found");

            // CUDA環境変数確認
            result.CudaHome = Environment.GetEnvironmentVariable("CUDA_HOME");
            result.CudaPath = Environment.GetEnvironmentVariable("CUDA_PATH");

            // DirectML可用性確認（Windows）
            if (OperatingSystem.IsWindows())
            {
                result.IsDirectMlAvailable = CheckDirectMlAvailability();
            }

            result.IsHealthy = result.IsNvidiaGpuDetected || result.IsDirectMlAvailable;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// ネットワーク診断
    /// </summary>
    private async Task<NetworkDiagnosticsResult> GetNetworkDiagnosticsAsync()
    {
        var result = new NetworkDiagnosticsResult();

        try
        {
            // ポート使用状況確認
            result.PortStatus = await GetPortStatusAsync([5557, 5558, 5559, 5560, 5561]);

            // ファイアウォール状態確認（Windows）
            if (OperatingSystem.IsWindows())
            {
                result.FirewallRules = await GetFirewallRulesAsync();
            }

            // インターネット接続確認
            result.InternetConnectivity = await TestInternetConnectivityAsync();

            result.IsHealthy = result.PortStatus.Any(kvp => kvp.Value == "Available") && result.InternetConnectivity;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// プロセス診断
    /// </summary>
    private async Task<ProcessDiagnosticsResult> GetProcessDiagnosticsAsync()
    {
        var result = new ProcessDiagnosticsResult();

        try
        {
            // Python関連プロセス確認
            var pythonProcesses = Process.GetProcessesByName("python")
                .Concat(Process.GetProcessesByName("py"))
                .ToArray();

            result.ActivePythonProcesses = [.. pythonProcesses.Select(p => new ProcessInfo
            {
                ProcessId = p.Id,
                ProcessName = p.ProcessName,
                StartTime = p.StartTime,
                WorkingSet = p.WorkingSet64,
                CommandLine = GetProcessCommandLine(p)
            })];

            // NLLB翻訳サーバープロセス特定
            result.TranslationServerProcesses = [.. result.ActivePythonProcesses
                .Where(p => p.CommandLine?.Contains("nllb_translation_server") == true ||
                           p.CommandLine?.Contains("optimized_translation_server") == true)];

            // リソース使用量
            result.SystemMemoryUsage = GC.GetTotalMemory(false);
            result.AvailableMemory = GetAvailableMemory();

            result.IsHealthy = result.TranslationServerProcesses.Length <= 1; // 重複プロセスなし
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// システム診断
    /// </summary>
    private SystemDiagnosticsResult GetSystemDiagnostics()
    {
        var result = new SystemDiagnosticsResult();

        try
        {
            result.OperatingSystem = Environment.OSVersion.ToString();
            result.Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86";
            result.ProcessorCount = Environment.ProcessorCount;
            result.MachineName = Environment.MachineName;
            result.UserName = Environment.UserName;
            result.CurrentDirectory = Environment.CurrentDirectory;
            result.DotNetVersion = Environment.Version.ToString();

            // ディスク容量
            var drives = DriveInfo.GetDrives().Where(d => d.IsReady).ToArray();
            result.DriveInfo = [.. drives.Select(d => new DriveInfoResult
            {
                Name = d.Name,
                TotalSize = d.TotalSize,
                AvailableSpace = d.AvailableFreeSpace,
                DriveType = d.DriveType.ToString()
            })];

            result.IsHealthy = drives.Any(d => d.AvailableFreeSpace > 5L * 1024 * 1024 * 1024); // 5GB以上
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 必須パッケージの確認
    /// </summary>
    private Dictionary<string, bool> CheckRequiredPackages(string[] installedPackages)
    {
        var requiredPackages = new[] { "torch", "transformers", "sentencepiece", "fastapi", "uvicorn" };
        var result = new Dictionary<string, bool>();

        foreach (var package in requiredPackages)
        {
            result[package] = installedPackages.Any(p => p.StartsWith($"{package}==", StringComparison.OrdinalIgnoreCase));
        }

        return result;
    }

    /// <summary>
    /// PyTorchのCUDA対応確認
    /// </summary>
    private async Task<bool> CheckTorchCudaAvailabilityAsync(string pythonPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-c \"import torch; print(torch.cuda.is_available())\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            await process!.WaitForExitAsync();

            if (process.ExitCode == 0)
            {
                var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
                return output.Equals("True", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (Exception)
        {
            // torch未インストールまたは実行エラー
        }

        return false;
    }

    /// <summary>
    /// コマンド実行ヘルパー
    /// </summary>
    private async Task<string> ExecuteCommandAsync(string fileName, string arguments, int timeoutMs = 10000)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);

            using var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await process!.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill();
                return "Command timed out";
            }

            return process.ExitCode == 0
                ? await process.StandardOutput.ReadToEndAsync()
                : await process.StandardError.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            return $"Command error: {ex.Message}";
        }
    }

    /// <summary>
    /// ポート使用状況確認
    /// </summary>
    private async Task<Dictionary<int, string>> GetPortStatusAsync(int[] ports)
    {
        var result = new Dictionary<int, string>();

        foreach (var port in ports)
        {
            try
            {
                var netstatOutput = await ExecuteCommandAsync("netstat", $"-an | findstr :{port}");
                result[port] = netstatOutput.Contains($":{port}") ? "In Use" : "Available";
            }
            catch
            {
                result[port] = "Unknown";
            }
        }

        return result;
    }

    /// <summary>
    /// ファイアウォールルール確認（Windows）
    /// </summary>
    private async Task<string[]> GetFirewallRulesAsync()
    {
        try
        {
            var output = await ExecuteCommandAsync("netsh", "advfirewall firewall show rule name=all | findstr Python");
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch
        {
            return ["Firewall check failed"];
        }
    }

    /// <summary>
    /// インターネット接続確認
    /// </summary>
    private async Task<bool> TestInternetConnectivityAsync()
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync("8.8.8.8", 5000);
            return reply.Status == IPStatus.Success;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// DirectML可用性確認
    /// </summary>
    private bool CheckDirectMlAvailability()
    {
        // 簡易確認：Windows 10 version 1903以上
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18362))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// プロセスコマンドライン取得
    /// </summary>
    private string? GetProcessCommandLine(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 利用可能メモリ取得
    /// </summary>
    private long GetAvailableMemory()
    {
        try
        {
            var pc = new PerformanceCounter("Memory", "Available Bytes");
            return (long)pc.NextValue();
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>
    /// 推奨アクションの生成
    /// </summary>
    private string[] GenerateSuggestedActions(ComprehensiveDiagnosticResult result)
    {
        var actions = new List<string>();

        // Python関連
        if (result.PythonDiagnostics?.IsHealthy != true)
        {
            actions.Add("Python 3.10以上をインストールしてください");
            actions.Add("必須パッケージ（torch, transformers, sentencepiece）をインストールしてください");
        }

        // GPU関連
        if (result.GpuDiagnostics?.IsHealthy != true)
        {
            actions.Add("NVIDIA ドライバーまたはCUDAをインストールしてください");
        }

        // ネットワーク関連
        if (result.NetworkDiagnostics?.IsHealthy != true)
        {
            actions.Add("ポート5557-5561が利用可能であることを確認してください");
            actions.Add("ファイアウォールでPythonの通信を許可してください");
        }

        // プロセス関連
        if (result.ProcessDiagnostics?.IsHealthy != true)
        {
            actions.Add("重複したPython翻訳サーバープロセスを終了してください");
        }

        return actions.Count > 0 ? [.. actions] : ["システムは正常です"];
    }
}

// 診断結果データ構造
public sealed class ComprehensiveDiagnosticResult
{
    public DateTime Timestamp { get; set; }
    public string ReportVersion { get; set; } = string.Empty;
    public PythonDiagnosticsResult? PythonDiagnostics { get; set; }
    public GpuDiagnosticsResult? GpuDiagnostics { get; set; }
    public NetworkDiagnosticsResult? NetworkDiagnostics { get; set; }
    public ProcessDiagnosticsResult? ProcessDiagnostics { get; set; }
    public SystemDiagnosticsResult? SystemDiagnostics { get; set; }
    public string[] SuggestedActions { get; set; } = [];
    public string? GeneralError { get; set; }
}

public sealed class PythonDiagnosticsResult
{
    public string ExecutablePath { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string[] InstalledPackages { get; set; } = [];
    public string PyenvStatus { get; set; } = string.Empty;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public Dictionary<string, bool> RequiredPackagesStatus { get; set; } = [];
    public bool TorchCudaAvailable { get; set; }
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

public sealed class GpuDiagnosticsResult
{
    public string? NvidiaSmiOutput { get; set; }
    public bool IsNvidiaGpuDetected { get; set; }
    public string? CudaHome { get; set; }
    public string? CudaPath { get; set; }
    public bool IsDirectMlAvailable { get; set; }
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

public sealed class NetworkDiagnosticsResult
{
    public Dictionary<int, string> PortStatus { get; set; } = [];
    public string[] FirewallRules { get; set; } = [];
    public bool InternetConnectivity { get; set; }
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

public sealed class ProcessDiagnosticsResult
{
    public ProcessInfo[] ActivePythonProcesses { get; set; } = [];
    public ProcessInfo[] TranslationServerProcesses { get; set; } = [];
    public long SystemMemoryUsage { get; set; }
    public long AvailableMemory { get; set; }
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

public sealed class SystemDiagnosticsResult
{
    public string OperatingSystem { get; set; } = string.Empty;
    public string Architecture { get; set; } = string.Empty;
    public int ProcessorCount { get; set; }
    public string MachineName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string CurrentDirectory { get; set; } = string.Empty;
    public string DotNetVersion { get; set; } = string.Empty;
    public DriveInfoResult[] DriveInfo { get; set; } = [];
    public bool IsHealthy { get; set; }
    public string? Error { get; set; }
}

public sealed class ProcessInfo
{
    public int ProcessId { get; set; }
    public string ProcessName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public long WorkingSet { get; set; }
    public string? CommandLine { get; set; }
}

public sealed class DriveInfoResult
{
    public string Name { get; set; } = string.Empty;
    public long TotalSize { get; set; }
    public long AvailableSpace { get; set; }
    public string DriveType { get; set; } = string.Empty;
}
