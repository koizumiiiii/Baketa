using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Baketa.Core.Abstractions.CrashReporting;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.CrashReporting;

/// <summary>
/// [Issue #287] クラッシュレポート送信サービス
/// IHttpClientFactory経由でHttpClientを取得し、JwtTokenAuthHandlerでJWT認証を行う
/// </summary>
/// <param name="httpClient">IHttpClientFactory経由で取得されたHttpClient</param>
/// <param name="logger">ロガー</param>
public sealed class CrashReportSender(
    HttpClient httpClient,
    ILogger<CrashReportSender> logger) : ICrashReportSender
{
    /// <summary>
    /// HttpClient名（DI登録時に使用）
    /// </summary>
    public const string HttpClientName = "CrashReportSender";

    /// <summary>
    /// クラッシュレポート送信先エンドポイント
    /// </summary>
    private const string CrashReportEndpoint = "/api/crash-report";

    private readonly HttpClient _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    private readonly ILogger<CrashReportSender> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        CrashReport report,
        bool includeSystemInfo,
        bool includeLogs,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // リクエストボディ作成
            var stackTrace = GetEffectiveStackTrace(report.Exception);

            var requestBody = new Dictionary<string, object?>
            {
                ["report_id"] = report.ReportId,
                ["crash_timestamp"] = report.CrashedAt.ToString("O"),
                ["error_message"] = report.Exception.Message,
                ["stack_trace"] = stackTrace,
                ["app_version"] = report.BaketaVersion,
                ["os_version"] = report.SystemInfo.OsVersion,
                ["include_system_info"] = includeSystemInfo,
                ["include_logs"] = includeLogs
            };

            // システム情報を含める場合
            if (includeSystemInfo)
            {
                requestBody["system_info"] = new Dictionary<string, object?>
                {
                    ["cpu"] = $"{report.SystemInfo.ProcessorCount} cores",
                    ["ram_mb"] = report.SystemInfo.WorkingSetBytes / 1024 / 1024,
                    ["dotnet_version"] = report.SystemInfo.ClrVersion,
                    ["is_64bit"] = report.SystemInfo.Is64BitProcess
                };
            }

            // ログを含める場合
            if (includeLogs && report.RecentLogs.Count > 0)
            {
                requestBody["logs"] = string.Join(Environment.NewLine, report.RecentLogs);
            }

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            // JwtTokenAuthHandler経由でJWT認証が自動付与される
            var response = await _httpClient.PostAsync(CrashReportEndpoint, content, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[Issue #287] クラッシュレポート送信成功: {ReportId}", report.ReportId);
                return true;
            }

            var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(
                "[Issue #287] クラッシュレポート送信失敗: StatusCode={StatusCode}, Body={Body}",
                response.StatusCode, errorBody);
            return false;
        }
        catch (TaskCanceledException)
        {
            _logger.LogWarning("[Issue #287] クラッシュレポート送信タイムアウト: {ReportId}", report.ReportId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Issue #287] クラッシュレポート送信エラー: {ReportId}", report.ReportId);
            return false;
        }
    }

    /// <summary>
    /// AggregateExceptionなどから有効なスタックトレースを取得
    /// </summary>
    private static string? GetEffectiveStackTrace(ExceptionInfo exception)
    {
        var stackTrace = exception.StackTrace;

        if (string.IsNullOrEmpty(stackTrace))
        {
            // InnerExceptionチェーンを走査してスタックトレースを収集
            var innerTraces = new List<string>();
            var currentException = exception.InnerException;

            while (currentException != null)
            {
                if (!string.IsNullOrEmpty(currentException.StackTrace))
                {
                    innerTraces.Add(currentException.StackTrace);
                }
                currentException = currentException.InnerException;
            }

            if (innerTraces.Count > 0)
            {
                stackTrace = string.Join(
                    $"{Environment.NewLine}--- End of Inner Exception Stack Trace ---{Environment.NewLine}",
                    innerTraces);
            }
        }

        return stackTrace;
    }
}
