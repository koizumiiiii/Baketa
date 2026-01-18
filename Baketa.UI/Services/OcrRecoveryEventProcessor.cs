using System.Threading;
using System.Threading.Tasks;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// Issue #300: OCRサーバー復旧イベントプロセッサ
/// OcrServerRecoveryEventを購読してトースト通知でユーザーに表示
/// </summary>
public sealed class OcrRecoveryEventProcessor : IEventProcessor<OcrServerRecoveryEvent>
{
    private readonly INotificationService _notificationService;
    private readonly ILogger<OcrRecoveryEventProcessor>? _logger;

    public OcrRecoveryEventProcessor(
        INotificationService notificationService,
        ILogger<OcrRecoveryEventProcessor>? logger = null)
    {
        _notificationService = notificationService;
        _logger = logger;
    }

    /// <summary>
    /// 処理の優先度（高優先度 - ユーザー通知）
    /// </summary>
    public int Priority => 50;

    /// <summary>
    /// 同期実行が必要かどうか（UI更新のため同期実行）
    /// </summary>
    public bool SynchronousExecution => true;

    /// <summary>
    /// OCRサーバー復旧イベントを処理
    /// </summary>
    public async Task HandleAsync(OcrServerRecoveryEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "[Issue #300] OcrServerRecoveryEvent received: Action={Action}",
            eventData.Action);

        var title = Strings.OcrRecovery_Title;

        switch (eventData.Action)
        {
            case OcrRecoveryAction.RestartStarted:
                // 復旧開始時は警告トースト（8秒表示）
                await _notificationService.ShowWarningAsync(title, Strings.OcrRecovery_Started, 8000).ConfigureAwait(false);
                _logger?.LogDebug("[Issue #300] Showing recovery started toast");
                break;

            case OcrRecoveryAction.RestartCompleted:
                // 復旧完了時は成功トースト（3秒表示）
                await _notificationService.ShowSuccessAsync(title, Strings.OcrRecovery_Completed, 3000).ConfigureAwait(false);
                _logger?.LogDebug("[Issue #300] Showing recovery completed toast");
                break;

            case OcrRecoveryAction.RestartFailed:
                // 復旧失敗時はエラートースト（自動で消えない: 0秒）
                await _notificationService.ShowErrorAsync(title, Strings.OcrRecovery_Failed, 0).ConfigureAwait(false);
                _logger?.LogWarning("[Issue #300] Showing recovery failed toast");
                break;
        }
    }
}
