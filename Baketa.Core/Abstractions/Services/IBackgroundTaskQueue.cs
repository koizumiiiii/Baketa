namespace Baketa.Core.Abstractions.Services;

/// <summary>
/// バックグラウンドタスクキューの抽象化
/// メイン処理をブロックしない非同期処理のためのキュー
/// </summary>
public interface IBackgroundTaskQueue
{
    /// <summary>
    /// バックグラウンドで実行するタスクをキューに追加
    /// </summary>
    void QueueBackgroundWorkItem(Func<CancellationToken, Task> workItem);
    
    /// <summary>
    /// キューからタスクを取得（ホステッドサービス用）
    /// </summary>
    Task<Func<CancellationToken, Task>> DequeueAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// キューに蓄積されているタスク数
    /// </summary>
    int Count { get; }
}