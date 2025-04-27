using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace Baketa.UI.Framework
{
    /// <summary>
    /// 反応型コマンドファクトリ
    /// </summary>
    internal static class ReactiveCommandFactory
    {
        /// <summary>
        /// パラメータなしのコマンドを作成します
        /// </summary>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<Unit, Unit> Create(
            Func<Task> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータ付きのコマンドを作成します
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<TParam, Unit> Create<TParam>(
            Func<TParam, Task> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// 戻り値のあるコマンドを作成します
        /// </summary>
        /// <typeparam name="TResult">戻り値の型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<Unit, TResult> CreateWithResult<TResult>(
            Func<Task<TResult>> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータと戻り値のあるコマンドを作成します
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <typeparam name="TResult">戻り値の型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>コマンド</returns>
        public static ReactiveCommand<TParam, TResult> CreateWithResult<TParam, TResult>(
            Func<TParam, Task<TResult>> execute,
            IObservable<bool>? canExecute = null)
        {
            return ReactiveCommand.CreateFromTask(execute, canExecute);
        }
    }
}