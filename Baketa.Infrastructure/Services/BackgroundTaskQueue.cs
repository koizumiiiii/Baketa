using System.Collections.Concurrent;
using System.Threading.Channels;
using Baketa.Core.Abstractions.Services;
using Microsoft.Extensions.Logging;

namespace Baketa.Infrastructure.Services;

/// <summary>
/// バックグラウンドタスクキューの実装
/// Channel-based高性能キューでメイン処理をブロックしない
/// </summary>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, Task>> _queue;
    private readonly SemaphoreSlim _semaphore;

    public BackgroundTaskQueue(int capacity = 100)
    {
        // Bounded channel で適切な容量制限を設定
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        };

        _queue = Channel.CreateBounded<Func<CancellationToken, Task>>(options);
        _semaphore = new SemaphoreSlim(0);
    }

    public int Count => _semaphore.CurrentCount;

    public void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        if (!_queue.Writer.TryWrite(workItem))
        {
            // キューが満杯の場合は非同期で待機
            _ = Task.Run(async () =>
            {
                await _queue.Writer.WriteAsync(workItem).ConfigureAwait(false);
                _semaphore.Release();
            });
        }
        else
        {
            _semaphore.Release();
        }
    }

    public async Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        
        var workItem = await _queue.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        return workItem;
    }
}

/// <summary>
/// BackgroundTaskQueueを処理するホステッドサービス
/// </summary>
public sealed class QueuedHostedService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IBackgroundTaskQueue _taskQueue;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(IBackgroundTaskQueue taskQueue, ILogger<QueuedHostedService> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("バックグラウンドタスクキュー開始");

        await BackgroundProcessing(stoppingToken);
    }

    private async Task BackgroundProcessing(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var workItem = await _taskQueue.DequeueAsync(stoppingToken);
                
                await workItem(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // 期待される例外（停止時）
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "バックグラウンドタスク実行エラー");
            }
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("バックグラウンドタスクキュー停止");

        await base.StopAsync(stoppingToken);
    }
}