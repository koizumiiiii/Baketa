using System;
using System.Threading;
using System.Threading.Tasks;

namespace Baketa.UI.Tests.TestUtilities;

/// <summary>
/// 非同期テスト用のヘルパークラス
/// </summary>
internal static class AsyncTestHelper
{
    /// <summary>
    /// 指定された条件が満たされるまで待機する
    /// </summary>
    /// <param name="condition">待機する条件</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <param name="pollIntervalMs">ポーリング間隔（ミリ秒）</param>
    /// <returns>条件が満たされた場合はtrue、タイムアウトした場合はfalse</returns>
    public static async Task<bool> WaitForConditionAsync(
        Func<bool> condition, 
        int timeoutMs = 5000, 
        int pollIntervalMs = 50)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        
        try
        {
            while (!cts.Token.IsCancellationRequested)
            {
                if (condition())
                    return true;
                    
                await Task.Delay(pollIntervalMs, cts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // タイムアウト
        }
        
        return false;
    }

    /// <summary>
    /// Observableから最初の値が発行されるまで待機する
    /// </summary>
    /// <typeparam name="T">Observable の型</typeparam>
    /// <param name="observable">監視するObservable</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>発行された値。タイムアウトした場合はdefault(T)</returns>
    public static async Task<T?> WaitForObservableAsync<T>(
        IObservable<T> observable, 
        int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var tcs = new TaskCompletionSource<T?>();
        
        using var subscription = observable.Subscribe(
            value => tcs.TrySetResult(value),
            error => tcs.TrySetException(error));
        
        try
        {
            cts.Token.Register(() => tcs.TrySetResult(default));
            return await tcs.Task;
        }
        catch (OperationCanceledException)
        {
            return default;
        }
    }

    /// <summary>
    /// 指定時間内に非同期操作が完了することを検証する
    /// </summary>
    /// <param name="operation">検証する非同期操作</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>操作が時間内に完了した場合はtrue</returns>
    public static async Task<bool> CompletesWithinTimeoutAsync(
        Func<Task> operation, 
        int timeoutMs = 1000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        
        try
        {
            await operation().WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// 複数の非同期操作を並行実行し、すべてが完了するまで待機する
    /// </summary>
    /// <param name="operations">並行実行する操作</param>
    /// <param name="timeoutMs">タイムアウト時間（ミリ秒）</param>
    /// <returns>すべての操作が時間内に完了した場合はtrue</returns>
    public static async Task<bool> AllCompleteWithinTimeoutAsync(
        Func<Task>[] operations, 
        int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        
        try
        {
            var tasks = Array.ConvertAll(operations, op => op());
            await Task.WhenAll(tasks).WaitAsync(cts.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
