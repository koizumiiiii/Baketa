using System.Threading.Tasks;
using ReactiveUI;

namespace Baketa.UI.Framework.Navigation
{
    /// <summary>
    /// ナビゲーションホストのインターフェース
    /// </summary>
    internal interface INavigationHost : IScreen
    {
        /// <summary>
        /// 現在表示中のビューモデル
        /// </summary>
        IReactiveObject CurrentViewModel { get; }
        
        /// <summary>
        /// 指定したビューモデルに画面遷移します
        /// </summary>
        /// <typeparam name="T">ビューモデルの型</typeparam>
        /// <returns>画面遷移タスク</returns>
        Task NavigateToAsync<T>() where T : IRoutableViewModel;
        
        /// <summary>
        /// 指定したビューモデルに画面遷移します (パラメータ付き)
        /// </summary>
        /// <typeparam name="T">ビューモデルの型</typeparam>
        /// <typeparam name="TParam">パラメータの型</typeparam>
        /// <param name="parameter">パラメータ</param>
        /// <returns>画面遷移タスク</returns>
        Task NavigateToAsync<T, TParam>(TParam parameter) where T : IRoutableViewModel;
        
        /// <summary>
        /// 前の画面に戻ります
        /// </summary>
        /// <returns>画面遷移タスク</returns>
        Task NavigateBackAsync();
    }
}
