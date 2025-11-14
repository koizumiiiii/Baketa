namespace Baketa.Infrastructure.Tests.TestUtilities;

/// <summary>
/// 非同期テスト用ユーティリティ
/// xUnit1031警告の根本解決：ブロッキング呼び出し回避
/// </summary>
public static class AsyncTestHelper
{
    /// <summary>
    /// 安全な非同期待機（デッドロック回避）
    /// Task.Wait()の代替
    /// </summary>
    /// <param name="task">待機対象のTask</param>
    /// <param name="timeoutMs">タイムアウト（ミリ秒）</param>
    public static async Task SafeWaitAsync(Task task, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Task did not complete within {timeoutMs}ms");
        }
    }

    /// <summary>
    /// 安全な非同期結果取得（デッドロック回避）
    /// Task<T>.Result の代替
    /// </summary>
    /// <typeparam name="T">結果の型</typeparam>
    /// <param name="task">待機対象のTask</param>
    /// <param name="timeoutMs">タイムアウト（ミリ秒）</param>
    /// <returns>タスクの結果</returns>
    public static async Task<T> SafeGetResultAsync<T>(Task<T> task, int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            return await task.WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Task did not complete within {timeoutMs}ms");
        }
    }

    /// <summary>
    /// 複数タスクの安全な並行実行
    /// </summary>
    /// <param name="tasks">実行するタスク群</param>
    /// <param name="timeoutMs">全体のタイムアウト（ミリ秒）</param>
    public static async Task SafeWhenAllAsync(IEnumerable<Task> tasks, int timeoutMs = 10000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await Task.WhenAll(tasks).WaitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cts.Token.IsCancellationRequested)
        {
            throw new TimeoutException($"Tasks did not complete within {timeoutMs}ms");
        }
    }
}
