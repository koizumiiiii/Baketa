using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Auth;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Security;

/// <summary>
/// アカウント復旧ワークフロー管理クラス
/// </summary>
public sealed class RecoveryWorkflowManager : IDisposable
{
    private readonly SecurityAuditLogger _auditLogger;
    private readonly PasswordResetManager _passwordResetManager;
    private readonly HijackingDetectionManager _hijackingDetectionManager;
    private readonly SecurityNotificationService _notificationService;
    private readonly ILogger<RecoveryWorkflowManager>? _logger;
    private readonly ConcurrentDictionary<string, RecoveryWorkflow> _activeWorkflows = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    // 設定値
    private static readonly TimeSpan WorkflowTimeout = TimeSpan.FromHours(24);
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan VerificationCodeValidDuration = TimeSpan.FromMinutes(10);
    private const int MaxRecoveryAttemptsPerDay = 3;
    private const int VerificationCodeLength = 6;

    // LoggerMessage delegates
    private static readonly Action<ILogger, string, RecoveryType, Exception?> _logWorkflowStarted =
        LoggerMessage.Define<string, RecoveryType>(
            LogLevel.Information,
            new EventId(1, "RecoveryWorkflowStarted"),
            "復旧ワークフロー開始: {Email} ({RecoveryType})");

    private static readonly Action<ILogger, string, RecoveryType, Exception?> _logWorkflowCompleted =
        LoggerMessage.Define<string, RecoveryType>(
            LogLevel.Information,
            new EventId(2, "RecoveryWorkflowCompleted"),
            "復旧ワークフロー完了: {Email} ({RecoveryType})");

