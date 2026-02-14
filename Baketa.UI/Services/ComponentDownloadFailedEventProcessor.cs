using System.Threading;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.Setup;
using Baketa.UI.Resources;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Services;

/// <summary>
/// [Gemini Review] コンポーネントダウンロード失敗イベントプロセッサ
/// ダウンロード失敗時にユーザーに再起動を促す通知を表示
/// </summary>
public sealed class ComponentDownloadFailedEventProcessor : IEventProcessor<ComponentDownloadFailedEvent>
{
    private readonly ILogger<ComponentDownloadFailedEventProcessor> _logger;
    private readonly INotificationService _notificationService;

    /// <summary>
    /// 同期実行フラグ（通知は非同期で良い）
    /// </summary>
    public bool SynchronousExecution => false;

    /// <summary>
    /// 優先度（デフォルト）
    /// </summary>
    public int Priority => 0;

    public ComponentDownloadFailedEventProcessor(
        ILogger<ComponentDownloadFailedEventProcessor> logger,
        INotificationService notificationService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _notificationService = notificationService ?? throw new ArgumentNullException(nameof(notificationService));
    }

    public async Task HandleAsync(ComponentDownloadFailedEvent eventData, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[Gemini Review] コンポーネントダウンロード失敗イベント受信: Required={HasRequired}, Failed={FailedCount}",
            eventData.HasRequiredFailures, eventData.FailedComponentIds.Count);

        try
        {
            if (eventData.HasRequiredFailures)
            {
                // 必須コンポーネントの失敗 - エラー通知（手動で閉じるまで表示）
                await _notificationService.ShowErrorAsync(
                    Strings.Download_Failed_Title,
                    string.Format(Strings.Download_Failed_RequiredMessage,
                        string.Join(", ", eventData.FailedComponentIds)),
                    duration: 0 // 手動で閉じるまで表示
                ).ConfigureAwait(false);
            }
            else
            {
                // オプションコンポーネントの失敗 - 警告通知（10秒表示）
                await _notificationService.ShowWarningAsync(
                    Strings.Download_Failed_OptionalTitle,
                    Strings.Download_Failed_OptionalMessage,
                    duration: 10000
                ).ConfigureAwait(false);
            }

            _logger.LogInformation("[Gemini Review] ダウンロード失敗通知を表示しました");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Gemini Review] ダウンロード失敗通知の表示に失敗");
        }
    }
}
