using System;
using System.Reactive.Disposables;
using ReactiveUI;

namespace Baketa.UI.Framework
{
    /// <summary>
    /// ReactiveObjectに対するDisposable拡張メソッド
    /// </summary>
    internal static class ReactiveObjectDisposableExtensions
    {
        /// <summary>
        /// オブジェクトを破棄対象コレクションに追加します
        /// </summary>
        /// <typeparam name="T">追加するオブジェクトの型</typeparam>
        /// <param name="This">対象オブジェクト</param>
        /// <param name="disposables">破棄可能オブジェクトコレクション</param>
        /// <returns>追加したオブジェクト</returns>
        public static T DisposeWith<T>(this T This, CompositeDisposable disposables) where T : IDisposable
        {
            ArgumentNullException.ThrowIfNull(disposables);
                
            disposables.Add(This);
            return This;
        }
    }
}
