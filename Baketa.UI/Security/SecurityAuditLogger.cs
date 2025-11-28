using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// セキュリティイベントの監査ログ記録クラス
/// </summary>
/// <remarks>
/// SecurityAuditLoggerを初期化します
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class SecurityAuditLogger(ILogger<SecurityAuditLogger> logger)
{
    private readonly ILogger<SecurityAuditLogger> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    // JsonSerializerOptionsのキャッシュ（パフォーマンス最適化）
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// メールアドレスをマスク（例: te***@example.com）
    /// </summary>
    private static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return "[unknown]";

        var atIndex = email.IndexOf('@');
        if (atIndex <= 0)
            return "[invalid-email]";

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        // ローカル部分を最初の2文字 + *** に置換
        var maskedLocal = localPart.Length <= 2
            ? "***"
            : localPart[..2] + "***";

        return maskedLocal + domain;
    }

    // セキュリティイベントタイプ
    public enum SecurityEventType
    {
        LoginAttempt,
        LoginSuccess,
        LoginFailure,
        LoginBlocked,
        PasswordChange,
        AccountLockout,
        SuspiciousActivity,
        SessionExpired,
        UnauthorizedAccess,
        DataAccess,
        PermissionDenied,
        SecurityPolicyViolation,
        AccountProtection,
        AccountRecovery
    }

    // ログレベルマッピング
    private static readonly Dictionary<SecurityEventType, LogLevel> EventLogLevels = new()
    {
        { SecurityEventType.LoginAttempt, LogLevel.Information },
        { SecurityEventType.LoginSuccess, LogLevel.Information },
        { SecurityEventType.LoginFailure, LogLevel.Warning },
        { SecurityEventType.LoginBlocked, LogLevel.Warning },
        { SecurityEventType.PasswordChange, LogLevel.Information },
        { SecurityEventType.AccountLockout, LogLevel.Warning },
        { SecurityEventType.SuspiciousActivity, LogLevel.Error },
        { SecurityEventType.SessionExpired, LogLevel.Information },
        { SecurityEventType.UnauthorizedAccess, LogLevel.Error },
        { SecurityEventType.DataAccess, LogLevel.Information },
        { SecurityEventType.PermissionDenied, LogLevel.Warning },
        { SecurityEventType.SecurityPolicyViolation, LogLevel.Error },
        { SecurityEventType.AccountProtection, LogLevel.Warning },
        { SecurityEventType.AccountRecovery, LogLevel.Information }
    };

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, string, string, string, string, DateTime, Exception?> _logSecurityEvent =
        LoggerMessage.Define<string, string, string, string, string, DateTime>(
            LogLevel.Information, // 実際のレベルは動的に設定
            new EventId(1000, "SecurityEvent"),
            "SECURITY_EVENT | Type: {EventType} | User: {User} | IP: {IPAddress} | Details: {Details} | Source: {Source} | Timestamp: {Timestamp}");

    /// <summary>
    /// セキュリティイベントをログに記録
    /// </summary>
    /// <param name="eventType">イベントタイプ</param>
    /// <param name="details">詳細情報</param>
    /// <param name="userInfo">ユーザー情報</param>
    /// <param name="ipAddress">IPアドレス</param>
    /// <param name="additionalData">追加データ</param>
    /// <param name="source">呼び出し元メソッド名</param>
    public void LogSecurityEvent(
        SecurityEventType eventType,
        string details,
        string? userInfo = null,
        string? ipAddress = null,
        object? additionalData = null,
        [CallerMemberName] string source = "")
    {
        var logLevel = EventLogLevels.GetValueOrDefault(eventType, LogLevel.Information);
        var timestamp = DateTime.UtcNow;
        var normalizedUserInfo = userInfo ?? "Anonymous";
        var normalizedIpAddress = ipAddress ?? GetLocalIPAddress();

        // 構造化データの準備
        var eventData = new SecurityEventData
        {
            EventType = eventType.ToString(),
            Details = details,
            UserInfo = normalizedUserInfo,
            IPAddress = normalizedIpAddress,
            Source = source,
            Timestamp = timestamp,
            AdditionalData = additionalData
        };

        // 構造化ログの出力
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["SecurityEvent"] = true,
            ["EventType"] = eventType.ToString(),
            ["UserId"] = normalizedUserInfo,
            ["IPAddress"] = normalizedIpAddress,
            ["Source"] = source,
            ["AdditionalData"] = additionalData ?? new { }
        });

        _logger.Log(logLevel, "SECURITY_EVENT | Type: {EventType} | User: {User} | IP: {IPAddress} | Details: {Details} | Source: {Source} | Timestamp: {Timestamp}",
            eventType.ToString(), normalizedUserInfo, normalizedIpAddress, details, source, timestamp);

        // 重要なセキュリティイベントの場合、追加の詳細ログ
        if (logLevel >= LogLevel.Warning)
        {
            LogDetailedSecurityEvent(eventData);
        }
    }

    /// <summary>
    /// ログイン試行をログに記録
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="success">成功したかどうか</param>
    /// <param name="failureReason">失敗理由（失敗時のみ）</param>
    /// <param name="ipAddress">IPアドレス</param>
    public void LogLoginAttempt(string email, bool success, string? failureReason = null, string? ipAddress = null)
    {
        var maskedEmail = MaskEmail(email);
        var eventType = success ? SecurityEventType.LoginSuccess : SecurityEventType.LoginFailure;
        var details = success
            ? $"ログイン成功: {maskedEmail}"
            : $"ログイン失敗: {maskedEmail} - {failureReason}";

        LogSecurityEvent(eventType, details, maskedEmail, ipAddress);
    }

    /// <summary>
    /// アカウントロックアウトをログに記録
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="attemptCount">試行回数</param>
    /// <param name="lockoutDuration">ロックアウト期間</param>
    /// <param name="ipAddress">IPアドレス</param>
    public void LogAccountLockout(string email, int attemptCount, TimeSpan lockoutDuration, string? ipAddress = null)
    {
        var maskedEmail = MaskEmail(email);
        var details = $"アカウントロックアウト: {maskedEmail} - 試行回数: {attemptCount}, ロックアウト期間: {lockoutDuration.TotalMinutes}分";

        LogSecurityEvent(SecurityEventType.AccountLockout, details, maskedEmail, ipAddress, new
        {
            AttemptCount = attemptCount,
            LockoutDuration = lockoutDuration,
            LockoutUntil = DateTime.UtcNow.Add(lockoutDuration)
        });
    }

    /// <summary>
    /// 疑わしい活動をログに記録
    /// </summary>
    /// <param name="activity">活動内容</param>
    /// <param name="userInfo">ユーザー情報</param>
    /// <param name="ipAddress">IPアドレス</param>
    /// <param name="riskLevel">リスクレベル（1-10）</param>
    public void LogSuspiciousActivity(string activity, string? userInfo = null, string? ipAddress = null, int riskLevel = 5)
    {
        var details = $"疑わしい活動 (リスクレベル: {riskLevel}): {activity}";

        LogSecurityEvent(SecurityEventType.SuspiciousActivity, details, userInfo, ipAddress, new
        {
            Activity = activity,
            RiskLevel = riskLevel,
            RequiresInvestigation = riskLevel >= 7
        });
    }

    /// <summary>
    /// パスワード変更をログに記録
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="success">成功したかどうか</param>
    /// <param name="initiatedBy">変更実行者</param>
    /// <param name="ipAddress">IPアドレス</param>
    public void LogPasswordChange(string email, bool success, string initiatedBy = "User", string? ipAddress = null)
    {
        var details = success
            ? $"パスワード変更成功: {email} (実行者: {initiatedBy})"
            : $"パスワード変更失敗: {email} (実行者: {initiatedBy})";

        LogSecurityEvent(SecurityEventType.PasswordChange, details, email, ipAddress, new
        {
            Success = success,
            InitiatedBy = initiatedBy
        });
    }

    /// <summary>
    /// データアクセスをログに記録
    /// </summary>
    /// <param name="resource">アクセスされたリソース</param>
    /// <param name="action">実行されたアクション</param>
    /// <param name="userInfo">ユーザー情報</param>
    /// <param name="success">成功したかどうか</param>
    /// <param name="ipAddress">IPアドレス</param>
    public void LogDataAccess(string resource, string action, string? userInfo = null, bool success = true, string? ipAddress = null)
    {
        var details = $"データアクセス: {action} on {resource} - {(success ? "成功" : "失敗")}";

        LogSecurityEvent(SecurityEventType.DataAccess, details, userInfo, ipAddress, new
        {
            Resource = resource,
            Action = action,
            Success = success
        });
    }

    /// <summary>
    /// セッション期限切れをログに記録
    /// </summary>
    /// <param name="userInfo">ユーザー情報</param>
    /// <param name="sessionDuration">セッション期間</param>
    /// <param name="reason">期限切れの理由</param>
    public void LogSessionExpired(string? userInfo, TimeSpan sessionDuration, string reason = "Timeout")
    {
        var details = $"セッション期限切れ: {userInfo} - 期間: {sessionDuration.TotalMinutes:F1}分, 理由: {reason}";

        LogSecurityEvent(SecurityEventType.SessionExpired, details, userInfo, additionalData: new
        {
            SessionDuration = sessionDuration,
            Reason = reason
        });
    }

    /// <summary>
    /// 詳細なセキュリティイベントログを記録
    /// </summary>
    private void LogDetailedSecurityEvent(SecurityEventData eventData)
    {
        try
        {
            var serializedData = JsonSerializer.Serialize(eventData, SerializerOptions);

            _logger.LogInformation("SECURITY_EVENT_DETAIL: {SerializedData}", serializedData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "セキュリティイベントの詳細ログ記録に失敗");
        }
    }

    /// <summary>
    /// ローカルIPアドレスを取得
    /// </summary>
    private static string GetLocalIPAddress()
    {
        try
        {
            var hostname = Dns.GetHostName();
            var addresses = Dns.GetHostAddresses(hostname);

            foreach (var addr in addresses)
            {
                if (addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(addr))
                {
                    return addr.ToString();
                }
            }

            return "127.0.0.1";
        }
        catch
        {
            return "Unknown";
        }
    }

    /// <summary>
    /// セキュリティイベントデータ構造
    /// </summary>
    private sealed record SecurityEventData
    {
        public string EventType { get; init; } = string.Empty;
        public string Details { get; init; } = string.Empty;
        public string UserInfo { get; init; } = string.Empty;
        public string IPAddress { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public DateTime Timestamp { get; init; }
        public object? AdditionalData { get; init; }
    }
}
