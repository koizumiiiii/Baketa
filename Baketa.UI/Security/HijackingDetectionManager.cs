using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// アカウント乗っ取り検出・対応管理クラス
/// </summary>
public sealed class HijackingDetectionManager : IDisposable
{
    private readonly SecurityAuditLogger _auditLogger;
    private readonly ILogger<HijackingDetectionManager>? _logger;
    private readonly ConcurrentDictionary<string, UserActivityProfile> _userProfiles = new();
    private readonly ConcurrentDictionary<string, SuspiciousActivity> _suspiciousActivities = new();
    private readonly Timer _analysisTimer;
    private bool _disposed;

    // 設定値
    private static readonly TimeSpan AnalysisInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ActivityRetentionPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan SuspiciousActivityRetentionPeriod = TimeSpan.FromDays(7);

    // 検出しきい値
    private const int MaxConcurrentSessions = 3;
    private const int MaxPasswordChangesPerDay = 3;
    private const double SuspiciousScoreThreshold = 0.7;

    // 地理的異常検出設定
    private const int ImpossibleTravelDistanceKm = 1000; // 1時間で1000km以上の移動は物理的に不可能
    private const int SuspiciousVelocityKmPerHour = 500; // 500km/h以上は疑わしい（商用航空機レベル）
    private const int TypicalTimeZoneToleranceHours = 12; // 正常な時差の範囲

    // LoggerMessage delegates
    private static readonly Action<ILogger, string, double, Exception?> _logSuspiciousActivityDetected =
        LoggerMessage.Define<string, double>(
            LogLevel.Warning,
            new EventId(1, "SuspiciousActivityDetected"),
            "疑わしい活動を検出: {Email} (スコア: {Score})");

    private static readonly Action<ILogger, string, string, Exception?> _logAccountProtectionActivated =
        LoggerMessage.Define<string, string>(
            LogLevel.Error,
            new EventId(2, "AccountProtectionActivated"),
            "アカウント保護を有効化: {Email} - {Reason}");

    private static readonly Action<ILogger, string, string, Exception?> _logHijackingAttemptDetected =
        LoggerMessage.Define<string, string>(
            LogLevel.Critical,
            new EventId(3, "HijackingAttemptDetected"),
            "乗っ取り試行を検出: {Email} - {Details}");

    /// <summary>
    /// HijackingDetectionManagerを初期化します
    /// </summary>
    /// <param name="auditLogger">セキュリティ監査ログ</param>
    /// <param name="logger">ロガー</param>
    public HijackingDetectionManager(SecurityAuditLogger auditLogger, ILogger<HijackingDetectionManager>? logger = null)
    {
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger;

        // 定期的な分析タイマー
        _analysisTimer = new Timer(AnalyzeUserActivities, null, AnalysisInterval, AnalysisInterval);
    }

    /// <summary>
    /// ユーザー活動を記録
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="activityType">活動タイプ</param>
    /// <param name="ipAddress">IPアドレス</param>
    /// <param name="userAgent">ユーザーエージェント</param>
    /// <param name="geoLocation">地理的位置情報（オプション）</param>
    public void RecordUserActivity(string email, UserActivityType activityType,
        string? ipAddress = null, string? userAgent = null, GeoLocation? geoLocation = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        var now = DateTime.UtcNow;

        var activity = new UserActivity(
            Timestamp: now,
            ActivityType: activityType,
            IPAddress: ipAddress ?? "Unknown",
            UserAgent: userAgent ?? "Unknown",
            GeoLocation: geoLocation
        );

        _userProfiles.AddOrUpdate(normalizedEmail,
            new UserActivityProfile(normalizedEmail, [activity], now),
            (key, existing) =>
            {
                var updatedActivities = new List<UserActivity>(existing.Activities) { activity };

                // 古い活動を削除（30日以内のもののみ保持）
                var cutoffTime = now - ActivityRetentionPeriod;
                updatedActivities = [.. updatedActivities.Where(a => a.Timestamp >= cutoffTime)];

                return new UserActivityProfile(normalizedEmail, updatedActivities, now);
            });

        // リアルタイム分析
        AnalyzeUserActivity(normalizedEmail, activity);
    }

