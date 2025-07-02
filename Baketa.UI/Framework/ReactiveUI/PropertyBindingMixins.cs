using System;
using System.Linq.Expressions;
using System.Reactive.Disposables;
using ReactiveUI;

namespace Baketa.UI.Framework.ReactiveUI;

    /// <summary>
    /// ReactiveUIのPropertyBindingMixinsのラッパークラス
    /// バージョン間の互換性を確保するために独自実装を提供
    /// </summary>
    public static class PropertyBindingMixins
    {
        /// <summary>
        /// 一方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TVMProp">ビューモデルプロパティの型</typeparam>
        /// <typeparam name="TVProp">ビュープロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewModel">ビューモデルインスタンス</param>
        /// <param name="vmProperty">ビューモデルプロパティセレクタ</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="conversionHint">変換ヒント（オプション）</param>
        /// <param name="vmToViewConverter">ビューモデルからビューへの変換器（オプション）</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable OneWayBind<TViewModel, TView, TVMProp, TVProp>(
            TView view, 
            TViewModel? viewModel,
            Expression<Func<TViewModel, TVMProp?>> vmProperty,
            Expression<Func<TView, TVProp>> viewProperty,
            object? conversionHint = null,
            IBindingTypeConverter? vmToViewConverter = null)
            where TViewModel : class
            where TView : class, IViewFor<TViewModel>
        {
            // ReactiveUIの標準APIを使用
            return global::ReactiveUI.PropertyBindingMixins.OneWayBind(
                view,
                viewModel,
                vmProperty,
                viewProperty,
                conversionHint,
                vmToViewConverter);
        }

        /// <summary>
        /// 双方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TVMProp">ビューモデルプロパティの型</typeparam>
        /// <typeparam name="TVProp">ビュープロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewModel">ビューモデルインスタンス</param>
        /// <param name="vmProperty">ビューモデルプロパティセレクタ</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="conversionHint">変換ヒント（オプション）</param>
        /// <param name="vmToViewConverter">ビューモデルからビューへの変換器（オプション）</param>
        /// <param name="viewToVmConverter">ビューからビューモデルへの変換器（オプション）</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable Bind<TViewModel, TView, TVMProp, TVProp>(
            TView view, 
            TViewModel? viewModel,
            Expression<Func<TViewModel, TVMProp?>> vmProperty,
            Expression<Func<TView, TVProp>> viewProperty,
            object? conversionHint = null,
            IBindingTypeConverter? vmToViewConverter = null,
            IBindingTypeConverter? viewToVmConverter = null)
            where TViewModel : class
            where TView : class, IViewFor<TViewModel>
        {
            // ReactiveUIの標準APIを使用
            return global::ReactiveUI.PropertyBindingMixins.Bind(
                view,
                viewModel,
                vmProperty,
                viewProperty,
                conversionHint,
                vmToViewConverter,
                viewToVmConverter);
        }
    }
