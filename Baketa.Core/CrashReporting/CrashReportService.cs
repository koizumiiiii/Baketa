using System.Globalization;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Baketa.Core.Abstractions.CrashReporting;
using Baketa.Core.Logging;
using Microsoft.Extensions.Logging;

namespace Baketa.Core.CrashReporting;

/// <summary>
/// [Issue #252] クラッシュレポートサービスの実装
/// 例外発生時のレポート生成、保存、管理を担当
/// </summary>
public sealed partial class CrashReportService : ICrashReportService
{
    private readonly ILogger<CrashReportService>? _logger;
    private readonly RingBufferLogger _ringBuffer;
    private readonly DateTime _startTime;
    private readonly string _reportsDirectory;
    private readonly string _crashPendingFlagPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// CrashReportServiceを初期化
    /// </summary>
    /// <param name="logger">ロガー（オプション）</param>
    /// <param name="ringBuffer">リングバッファロガー（nullの場合はシングルトンを使用）</param>
    public CrashReportService(
        ILogger<CrashReportService>? logger = null,
        RingBufferLogger? ringBuffer = null)
    {
        _logger = logger;
        _ringBuffer = ringBuffer ?? RingBufferLogger.Instance;
        _startTime = DateTime.UtcNow;

        _reportsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa", "Reports", "Crashes");

        _crashPendingFlagPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Baketa", ".crash_pending");

        EnsureDirectoryExists();
    }

    /// <inheritdoc />
    public string ReportsDirectory => _reportsDirectory;

    /// <inheritdoc />
    public Task<CrashReport> GenerateCrashReportAsync(
        Exception exception,
        string source,
        Dictionary<string, object?>? additionalContext = null)
    {
        var reportId = GenerateReportId();
        var exceptionInfo = ExtractExceptionInfo(exception);
        var systemInfo = CaptureSystemInfo();
        var recentLogs = GetSanitizedLogs();

        var report = new CrashReport
        {
            ReportId = reportId,
            CrashedAt = DateTime.UtcNow,
            BaketaVersion = GetBaketaVersion(),
            Source = SanitizeText(source),
            Exception = exceptionInfo,
            SystemInfo = systemInfo,
            RecentLogs = recentLogs,
            AdditionalContext = SanitizeContext(additionalContext)
        };

        _logger?.LogInformation("[Issue #252] クラッシュレポート生成完了: {ReportId}", reportId);

        return Task.FromResult(report);
    }

    /// <inheritdoc />
    public async Task<string> SaveCrashReportAsync(CrashReport report)
    {
        EnsureDirectoryExists();

        var fileName = $"crash_{report.CrashedAt:yyyyMMdd_HHmmss}_{report.ReportId[..8]}.json";
        var filePath = Path.Combine(_reportsDirectory, fileName);

        try
        {
            var jsonContent = JsonSerializer.Serialize(report, JsonOptions);
            await File.WriteAllTextAsync(filePath, jsonContent).ConfigureAwait(false);

            _logger?.LogInformation("[Issue #252] クラッシュレポート保存完了: {FilePath}", filePath);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #252] クラッシュレポート保存失敗: {ReportId}", report.ReportId);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task CreateCrashPendingFlagAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_crashPendingFlagPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var flagContent = new
            {
                CrashedAt = DateTime.UtcNow.ToString("O"),
                Version = GetBaketaVersion()
            };

            await File.WriteAllTextAsync(
                _crashPendingFlagPath,
                JsonSerializer.Serialize(flagContent, JsonOptions)).ConfigureAwait(false);

            _logger?.LogDebug("[Issue #252] .crash_pendingフラグ作成完了");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #252] .crash_pendingフラグ作成失敗");
        }
    }

    /// <inheritdoc />
    public bool HasPendingCrashReport()
    {
        return File.Exists(_crashPendingFlagPath);
    }

