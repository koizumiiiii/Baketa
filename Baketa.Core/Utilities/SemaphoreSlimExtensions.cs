using System.Diagnostics.CodeAnalysis;

namespace Baketa.Core.Utilities;

/// <summary>
/// SemaphoreSlimのリソース管理を自動化するための拡張メソッド
/// RAIIパターンによりセマフォリークを完全に防止
/// </summary>
public static class SemaphoreSlimExtensions
{
    /// <summary>
    /// セマフォを非同期で取得し、usingパターンで自動解放を保証
    /// </summary>
    /// <param name="semaphore">対象のSemaphoreSlim</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>Dispose時に自動でセマフォを解放するIDisposable</returns>
    public static async Task<IDisposable> WaitAsyncDisposable(
        this SemaphoreSlim semaphore,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semaphore);

        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new SemaphoreReleaser(semaphore);
    }

    /// <summary>
    /// タイムアウト付きでセマフォを非同期取得し、usingパターンで自動解放を保証
    /// </summary>
    /// <param name="semaphore">対象のSemaphoreSlim</param>
    /// <param name="timeout">タイムアウト時間</param>
    /// <param name="cancellationToken">キャンセレーショントークン</param>
    /// <returns>Dispose時に自動でセマフォを解放するIDisposable（取得失敗時はnull）</returns>
    public static async Task<IDisposable?> WaitAsyncDisposableWithTimeout(
        this SemaphoreSlim semaphore,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(semaphore);

        var acquired = await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        return acquired ? new SemaphoreReleaser(semaphore) : null;
    }

    /// <summary>
    /// セマフォの自動解放を担当するプライベートクラス
    /// IDisposableパターンにより確実なリソース管理を実現
    /// </summary>
    private sealed class SemaphoreReleaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private bool _isDisposed;

        public SemaphoreReleaser(SemaphoreSlim semaphore)
        {
            _semaphore = semaphore;
        }

        public void Dispose()
        {
            if (_isDisposed) return;

            try
            {
                _semaphore.Release();
            }
            catch (ObjectDisposedException)
            {
                // セマフォが既に破棄されている場合は無視
                // これは正常なシャットダウン時に発生する可能性がある
            }
            catch (SemaphoreFullException)
            {
                // セマフォが既にフル状態の場合は無視
                // 二重解放やバグが原因の可能性があるが、クラッシュを避ける
            }

            _isDisposed = true;
        }
    }
}
