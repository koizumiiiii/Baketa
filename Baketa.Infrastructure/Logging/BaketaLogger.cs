using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Logging;
using Baketa.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Logging;

/// <summary>
/// Baketa統一ログ実装
/// Console、ファイル、ILoggerの出力を統合し、重複を排除
/// </summary>
public sealed class BaketaLogger : IBaketaLogger, IDisposable
{
    private readonly ILogger<BaketaLogger>? _microsoftLogger;
    private readonly string _debugLogFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    private BaketaLogLevel _currentLogLevel = BaketaLogLevel.Information;
    private bool _debugModeEnabled;
    private bool _disposed;

    /// <summary>
    /// デバッグログの出力パス
    /// </summary>
    private static readonly string DefaultDebugLogPath = Path.Combine(
        BaketaSettingsPaths.LogDirectory,
        $"baketa_debug_{DateTime.Now:yyyyMMdd}.log");

    public BaketaLogger(
        ILogger<BaketaLogger>? microsoftLogger = null,
        string? customLogPath = null,
        bool debugModeEnabled = false)
    {
        _microsoftLogger = microsoftLogger;
        _debugLogFilePath = customLogPath ?? DefaultDebugLogPath;
        _debugModeEnabled = debugModeEnabled;

        // ログディレクトリの作成
        Directory.CreateDirectory(Path.GetDirectoryName(_debugLogFilePath) ?? BaketaSettingsPaths.LogDirectory);
    }

