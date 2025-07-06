using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// ログイン試行の追跡とブルートフォース攻撃対策
/// </summary>
public sealed class LoginAttemptTracker : IDisposable
{
    private readonly ConcurrentDictionary<string, AttemptInfo> _attempts = new();
    private readonly Timer _cleanupTimer;
    private readonly ILogger<LoginAttemptTracker>? _logger;
    private bool _disposed;

    // 設定値（ゲームアプリケーション向けに緩和）
    private const int MaxAttempts = 5;
    private const int LockoutMinutes = 10;        // 15分 → 10分に短縮
    private const int ProgressiveLockoutThreshold = 15;  // 10回 → 15回に緩和
    private const int CleanupIntervalMinutes = 60;       // 30分 → 1時間に延長

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, int, Exception?> _logAttemptBlocked =
        LoggerMessage.Define<string, int>(
            LogLevel.Warning,
            new EventId(1, "AttemptBlocked"),
            "ログイン試行がブロックされました: {Email} (試行回数: {Attempts})");

    private static readonly Action<ILogger, string, Exception?> _logSuspiciousActivity =
        LoggerMessage.Define<string>(
            LogLevel.Error,
            new EventId(2, "SuspiciousActivity"),
            "疑わしい活動を検出: {Email} - 短時間での大量試行");

    /// <summary>
    /// LoginAttemptTrackerを初期化します
    /// </summary>
    /// <param name="logger">ロガー</param>
    public LoginAttemptTracker(ILogger<LoginAttemptTracker>? logger = null)
    {
        _logger = logger;
        
        // 定期的なクリーンアップタイマー
        _cleanupTimer = new Timer(CleanupExpiredAttempts, null, 
            TimeSpan.FromMinutes(CleanupIntervalMinutes), 
            TimeSpan.FromMinutes(CleanupIntervalMinutes));
    }

