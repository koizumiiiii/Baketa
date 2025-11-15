using System.Diagnostics;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Diagnostics;

namespace Baketa.Core.Extensions;

/// <summary>
/// 診断イベント発行を簡素化する拡張メソッド
/// 既存コードからの診断イベント移行を容易にする
/// </summary>
public static class DiagnosticExtensions
{
    /// <summary>
    /// 処理成功時の診断イベントを発行
    /// </summary>
    public static async Task PublishSuccessAsync(
        this IEventAggregator eventAggregator,
        string stage,
        long processingTimeMs,
        Dictionary<string, object>? metrics = null,
        string? sessionId = null)
    {
        var diagnosticEvent = new PipelineDiagnosticEvent
        {
            Stage = stage,
            IsSuccess = true,
            ProcessingTimeMs = processingTimeMs,
            Metrics = metrics ?? [],
            Severity = DiagnosticSeverity.Information,
            SessionId = sessionId
        };

        await eventAggregator.PublishAsync(diagnosticEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// 処理失敗時の診断イベントを発行
    /// </summary>
    public static async Task PublishFailureAsync(
        this IEventAggregator eventAggregator,
        string stage,
        long processingTimeMs,
        string errorMessage,
        Dictionary<string, object>? metrics = null,
        string? sessionId = null,
        DiagnosticSeverity severity = DiagnosticSeverity.Error)
    {
        var diagnosticEvent = new PipelineDiagnosticEvent
        {
            Stage = stage,
            IsSuccess = false,
            ProcessingTimeMs = processingTimeMs,
            ErrorMessage = errorMessage,
            Metrics = metrics ?? [],
            Severity = severity,
            SessionId = sessionId
        };

        await eventAggregator.PublishAsync(diagnosticEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// 例外からの診断イベントを発行
    /// </summary>
    public static async Task PublishExceptionAsync(
        this IEventAggregator eventAggregator,
        string stage,
        Exception exception,
        long processingTimeMs = 0,
        Dictionary<string, object>? metrics = null,
        string? sessionId = null)
    {
        var enhancedMetrics = metrics ?? [];
        enhancedMetrics["ExceptionType"] = exception.GetType().Name;
        enhancedMetrics["StackTrace"] = exception.StackTrace ?? "N/A";

        var diagnosticEvent = new PipelineDiagnosticEvent
        {
            Stage = stage,
            IsSuccess = false,
            ProcessingTimeMs = processingTimeMs,
            ErrorMessage = exception.Message,
            Metrics = enhancedMetrics,
            Severity = DiagnosticSeverity.Error,
            SessionId = sessionId
        };

        await eventAggregator.PublishAsync(diagnosticEvent).ConfigureAwait(false);
    }

    /// <summary>
    /// Stopwatchを使った処理時間測定と診断イベント発行
    /// </summary>
    public static async Task PublishWithTimingAsync<T>(
        this IEventAggregator eventAggregator,
        string stage,
        Func<Task<T>> operation,
        string? sessionId = null,
        Action<Dictionary<string, object>, T>? metricsEnhancer = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await operation().ConfigureAwait(false);
            stopwatch.Stop();

            var metrics = new Dictionary<string, object>();
            metricsEnhancer?.Invoke(metrics, result);

            await eventAggregator.PublishSuccessAsync(stage, stopwatch.ElapsedMilliseconds, metrics, sessionId)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            await eventAggregator.PublishExceptionAsync(stage, ex, stopwatch.ElapsedMilliseconds,
                sessionId: sessionId).ConfigureAwait(false);

            throw; // Re-throw to maintain original exception flow
        }
    }

    /// <summary>
    /// Stopwatchを使った処理時間測定と診断イベント発行（戻り値なし）
    /// </summary>
    public static async Task PublishWithTimingAsync(
        this IEventAggregator eventAggregator,
        string stage,
        Func<Task> operation,
        string? sessionId = null,
        Dictionary<string, object>? additionalMetrics = null)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            await operation().ConfigureAwait(false);
            stopwatch.Stop();

            await eventAggregator.PublishSuccessAsync(stage, stopwatch.ElapsedMilliseconds,
                additionalMetrics, sessionId).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var metrics = additionalMetrics ?? [];
            await eventAggregator.PublishExceptionAsync(stage, ex, stopwatch.ElapsedMilliseconds,
                metrics, sessionId).ConfigureAwait(false);

            throw; // Re-throw to maintain original exception flow
        }
    }

    /// <summary>
    /// パフォーマンス警告用の診断イベント発行
    /// </summary>
    public static async Task PublishPerformanceWarningAsync(
        this IEventAggregator eventAggregator,
        string stage,
        long processingTimeMs,
        long thresholdMs,
        Dictionary<string, object>? metrics = null,
        string? sessionId = null)
    {
        var enhancedMetrics = metrics ?? [];
        enhancedMetrics["ThresholdMs"] = thresholdMs;
        enhancedMetrics["ExceededBy"] = processingTimeMs - thresholdMs;

        var diagnosticEvent = new PipelineDiagnosticEvent
        {
            Stage = stage,
            IsSuccess = true, // 処理は成功したが性能問題
            ProcessingTimeMs = processingTimeMs,
            ErrorMessage = $"処理時間が閾値を超過: {processingTimeMs}ms > {thresholdMs}ms",
            Metrics = enhancedMetrics,
            Severity = DiagnosticSeverity.Warning,
            SessionId = sessionId
        };

        await eventAggregator.PublishAsync(diagnosticEvent).ConfigureAwait(false);
    }
}
