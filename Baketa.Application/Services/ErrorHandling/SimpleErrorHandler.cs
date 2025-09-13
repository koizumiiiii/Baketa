using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.ErrorHandling;
using Microsoft.Extensions.Logging;

namespace Baketa.Application.Services.ErrorHandling;

/// <summary>
/// シンプルなエラーハンドリング実装
/// 複雑なエラー階層を排除し、実用的なエラー処理を提供
/// </summary>
public sealed class SimpleErrorHandler : ISimpleErrorHandler
{
    private readonly ILogger<SimpleErrorHandler> _logger;
    private readonly ConcurrentDictionary<string, ErrorStatistics> _errorStats;

    public SimpleErrorHandler(ILogger<SimpleErrorHandler> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _errorStats = new ConcurrentDictionary<string, ErrorStatistics>();
    }

    /// <summary>
    /// エラーをログに記録し、必要に応じて復旧処理を実行
    /// </summary>
    public async Task<bool> HandleErrorAsync(SimpleError errorInfo)
    {
        if (errorInfo == null)
            throw new ArgumentNullException(nameof(errorInfo));

        await Task.CompletedTask.ConfigureAwait(false);

        // 統計情報を更新
        var operationKey = errorInfo.Operation;
        _errorStats.AddOrUpdate(operationKey,
            new ErrorStatistics { Count = 1, LastOccurrence = errorInfo.Timestamp },
            (key, existing) => new ErrorStatistics
            {
                Count = existing.Count + 1,
                LastOccurrence = errorInfo.Timestamp
            });

        // エラーレベル別にログ出力
        switch (errorInfo.Level)
        {
            case ErrorLevel.Information:
                _logger.LogInformation("Operation: {Operation}, Message: {Message}, Context: {Context}",
                    errorInfo.Operation, errorInfo.Message, errorInfo.Context);
                return true;

            case ErrorLevel.Warning:
                _logger.LogWarning("Operation: {Operation}, Message: {Message}, Context: {Context}",
                    errorInfo.Operation, errorInfo.Message, errorInfo.Context);
                return await TryRecoveryAsync(errorInfo).ConfigureAwait(false);

            case ErrorLevel.Error:
                _logger.LogError(errorInfo.Exception,
                    "Operation: {Operation}, Message: {Message}, Context: {Context}",
                    errorInfo.Operation, errorInfo.Message ?? errorInfo.Exception?.Message, errorInfo.Context);
                return await TryRecoveryAsync(errorInfo).ConfigureAwait(false);

            case ErrorLevel.Critical:
                _logger.LogCritical(errorInfo.Exception,
                    "CRITICAL ERROR - Operation: {Operation}, Message: {Message}, Context: {Context}",
                    errorInfo.Operation, errorInfo.Message ?? errorInfo.Exception?.Message, errorInfo.Context);

                // クリティカルエラーの場合は追加処理
                await NotifyCriticalErrorAsync(errorInfo).ConfigureAwait(false);
                return false; // クリティカルエラーは復旧不可とみなす

            default:
                _logger.LogError("Unknown error level: {Level} for operation: {Operation}",
                    errorInfo.Level, errorInfo.Operation);
                return false;
        }
    }

    /// <summary>
    /// エラーの重要度を評価
    /// </summary>
    public ErrorLevel EvaluateErrorLevel(Exception exception)
    {
        if (exception == null)
            return ErrorLevel.Information;

        // 例外タイプに基づいてレベルを決定
        return exception switch
        {
            ObjectDisposedException => ErrorLevel.Critical, // ObjectDisposedException は高優先度
            OutOfMemoryException => ErrorLevel.Critical,
            StackOverflowException => ErrorLevel.Critical,
            AccessViolationException => ErrorLevel.Critical,
            ArgumentNullException or ArgumentException => ErrorLevel.Error,
            InvalidOperationException => ErrorLevel.Error,
            SystemException => ErrorLevel.Error,
            ApplicationException => ErrorLevel.Warning,
            _ => ErrorLevel.Error
        };
    }

