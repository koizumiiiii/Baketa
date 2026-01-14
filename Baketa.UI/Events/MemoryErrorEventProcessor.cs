using System.Threading;
using Avalonia.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;
using Baketa.UI.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Events;

/// <summary>
/// メモリ不足エラーイベントを処理してUI通知を表示
/// Issue #239: OutOfMemoryException発生時にユーザーに警告
/// </summary>
public sealed class MemoryErrorEventProcessor : IEventProcessor<MemoryErrorEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<MemoryErrorEventProcessor> _logger;
    private DateTime _lastNotificationTime = DateTime.MinValue;
    private const int NotificationCooldownSeconds = 30; // 30秒間は再通知を抑制

    public MemoryErrorEventProcessor(
        INotificationService notificationService,
        ILogger<MemoryErrorEventProcessor> logger)
    {
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// メモリエラーイベントを処理
    /// </summary>
    public async Task HandleAsync(MemoryErrorEvent eventData, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        // クールダウン期間中は通知をスキップ
        if ((DateTime.UtcNow - _lastNotificationTime).TotalSeconds < NotificationCooldownSeconds)
        {
            _logger.LogDebug("Memory error notification skipped due to cooldown");
            return;
        }

        _lastNotificationTime = DateTime.UtcNow;

        var title = eventData.RecoverySucceeded
            ? "Memory Warning"
            : "Memory Critical";

        var message = eventData.RecoverySucceeded
            ? $"メモリ使用量が高くなっています ({eventData.CurrentMemoryMB:F0} MB)。\n不要なアプリケーションを閉じることをお勧めします。"
            : $"メモリ不足が発生しました ({eventData.CurrentMemoryMB:F0} MB)。\nアプリケーションを再起動してください。";

        _logger.LogWarning("Showing memory error notification: {Title} - {Message}", title, message);

        // UIスレッドで通知を表示
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            if (eventData.RecoverySucceeded)
            {
                await _notificationService.ShowWarningAsync(title, message, 10000).ConfigureAwait(false);
            }
            else
            {
                await _notificationService.ShowErrorAsync(title, message, 0).ConfigureAwait(false); // 0 = 手動で閉じるまで表示
            }
        });
    }
}
