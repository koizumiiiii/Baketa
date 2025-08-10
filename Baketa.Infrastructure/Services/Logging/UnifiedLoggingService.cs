using System;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Logging;

namespace Baketa.Infrastructure.Services.Logging;

/// <summary>
/// 統一ログサービスの実装
/// Console.WriteLine、File.AppendAllText、DebugLogUtilityを統一し、
/// ILoggerベースの標準ロギングに移行するサービス
/// </summary>
public sealed class UnifiedLoggingService : IUnifiedLoggingService
{
    private readonly ILogger<UnifiedLoggingService> _logger;
    private readonly bool _enableConsoleOutput;

    public UnifiedLoggingService(ILogger<UnifiedLoggingService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _enableConsoleOutput = Environment.GetEnvironmentVariable("BAKETA_ENABLE_CONSOLE_LOG") == "true";
    }

    /// <inheritdoc />
    public void LogInformation(string message)
    {
        _logger.LogInformation("{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[INFO] {message}");
        }
    }

    /// <inheritdoc />
    public void LogInformation(string template, params object[] args)
    {
        _logger.LogInformation(template, args);
        
        if (_enableConsoleOutput)
        {
            try
            {
                var formattedMessage = string.Format(template, args);
                Console.WriteLine($"[INFO] {formattedMessage}");
            }
            catch
            {
                Console.WriteLine($"[INFO] {template} (format error)");
            }
        }
    }

    /// <inheritdoc />
    public void LogWarning(string message)
    {
        _logger.LogWarning("{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[WARN] {message}");
        }
    }

    /// <inheritdoc />
    public void LogWarning(Exception exception, string message)
    {
        _logger.LogWarning(exception, "{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[WARN] {message}: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public void LogError(string message)
    {
        _logger.LogError("{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[ERROR] {message}");
        }
    }

    /// <inheritdoc />
    public void LogError(Exception exception, string message)
    {
        _logger.LogError(exception, "{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[ERROR] {message}: {exception.Message}");
        }
    }

    /// <inheritdoc />
    public void LogDebug(string message)
    {
        _logger.LogDebug("{Message}", message);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[DEBUG] {message}");
        }
    }

    /// <inheritdoc />
    public void LogDebug(string template, params object[] args)
    {
        _logger.LogDebug(template, args);
        
        if (_enableConsoleOutput)
        {
            try
            {
                var formattedMessage = string.Format(template, args);
                Console.WriteLine($"[DEBUG] {formattedMessage}");
            }
            catch
            {
                Console.WriteLine($"[DEBUG] {template} (format error)");
            }
        }
    }

    /// <inheritdoc />
    public void WriteDebugLog(string message)
    {
        // 旧DebugLogUtilityの互換機能 - ILoggerのDebugレベルとして統一
        var threadId = Environment.CurrentManagedThreadId;
        var formattedMessage = $"[T{threadId:D2}] {message}";
        
        _logger.LogDebug("{DebugMessage}", formattedMessage);
        
        if (_enableConsoleOutput)
        {
            Console.WriteLine($"[DEBUG-FILE] {formattedMessage}");
        }
    }

    /// <inheritdoc />
    public void WriteConsole(string message)
    {
        // 開発・デバッグ用のコンソール専用出力
        Console.WriteLine(message);
        
        // 同時にLoggerにも記録（Traceレベル）
        _logger.LogTrace("{ConsoleMessage}", message);
    }
}