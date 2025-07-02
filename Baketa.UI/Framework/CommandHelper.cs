using System;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;

namespace Baketa.UI.Framework;

    /// <summary>
    /// コマンド作成ヘルパー
    /// </summary>
    internal static class CommandHelper
    {
        /// <summary>
        /// ReactiveCommand作成のヘルパーメソッド
        /// </summary>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>作成されたコマンド</returns>
        public static ReactiveCommand<Unit, Unit> CreateCommand(
            Func<Task> execute, 
            IObservable<bool>? canExecute = null)
        {
            return Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータ付きReactiveCommand作成のヘルパーメソッド
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>作成されたコマンド</returns>
        public static ReactiveCommand<TParam, Unit> CreateCommand<TParam>(
            Func<TParam, Task> execute, 
            IObservable<bool>? canExecute = null)
        {
            return Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(execute, canExecute);
        }
        
        /// <summary>
        /// 同期アクションのReactiveCommand作成のヘルパーメソッド
        /// </summary>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>作成されたコマンド</returns>
        public static ReactiveCommand<Unit, Unit> CreateCommand(
            Action execute, 
            IObservable<bool>? canExecute = null)
        {
            return Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(execute, canExecute);
        }
        
        /// <summary>
        /// パラメータ付き同期アクションのReactiveCommand作成のヘルパーメソッド
        /// </summary>
        /// <typeparam name="TParam">パラメータ型</typeparam>
        /// <param name="execute">実行関数</param>
        /// <param name="canExecute">実行可能条件</param>
        /// <returns>作成されたコマンド</returns>
        public static ReactiveCommand<TParam, Unit> CreateCommand<TParam>(
            Action<TParam> execute, 
            IObservable<bool>? canExecute = null)
        {
            return Baketa.UI.Framework.ReactiveUI.ReactiveCommandFactory.Create(execute, canExecute);
        }
    }
