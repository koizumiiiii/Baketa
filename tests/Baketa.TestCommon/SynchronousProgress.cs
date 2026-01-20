namespace Baketa.TestCommon;

/// <summary>
/// 同期的にコールバックを実行するIProgress&lt;T&gt;実装
/// </summary>
/// <remarks>
/// <para>
/// 標準の<see cref="Progress{T}"/>はSynchronizationContextまたはThreadPool経由で
/// 非同期的にコールバックを実行するため、テストでタイミング問題が発生します。
/// </para>
/// <para>
/// この実装は即座に同期的にコールバックを実行するため、テストで確実に
/// 進捗レポートをキャプチャできます。
/// </para>
/// </remarks>
/// <typeparam name="T">進捗レポートの型</typeparam>
/// <example>
/// <code>
/// var reports = new List&lt;MyProgress&gt;();
/// var progress = new SynchronousProgress&lt;MyProgress&gt;(p => reports.Add(p));
///
/// await sut.DoWorkAsync(progress);
///
/// reports.Should().NotBeEmpty();
/// </code>
/// </example>
public sealed class SynchronousProgress<T> : IProgress<T>
{
    private readonly Action<T> _handler;

    /// <summary>
    /// 新しい<see cref="SynchronousProgress{T}"/>インスタンスを作成します
    /// </summary>
    /// <param name="handler">進捗レポート時に呼び出されるハンドラ</param>
    /// <exception cref="ArgumentNullException"><paramref name="handler"/>がnullの場合</exception>
    public SynchronousProgress(Action<T> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <inheritdoc/>
    public void Report(T value) => _handler(value);
}