    private static readonly Action<ILogger, string, string, Exception?> _logWorkflowFailed =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(3, "RecoveryWorkflowFailed"),
            "復旧ワークフロー失敗: {Email} - {Reason}");

    private static readonly Action<ILogger, string, string, Exception?> _logSuspiciousRecoveryAttempt =
        LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            new EventId(4, "SuspiciousRecoveryAttempt"),
            "疑わしい復旧試行: {Email} - {Details}");

    /// <summary>
    /// RecoveryWorkflowManagerを初期化します
    /// </summary>
    /// <param name="auditLogger">セキュリティ監査ログ</param>
    /// <param name="passwordResetManager">パスワードリセット管理</param>
    /// <param name="hijackingDetectionManager">乗っ取り検出管理</param>
    /// <param name="notificationService">セキュリティ通知サービス</param>
    /// <param name="logger">ロガー</param>
    public RecoveryWorkflowManager(
        SecurityAuditLogger auditLogger,
        PasswordResetManager passwordResetManager,
        HijackingDetectionManager hijackingDetectionManager,
        SecurityNotificationService notificationService,
        ILogger<RecoveryWorkflowManager>? logger = null)
    {
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _passwordResetManager = passwordResetManager ?? throw new ArgumentNullException(nameof(passwordResetManager));
        _hijackingDetectionManager = hijackingDetectionManager ?? throw new ArgumentNullException(nameof(hijackingDetectionManager));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger;

        // 定期的なクリーンアップタイマー
        _cleanupTimer = new Timer(CleanupExpiredWorkflows, null, CleanupInterval, CleanupInterval);
    }

    /// <summary>
    /// パスワード忘れ復旧ワークフローを開始
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="ipAddress">要求元IPアドレス</param>
    /// <returns>ワークフロー開始結果</returns>
    public async Task<WorkflowResult> StartPasswordRecoveryWorkflowAsync(string email, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return new WorkflowResult(false, "無効なメールアドレスです", null);

        var normalizedEmail = email.ToLowerInvariant().Trim();

        // レート制限チェック
        if (IsRateLimited(normalizedEmail))
        {
            return new WorkflowResult(false, "復旧試行の上限に達しました。24時間後に再試行してください", null);
        }

        try
        {
            // パスワードリセットトークンの生成
            var resetToken = await _passwordResetManager.RequestPasswordResetAsync(normalizedEmail, ipAddress).ConfigureAwait(false);
            if (resetToken == null)
            {
                return new WorkflowResult(false, "パスワードリセット要求に失敗しました", null);
            }

            // 復旧ワークフローの作成
            var workflowId = Guid.NewGuid().ToString();
            var workflow = new RecoveryWorkflow(
                Id: workflowId,
                Email: normalizedEmail,
                Type: RecoveryType.PasswordForgotten,
                Status: WorkflowStatus.PasswordResetRequested,
                CreatedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.Add(WorkflowTimeout),
                Steps:
                [
                    new("パスワードリセット要求", true, DateTime.UtcNow, "リセットトークン生成完了"),
                    new("メール確認", false, null, null),
                    new("新しいパスワード設定", false, null, null),
                    new("ログイン確認", false, null, null)
                ],
                Data: new() { { "ResetToken", resetToken } },
                IPAddress: ipAddress ?? "Unknown"
            );

            _activeWorkflows[workflowId] = workflow;

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.PasswordChange,
                $"パスワード復旧ワークフロー開始: {normalizedEmail}",
                normalizedEmail, ipAddress);

            if (_logger != null)
                _logWorkflowStarted(_logger, normalizedEmail, RecoveryType.PasswordForgotten, null);

            return new WorkflowResult(true, "パスワードリセット要求を送信しました", workflowId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "パスワード復旧ワークフロー開始エラー: {Email}", normalizedEmail);
            return new WorkflowResult(false, "復旧ワークフローの開始に失敗しました", null);
        }
    }

    /// <summary>
    /// アカウント乗っ取り復旧ワークフローを開始
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <param name="ipAddress">要求元IPアドレス</param>
    /// <returns>ワークフロー開始結果</returns>
    public async Task<WorkflowResult> StartHijackingRecoveryWorkflowAsync(string email, string? ipAddress = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return new WorkflowResult(false, "無効なメールアドレスです", null);

        var normalizedEmail = email.ToLowerInvariant().Trim();

        // レート制限チェック
        if (IsRateLimited(normalizedEmail))
        {
            return new WorkflowResult(false, "復旧試行の上限に達しました。24時間後に再試行してください", null);
        }

        try
        {
            // 乗っ取り検出と保護の有効化
            var protectionResult = await _hijackingDetectionManager.ActivateAccountProtectionAsync(
                normalizedEmail, "ユーザー要求による乗っ取り報告").ConfigureAwait(false);

            if (!protectionResult.Success)
            {
                return new WorkflowResult(false, "アカウント保護の有効化に失敗しました", null);
            }

            // 復旧ワークフローの作成
            var workflowId = Guid.NewGuid().ToString();
            var verificationCode = GenerateVerificationCode();

            var workflow = new RecoveryWorkflow(
                Id: workflowId,
                Email: normalizedEmail,
                Type: RecoveryType.AccountHijacked,
                Status: WorkflowStatus.AccountProtected,
                CreatedAt: DateTime.UtcNow,
                ExpiresAt: DateTime.UtcNow.Add(WorkflowTimeout),
                Steps:
                [
                    new("アカウント保護", true, DateTime.UtcNow, "全セッション無効化・一時ロック完了"),
                    new("身元確認", false, null, null),
                    new("セキュリティ強化", false, null, null),
                    new("アカウント復旧", false, null, null),
                    new("監視強化", false, null, null)
                ],
                Data: new()
                {
                    { "VerificationCode", verificationCode },
                    { "ProtectionActivated", true }
                },
                IPAddress: ipAddress ?? "Unknown"
            );

            _activeWorkflows[workflowId] = workflow;

            // セキュリティ通知送信
            var suspiciousActivity = _hijackingDetectionManager.GetSuspiciousActivity(normalizedEmail);
            var reasons = suspiciousActivity?.Reasons ?? ["ユーザー要求による乗っ取り報告"];
            await _notificationService.SendHijackingAlertNotificationAsync(normalizedEmail, verificationCode, reasons, ipAddress).ConfigureAwait(false);

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.AccountProtection,
                $"乗っ取り復旧ワークフロー開始: {normalizedEmail}",
                normalizedEmail, ipAddress);

            if (_logger != null)
                _logWorkflowStarted(_logger, normalizedEmail, RecoveryType.AccountHijacked, null);

            return new WorkflowResult(true, "アカウント保護を有効化し、復旧手順を開始しました", workflowId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "乗っ取り復旧ワークフロー開始エラー: {Email}", normalizedEmail);
            return new WorkflowResult(false, "復旧ワークフローの開始に失敗しました", null);
        }
    }

    /// <summary>
    /// 身元確認コードの検証
    /// </summary>
    /// <param name="workflowId">ワークフローID</param>
    /// <param name="verificationCode">確認コード</param>
    /// <returns>検証結果</returns>
    public Task<WorkflowResult> VerifyIdentityAsync(string workflowId, string verificationCode)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(workflowId) || string.IsNullOrWhiteSpace(verificationCode))
            return Task.FromResult(new WorkflowResult(false, "無効なパラメータです", null));

        if (!_activeWorkflows.TryGetValue(workflowId, out var workflow))
            return Task.FromResult(new WorkflowResult(false, "ワークフローが見つかりません", null));

        if (workflow.Status != WorkflowStatus.AccountProtected)
            return Task.FromResult(new WorkflowResult(false, "無効なワークフローステータスです", null));

        if (DateTime.UtcNow > workflow.ExpiresAt)
            return Task.FromResult(new WorkflowResult(false, "ワークフローの有効期限が切れています", null));

        try
        {
            // 確認コードの検証
            if (!workflow.Data.TryGetValue("VerificationCode", out var storedCode) ||
                !string.Equals(storedCode.ToString(), verificationCode, StringComparison.Ordinal))
            {
                // 疑わしい確認試行をログ
                if (_logger != null)
                    _logSuspiciousRecoveryAttempt(_logger, workflow.Email, "無効な確認コード", null);

                return Task.FromResult(new WorkflowResult(false, "確認コードが正しくありません", null));
            }

            // ワークフローの更新
            var updatedSteps = workflow.Steps.ToList();
            var identityStep = updatedSteps.FirstOrDefault(s => s.Name == "身元確認");
            if (identityStep != null)
            {
                var index = updatedSteps.IndexOf(identityStep);
                updatedSteps[index] = identityStep with { IsCompleted = true, CompletedAt = DateTime.UtcNow, Notes = "身元確認完了" };
            }

            var updatedWorkflow = workflow with
            {
                Status = WorkflowStatus.IdentityVerified,
                Steps = updatedSteps
            };

            _activeWorkflows[workflowId] = updatedWorkflow;

            // 監査ログ記録
            _auditLogger.LogSecurityEvent(
                SecurityAuditLogger.SecurityEventType.SecurityPolicyViolation,
                $"身元確認完了: {workflow.Email}",
                workflow.Email, workflow.IPAddress);

            return Task.FromResult(new WorkflowResult(true, "身元確認が完了しました", workflowId));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "身元確認エラー: {WorkflowId}", workflowId);
            return Task.FromResult(new WorkflowResult(false, "身元確認中にエラーが発生しました", null));
        }
    }

    /// <summary>
    /// 復旧ワークフローを完了
    /// </summary>
    /// <param name="workflowId">ワークフローID</param>
    /// <param name="newPassword">新しいパスワード（パスワード復旧の場合）</param>
    /// <returns>完了結果</returns>
    public async Task<WorkflowResult> CompleteRecoveryWorkflowAsync(string workflowId, string? newPassword = null)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(workflowId))
            return new WorkflowResult(false, "無効なワークフローIDです", null);

        if (!_activeWorkflows.TryGetValue(workflowId, out var workflow))
            return new WorkflowResult(false, "ワークフローが見つかりません", null);

        if (DateTime.UtcNow > workflow.ExpiresAt)
            return new WorkflowResult(false, "ワークフローの有効期限が切れています", null);

        try
        {
            var result = workflow.Type switch
            {
                RecoveryType.PasswordForgotten => await CompletePasswordRecoveryAsync(workflow, newPassword).ConfigureAwait(false),
                RecoveryType.AccountHijacked => await CompleteHijackingRecoveryAsync(workflow).ConfigureAwait(false),
                _ => new WorkflowResult(false, "未知の復旧タイプです", null)
            };

            if (result.Success)
            {
                // ワークフローの削除
                _activeWorkflows.TryRemove(workflowId, out _);

                // 監査ログ記録
                _auditLogger.LogSecurityEvent(
                    SecurityAuditLogger.SecurityEventType.AccountRecovery,
                    $"復旧ワークフロー完了: {workflow.Email} ({workflow.Type})",
                    workflow.Email, workflow.IPAddress);

                if (_logger != null)
                    _logWorkflowCompleted(_logger, workflow.Email, workflow.Type, null);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "復旧ワークフロー完了エラー: {WorkflowId}", workflowId);
            return new WorkflowResult(false, "復旧ワークフローの完了に失敗しました", null);
        }
    }

    /// <summary>
    /// アクティブなワークフローを取得
    /// </summary>
    /// <param name="email">メールアドレス</param>
    /// <returns>アクティブなワークフロー、なければnull</returns>
    public RecoveryWorkflow? GetActiveWorkflow(string email)
    {
        ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizedEmail = email.ToLowerInvariant().Trim();
        return _activeWorkflows.Values.FirstOrDefault(w => w.Email == normalizedEmail);
    }

    /// <summary>
    /// パスワード復旧を完了
    /// </summary>
    private async Task<WorkflowResult> CompletePasswordRecoveryAsync(RecoveryWorkflow workflow, string? newPassword)
    {
        if (string.IsNullOrWhiteSpace(newPassword))
            return new WorkflowResult(false, "新しいパスワードが必要です", null);

        if (!InputValidator.IsStrongPassword(newPassword))
            return new WorkflowResult(false, "パスワードが要件を満たしていません", null);

        // パスワードリセットの実行
        var resetResult = await _passwordResetManager.ExecutePasswordResetAsync(
            workflow.Email,
            workflow.Data["ResetToken"].ToString()!,
            newPassword,
            workflow.IPAddress).ConfigureAwait(false);

        if (!resetResult.Success)
            return new WorkflowResult(false, resetResult.Message, null);

        // 復旧完了通知を送信
        await _notificationService.SendRecoveryCompletedNotificationAsync(
            workflow.Email, RecoveryType.PasswordForgotten, workflow.IPAddress).ConfigureAwait(false);

        return new WorkflowResult(true, "パスワードの復旧が完了しました", null);
    }

    /// <summary>
    /// 乗っ取り復旧を完了
    /// </summary>
    private async Task<WorkflowResult> CompleteHijackingRecoveryAsync(RecoveryWorkflow workflow)
    {
        if (workflow.Status != WorkflowStatus.IdentityVerified)
            return new WorkflowResult(false, "身元確認が完了していません", null);

        // アカウントのロック解除
        await UnlockAccountAsync(workflow.Email).ConfigureAwait(false);

        // 疑わしい活動のクリア
        _hijackingDetectionManager.ClearSuspiciousActivity(workflow.Email, "復旧完了");

        // 監視強化の設定
        await EnableEnhancedMonitoringAsync(workflow.Email).ConfigureAwait(false);

        // 復旧完了通知を送信
        await _notificationService.SendRecoveryCompletedNotificationAsync(
            workflow.Email, RecoveryType.AccountHijacked, workflow.IPAddress).ConfigureAwait(false);

        return new WorkflowResult(true, "アカウントの復旧が完了しました。しばらくの間、監視を強化します", null);
    }

    /// <summary>
    /// レート制限チェック
    /// </summary>
    private bool IsRateLimited(string email)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddDays(-1);

        var recentAttempts = _activeWorkflows.Values
            .Count(w => w.Email == email && w.CreatedAt >= windowStart);

        return recentAttempts >= MaxRecoveryAttemptsPerDay;
    }

    /// <summary>
    /// 確認コードを生成
    /// </summary>
    private static string GenerateVerificationCode()
    {
        var random = new Random();
        return random.Next(100000, 999999).ToString(CultureInfo.InvariantCulture);
    }


    /// <summary>
    /// アカウントのロック解除
    /// </summary>
    private async Task UnlockAccountAsync(string email)
    {
        // TODO: 実際のアカウントロック解除処理を実装
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        _logger?.LogInformation("アカウントロック解除: {Email}", email);
    }

    /// <summary>
    /// 監視強化を有効化
    /// </summary>
    private async Task EnableEnhancedMonitoringAsync(string email)
    {
        // TODO: 実際の監視強化処理を実装
        await Task.Delay(100).ConfigureAwait(false); // Placeholder
        _logger?.LogInformation("監視強化を有効化: {Email}", email);
    }

    /// <summary>
    /// 期限切れワークフローのクリーンアップ
    /// </summary>
    private void CleanupExpiredWorkflows(object? state)
    {
        if (_disposed)
            return;

        var now = DateTime.UtcNow;
        var expiredWorkflows = _activeWorkflows
            .Where(kvp => now > kvp.Value.ExpiresAt)
            .ToList();

        foreach (var kvp in expiredWorkflows)
        {
            _activeWorkflows.TryRemove(kvp.Key, out _);

            if (_logger != null)
                _logWorkflowFailed(_logger, kvp.Value.Email, "期限切れ", null);
        }

        if (expiredWorkflows.Count > 0)
        {
            _logger?.LogDebug("期限切れ復旧ワークフローをクリーンアップ: {Count}件", expiredWorkflows.Count);
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
            _activeWorkflows.Clear();
            _disposed = true;
        }
    }
}

