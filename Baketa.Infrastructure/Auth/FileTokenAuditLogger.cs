using System.IO;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Auth;

/// <summary>
/// File-based implementation of token audit logging
/// Records all token operations for security compliance and incident investigation
/// </summary>
public sealed class FileTokenAuditLogger : ITokenAuditLogger, IDisposable
{
    private bool _disposed;
    private readonly ILogger<FileTokenAuditLogger> _logger;
    private readonly string _logFilePath;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // Log rotation settings
    private const long MaxLogFileSizeBytes = 10 * 1024 * 1024; // 10MB
    private static readonly TimeSpan ArchiveRetentionPeriod = TimeSpan.FromDays(30);

    /// <summary>
    /// Initialize file token audit logger with configuration
    /// </summary>
    public FileTokenAuditLogger(
        ILogger<FileTokenAuditLogger> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Get log file path from configuration, or use default
        var configuredPath = configuration?["Logging:TokenAuditLogPath"];
        _logFilePath = string.IsNullOrWhiteSpace(configuredPath)
            ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", "token_audit.log")
            : configuredPath;

        _logger.LogDebug("TokenAuditLogger initialized with log path: {LogPath}", _logFilePath);
    }

    /// <inheritdoc/>
    public async Task LogTokenIssuedAsync(
        string userId,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        var logEntry = FormatLogEntry(
            "TOKEN_ISSUED",
            $"UserId={MaskUserId(userId)} | ExpiresAt={expiresAt:O}");

        _logger.LogInformation(
            "Token issued for user {UserId}, expires at {ExpiresAt}",
            MaskUserId(userId),
            expiresAt);

        await AppendToLogFileAsync(logEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LogTokenRefreshedAsync(
        string userId,
        DateTime oldExpiry,
        DateTime newExpiry,
        CancellationToken cancellationToken = default)
    {
        var logEntry = FormatLogEntry(
            "TOKEN_REFRESHED",
            $"UserId={MaskUserId(userId)} | OldExpiry={oldExpiry:O} | NewExpiry={newExpiry:O}");

        _logger.LogInformation(
            "Token refreshed for user {UserId}, old expiry: {OldExpiry}, new expiry: {NewExpiry}",
            MaskUserId(userId),
            oldExpiry,
            newExpiry);

        await AppendToLogFileAsync(logEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LogTokenRevokedAsync(
        string userId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var logEntry = FormatLogEntry(
            "TOKEN_REVOKED",
            $"UserId={MaskUserId(userId)} | Reason={SanitizeReason(reason)}");

        _logger.LogWarning(
            "Token revoked for user {UserId}, reason: {Reason}",
            MaskUserId(userId),
            reason);

        await AppendToLogFileAsync(logEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task LogTokenValidationFailedAsync(
        string reason,
        CancellationToken cancellationToken = default)
    {
        var logEntry = FormatLogEntry(
            "TOKEN_VALIDATION_FAILED",
            $"Reason={SanitizeReason(reason)}");

        _logger.LogWarning("Token validation failed: {Reason}", reason);

        await AppendToLogFileAsync(logEntry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Format a log entry with timestamp and event type
    /// </summary>
    private static string FormatLogEntry(string eventType, string details)
    {
        return $"[{DateTime.UtcNow:O}] {eventType} | {details}";
    }

    /// <summary>
    /// Mask user ID for privacy (show first 4 chars and last 4 chars)
    /// </summary>
    private static string MaskUserId(string userId)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return "unknown";
        }

        if (userId.Length <= 8)
        {
            return $"{userId[..Math.Min(2, userId.Length)]}****";
        }

        return $"{userId[..4]}****{userId[^4..]}";
    }

    /// <summary>
    /// Sanitize reason string to prevent log injection
    /// </summary>
    private static string SanitizeReason(string reason)
    {
        if (string.IsNullOrEmpty(reason))
        {
            return "unspecified";
        }

        // Remove newlines and pipe characters that could break log format
        return reason
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("|", "-");
    }

    /// <summary>
    /// Append log entry to file with thread safety
    /// </summary>
    private async Task AppendToLogFileAsync(string logEntry, CancellationToken cancellationToken)
    {
        try
        {
            await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // Ensure directory exists
                var directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogDebug("Created audit log directory: {Directory}", directory);
                }

                // Check for log rotation before writing
                await RotateLogIfNeededAsync().ConfigureAwait(false);

                // Append to log file
                await File.AppendAllTextAsync(
                    _logFilePath,
                    logEntry + Environment.NewLine,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _writeLock.Release();
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is expected, don't log as error
            throw;
        }
        catch (Exception ex)
        {
            // Log error but don't fail the application
            _logger.LogError(ex, "Failed to write to audit log file: {LogPath}", _logFilePath);
        }
    }

    /// <summary>
    /// Rotate log file if it exceeds size limit (archive instead of delete)
    /// </summary>
    private async Task RotateLogIfNeededAsync()
    {
        try
        {
            if (!File.Exists(_logFilePath))
            {
                return;
            }

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length <= MaxLogFileSizeBytes)
            {
                return;
            }

            // Archive the current log file with timestamp
            var directory = Path.GetDirectoryName(_logFilePath);
            var archiveFileName = $"token_audit_{DateTime.UtcNow:yyyyMMdd_HHmmss}.log";
            var archivePath = Path.Combine(directory!, archiveFileName);

            File.Move(_logFilePath, archivePath);
            _logger.LogInformation(
                "Audit log rotated: {OriginalPath} -> {ArchivePath}",
                _logFilePath,
                archivePath);

            // Cleanup old archives
            await CleanupOldArchivesAsync(directory!).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to rotate audit log file");
        }
    }

    /// <summary>
    /// Delete archived log files older than retention period
    /// </summary>
    private Task CleanupOldArchivesAsync(string directory)
    {
        try
        {
            var archiveFiles = Directory.GetFiles(directory, "token_audit_*.log");
            var cutoffDate = DateTime.UtcNow - ArchiveRetentionPeriod;

            foreach (var file in archiveFiles)
            {
                // Skip the current log file
                if (file.Equals(_logFilePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var fileInfo = new FileInfo(file);
                if (fileInfo.CreationTimeUtc < cutoffDate)
                {
                    File.Delete(file);
                    _logger.LogDebug("Deleted old audit log archive: {FilePath}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup old audit log archives");
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writeLock.Dispose();
    }
}
