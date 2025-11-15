using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace Baketa.UI.Framework.ReactiveUI;

/// <summary>
/// ReactiveCommandを作成するファクトリークラス
/// バージョン互換性を吸収するためのラッパー
/// </summary>
public static class ReactiveCommandFactory
{
    /// <summary>
    /// パラメーターなしのReactiveCommandを作成します
    /// </summary>
    /// <param name="execute">実行時の処理</param>
    /// <param name="canExecute">実行可能条件（オプション）</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<Unit, Unit> Create(Func<Task> execute, IObservable<bool>? canExecute = null)
    {
        return ReactiveCommand.CreateFromTask(execute, canExecute);
    }

    /// <summary>
    /// パラメーターなしのReactiveCommandを作成します（同期バージョン）
    /// </summary>
    /// <param name="execute">実行時の処理</param>
    /// <param name="canExecute">実行可能条件（オプション）</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<Unit, Unit> Create(Action execute, IObservable<bool>? canExecute = null)
    {
        return ReactiveCommand.Create(execute, canExecute);
    }

    /// <summary>
    /// パラメーター付きのReactiveCommandを作成します
    /// </summary>
    /// <typeparam name="TParam">パラメーター型</typeparam>
    /// <param name="execute">実行時の処理</param>
    /// <param name="canExecute">実行可能条件（オプション）</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<TParam, Unit> Create<TParam>(Func<TParam, Task> execute, IObservable<bool>? canExecute = null)
    {
        return ReactiveCommand.CreateFromTask<TParam>(execute, canExecute);
    }

    /// <summary>
    /// パラメーター付きのReactiveCommandを作成します（同期バージョン）
    /// </summary>
    /// <typeparam name="TParam">パラメーター型</typeparam>
    /// <param name="execute">実行時の処理</param>
    /// <param name="canExecute">実行可能条件（オプション）</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<TParam, Unit> Create<TParam>(Action<TParam> execute, IObservable<bool>? canExecute = null)
    {
        return ReactiveCommand.Create<TParam>(execute, canExecute);
    }

    /// <summary>
    /// 戻り値を持つReactiveCommandを作成します
    /// </summary>
    /// <typeparam name="TResult">戻り値の型</typeparam>
    /// <param name="execute">実行時の処理</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<Unit, TResult> CreateWithResult<TResult>(Func<Task<TResult>> execute)
    {
        return ReactiveCommand.CreateFromTask(execute);
    }

    /// <summary>
    /// パラメーターと戻り値を持つReactiveCommandを作成します
    /// </summary>
    /// <typeparam name="TParam">パラメーター型</typeparam>
    /// <typeparam name="TResult">戻り値の型</typeparam>
    /// <param name="execute">実行時の処理</param>
    /// <returns>作成されたReactiveCommand</returns>
    public static ReactiveCommand<TParam, TResult> CreateWithResult<TParam, TResult>(Func<TParam, Task<TResult>> execute)
    {
        return ReactiveCommand.CreateFromTask<TParam, TResult>(execute);
    }
}