/// <summary>
/// 復旧ワークフロー情報
/// </summary>
/// <param name="Id">ワークフローID</param>
/// <param name="Email">メールアドレス</param>
/// <param name="Type">復旧タイプ</param>
/// <param name="Status">ワークフローステータス</param>
/// <param name="CreatedAt">作成日時</param>
/// <param name="ExpiresAt">期限切れ日時</param>
/// <param name="Steps">復旧ステップ</param>
/// <param name="Data">追加データ</param>
/// <param name="IPAddress">要求元IPアドレス</param>
public sealed record RecoveryWorkflow(
    string Id,
    string Email,
    RecoveryType Type,
    WorkflowStatus Status,
    DateTime CreatedAt,
    DateTime ExpiresAt,
    List<RecoveryStep> Steps,
    Dictionary<string, object> Data,
    string IPAddress);

/// <summary>
/// 復旧ステップ
/// </summary>
/// <param name="Name">ステップ名</param>
/// <param name="IsCompleted">完了フラグ</param>
/// <param name="CompletedAt">完了日時</param>
/// <param name="Notes">備考</param>
public sealed record RecoveryStep(
    string Name,
    bool IsCompleted,
    DateTime? CompletedAt,
    string? Notes);

/// <summary>
/// ワークフロー結果
/// </summary>
/// <param name="Success">成功したかどうか</param>
/// <param name="Message">結果メッセージ</param>
/// <param name="WorkflowId">ワークフローID</param>
public sealed record WorkflowResult(bool Success, string Message, string? WorkflowId);

/// <summary>
/// 復旧タイプ
/// </summary>
public enum RecoveryType
{
    PasswordForgotten,
    AccountHijacked
}

/// <summary>
/// ワークフローステータス
/// </summary>
public enum WorkflowStatus
{
    PasswordResetRequested,
    AccountProtected,
    IdentityVerified,
    SecurityEnhanced,
    RecoveryCompleted
}
