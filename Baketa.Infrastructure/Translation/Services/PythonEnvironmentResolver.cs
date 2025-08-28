using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Translation.Services;

/// <summary>
/// Python実行環境の堅牢な解決（Step 1: 即座の応急処置）
/// Gemini推奨のpy.exe優先戦略を実装
/// </summary>
public sealed class PythonEnvironmentResolver
{
    private readonly ILogger<PythonEnvironmentResolver> _logger;
    private readonly IConfiguration _configuration;
    
    // Gemini推奨: py.exe優先は「極めて適切」
    private static readonly string[] PythonExecutableCandidates = [
        "py",        // 1. py.exe (Windows Python Launcher) - 最高信頼性
        "python3",   // 2. python3 (Linux/macOS互換性)
        "python"     // 3. python (フォールバック)
    ];
    
    public PythonEnvironmentResolver(ILogger<PythonEnvironmentResolver> logger, IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
    }
    
    /// <summary>
    /// 最適なPython実行可能ファイルパスを解決
    /// </summary>
    public async Task<string> ResolvePythonExecutableAsync()
    {
        try
        {
            // 1. appsettings.jsonの明示的パス優先
            var explicitPath = _configuration["Translation:Python:ExecutablePath"];
            if (!string.IsNullOrWhiteSpace(explicitPath))
            {
                _logger.LogInformation("🎯 明示的Python実行パスを使用: {Path}", explicitPath);
                
                if (await ValidatePythonExecutableAsync(explicitPath))
                {
                    return explicitPath;
                }
                
                _logger.LogWarning("⚠️ 明示的Python実行パスが無効: {Path}", explicitPath);
            }
            
            // 2. 候補の順次検証（py.exe優先戦略）
            foreach (var candidate in PythonExecutableCandidates)
            {
                _logger.LogDebug("🔍 Python実行候補を検証中: {Candidate}", candidate);
                
                if (await ValidatePythonExecutableAsync(candidate))
                {
                    _logger.LogInformation("✅ 有効なPython実行環境発見: {Candidate}", candidate);
                    return candidate;
                }
            }
            
            // 3. where/which コマンドでのフォールバック検索
            var whereResult = await FindPythonViaSystemCommandAsync();
            if (!string.IsNullOrWhiteSpace(whereResult))
            {
                if (await ValidatePythonExecutableAsync(whereResult))
                {
                    _logger.LogInformation("✅ システムコマンドでPython発見: {Path}", whereResult);
                    return whereResult;
                }
            }
            
            // 4. pyenv which pythonフォールバック（最後の手段）
            var pyenvResult = await FindPythonViaPyenvAsync();
            if (!string.IsNullOrWhiteSpace(pyenvResult))
            {
                if (await ValidatePythonExecutableAsync(pyenvResult))
                {
                    _logger.LogInformation("✅ pyenvでPython発見: {Path}", pyenvResult);
                    return pyenvResult;
                }
            }
            
            throw new InvalidOperationException("有効なPython実行環境が見つかりません。Python 3.10以上がインストールされていることを確認してください。");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex, "❌ Python実行環境解決エラー");
            throw new InvalidOperationException("Python実行環境の解決に失敗しました", ex);
        }
    }
    
    /// <summary>
    /// Python実行可能ファイルの検証
    /// </summary>
    private async Task<bool> ValidatePythonExecutableAsync(string pythonPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogDebug("❌ Python実行プロセス開始失敗: {Path}", pythonPath);
                return false;
            }
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode != 0)
            {
                var stderr = await process.StandardError.ReadToEndAsync();
                _logger.LogDebug("❌ Python実行エラー (Exit {ExitCode}): {Error}", process.ExitCode, stderr);
                return false;
            }
            
            var output = await process.StandardOutput.ReadToEndAsync();
            var versionMatch = System.Text.RegularExpressions.Regex.Match(output, @"Python (\d+)\.(\d+)\.(\d+)");
            
            if (!versionMatch.Success)
            {
                _logger.LogDebug("❌ Python バージョン解析失敗: {Output}", output);
                return false;
            }
            
            // Python 3.10以上の確認
            var major = int.Parse(versionMatch.Groups[1].Value);
            var minor = int.Parse(versionMatch.Groups[2].Value);
            
            if (major < 3 || (major == 3 && minor < 10))
            {
                _logger.LogWarning("❌ Pythonバージョンが古すぎます: {Version} (3.10以上が必要)", output.Trim());
                return false;
            }
            
            _logger.LogDebug("✅ Python実行確認成功: {Version} @ {Path}", output.Trim(), pythonPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("❌ Python実行検証例外: {Path} - {Error}", pythonPath, ex.Message);
            return false;
        }
    }
    
    /// <summary>
    /// whereコマンド（Windows）またはwhichコマンド（Linux/macOS）でPython検索
    /// </summary>
    private async Task<string?> FindPythonViaSystemCommandAsync()
    {
        try
        {
            var isWindows = OperatingSystem.IsWindows();
            var commandName = isWindows ? "where" : "which";
            
            foreach (var candidate in PythonExecutableCandidates)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = commandName,
                    Arguments = candidate,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                
                using var process = Process.Start(startInfo);
                if (process == null) continue;
                
                await process.WaitForExitAsync();
                
                if (process.ExitCode == 0)
                {
                    var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        // Windowsでは複数行返る場合があるので最初の行を使用
                        var firstLine = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0].Trim();
                        _logger.LogDebug("🔍 {Command}で発見: {Candidate} -> {Path}", commandName, candidate, firstLine);
                        return firstLine;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("システムコマンドでのPython検索失敗: {Error}", ex.Message);
        }
        
        return null;
    }
    
    /// <summary>
    /// pyenv which pythonでPython検索（最後の手段）
    /// </summary>
    private async Task<string?> FindPythonViaPyenvAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pyenv",
                Arguments = "which python",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process == null) return null;
            
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                var output = (await process.StandardOutput.ReadToEndAsync()).Trim();
                if (!string.IsNullOrWhiteSpace(output))
                {
                    _logger.LogDebug("🔍 pyenvで発見: {Path}", output);
                    return output;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug("pyenvでのPython検索失敗: {Error}", ex.Message);
        }
        
        return null;
    }
    
    /// <summary>
    /// Python環境の詳細診断情報を取得
    /// </summary>
    public async Task<PythonEnvironmentDiagnostics> GetEnvironmentDiagnosticsAsync(string? pythonPath = null)
    {
        var diagnostics = new PythonEnvironmentDiagnostics();
        
        try
        {
            // Python実行パス解決
            pythonPath ??= await ResolvePythonExecutableAsync();
            diagnostics.PythonExecutablePath = pythonPath;
            
            // Pythonバージョン取得
            diagnostics.PythonVersion = await GetPythonVersionAsync(pythonPath);
            
            // pip パッケージリスト取得
            diagnostics.InstalledPackages = await GetPipPackagesAsync(pythonPath);
            
            // pyenvステータス取得
            diagnostics.PyenvStatus = await GetPyenvStatusAsync();
            
            // 関連環境変数取得
            diagnostics.EnvironmentVariables = GetRelevantEnvironmentVariables();
            
            _logger.LogInformation("✅ Python環境診断完了: {Version} @ {Path}", 
                diagnostics.PythonVersion, diagnostics.PythonExecutablePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Python環境診断エラー");
            diagnostics.DiagnosticError = ex.Message;
        }
        
        return diagnostics;
    }
    
    private async Task<string> GetPythonVersionAsync(string pythonPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "--version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            await process!.WaitForExitAsync();
            
            return (await process.StandardOutput.ReadToEndAsync()).Trim();
        }
        catch (Exception)
        {
            return "Unknown";
        }
    }
    
    private async Task<string[]> GetPipPackagesAsync(string pythonPath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = pythonPath,
                Arguments = "-m pip list --format=freeze",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            await process!.WaitForExitAsync();
            
            var output = await process.StandardOutput.ReadToEndAsync();
            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        catch (Exception)
        {
            return [];
        }
    }
    
    private async Task<string> GetPyenvStatusAsync()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "pyenv",
                Arguments = "version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            await process!.WaitForExitAsync();
            
            return process.ExitCode == 0 
                ? (await process.StandardOutput.ReadToEndAsync()).Trim()
                : "pyenv not available";
        }
        catch (Exception)
        {
            return "pyenv not available";
        }
    }
    
    private Dictionary<string, string> GetRelevantEnvironmentVariables()
    {
        var relevantVars = new[] { "PATH", "PYTHONPATH", "CUDA_HOME", "HF_HOME", "PYENV_ROOT" };
        var result = new Dictionary<string, string>();
        
        foreach (var varName in relevantVars)
        {
            var value = Environment.GetEnvironmentVariable(varName);
            if (!string.IsNullOrEmpty(value))
            {
                result[varName] = value;
            }
        }
        
        return result;
    }
}

/// <summary>
/// Python環境診断結果
/// </summary>
public sealed class PythonEnvironmentDiagnostics
{
    public string PythonExecutablePath { get; set; } = string.Empty;
    public string PythonVersion { get; set; } = string.Empty;
    public string[] InstalledPackages { get; set; } = [];
    public string PyenvStatus { get; set; } = string.Empty;
    public Dictionary<string, string> EnvironmentVariables { get; set; } = [];
    public string? DiagnosticError { get; set; }
}