    /// <inheritdoc />
    public Task ClearCrashPendingFlagAsync()
    {
        try
        {
            if (File.Exists(_crashPendingFlagPath))
            {
                File.Delete(_crashPendingFlagPath);
                _logger?.LogDebug("[Issue #252] .crash_pendingフラグ削除完了");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #252] .crash_pendingフラグ削除失敗");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrashReportSummary>> GetPendingCrashReportsAsync()
    {
        var summaries = new List<CrashReportSummary>();

        if (!Directory.Exists(_reportsDirectory))
        {
            return summaries;
        }

        var files = Directory.GetFiles(_reportsDirectory, "crash_*.json")
            .OrderByDescending(f => File.GetCreationTimeUtc(f));

        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file).ConfigureAwait(false);
                var report = JsonSerializer.Deserialize<CrashReport>(json, JsonOptions);

                if (report != null && !report.IsSent)
                {
                    summaries.Add(new CrashReportSummary
                    {
                        ReportId = report.ReportId,
                        FilePath = file,
                        CrashedAt = report.CrashedAt,
                        ExceptionType = report.Exception.Type,
                        ExceptionMessage = TruncateMessage(report.Exception.Message, 100),
                        Source = report.Source,
                        IsSent = report.IsSent
                    });
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Issue #252] クラッシュレポート読み込み失敗: {File}", file);
            }
        }

        return summaries;
    }

    /// <inheritdoc />
    public async Task<CrashReport?> LoadCrashReportAsync(string reportId)
    {
        if (!Directory.Exists(_reportsDirectory))
        {
            return null;
        }

        var files = Directory.GetFiles(_reportsDirectory, $"crash_*_{reportId[..Math.Min(8, reportId.Length)]}*.json");

        if (files.Length == 0)
        {
            return null;
        }

        try
        {
            var json = await File.ReadAllTextAsync(files[0]).ConfigureAwait(false);
            return JsonSerializer.Deserialize<CrashReport>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #252] クラッシュレポート読み込み失敗: {ReportId}", reportId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task MarkReportAsSentAsync(string reportId)
    {
        var report = await LoadCrashReportAsync(reportId).ConfigureAwait(false);
        if (report == null)
        {
            return;
        }

        report.IsSent = true;
        report.SentAt = DateTime.UtcNow;

        // 既存ファイルを探して更新
        var files = Directory.GetFiles(_reportsDirectory, $"crash_*_{reportId[..Math.Min(8, reportId.Length)]}*.json");
        if (files.Length > 0)
        {
            var jsonContent = JsonSerializer.Serialize(report, JsonOptions);
            await File.WriteAllTextAsync(files[0], jsonContent).ConfigureAwait(false);
            _logger?.LogDebug("[Issue #252] レポート送信済みマーク完了: {ReportId}", reportId);
        }
    }

    /// <inheritdoc />
    public Task DeleteCrashReportAsync(string reportId)
    {
        if (!Directory.Exists(_reportsDirectory))
        {
            return Task.CompletedTask;
        }

        var files = Directory.GetFiles(_reportsDirectory, $"crash_*_{reportId[..Math.Min(8, reportId.Length)]}*.json");

        foreach (var file in files)
        {
            try
            {
                File.Delete(file);
                _logger?.LogDebug("[Issue #252] クラッシュレポート削除完了: {File}", file);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[Issue #252] クラッシュレポート削除失敗: {File}", file);
            }
        }

        return Task.CompletedTask;
    }

    #region Private Methods

    private static string GenerateReportId()
    {
        return $"{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..24];
    }

    private ExceptionInfo ExtractExceptionInfo(Exception exception)
    {
        var data = new Dictionary<string, string>();
        foreach (var key in exception.Data.Keys)
        {
            if (key != null)
            {
                data[key.ToString() ?? ""] = SanitizeText(exception.Data[key]?.ToString() ?? "");
            }
        }

        return new ExceptionInfo
        {
            Type = exception.GetType().FullName ?? exception.GetType().Name,
            Message = SanitizeText(exception.Message),
            StackTrace = SanitizeStackTrace(exception.StackTrace),
            InnerException = exception.InnerException != null
                ? ExtractExceptionInfo(exception.InnerException)
                : null,
            Source = SanitizeText(exception.Source ?? ""),
            HResult = exception.HResult,
            Data = data
        };
    }

    private SystemInfoSnapshot CaptureSystemInfo()
    {
        return new SystemInfoSnapshot
        {
            OsVersion = Environment.OSVersion.ToString(),
            OsPlatform = Environment.OSVersion.Platform.ToString(),
            MachineNameHash = HashMachineName(Environment.MachineName),
            ProcessorCount = Environment.ProcessorCount,
            Is64BitProcess = Environment.Is64BitProcess,
            WorkingSetBytes = Environment.WorkingSet,
            ClrVersion = Environment.Version.ToString(),
            Uptime = DateTime.UtcNow - _startTime,
            Culture = CultureInfo.CurrentCulture.Name,
            TimeZone = TimeZoneInfo.Local.Id
        };
    }

    private List<string> GetSanitizedLogs()
    {
        var entries = _ringBuffer.GetAllEntries();
        return entries.Select(e => SanitizeText(e.ToString())).ToList();
    }

    private static string GetBaketaVersion()
    {
        try
        {
            var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                         ?? assembly.GetName().Version?.ToString()
                         ?? "Unknown";
            return version;
        }
        catch
        {
            return "Unknown";
        }
    }

    private void EnsureDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(_reportsDirectory))
            {
                Directory.CreateDirectory(_reportsDirectory);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[Issue #252] クラッシュレポートディレクトリ作成失敗");
        }
    }

    private static string TruncateMessage(string message, int maxLength)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        return message.Length <= maxLength
            ? message
            : message[..maxLength] + "...";
    }

    #endregion

    #region Sanitization

    /// <summary>
    /// テキストからユーザー識別情報を除去
    /// </summary>
    private static string SanitizeText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        // ユーザー名パターン（Windowsパス）
        text = WindowsUserPathRegex().Replace(text, @"C:\Users\[REDACTED]\");

        // メールアドレス
        text = EmailRegex().Replace(text, "[EMAIL_REDACTED]");

        // APIキー/トークン（一般的なパターン）
        text = ApiKeyRegex().Replace(text, m => $"{m.Groups[1].Value}[API_KEY_REDACTED]");

        // Base64エンコードされた長い文字列（潜在的なシークレット）
        text = LongBase64Regex().Replace(text, "[BASE64_REDACTED]");

        // IPアドレス（ローカルアドレス以外）
        text = IpAddressRegex().Replace(text, match =>
        {
            var ip = match.Value;
            if (ip.StartsWith("127.", StringComparison.Ordinal) ||
                ip.StartsWith("192.168.", StringComparison.Ordinal) ||
                ip.StartsWith("10.", StringComparison.Ordinal))
            {
                return ip; // ローカルIPは保持
            }
            return "[IP_REDACTED]";
        });

        return text;
    }

    /// <summary>
    /// スタックトレースをサニタイズ
    /// </summary>
    private static string? SanitizeStackTrace(string? stackTrace)
    {
        if (string.IsNullOrEmpty(stackTrace))
        {
            return stackTrace;
        }

        // スタックトレース内のユーザーパスをサニタイズ
        return WindowsUserPathRegex().Replace(stackTrace, @"C:\Users\[REDACTED]\");
    }

    /// <summary>
    /// マシン名をハッシュ化（プライバシー保護）
    /// </summary>
    private static string HashMachineName(string machineName)
    {
        var bytes = Encoding.UTF8.GetBytes(machineName);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..12]; // 最初の12文字のみ
    }

    /// <summary>
    /// コンテキスト辞書をサニタイズ
    /// </summary>
    private static Dictionary<string, object?> SanitizeContext(Dictionary<string, object?>? context)
    {
        if (context == null)
        {
            return [];
        }

        var sanitized = new Dictionary<string, object?>();
        foreach (var (key, value) in context)
        {
            // センシティブなキーを除外
            var lowerKey = key.ToLowerInvariant();
            if (lowerKey.Contains("password") ||
                lowerKey.Contains("secret") ||
                lowerKey.Contains("token") ||
                lowerKey.Contains("apikey") ||
                lowerKey.Contains("credential"))
            {
                sanitized[key] = "[REDACTED]";
            }
            else
            {
                sanitized[key] = value is string s ? SanitizeText(s) : value;
            }
        }

        return sanitized;
    }

    // 正規表現（GeneratedRegex使用 - .NET 7+）
    [GeneratedRegex(@"C:\\Users\\[^\\]+\\", RegexOptions.IgnoreCase)]
    private static partial Regex WindowsUserPathRegex();

    [GeneratedRegex(@"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}")]
    private static partial Regex EmailRegex();

    [GeneratedRegex(@"(api[_-]?key|token|secret|password|bearer)\s*[=:]\s*['""]?([a-zA-Z0-9_\-\.]+)['""]?", RegexOptions.IgnoreCase)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"[A-Za-z0-9+/]{64,}={0,2}")]
    private static partial Regex LongBase64Regex();

    [GeneratedRegex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b")]
    private static partial Regex IpAddressRegex();

    #endregion
}