    /// <summary>
    /// 疑わしい活動の検出状態を取得
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <returns>疑わしい活動情報、なければnull</returns>
    public SuspiciousActivity? GetSuspiciousActivity(string email)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        _suspiciousActivities.TryGetValue(normalizedEmail, out var activity);
        return activity;
    }

    /// <summary>
    /// アカウント保護が必要かどうかを判定
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <returns>保護が必要な場合true</returns>
    public bool RequiresAccountProtection(string email)
    {
        var suspiciousActivity = GetSuspiciousActivity(email);
        return suspiciousActivity != null && suspiciousActivity.SuspiciousScore >= SuspiciousScoreThreshold;
    }

    /// <summary>
    /// アカウント保護を有効化
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="reason">保護理由</param>
    /// <returns>保護有効化の結果</returns>
    public async Task<ProtectionResult> ActivateAccountProtectionAsync(string email, string reason)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return new ProtectionResult(false, "無効なメールアドレス");

        var normalizedEmail = email.ToLowerInvariant().Trim();

        try
        {
            // 既存セッションの無効化
            await InvalidateAllSessionsAsync(normalizedEmail).ConfigureAwait(false);

            // 一時的なアカウントロック
            await LockAccountTemporarilyAsync(normalizedEmail).ConfigureAwait(false);

            // セキュリティ通知の送信
            await SendSecurityNotificationAsync(normalizedEmail, reason).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.AccountProtection,
                $"アカウント保護有効化: {normalizedEmail} - {reason}",
                normalizedEmail);

            if (_logger != null)
                _logAccountProtectionActivated(_logger, normalizedEmail, reason, null);

            return new ProtectionResult(true, "アカウント保護を有効化しました");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "アカウント保護有効化エラー: {Email}", normalizedEmail);
            return new ProtectionResult(false, "保護有効化中にエラーが発生しました");
        }
    }

    /// <summary>
    /// 疑わしい活動をクリア
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="reason">クリア理由</param>
    public void ClearSuspiciousActivity(string email, string reason = "Manual clearance")
    {
        ThrowIfDisposed();

        var normalizedEmail = email.ToLowerInvariant().Trim();
        if (_suspiciousActivities.TryRemove(normalizedEmail, out _))
        {
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.SecurityPolicyViolation,
                $"疑わしい活動クリア: {normalizedEmail} - {reason}",
                normalizedEmail);
        }
    }

    /// <summary>
    /// ユーザー活動の分析（リアルタイム）
    /// </summary>
    private void AnalyzeUserActivity(string email, UserActivity activity)
    {
        if (!_userProfiles.TryGetValue(email, out var profile))
            return;

        var suspiciousScore = CalculateSuspiciousScore(profile, activity);

        if (suspiciousScore >= SuspiciousScoreThreshold)
        {
            var suspiciousActivity = new SuspiciousActivity(
                Email: email,
                DetectedAt: DateTime.UtcNow,
                SuspiciousScore: suspiciousScore,
                Reasons: GenerateSuspiciousReasons(profile, activity),
                LastActivity: activity
            );

            _suspiciousActivities.AddOrUpdate(email, suspiciousActivity, (key, existing) =>
                suspiciousActivity with { DetectedAt = existing.DetectedAt });

            if (_logger != null)
                _logSuspiciousActivityDetected(_logger, email, suspiciousScore, null);

            // 高リスクの場合は自動保護を有効化
            if (suspiciousScore >= 0.9)
            {
                _ = Task.Run(async () => await ActivateAccountProtectionAsync(email, "高リスク活動の自動検出").ConfigureAwait(false));
            }
        }
    }

    /// <summary>
    /// 疑わしいスコアを計算
    /// </summary>
    private static double CalculateSuspiciousScore(UserActivityProfile profile, UserActivity currentActivity)
    {
        var score = 0.0;
        var recentActivities = profile.Activities.Where(a => a.Timestamp >= DateTime.UtcNow.AddHours(-24)).ToList();

        // 異常なIP アドレス変更
        var uniqueIPs = recentActivities.Select(a => a.IPAddress).Distinct().Count();
        if (uniqueIPs > MaxConcurrentSessions)
            score += 0.3;

        // 物理的に不可能な移動の検出
        var impossibleTravelScore = DetectImpossibleTravel(profile.Activities, currentActivity);
        score += impossibleTravelScore;

        // 異常な時間帯のアクセス（ユーザーの典型的パターンとの比較）
        var timeAnomalyScore = DetectTimeAnomalies(profile.Activities, currentActivity);
        score += timeAnomalyScore;

        // VPN/Proxy使用の検出
        var vpnScore = DetectVpnUsage(currentActivity);
        score += vpnScore;

        // パスワード変更の頻度
        var passwordChanges = recentActivities.Count(a => a.ActivityType == UserActivityType.PasswordChange);
        if (passwordChanges > MaxPasswordChangesPerDay)
            score += 0.4;

        // 短時間での大量活動
        var recentCount = recentActivities.Count(a => a.Timestamp >= DateTime.UtcNow.AddMinutes(-10));
        if (recentCount > 10)
            score += 0.2;

        return Math.Min(score, 1.0);
    }

    /// <summary>
    /// 物理的に不可能な移動を検出
    /// </summary>
    private static double DetectImpossibleTravel(List<UserActivity> activities, UserActivity currentActivity)
    {
        if (currentActivity.GeoLocation == null)
            return 0.0;

        var recentActivitiesWithLocation = activities
            .Where(a => a.GeoLocation != null && a.Timestamp >= DateTime.UtcNow.AddHours(-6))
            .OrderByDescending(a => a.Timestamp)
            .ToList();

        if (recentActivitiesWithLocation.Count == 0)
            return 0.0;

        var lastActivity = recentActivitiesWithLocation.First();
        if (lastActivity.GeoLocation == null)
            return 0.0;

        // 距離と時間を計算
        var distance = CalculateDistance(lastActivity.GeoLocation, currentActivity.GeoLocation);
        var timeDiff = currentActivity.Timestamp - lastActivity.Timestamp;

        if (timeDiff.TotalHours <= 0)
            return 0.0;

        var velocity = distance / timeDiff.TotalHours;

        // 物理的に不可能な移動の判定
        if (velocity > ImpossibleTravelDistanceKm)
            return 0.5; // 高リスク

        if (velocity > SuspiciousVelocityKmPerHour)
            return 0.3; // 中リスク

        return 0.0;
    }

    /// <summary>
    /// 時間帯の異常を検出
    /// </summary>
    private static double DetectTimeAnomalies(List<UserActivity> activities, UserActivity currentActivity)
    {
        if (activities.Count < 10) // 十分な履歴がない場合は判定しない
            return 0.0;

        // ユーザーの典型的な活動時間帯を分析
        var typicalHours = activities
            .GroupBy(a => a.Timestamp.Hour)
            .OrderByDescending(g => g.Count())
            .Take(8) // 上位8時間を典型的時間とする
            .Select(g => g.Key)
            .ToHashSet();

        var currentHour = currentActivity.Timestamp.Hour;

        // 地理的位置による時差を考慮
        if (currentActivity.GeoLocation != null)
        {
            // 大まかな時差補正（詳細な実装では実際のタイムゾーンAPIを使用）
            var timeZoneOffset = EstimateTimeZoneOffset(currentActivity.GeoLocation);
            var adjustedHour = (currentHour + timeZoneOffset + 24) % 24;

            if (typicalHours.Contains(adjustedHour))
                return 0.0;
        }

        if (!typicalHours.Contains(currentHour))
            return 0.15;

        return 0.0;
    }

    /// <summary>
    /// VPN/Proxy使用を検出
    /// </summary>
    private static double DetectVpnUsage(UserActivity activity)
    {
        // 簡易的なVPN検出（実際の実装では外部VPN検出サービスを使用）
        var ipAddress = activity.IPAddress;

        // よく知られたVPNサービスのIP範囲をチェック
        var knownVpnRanges = new[]
        {
            "10.", "172.16.", "192.168.", // プライベートIP（企業VPN等）
            "8.8.8.", "8.8.4."           // 一部のパブリックDNS/VPN
        };

        if (knownVpnRanges.Any(range => ipAddress.StartsWith(range, StringComparison.OrdinalIgnoreCase)))
            return 0.1; // 軽微なリスク（企業VPN等も含む）

        return 0.0;
    }

    /// <summary>
    /// 2つの地点間の距離を計算（ハーバサイン公式）
    /// </summary>
    private static double CalculateDistance(GeoLocation loc1, GeoLocation loc2)
    {
        const double EarthRadiusKm = 6371.0;

        var dLat = ToRadians(loc2.Latitude - loc1.Latitude);
        var dLon = ToRadians(loc2.Longitude - loc1.Longitude);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(loc1.Latitude)) * Math.Cos(ToRadians(loc2.Latitude)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return EarthRadiusKm * c;
    }

    /// <summary>
    /// 度をラジアンに変換
    /// </summary>
    private static double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    /// <summary>
    /// 地理的位置から大まかなタイムゾーンオフセットを推定
    /// </summary>
    private static int EstimateTimeZoneOffset(GeoLocation location)
    {
        // 簡易的な推定（実際の実装では詳細なタイムゾーンライブラリを使用）
        var longitude = location.Longitude;
        return (int)Math.Round(longitude / 15.0); // 経度15度で1時間の時差
    }

    /// <summary>
    /// 疑わしい理由を生成
    /// </summary>
    private static List<string> GenerateSuspiciousReasons(UserActivityProfile profile, UserActivity activity)
    {
        var reasons = new List<string>();
        var recentActivities = profile.Activities.Where(a => a.Timestamp >= DateTime.UtcNow.AddHours(-24)).ToList();

        var uniqueIPs = recentActivities.Select(a => a.IPAddress).Distinct().Count();
        if (uniqueIPs > MaxConcurrentSessions)
            reasons.Add($"異常な数のIPアドレス（{uniqueIPs}個）");

        // 物理的に不可能な移動の検出
        var impossibleTravelScore = DetectImpossibleTravel(profile.Activities, activity);
        if (impossibleTravelScore >= 0.5)
            reasons.Add("物理的に不可能な速度での地理的移動");
        else if (impossibleTravelScore >= 0.3)
            reasons.Add("疑わしい速度での地理的移動（航空機レベル）");

        // 時間帯の異常
        var timeAnomalyScore = DetectTimeAnomalies(profile.Activities, activity);
        if (timeAnomalyScore > 0)
            reasons.Add("通常とは異なる時間帯での活動");

        // VPN使用の検出
        var vpnScore = DetectVpnUsage(activity);
        if (vpnScore > 0)
            reasons.Add("VPN/Proxy経由でのアクセス");

        var passwordChanges = recentActivities.Count(a => a.ActivityType == UserActivityType.PasswordChange);
        if (passwordChanges > MaxPasswordChangesPerDay)
            reasons.Add($"頻繁なパスワード変更（{passwordChanges}回）");

        var recentCount = recentActivities.Count(a => a.Timestamp >= DateTime.UtcNow.AddMinutes(-10));
        if (recentCount > 10)
            reasons.Add($"短時間での大量活動（{recentCount}回）");

        return reasons;
    }

    /// <summary>
    /// 全体的な分析（定期実行）
    /// </summary>
    private void AnalyzeUserActivities(object? state)
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;
        var cutoffTime = now - SuspiciousActivityRetentionPeriod;

        // 期限切れの疑わしい活動を削除
        var expiredKeys = _suspiciousActivities
            .Where(kvp => kvp.Value.DetectedAt < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _suspiciousActivities.TryRemove(key, out _);
        }

        // 新しい疑わしいパターンの検出
        foreach (var profile in _userProfiles.Values)
        {
            var recentActivities = profile.Activities.Where(a => a.Timestamp >= now.AddHours(-24)).ToList();
            if (recentActivities.Count > 0)
            {
                var latestActivity = recentActivities.OrderByDescending(a => a.Timestamp).First();
                AnalyzeUserActivity(profile.Email, latestActivity);
            }
        }
    }

    /// <summary>
    /// 全セッションを無効化
    /// </summary>
    private async Task InvalidateAllSessionsAsync(string email)
    {
        // TODO: 実際のセッション無効化処理を実装
        // await _sessionService.InvalidateAllSessionsAsync(email);
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        _logger?.LogInformation("全セッションを無効化: {Email}", email);
    }

    /// <summary>
    /// アカウントを一時的にロック
    /// </summary>
    private async Task LockAccountTemporarilyAsync(string email)
    {
        // TODO: 実際のアカウントロック処理を実装
        // await _authService.LockAccountTemporarilyAsync(email, TimeSpan.FromHours(24));
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        _logger?.LogInformation("アカウントを一時的にロック: {Email}", email);
    }

    /// <summary>
    /// セキュリティ通知を送信
    /// </summary>
    private async Task SendSecurityNotificationAsync(string email, string reason)
    {
        // TODO: 実際の通知送信処理を実装
        // await _notificationService.SendSecurityNotificationAsync(email, reason);
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        _logger?.LogInformation("セキュリティ通知送信: {Email} - {Reason}", email, reason);
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
            _analysisTimer?.Dispose();
            _userProfiles.Clear();
            _suspiciousActivities.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// ユーザー活動プロファイル
/// </summary>
/// <param name="Email">メールアドレス</param>
/// <param name="Activities">活動履歴</param>
/// <param name="LastUpdated">最終更新時刻</param>
public sealed record UserActivityProfile(
    string Email,
    List<UserActivity> Activities,
    DateTime LastUpdated);

/// <summary>
/// ユーザー活動記録
/// </summary>
/// <param name="Timestamp">タイムスタンプ</param>
/// <param name="ActivityType">活動タイプ</param>
/// <param name="IPAddress">IPアドレス</param>
/// <param name="UserAgent">ユーザーエージェント</param>
/// <param name="GeoLocation">地理的位置情報</param>
public sealed record UserActivity(
    DateTime Timestamp,
    UserActivityType ActivityType,
    string IPAddress,
    string UserAgent,
    GeoLocation? GeoLocation);

/// <summary>
/// 疑わしい活動情報
/// </summary>
/// <param name="Email">メールアドレス</param>
/// <param name="DetectedAt">検出時刻</param>
/// <param name="SuspiciousScore">疑わしさスコア（0.0-1.0）</param>
/// <param name="Reasons">疑わしい理由のリスト</param>
/// <param name="LastActivity">最後の活動</param>
public sealed record SuspiciousActivity(
    string Email,
    DateTime DetectedAt,
    double SuspiciousScore,
    List<string> Reasons,
    UserActivity LastActivity);

/// <summary>
/// 保護実行結果
/// </summary>
/// <param name="Success">成功したかどうか</param>
/// <param name="Message">結果メッセージ</param>
public sealed record ProtectionResult(bool Success, string Message);

/// <summary>
/// ユーザー活動タイプ
/// </summary>
public enum UserActivityType
{
    Login,
    Logout,
    PasswordChange,
    ProfileUpdate,
    SessionStart,
    SessionEnd,
    SuspiciousLogin,
    FailedLogin,
    AccountRecovery
}
