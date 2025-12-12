using Baketa.Core.Abstractions.Services;

namespace Baketa.Application.Services;

/// <summary>
/// [Issue #198] アプリケーション初期化完了シグナルの実装
/// TaskCompletionSourceを使用して、初期化完了を非同期で待機可能にする
/// [Gemini Review] Task.WaitAsyncを使用してシンプル化
/// </summary>
public class InitializationCompletionSignal : IInitializationCompletionSignal
{
    private readonly TaskCompletionSource _completionSource = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc/>
    /// <remarks>
    /// [Gemini Review] TaskCompletionSource.Task.IsCompletedSuccessfullyを直接参照
    /// volatile bool _isCompletedの重複管理を排除
    /// </remarks>
    public bool IsCompleted => _completionSource.Task.IsCompletedSuccessfully;

    /// <inheritdoc/>
    /// <remarks>
    /// [Gemini Review] Task.WaitAsyncを使用してCancellationToken対応を簡潔化
    /// .NET 6以降の標準APIを活用
    /// </remarks>
    public Task WaitForCompletionAsync(CancellationToken cancellationToken = default)
    {
        // Task.WaitAsyncは完了済みのタスクに対しても正しく動作し、キャンセルも適切に処理する
        return _completionSource.Task.WaitAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public void SignalCompletion()
    {
        _completionSource.TrySetResult();
    }
}
