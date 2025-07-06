using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Baketa.Core.Abstractions.Auth;

namespace Baketa.UI.Security;

/// <summary>
/// パスワードリセット管理クラス
/// </summary>
public sealed class PasswordResetManager : IDisposable
{
    private readonly SecurityAuditLogger _auditLogger;
    private readonly ILogger<PasswordResetManager>? _logger;
    private readonly ConcurrentDictionary<string, ResetRequest> _activeResets = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    // 設定値
    private static readonly TimeSpan ResetTokenValidDuration = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan RateLimitWindow = TimeSpan.FromMinutes(10);
    private const int MaxResetsPerWindow = 3;
    private const int TokenLength = 32;

    // LoggerMessage delegates
    private static readonly Action<ILogger, string, string, Exception?> _logResetRequested =
        LoggerMessage.Define<string, string>(
            LogLevel.Information,
            new EventId(1, "PasswordResetRequested"),
            "パスワードリセット要求: {Email} (トークン: {Token})");

    private static readonly Action<ILogger, string, Exception?> _logResetCompleted =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "PasswordResetCompleted"),
            "パスワードリセット完了: {Email}");

    private static readonly Action<ILogger, string, string, Exception?> _logSuspiciousReset =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3, "SuspiciousPasswordReset"),
            "疑わしいパスワードリセット: {Email} - {Reason}");

    /// <summary>
    /// PasswordResetManagerを初期化します
    /// </summary>
    /// <param name="auditLogger">セキュリティ監査ログ</param>
    /// <param name="logger">ロガー</param>
    public PasswordResetManager(SecurityAuditLogger auditLogger, ILogger<PasswordResetManager>? logger = null)
    {
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger;

        // 期限切れトークンのクリーンアップタイマー
        _cleanupTimer = new Timer(CleanupExpiredTokens, null, 
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10));
    }

    /// <summary>
    /// パスワードリセットを要求
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="ipAddress">要求元IPアドレス</param>
    /// <returns>リセットトークン（成功時）、null（失敗時）</returns>
    public Task<string?> RequestPasswordResetAsync(string email, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return Task.FromResult<string?>(null);

        var normalizedEmail = email.ToLowerInvariant().Trim();

        // レート制限チェック
        if (IsRateLimited(normalizedEmail))
        {
            _auditLogger.LogSuspiciousActivity(
                $"パスワードリセットレート制限超過: {normalizedEmail}",
                normalizedEmail, ipAddress, 6);

            if (_logger != null)
                _logSuspiciousReset(_logger, normalizedEmail, "レート制限超過", null);

            return Task.FromResult<string?>(null);
        }

        // セキュアなトークン生成
        var resetToken = GenerateSecureToken();
        var expiresAt = DateTime.UtcNow.Add(ResetTokenValidDuration);

        var resetRequest = new ResetRequest(
            Email: normalizedEmail,
            Token: resetToken,
            RequestedAt: DateTime.UtcNow,
            ExpiresAt: expiresAt,
            IPAddress: ipAddress ?? "Unknown",
            IsUsed: false
        );

        // 既存のリセット要求を上書き（最新のもののみ有効）
        _activeResets.AddOrUpdate(normalizedEmail, resetRequest, (key, existing) => resetRequest);

        // 監査ログ記録
        _auditLogger.LogSecurityEvent(
            SecurityAuditLogger.SecurityEventType.PasswordChange,
            $"パスワードリセット要求: {normalizedEmail}",
            normalizedEmail, ipAddress);

        if (_logger != null)
            _logResetRequested(_logger, normalizedEmail, resetToken, null);

        return Task.FromResult<string?>(resetToken);
    }

    /// <summary>
    /// リセットトークンの検証
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="token">リセットトークン</param>
    /// <returns>検証結果</returns>
    public TokenValidationResult ValidateResetToken(string email, string token)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            return TokenValidationResult.Invalid;

        var normalizedEmail = email.ToLowerInvariant().Trim();

        if (!_activeResets.TryGetValue(normalizedEmail, out var resetRequest))
            return TokenValidationResult.NotFound;

        if (resetRequest.IsUsed)
            return TokenValidationResult.AlreadyUsed;

        if (DateTime.UtcNow > resetRequest.ExpiresAt)
        {
            // 期限切れトークンを削除
            _activeResets.TryRemove(normalizedEmail, out _);
            return TokenValidationResult.Expired;
        }

        if (!string.Equals(resetRequest.Token, token, StringComparison.Ordinal))
            return TokenValidationResult.Invalid;

        return TokenValidationResult.Valid;
    }

    /// <summary>
    /// パスワードリセット実行
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="token">リセットトークン</param>
    /// <param name="newPassword">新しいパスワード</param>
    /// <param name="ipAddress">実行元IPアドレス</param>
    /// <returns>実行結果</returns>
    public async Task<ResetExecutionResult> ExecutePasswordResetAsync(
        string email, string token, string newPassword, string? ipAddress = null)
    {
        ThrowIfDisposed();

        var validationResult = ValidateResetToken(email, token);
        if (validationResult != TokenValidationResult.Valid)
        {
            return new ResetExecutionResult(false, GetValidationErrorMessage(validationResult));
        }

        var normalizedEmail = email.ToLowerInvariant().Trim();

        // パスワード強度チェック
        if (!InputValidator.IsStrongPassword(newPassword))
        {
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.SecurityPolicyViolation,
                $"弱いパスワードでのリセット試行: {normalizedEmail}",
                normalizedEmail, ipAddress);

            return new ResetExecutionResult(false, "パスワードが要件を満たしていません");
        }

        try
        {
            // トークンを使用済みにマーク
            if (_activeResets.TryGetValue(normalizedEmail, out var resetRequest))
            {
                var usedRequest = resetRequest with { IsUsed = true };
                _activeResets.TryUpdate(normalizedEmail, usedRequest, resetRequest);
            }

            // TODO: 実際のパスワード更新処理
            // await _authService.UpdatePasswordAsync(normalizedEmail, newPassword);
            await Task.Delay(100).ConfigureAwait(false); // Placeholder

            // 成功ログ
            _auditLogger.LogPasswordChange(normalizedEmail, true, "PasswordReset", ipAddress);

            if (_logger != null)
                _logResetCompleted(_logger, normalizedEmail, null);

            // 使用済みトークンを削除
            _activeResets.TryRemove(normalizedEmail, out _);

            return new ResetExecutionResult(true, "パスワードが正常に更新されました");
        }
        catch (Exception ex)
        {
            _auditLogger.LogPasswordChange(normalizedEmail, false, "PasswordReset", ipAddress);
            _logger?.LogError(ex, "パスワードリセット実行エラー: {Email}", normalizedEmail);
            
            return new ResetExecutionResult(false, "パスワード更新中にエラーが発生しました");
        }
    }

    /// <summary>
    /// アクティブなリセット要求をキャンセル
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="reason">キャンセル理由</param>
    public void CancelPasswordReset(string email, string reason = "User request")
    {
        ThrowIfDisposed();

        var normalizedEmail = email.ToLowerInvariant().Trim();
        if (_activeResets.TryRemove(normalizedEmail, out _))
        {
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.PasswordChange,
                $"パスワードリセットキャンセル: {normalizedEmail} - {reason}",
                normalizedEmail);
        }
    }

    /// <summary>
    /// レート制限チェック
    /// </summary>
    private bool IsRateLimited(string email)
    {
        var now = DateTime.UtcNow;
        var windowStart = now - RateLimitWindow;
        
        int recentRequests = 0;
        foreach (var kvp in _activeResets)
        {
            if (kvp.Key == email && kvp.Value.RequestedAt >= windowStart)
            {
                recentRequests++;
            }
        }

        return recentRequests >= MaxResetsPerWindow;
    }

    /// <summary>
    /// セキュアなトークン生成
    /// </summary>
    private static string GenerateSecureToken()
    {
        var bytes = new byte[TokenLength];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        
        // Base64エンコード（URLセーフ）
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// 期限切れトークンのクリーンアップ
    /// </summary>
    private void CleanupExpiredTokens(object? state)
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;
        var expiredKeys = new List<string>();

        foreach (var kvp in _activeResets)
        {
            if (now > kvp.Value.ExpiresAt || 
                (kvp.Value.IsUsed && now > kvp.Value.RequestedAt.AddHours(1)))
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _activeResets.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger?.LogDebug("期限切れパスワードリセットトークンをクリーンアップ: {Count}件", expiredKeys.Count);
        }
    }

    /// <summary>
    /// 検証エラーメッセージを取得
    /// </summary>
    private static string GetValidationErrorMessage(TokenValidationResult result)
    {
        return result switch
        {
            TokenValidationResult.NotFound => "リセット要求が見つかりません",
            TokenValidationResult.Expired => "リセットトークンの有効期限が切れています",
            TokenValidationResult.AlreadyUsed => "このリセットトークンは既に使用されています",
            TokenValidationResult.Invalid => "無効なリセットトークンです",
            _ => "トークン検証エラー"
        };
    }

    /// <summary>
    /// オブジェクトが破棄されているかチェック
    /// </summary>
    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// リソースを解放
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _cleanupTimer?.Dispose();
            _activeResets.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// パスワードリセット要求情報
/// </summary>
/// <param name="Email">メールアドレス</param>
/// <param name="Token">リセットトークン</param>
/// <param name="RequestedAt">要求日時</param>
/// <param name="ExpiresAt">期限切れ日時</param>
/// <param name="IPAddress">要求元IPアドレス</param>
/// <param name="IsUsed">使用済みフラグ</param>
public sealed record ResetRequest(
    string Email,
    string Token,
    DateTime RequestedAt,
    DateTime ExpiresAt,
    string IPAddress,
    bool IsUsed);

/// <summary>
/// トークン検証結果
/// </summary>
public enum TokenValidationResult
{
    Valid,
    Invalid,
    NotFound,
    Expired,
    AlreadyUsed
}

/// <summary>
/// リセット実行結果
/// </summary>
/// <param name="Success">成功したかどうか</param>
/// <param name="Message">結果メッセージ</param>
public sealed record ResetExecutionResult(bool Success, string Message);
