using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// セキュアなセッション管理クラス
/// </summary>
/// <remarks>
/// SecureSessionManagerを初期化します
/// </remarks>
/// <param name="logger">ロガー</param>
public sealed class SecureSessionManager(ILogger<SecureSessionManager>? logger = null) : IDisposable
{
    private Timer? _sessionTimer;
    private Timer? _warningTimer;
    private bool _disposed;

    // セッション設定
    public static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromDays(7);         // 標準セッション: 1週間
    public static readonly TimeSpan ExtendedSessionTimeout = TimeSpan.FromDays(30);       // 拡張セッション: 1ヶ月（Remember Me）
    public static readonly TimeSpan MaxSessionTimeout = TimeSpan.FromDays(90);            // 最大セッション: 3ヶ月
    public static readonly TimeSpan SessionWarningTime = TimeSpan.FromDays(1);            // 警告タイミング: 1日前（実際は使用しない）

    // イベント
    public event EventHandler<SessionEventArgs>? SessionExpiring;
    public event EventHandler<SessionEventArgs>? SessionExpired;
    public event EventHandler<SessionEventArgs>? SessionExtended;

    // LoggerMessage delegates for structured logging
    private static readonly Action<ILogger, string, TimeSpan, Exception?> _logSessionStarted =
        LoggerMessage.Define<string, TimeSpan>(
            LogLevel.Information,
            new EventId(1, "SessionStarted"),
            "セッション開始: {UserId} (有効期限: {Timeout})");

    private static readonly Action<ILogger, string, Exception?> _logSessionExpired =
        LoggerMessage.Define<string>(
            LogLevel.Information,
            new EventId(2, "SessionExpired"),
            "セッション期限切れ: {UserId}");

    private static readonly Action<ILogger, string, TimeSpan, Exception?> _logSessionExtended =
        LoggerMessage.Define<string, TimeSpan>(
            LogLevel.Information,
            new EventId(3, "SessionExtended"),
            "セッション延長: {UserId} (新しい有効期限: {NewTimeout})");

