using System;
using System.Threading.Tasks;

namespace Baketa.UI.Services;

/// <summary>
/// UIスレッドディスパッチャーの抽象化
/// </summary>
/// <remarks>
/// [Issue #485] Headlessテスト環境での Dispatcher.UIThread.InvokeAsync デッドロックを解消するため、
/// UIスレッドへのディスパッチを抽象化します。
/// プロダクション環境では Avalonia の Dispatcher.UIThread を使用し、
/// テスト環境では同期的に実行するモックを注入できます。
/// </remarks>
public interface IUIDispatcher
{
    /// <summary>
    /// UIスレッドで非同期アクションを実行します
    /// </summary>
    /// <param name="action">実行するアクション</param>
    Task InvokeAsync(Func<Task> action);

    /// <summary>
    /// 現在のスレッドがUIスレッドかどうかを確認します
    /// </summary>
    bool CheckAccess();
}