    /// <inheritdoc />
    public void LogTranslationEvent(string eventType, string message, object? data = null, BaketaLogLevel level = BaketaLogLevel.Information)
    {
        if (!ShouldLog(level))
            return;

        var logData = new Dictionary<string, object>
        {
            ["EventType"] = eventType,
            ["Category"] = "Translation"
        };

        if (data != null)
        {
            logData["AdditionalData"] = data;
        }

        var entry = new BaketaLogEntry(level, "Translation", message, logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogPerformanceMetrics(string operation, TimeSpan duration, bool success, Dictionary<string, object>? additionalMetrics = null)
    {
        if (!ShouldLog(BaketaLogLevel.Information))
            return;

        var logData = new Dictionary<string, object>
        {
            ["Operation"] = operation,
            ["Duration"] = $"{duration.TotalMilliseconds:F2}ms",
            ["Success"] = success,
            ["Category"] = "Performance"
        };

        if (additionalMetrics != null)
        {
            foreach (var metric in additionalMetrics)
            {
                logData[metric.Key] = metric.Value;
            }
        }

        var level = success ? BaketaLogLevel.Information : BaketaLogLevel.Warning;
        var message = $"📊 {operation}: {duration.TotalMilliseconds:F1}ms ({(success ? "成功" : "失敗")})";

        var entry = new BaketaLogEntry(level, "Performance", message, logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogUserAction(string action, Dictionary<string, object>? context = null, BaketaLogLevel level = BaketaLogLevel.Information)
    {
        if (!ShouldLog(level))
            return;

        var logData = new Dictionary<string, object>
        {
            ["Action"] = action,
            ["Category"] = "UserAction"
        };

        if (context != null)
        {
            foreach (var ctx in context)
            {
                logData[ctx.Key] = ctx.Value;
            }
        }

        var entry = new BaketaLogEntry(level, "UserAction", $"🎯 ユーザーアクション: {action}", logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogDebug(string component, string message, object? data = null)
    {
        if (!_debugModeEnabled || !ShouldLog(BaketaLogLevel.Debug))
            return;

        var logData = data != null ? new Dictionary<string, object> { ["Data"] = data } : null;
        var entry = new BaketaLogEntry(BaketaLogLevel.Debug, component, message, logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogError(string component, string message, Exception? exception = null, object? data = null)
    {
        var logData = new Dictionary<string, object>
        {
            ["Category"] = "Error"
        };

        if (data != null)
        {
            logData["Data"] = data;
        }

        if (exception != null)
        {
            logData["ExceptionType"] = exception.GetType().Name;
            logData["StackTrace"] = exception.StackTrace ?? string.Empty;
        }

        var entry = new BaketaLogEntry(BaketaLogLevel.Error, component, message, logData, exception);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogWarning(string component, string message, object? data = null)
    {
        if (!ShouldLog(BaketaLogLevel.Warning))
            return;

        var logData = data != null ? new Dictionary<string, object> { ["Data"] = data } : null;
        var entry = new BaketaLogEntry(BaketaLogLevel.Warning, component, message, logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public void LogInformation(string component, string message, object? data = null)
    {
        if (!ShouldLog(BaketaLogLevel.Information))
            return;

        var logData = data != null ? new Dictionary<string, object> { ["Data"] = data } : null;
        var entry = new BaketaLogEntry(BaketaLogLevel.Information, component, message, logData);
        WriteLogEntry(entry);
    }

    /// <inheritdoc />
    public async Task FlushAsync()
    {
        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // ファイルシステムへのフラッシュ
            // Microsoft.Extensions.LoggingのフラッシュはILoggerExternalScopeProviderで対応
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <inheritdoc />
    public void SetDebugMode(bool enabled)
    {
        _debugModeEnabled = enabled;
    }

    /// <inheritdoc />
    public void SetLogLevel(BaketaLogLevel level)
    {
        _currentLogLevel = level;
    }

    /// <summary>
    /// ログレベルに基づいてログ出力すべきかを判定
    /// </summary>
    private bool ShouldLog(BaketaLogLevel level) => level >= _currentLogLevel;

    /// <summary>
    /// ログエントリを実際に書き込む
    /// </summary>
    private void WriteLogEntry(BaketaLogEntry entry)
    {
        // 1. コンソール出力（開発時・デバッグ用）
        if (_debugModeEnabled || entry.Level >= BaketaLogLevel.Warning)
        {
            Console.WriteLine(entry.Format(includeData: _debugModeEnabled));
        }

        // 2. Microsoft.Extensions.Logging出力（構造化ログ）
        if (_microsoftLogger != null)
        {
            var logLevel = ConvertToMicrosoftLogLevel(entry.Level);
            using var scope = _microsoftLogger.BeginScope(entry.Data);

            if (entry.Exception != null)
            {
                _microsoftLogger.Log(logLevel, entry.Exception, "{Component}: {Message}", entry.Component, entry.Message);
            }
            else
            {
                _microsoftLogger.Log(logLevel, "{Component}: {Message}", entry.Component, entry.Message);
            }
        }

        // 3. デバッグファイル出力（非同期）
        Task.Run(async () => await WriteToFileAsync(entry).ConfigureAwait(false));
    }

    /// <summary>
    /// ファイルに非同期書き込み
    /// </summary>
    private async Task WriteToFileAsync(BaketaLogEntry entry)
    {
        if (!_debugModeEnabled && entry.Level < BaketaLogLevel.Warning)
            return;

        await _writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var logLine = entry.Format(includeData: true) + Environment.NewLine;
            await File.AppendAllTextAsync(_debugLogFilePath, logLine).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ログ出力でエラーが起きた場合は、最低限コンソールに出力
            Console.WriteLine($"⚠️ ログファイル書き込みエラー: {ex.Message}");
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// BaketaLogLevelをMicrosoft.Extensions.LoggingのLogLevelに変換
    /// </summary>
    private static LogLevel ConvertToMicrosoftLogLevel(BaketaLogLevel level) => level switch
    {
        BaketaLogLevel.Trace => LogLevel.Trace,
        BaketaLogLevel.Debug => LogLevel.Debug,
        BaketaLogLevel.Information => LogLevel.Information,
        BaketaLogLevel.Warning => LogLevel.Warning,
        BaketaLogLevel.Error => LogLevel.Error,
        BaketaLogLevel.Critical => LogLevel.Critical,
        _ => LogLevel.Information
    };

    public void Dispose()
    {
        if (_disposed)
            return;

        FlushAsync().GetAwaiter().GetResult();
        _writeLock.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// BaketaLogger拡張メソッド
/// </summary>
public static class BaketaLoggerExtensions
{
    /// <summary>
    /// 翻訳開始ログ（便利メソッド）
    /// </summary>
    public static void LogTranslationStart(this IBaketaLogger logger, string sourceText, string sourceLanguage, string targetLanguage)
    {
        logger.LogTranslationEvent("TranslationStart", $"翻訳開始: '{sourceText}'", new
        {
            SourceLanguage = sourceLanguage,
            TargetLanguage = targetLanguage,
            TextLength = sourceText.Length
        });
    }

    /// <summary>
    /// 翻訳完了ログ（便利メソッド）
    /// </summary>
    public static void LogTranslationCompleted(this IBaketaLogger logger, string sourceText, string translatedText, TimeSpan duration)
    {
        logger.LogTranslationEvent("TranslationCompleted", $"翻訳完了: '{sourceText}' → '{translatedText}'", new
        {
            Duration = $"{duration.TotalMilliseconds:F1}ms",
            SourceLength = sourceText.Length,
            TranslatedLength = translatedText.Length
        });
    }

    /// <summary>
    /// OCR完了ログ（便利メソッド）
    /// </summary>
    public static void LogOcrCompleted(this IBaketaLogger logger, int detectedTextCount, TimeSpan processingTime)
    {
        logger.LogTranslationEvent("OcrCompleted", $"OCR完了: {detectedTextCount}個のテキスト検出", new
        {
            DetectedCount = detectedTextCount,
            ProcessingTime = $"{processingTime.TotalMilliseconds:F1}ms"
        });
    }
}
