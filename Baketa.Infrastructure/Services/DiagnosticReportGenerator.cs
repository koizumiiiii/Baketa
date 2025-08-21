using System.IO;
using System.Reflection;
using System.Text.Json;
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Events.Diagnostics;
using Baketa.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// 診断レポート生成サービスの実装
/// JSON形式でのレポート生成とファイル書き込み
/// </summary>
public sealed class DiagnosticReportGenerator : IDiagnosticReportGenerator
{
    private readonly ILogger<DiagnosticReportGenerator> _logger;
    private static readonly string ReportsDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), 
        "Baketa", "Reports");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public DiagnosticReportGenerator(ILogger<DiagnosticReportGenerator> logger)
    {
        _logger = logger;
        EnsureReportsDirectoryExists();
    }

    public async Task<string> GenerateReportAsync(
        IEnumerable<PipelineDiagnosticEvent> events, 
        string reportType,
        string? userComment = null,
        CancellationToken cancellationToken = default)
    {
        var systemInfo = GetBasicSystemInfo();
        return await GenerateComprehensiveReportAsync(events, reportType, systemInfo, userComment, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<string> GenerateComprehensiveReportAsync(
        IEnumerable<PipelineDiagnosticEvent> events,
        string reportType,
        Dictionary<string, object>? systemInfo = null,
        string? userComment = null,
        CancellationToken cancellationToken = default)
    {
        var eventsList = events.ToList();
        var reportId = GenerateReportId(reportType);
        var fileName = $"{reportType}_{DateTime.Now:yyyyMMdd_HHmmss}_{reportId[..8]}.json";
        var filePath = Path.Combine(ReportsDirectory, fileName);

        var report = new DiagnosticReport
        {
            ReportId = reportId,
            GeneratedAt = DateTime.UtcNow,
            BaketaVersion = GetBaketaVersion(),
            SystemInfo = systemInfo ?? GetBasicSystemInfo(),
            PipelineEvents = eventsList,
            UserComment = userComment,
            ReportType = reportType,
            IsReviewed = false
        };

        try
        {
            var jsonContent = JsonSerializer.Serialize(report, JsonOptions);
            
            // SafeFileWriterを使用してファイル競合を回避
            SafeFileWriter.AppendTextSafely(filePath, jsonContent);
            
            _logger.LogInformation("診断レポート生成完了: {FilePath}, イベント数: {EventCount}", 
                filePath, eventsList.Count);

            return filePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "診断レポート生成エラー: {ReportType}", reportType);
            
            // フォールバック: エラー情報だけでも保存
            var errorReport = CreateErrorFallbackReport(reportType, ex, eventsList.Count);
            var errorFilePath = Path.Combine(ReportsDirectory, $"error_{DateTime.Now:yyyyMMdd_HHmmss}.json");
            
            SafeFileWriter.AppendTextSafely(errorFilePath, JsonSerializer.Serialize(errorReport, JsonOptions));
            
            return errorFilePath;
        }
    }

    private static string GenerateReportId(string reportType)
    {
        return $"{reportType}_{DateTime.UtcNow:yyyyMMddHHmmss}_{Guid.NewGuid():N}"[..32];
    }

    private static string GetBaketaVersion()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
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

    private static Dictionary<string, object> GetBasicSystemInfo()
    {
        return new Dictionary<string, object>
        {
            ["MachineName"] = Environment.MachineName,
            ["OSVersion"] = Environment.OSVersion.ToString(),
            ["OSPlatform"] = Environment.OSVersion.Platform.ToString(),
            ["ProcessorCount"] = Environment.ProcessorCount,
            ["Is64BitProcess"] = Environment.Is64BitProcess,
            ["WorkingSet"] = Environment.WorkingSet,
            ["CLRVersion"] = Environment.Version.ToString(),
            ["CurrentDirectory"] = Environment.CurrentDirectory,
            ["SystemPageSize"] = Environment.SystemPageSize,
            ["Timestamp"] = DateTime.UtcNow.ToString("O")
        };
    }

    private object CreateErrorFallbackReport(string reportType, Exception ex, int eventCount)
    {
        return new
        {
            ReportId = GenerateReportId($"error_{reportType}"),
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            ReportType = $"error_{reportType}",
            BaketaVersion = GetBaketaVersion(),
            Error = new
            {
                Type = ex.GetType().Name,
                Message = ex.Message,
                StackTrace = ex.StackTrace
            },
            OriginalEventCount = eventCount,
            SystemInfo = GetBasicSystemInfo()
        };
    }

    private static void EnsureReportsDirectoryExists()
    {
        try
        {
            if (!Directory.Exists(ReportsDirectory))
            {
                Directory.CreateDirectory(ReportsDirectory);
            }
        }
        catch (Exception ex)
        {
            // ディレクトリ作成に失敗した場合はコンソールに出力
            Console.WriteLine($"⚠️ [DIAGNOSTIC] レポートディレクトリ作成失敗: {ex.Message}");
        }
    }
}