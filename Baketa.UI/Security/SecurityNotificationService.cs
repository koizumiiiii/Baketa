using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// セキュリティ通知サービス
/// </summary>
/// <remarks>
/// SecurityNotificationServiceを初期化します
/// </remarks>
/// <param name="auditLogger">セキュリティ監査ログ</param>
/// <param name="logger">ロガー</param>
public sealed class SecurityNotificationService(SecurityAuditLogger auditLogger, ILogger<SecurityNotificationService>? logger = null) : IDisposable
{
    private readonly SecurityAuditLogger _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
    private bool _disposed;

    // LoggerMessage delegates
    private static readonly Action<ILogger, string, SecurityNotificationType, Exception?> _logNotificationSent =
        LoggerMessage.Define<string, SecurityNotificationType>(
            LogLevel.Information,
            new EventId(1, "SecurityNotificationSent"),
            "セキュリティ通知送信: {Email} ({NotificationType})");

    private static readonly Action<ILogger, string, SecurityNotificationType, Exception?> _logNotificationFailed =
        LoggerMessage.Define<string, SecurityNotificationType>(
            LogLevel.Error,
            new EventId(2, "SecurityNotificationFailed"),
            "セキュリティ通知送信失敗: {Email} ({NotificationType})");

    /// <summary>
    /// パスワードリセット通知を送信
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="resetToken">リセットトークン</param>
    /// <param name="ipAddress">要求元IPアドレス</param>
    /// <returns>送信結果</returns>
    public async Task<NotificationResult> SendPasswordResetNotificationAsync(
        string email, string resetToken, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(resetToken))
            return new NotificationResult(false, "無効なパラメータです");

        try
        {
            var notification = new SecurityNotification(
                Type: SecurityNotificationType.PasswordReset,
                Email: email,
                Subject: "Baketa - パスワードリセット要求",
                Message: GeneratePasswordResetMessage(resetToken, ipAddress),
                Data: new Dictionary<string, object> 
                { 
                    { "ResetToken", resetToken },
                    { "IPAddress", ipAddress ?? "Unknown" }
                }
            );

            await SendNotificationAsync(notification).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.PasswordChange,
                $"パスワードリセット通知送信: {email}",
                email, ipAddress);

            if (logger != null)
                _logNotificationSent(logger, email, SecurityNotificationType.PasswordReset, null);