    private static readonly Action<ILogger, string, Exception?> _logSuspiciousSessionActivity =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(4, "SuspiciousSessionActivity"),
            "疑わしいセッション活動: {Details}");

    /// <summary>
    /// 現在のセッション情報
    /// </summary>
    public AuthSession? CurrentSession { get; private set; }

    /// <summary>
    /// セッションが有効かどうか
    /// </summary>
    public bool IsSessionValid => CurrentSession != null && DateTime.UtcNow < CurrentSession.ExpiresAt;

    /// <summary>
    /// セッションを開始します
    /// </summary>
    /// <param name="session">セッション情報</param>
    /// <param name="rememberMe">自動ログインを有効にするか</param>
    /// <param name="customTimeout">カスタムタイムアウト（指定時のみ）</param>
    public void StartSession(AuthSession session, bool rememberMe = false, TimeSpan? customTimeout = null)
    {
        ThrowIfDisposed();

        ArgumentNullException.ThrowIfNull(session);

        // 既存セッションの無効化
        InvalidateSession();

        // タイムアウト時間の決定
        var timeout = customTimeout ?? (rememberMe ? ExtendedSessionTimeout : DefaultSessionTimeout);

        // 最大制限の適用
        if (timeout > MaxSessionTimeout)
            timeout = MaxSessionTimeout;

        // セッション情報の更新
        var expiresAt = DateTime.UtcNow.Add(timeout);
        CurrentSession = session with { ExpiresAt = expiresAt };

        // タイマーの設定
        SetupSessionTimers(timeout);

        // セッションIDの検証
        ValidateSessionSecurity(session);

        if (logger != null)
            _logSessionStarted(logger, session.User.Id, timeout, null);
    }

    /// <summary>
    /// セッションを延長します
    /// </summary>
    /// <param name="additionalTime">延長時間</param>
    /// <returns>延長が成功した場合true</returns>
    public bool ExtendSession(TimeSpan? additionalTime = null)
    {
        ThrowIfDisposed();

        if (CurrentSession == null)
            return false;

        var extension = additionalTime ?? DefaultSessionTimeout;
        var newExpiryTime = DateTime.UtcNow.Add(extension);

        // 最大セッション時間を超えないように制限
        var maxAllowedTime = CurrentSession.ExpiresAt.Subtract(DefaultSessionTimeout).Add(MaxSessionTimeout);
        if (newExpiryTime > maxAllowedTime)
        {
            newExpiryTime = maxAllowedTime;
        }

        CurrentSession = CurrentSession with { ExpiresAt = newExpiryTime };

        // タイマーの再設定
        var remainingTime = newExpiryTime - DateTime.UtcNow;
        SetupSessionTimers(remainingTime);

        SessionExtended?.Invoke(this, new SessionEventArgs(CurrentSession));

        if (logger != null)
            _logSessionExtended(logger, CurrentSession.User.Id, remainingTime, null);

        return true;
    }

    /// <summary>
    /// セッションを無効化します
    /// </summary>
    public void InvalidateSession()
    {
        if (_disposed)
            return;

        var sessionToInvalidate = CurrentSession;
        CurrentSession = null;

        // タイマーの停止
        _sessionTimer?.Dispose();
        _warningTimer?.Dispose();
        _sessionTimer = null;
        _warningTimer = null;

        if (sessionToInvalidate != null)
        {
            SessionExpired?.Invoke(this, new SessionEventArgs(sessionToInvalidate));

            if (logger != null)
                _logSessionExpired(logger, sessionToInvalidate.User.Id, null);
        }
    }

    /// <summary>
    /// セッションアクティビティを記録（自動延長機能）
    /// </summary>
    public void RecordActivity()
    {
        ThrowIfDisposed();

        if (CurrentSession == null)
            return;

        var now = DateTime.UtcNow;
        var timeUntilExpiry = CurrentSession.ExpiresAt - now;

        // セッションの残り時間が1週間以下になったら自動延長
        // 長期セッションでは頻繁な延長を避け、必要時のみ実行
        if (timeUntilExpiry < TimeSpan.FromDays(7))
        {
            ExtendSession();
        }
    }

    /// <summary>
    /// セッションの残り時間を取得
    /// </summary>
    /// <returns>残り時間、セッションがない場合はnull</returns>
    public TimeSpan? GetRemainingTime()
    {
        if (CurrentSession == null)
            return null;

        var remaining = CurrentSession.ExpiresAt - DateTime.UtcNow;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    /// <summary>
    /// セッションタイマーを設定
    /// </summary>
    private void SetupSessionTimers(TimeSpan timeout)
    {
        // 既存タイマーの停止
        _sessionTimer?.Dispose();
        _warningTimer?.Dispose();

        // セッション期限切れタイマーのみ設定（警告は不要）
        _sessionTimer = new Timer(OnSessionExpired, null, timeout, Timeout.InfiniteTimeSpan);

        // 事前警告は無効化（ユーザビリティを重視）
        // 長期セッションでは警告による中断を避ける
    }

    /// <summary>
    /// セッション期限切れ警告のコールバック
    /// </summary>
    private void OnSessionExpiring(object? _)
    {
        if (CurrentSession != null)
        {
            SessionExpiring?.Invoke(this, new SessionEventArgs(CurrentSession));
        }
    }

    /// <summary>
    /// セッション期限切れのコールバック
    /// </summary>
    private void OnSessionExpired(object? state)
    {
        InvalidateSession();
    }

    /// <summary>
    /// セッションのセキュリティ検証
    /// </summary>
    private void ValidateSessionSecurity(AuthSession session)
    {
        // アクセストークンの長さチェック
        if (string.IsNullOrWhiteSpace(session.AccessToken) || session.AccessToken.Length < 32)
        {
            if (logger != null)
                _logSuspiciousSessionActivity(logger, "短すぎるアクセストークン", null);
        }

        // リフレッシュトークンの長さチェック
        if (string.IsNullOrWhiteSpace(session.RefreshToken) || session.RefreshToken.Length < 32)
        {
            if (logger != null)
                _logSuspiciousSessionActivity(logger, "短すぎるリフレッシュトークン", null);
        }

        // セッション時間の妥当性チェック
        var sessionDuration = session.ExpiresAt - DateTime.UtcNow;
        if (sessionDuration > MaxSessionTimeout)
        {
            if (logger != null)
                _logSuspiciousSessionActivity(logger, $"異常に長いセッション時間: {sessionDuration}", null);
        }
    }

    /// <summary>
    /// セッション情報のハッシュ化（整合性チェック用）
    /// </summary>
    public static string GenerateSessionHash(AuthSession session)
    {
        var data = $"{session.AccessToken}:{session.User.Id}:{session.ExpiresAt:O}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(bytes);
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
            InvalidateSession();
            _disposed = true;
        }
    }
}

/// <summary>
/// セッションイベント引数
/// </summary>
/// <param name="Session">セッション情報</param>
public sealed record SessionEventArgs(AuthSession Session);
