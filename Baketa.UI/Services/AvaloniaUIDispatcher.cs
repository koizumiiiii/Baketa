using System;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace Baketa.UI.Services;

/// <summary>
/// Avalonia UIスレッドディスパッチャーの実装
/// </summary>
/// <remarks>
/// [Issue #485] プロダクション環境で使用する実装。
/// Avalonia の Dispatcher.UIThread を使用してUIスレッドにディスパッチします。
/// </remarks>
internal sealed class AvaloniaUIDispatcher : IUIDispatcher
{
    /// <inheritdoc/>
    public async Task InvokeAsync(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        await Dispatcher.UIThread.InvokeAsync(action).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public bool CheckAccess() => Dispatcher.UIThread.CheckAccess();
}
