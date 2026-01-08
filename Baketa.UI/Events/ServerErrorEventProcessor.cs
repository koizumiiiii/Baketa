using System.Collections.Concurrent;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Events;

/// <summary>
/// サーバーエラーイベントを処理してUI通知を表示
/// Issue #264: Pythonサーバーエラー時のユーザー通知
/// </summary>
public sealed class ServerErrorEventProcessor : IEventProcessor<ServerErrorEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<ServerErrorEventProcessor> _logger;

    // エラー種別ごとの最終通知時刻（スロットリング用）
    private readonly ConcurrentDictionary<string, DateTime> _lastNotificationTimes = new();

    // Warnレベルは30秒、Cautionレベルは60秒のクールダウン
    private const int WarnCooldownSeconds = 30;
    private const int CautionCooldownSeconds = 60;

    public ServerErrorEventProcessor(
        INotificationService notificationService,
        ILogger<ServerErrorEventProcessor> logger)
    {
        ArgumentNullException.ThrowIfNull(notificationService);
        ArgumentNullException.ThrowIfNull(logger);
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// イベント処理優先度
    /// </summary>
    public int Priority => 100;

    /// <summary>
    /// 同期実行モード（通知はバックグラウンドで十分）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// サーバーエラーイベントを処理
    /// </summary>
    public async Task HandleAsync(ServerErrorEvent eventData)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // スロットリングチェック
        var throttleKey = $"{eventData.Source}:{eventData.ErrorType}";
        var cooldownSeconds = eventData.Severity == ErrorSeverity.Warn
            ? WarnCooldownSeconds
            : CautionCooldownSeconds;

        if (_lastNotificationTimes.TryGetValue(throttleKey, out var lastTime))
        {
            if ((DateTime.UtcNow - lastTime).TotalSeconds < cooldownSeconds)
            {
                _logger.LogDebug(
                    "Server error notification skipped due to cooldown: {Source}:{ErrorType}",
                    eventData.Source, eventData.ErrorType);
                return;
            }
        }

        _lastNotificationTimes[throttleKey] = DateTime.UtcNow;

        // タイトルと通知内容を構築
        var title = eventData.Severity == ErrorSeverity.Warn
            ? "Server Error"
            : "Server Warning";

        var message = BuildNotificationMessage(eventData);

        _logger.LogWarning(
            "Showing server error notification: {Severity} {Source}:{ErrorType} - {Message}",
            eventData.Severity, eventData.Source, eventData.ErrorType, eventData.Message);

        // UIスレッドで通知を表示
        // Note: UIスレッド内ではConfigureAwait(false)を使用しない（Geminiレビュー指摘）
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (eventData.Severity == ErrorSeverity.Warn)
            {
                // Warnは手動で閉じるまで表示（duration = 0）
                await _notificationService.ShowErrorAsync(title, message, 0);
            }
            else
            {
                // Cautionは10秒表示
                await _notificationService.ShowWarningAsync(title, message, 10000);
            }
        });
    }

    /// <summary>
    /// 通知メッセージを構築
    /// </summary>
    private static string BuildNotificationMessage(ServerErrorEvent eventData)
    {
        var message = eventData.Message;

        // 推奨アクションがあれば追加
        if (!string.IsNullOrWhiteSpace(eventData.ActionHint))
        {
            message += $"\n\n{eventData.ActionHint}";
        }

        return message;
    }
}