    /// <summary>
    /// システムの健全性チェック
    /// </summary>
    public async Task<bool> CheckSystemHealthAsync()
    {
        await Task.CompletedTask.ConfigureAwait(false);

        try
        {
            // 基本的な健全性指標をチェック
            var criticalErrorCount = GetCriticalErrorCount();
            var memoryPressure = GC.GetTotalMemory(false);
            var activeErrors = _errorStats.Count;

            _logger.LogDebug("Health check - Critical errors: {CriticalErrors}, Memory: {Memory} bytes, Active error types: {ActiveErrors}",
                criticalErrorCount, memoryPressure, activeErrors);

            // 健全性判定基準
            var isHealthy = criticalErrorCount < 5 &&
                           memoryPressure < 1024 * 1024 * 100 && // 100MB threshold
                           activeErrors < 20;

            if (!isHealthy)
            {
                _logger.LogWarning("System health degraded - Critical errors: {CriticalErrors}, Memory pressure: {Memory} bytes",
                    criticalErrorCount, memoryPressure);
            }

            return isHealthy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform system health check");
            return false;
        }
    }

    #region Private Methods

    private async Task<bool> TryRecoveryAsync(SimpleError errorInfo)
    {
        if (!errorInfo.ShouldRetry)
            return false;

        await Task.CompletedTask.ConfigureAwait(false);

        // 操作別の復旧戦略
        var recoverySuccess = errorInfo.Operation switch
        {
            "ProcessTranslationAsync" => await RecoverTranslationErrorAsync(errorInfo).ConfigureAwait(false),
            "CreateSafeImageAsync" => await RecoverImageErrorAsync(errorInfo).ConfigureAwait(false),
            "CaptureImageAsync" => await RecoverCaptureErrorAsync(errorInfo).ConfigureAwait(false),
            _ => await GenericRecoveryAsync(errorInfo).ConfigureAwait(false)
        };

        if (recoverySuccess)
        {
            _logger.LogInformation("Recovery successful for operation: {Operation}", errorInfo.Operation);
        }
        else
        {
            _logger.LogWarning("Recovery failed for operation: {Operation}", errorInfo.Operation);
        }

        return recoverySuccess;
    }

    private async Task<bool> RecoverTranslationErrorAsync(SimpleError errorInfo)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // 翻訳エラーの復旧戦略
        if (errorInfo.Exception is ObjectDisposedException)
        {
            _logger.LogInformation("Detected ObjectDisposedException in translation - suggesting image lifecycle review");
            return false; // オブジェクト破棄エラーは復旧不可
        }

        // その他のエラーは再試行可能とみなす
        return true;
    }

    private async Task<bool> RecoverImageErrorAsync(SimpleError errorInfo)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // 画像作成エラーの復旧戦略
        if (errorInfo.Exception is OutOfMemoryException)
        {
            _logger.LogWarning("Memory pressure detected - triggering emergency GC collection as last resort");

            // 🚨 緊急避難措置：明示的なGC実行
            // この処理はパフォーマンスの観点から多用すべきではない
            // 根本的な原因（メモリリーク等）の解決が優先されるべき
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return true;
        }

        return true;
    }

    private async Task<bool> RecoverCaptureErrorAsync(SimpleError errorInfo)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // キャプチャエラーの復旧戦略
        _logger.LogInformation("Capture error detected - may retry with different approach");
        return true;
    }

    private async Task<bool> GenericRecoveryAsync(SimpleError errorInfo)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // 汎用復旧戦略
        var stats = _errorStats.GetValueOrDefault(errorInfo.Operation, new ErrorStatistics());

        // 連続エラーが多い場合は復旧不可とみなす
        if (stats.Count > 10)
        {
            _logger.LogWarning("Too many errors for operation {Operation} - giving up recovery",
                errorInfo.Operation);
            return false;
        }

        return true;
    }

    private async Task NotifyCriticalErrorAsync(SimpleError errorInfo)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        _logger.LogCritical("SYSTEM CRITICAL ERROR NOTIFICATION");
        _logger.LogCritical("Operation: {Operation}", errorInfo.Operation);
        _logger.LogCritical("Context: {Context}", errorInfo.Context);
        _logger.LogCritical("Exception: {Exception}", errorInfo.Exception?.ToString());

        // 将来的には外部通知システムと連携
        // - メール通知
        // - Slack通知
        // - システムイベントログ記録
    }

    private int GetCriticalErrorCount()
    {
        var cutoffTime = DateTime.UtcNow.AddMinutes(-10);
        int count = 0;

        foreach (var stat in _errorStats.Values)
        {
            if (stat.LastOccurrence > cutoffTime)
                count += stat.Count;
        }

        return count;
    }

    #endregion

    /// <summary>
    /// エラー統計情報
    /// </summary>
    private sealed class ErrorStatistics
    {
        public int Count { get; set; }
        public DateTime LastOccurrence { get; set; }
    }
}