            return new NotificationResult(true, "パスワードリセット通知を送信しました");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "パスワードリセット通知送信エラー: {Email}", email);
            
            if (logger != null)
                _logNotificationFailed(logger, email, SecurityNotificationType.PasswordReset, ex);

            return new NotificationResult(false, "通知送信に失敗しました");
        }
    }

    /// <summary>
    /// アカウント乗っ取り警告通知を送信
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="verificationCode">確認コード</param>
    /// <param name="suspiciousReasons">疑わしい理由</param>
    /// <param name="ipAddress">疑わしいIPアドレス</param>
    /// <returns>送信結果</returns>
    public async Task<NotificationResult> SendHijackingAlertNotificationAsync(
        string email, string verificationCode, List<string> suspiciousReasons, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(verificationCode))
            return new NotificationResult(false, "無効なパラメータです");

        try
        {
            var notification = new SecurityNotification(
                Type: SecurityNotificationType.HijackingAlert,
                Email: email,
                Subject: "Baketa - アカウントセキュリティ警告【緊急】",
                Message: GenerateHijackingAlertMessage(verificationCode, suspiciousReasons, ipAddress),
                Data: new Dictionary<string, object> 
                { 
                    { "VerificationCode", verificationCode },
                    { "SuspiciousReasons", suspiciousReasons },
                    { "IPAddress", ipAddress ?? "Unknown" }
                }
            );

            await SendNotificationAsync(notification).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.AccountProtection,
                $"乗っ取り警告通知送信: {email}",
                email, ipAddress);

            if (logger != null)
                _logNotificationSent(logger, email, SecurityNotificationType.HijackingAlert, null);

            return new NotificationResult(true, "アカウント乗っ取り警告通知を送信しました");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "乗っ取り警告通知送信エラー: {Email}", email);
            
            if (logger != null)
                _logNotificationFailed(logger, email, SecurityNotificationType.HijackingAlert, ex);

            return new NotificationResult(false, "通知送信に失敗しました");
        }
    }

    /// <summary>
    /// アカウント復旧完了通知を送信
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="recoveryType">復旧タイプ</param>
    /// <param name="ipAddress">復旧実行元IPアドレス</param>
    /// <returns>送信結果</returns>
    public async Task<NotificationResult> SendRecoveryCompletedNotificationAsync(
        string email, RecoveryType recoveryType, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return new NotificationResult(false, "無効なメールアドレスです");

        try
        {
            var notification = new SecurityNotification(
                Type: SecurityNotificationType.RecoveryCompleted,
                Email: email,
                Subject: "Baketa - アカウント復旧完了",
                Message: GenerateRecoveryCompletedMessage(recoveryType, ipAddress),
                Data: new Dictionary<string, object> 
                { 
                    { "RecoveryType", recoveryType },
                    { "IPAddress", ipAddress ?? "Unknown" }
                }
            );

            await SendNotificationAsync(notification).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.AccountRecovery,
                $"復旧完了通知送信: {email} ({recoveryType})",
                email, ipAddress);

            if (logger != null)
                _logNotificationSent(logger, email, SecurityNotificationType.RecoveryCompleted, null);

            return new NotificationResult(true, "アカウント復旧完了通知を送信しました");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "復旧完了通知送信エラー: {Email}", email);
            
            if (logger != null)
                _logNotificationFailed(logger, email, SecurityNotificationType.RecoveryCompleted, ex);

            return new NotificationResult(false, "通知送信に失敗しました");
        }
    }

    /// <summary>
    /// 疑わしい活動検出通知を送信
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="suspiciousScore">疑わしさスコア</param>
    /// <param name="reasons">疑わしい理由</param>
    /// <param name="ipAddress">疑わしいIPアドレス</param>
    /// <returns>送信結果</returns>
    public async Task<NotificationResult> SendSuspiciousActivityNotificationAsync(
        string email, double suspiciousScore, List<string> reasons, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return new NotificationResult(false, "無効なメールアドレスです");

        try
        {
            var notification = new SecurityNotification(
                Type: SecurityNotificationType.SuspiciousActivity,
                Email: email,
                Subject: "Baketa - 疑わしい活動を検出",
                Message: GenerateSuspiciousActivityMessage(suspiciousScore, reasons, ipAddress),
                Data: new Dictionary<string, object> 
                { 
                    { "SuspiciousScore", suspiciousScore },
                    { "Reasons", reasons },
                    { "IPAddress", ipAddress ?? "Unknown" }
                }
            );

            await SendNotificationAsync(notification).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSuspiciousActivity(
                $"疑わしい活動通知送信: {email}",
                email, ipAddress, 5);

            if (logger != null)
                _logNotificationSent(logger, email, SecurityNotificationType.SuspiciousActivity, null);

            return new NotificationResult(true, "疑わしい活動通知を送信しました");
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "疑わしい活動通知送信エラー: {Email}", email);
            
            if (logger != null)
                _logNotificationFailed(logger, email, SecurityNotificationType.SuspiciousActivity, ex);

            return new NotificationResult(false, "通知送信に失敗しました");
        }
    }

    /// <summary>
    /// パスワードリセット通知メッセージを生成
    /// </summary>
    private static string GeneratePasswordResetMessage(string resetToken, string? ipAddress)
    {
        var message = $@"
パスワードリセット要求を受け付けました。

リセットトークン: {resetToken}
要求元IPアドレス: {ipAddress ?? "Unknown"}
有効期限: {DateTime.UtcNow.AddMinutes(30):yyyy/MM/dd HH:mm:ss} (30分)

このリセット要求に心当たりがない場合は、アカウントが侵害された可能性があります。
すぐにサポートまでご連絡ください。

※このメールは自動送信です。返信しないでください。
";
        return message.Trim();
    }

    /// <summary>
    /// アカウント乗っ取り警告メッセージを生成
    /// </summary>
    private static string GenerateHijackingAlertMessage(string verificationCode, List<string> reasons, string? ipAddress)
    {
        var reasonsText = string.Join("\n- ", reasons);
        var message = $@"
【緊急】アカウントセキュリティ警告

あなたのアカウントで疑わしい活動が検出されました。
セキュリティ保護のため、アカウントを一時的にロックしました。

検出された疑わしい活動:
- {reasonsText}

疑わしいIPアドレス: {ipAddress ?? "Unknown"}

本人確認のため、以下の確認コードを使用してください:
確認コード: {verificationCode}

この活動に心当たりがない場合は、アカウントが乗っ取られた可能性があります。
すぐに復旧手順を実行してください。

※このメールは自動送信です。返信しないでください。
";
        return message.Trim();
    }

    /// <summary>
    /// 復旧完了通知メッセージを生成
    /// </summary>
    private static string GenerateRecoveryCompletedMessage(RecoveryType recoveryType, string? ipAddress)
    {
        var recoveryTypeText = recoveryType switch
        {
            RecoveryType.PasswordForgotten => "パスワード忘れによる復旧",
            RecoveryType.AccountHijacked => "アカウント乗っ取りからの復旧",
            _ => "不明な復旧タイプ"
        };

        var message = $@"
アカウント復旧が完了しました。

復旧タイプ: {recoveryTypeText}
復旧実行元IPアドレス: {ipAddress ?? "Unknown"}
復旧完了時刻: {DateTime.UtcNow:yyyy/MM/dd HH:mm:ss}

アカウントのセキュリティを向上させるため、以下の点をご確認ください:
- 強力なパスワードを設定してください
- 定期的にパスワードを変更してください
- 不審な活動があれば直ちに報告してください

今後とも安全にご利用いただくため、しばらくの間監視を強化します。

※このメールは自動送信です。返信しないでください。
";
        return message.Trim();
    }

    /// <summary>
    /// 疑わしい活動通知メッセージを生成
    /// </summary>
    private static string GenerateSuspiciousActivityMessage(double suspiciousScore, List<string> reasons, string? ipAddress)
    {
        var reasonsText = string.Join("\n- ", reasons);
        var riskLevel = suspiciousScore switch
        {
            >= 0.9 => "高",
            >= 0.7 => "中",
            _ => "低"
        };

        var message = $@"
疑わしい活動を検出しました。

リスクレベル: {riskLevel} (スコア: {suspiciousScore:F2})
検出IPアドレス: {ipAddress ?? "Unknown"}

検出された疑わしい活動:
- {reasonsText}

この活動に心当たりがない場合は、アカウントのセキュリティを確認してください。
必要に応じて、パスワードの変更やアカウント保護の有効化を検討してください。

※このメールは自動送信です。返信しないでください。
";
        return message.Trim();
    }

    /// <summary>
    /// 通知を送信（実際の送信処理）
    /// </summary>
    private async Task SendNotificationAsync(SecurityNotification notification)
    {
        // TODO: 実際の通知送信処理を実装
        // メール送信、プッシュ通知、SMS送信など
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        
        logger?.LogInformation("通知送信: {Email} - {Subject}", 
            notification.Email, notification.Subject);
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
            _disposed = true;
        }
    }
}

/// <summary>
/// セキュリティ通知
/// </summary>
/// <param name="Type">通知タイプ</param>
/// <param name="Email">送信先メールアドレス</param>
/// <param name="Subject">件名</param>
/// <param name="Message">メッセージ内容</param>
/// <param name="Data">追加データ</param>
public sealed record SecurityNotification(
    SecurityNotificationType Type,
    string Email,
    string Subject,
    string Message,
    Dictionary<string, object> Data);

/// <summary>
/// 通知結果
/// </summary>
/// <param name="Success">送信成功したかどうか</param>
/// <param name="Message">結果メッセージ</param>
public sealed record NotificationResult(bool Success, string Message);

/// <summary>
/// セキュリティ通知タイプ
/// </summary>
public enum SecurityNotificationType
{
    PasswordReset,
    HijackingAlert,
    RecoveryCompleted,
    SuspiciousActivity,
    AccountLocked,
    AccountUnlocked
}