    /// <summary>
    /// 指定されたメールアドレスがブロックされているかチェック
    /// </summary>
    /// <param name="email">チェックするメールアドレス</param>
    /// <returns>ブロックされている場合true</returns>
    public bool IsBlocked(string email)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(email))
            return false;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        
        if (!_attempts.TryGetValue(normalizedEmail, out var attemptInfo))
            return false;

        var timeSinceLastAttempt = DateTime.UtcNow - attemptInfo.LastAttempt;
        var lockoutDuration = CalculateLockoutDuration(attemptInfo.Attempts);

        bool isBlocked = attemptInfo.Attempts >= MaxAttempts && timeSinceLastAttempt < lockoutDuration;

        if (isBlocked && _logger != null)
        {
            _logAttemptBlocked(_logger, normalizedEmail, attemptInfo.Attempts, null);
        }

        return isBlocked;
    }

    /// <summary>
    /// ログイン失敗を記録
    /// </summary>
    /// <param name="email">ログインに失敗したメールアドレス</param>
    public void RecordFailedAttempt(string email)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        var now = DateTime.UtcNow;

        _attempts.AddOrUpdate(normalizedEmail,
            new AttemptInfo(1, now, now),
            (key, existing) =>
            {
                var timeSinceFirst = now - existing.FirstAttempt;
                var newAttempts = existing.Attempts + 1;

                // 短時間での大量試行を検出
                if (timeSinceFirst < TimeSpan.FromMinutes(5) && newAttempts >= ProgressiveLockoutThreshold)
                {
                    if (_logger != null)
                        _logSuspiciousActivity(_logger, normalizedEmail, null);
                }

                return new AttemptInfo(newAttempts, existing.FirstAttempt, now);
            });
    }

    /// <summary>
    /// ログイン成功時に試行回数をリセット
    /// </summary>
    /// <param name="email">ログインに成功したメールアドレス</param>
    public void RecordSuccessfulLogin(string email)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        _attempts.TryRemove(normalizedEmail, out _);
    }

    /// <summary>
    /// 残りロックアウト時間を取得
    /// </summary>
    /// <param name="email">チェックするメールアドレス</param>
    /// <returns>残り時間、ブロックされていない場合はnull</returns>
    public TimeSpan? GetRemainingLockoutTime(string email)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        
        if (!_attempts.TryGetValue(normalizedEmail, out var attemptInfo))
            return null;

        if (attemptInfo.Attempts < MaxAttempts)
            return null;

        var timeSinceLastAttempt = DateTime.UtcNow - attemptInfo.LastAttempt;
        var lockoutDuration = CalculateLockoutDuration(attemptInfo.Attempts);
        var remainingTime = lockoutDuration - timeSinceLastAttempt;

        return remainingTime > TimeSpan.Zero ? remainingTime : null;
    }

    /// <summary>
    /// 管理者による手動リセット
    /// </summary>
    /// <param name="email">リセットするメールアドレス</param>
    public void ResetAttempts(string email)
    {
        ThrowIfDisposed();
        
        if (string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        _attempts.TryRemove(normalizedEmail, out _);
        
        _logger?.LogInformation("手動でログイン試行回数をリセット: {Email}", normalizedEmail);
    }

    /// <summary>
    /// 統計情報を取得
    /// </summary>
    /// <returns>統計情報</returns>
    public LoginAttemptStats GetStats()
    {
        ThrowIfDisposed();
        
        int totalTrackedEmails = _attempts.Count;
        int currentlyBlocked = 0;

        foreach (var kvp in _attempts)
        {
            var timeSinceLastAttempt = DateTime.UtcNow - kvp.Value.LastAttempt;
            var lockoutDuration = CalculateLockoutDuration(kvp.Value.Attempts);
            
            if (kvp.Value.Attempts >= MaxAttempts && timeSinceLastAttempt < lockoutDuration)
            {
                currentlyBlocked++;
            }
        }

        return new LoginAttemptStats(totalTrackedEmails, currentlyBlocked);
    }

    /// <summary>
    /// ロックアウト期間を計算（累進的、ゲームアプリ向けに緩和）
    /// </summary>
    private static TimeSpan CalculateLockoutDuration(int attempts)
    {
        return attempts switch
        {
            >= 20 => TimeSpan.FromHours(12),     // 12時間（24時間から短縮）
            >= 15 => TimeSpan.FromHours(2),      // 2時間（4時間から短縮）
            >= 10 => TimeSpan.FromMinutes(30),   // 30分（1時間から短縮）
            _ => TimeSpan.FromMinutes(LockoutMinutes) // 10分
        };
    }

    /// <summary>
    /// 期限切れの試行記録をクリーンアップ
    /// </summary>
    private void CleanupExpiredAttempts(object? state)
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;
        var expiredKeys = new List<string>();

        foreach (var kvp in _attempts)
        {
            var timeSinceLastAttempt = now - kvp.Value.LastAttempt;
            var maxRetentionTime = TimeSpan.FromHours(48); // 48時間で完全削除

            if (timeSinceLastAttempt > maxRetentionTime)
            {
                expiredKeys.Add(kvp.Key);
            }
        }

        foreach (var key in expiredKeys)
        {
            _attempts.TryRemove(key, out _);
        }

        if (expiredKeys.Count > 0)
        {
            _logger?.LogDebug("期限切れログイン試行記録をクリーンアップ: {Count}件", expiredKeys.Count);
        }
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
            _attempts.Clear();
            _disposed = true;
        }
    }

    /// <summary>
    /// 試行情報を格納するレコード
    /// </summary>
    private sealed record AttemptInfo(int Attempts, DateTime FirstAttempt, DateTime LastAttempt);
}

/// <summary>
/// ログイン試行統計情報
/// </summary>
/// <param name="TotalTrackedEmails">追跡中のメールアドレス数</param>
/// <param name="CurrentlyBlocked">現在ブロック中のアカウント数</param>
public sealed record LoginAttemptStats(int TotalTrackedEmails, int CurrentlyBlocked);