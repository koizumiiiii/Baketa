using System;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Input;
using Avalonia.ReactiveUI;
using Baketa.UI.Framework.Validation;
using ReactiveUI;

namespace Baketa.UI.Framework
{
    /// <summary>
    /// ビューとビューモデルをバインドするヘルパー
    /// </summary>
    internal static class ViewBindingHelpers
    {
        /// <summary>
        /// ビューにビューモデルをバインドします
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="disposables">破棄可能オブジェクトコレクション</param>
        public static void BindViewModel<TView, TViewModel>(this TView view, CompositeDisposable disposables)
            where TView : class, IViewFor<TViewModel>
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(disposables);
            
            // ビューモデルがnullでないことを確認
            if (view.ViewModel == null)
            {
                throw new InvalidOperationException($"ViewModel is not set for {view.GetType().Name}");
            }

            // アクティブ化可能なビューモデルの場合
            if (view.ViewModel is IActivatableViewModel activatable)
            {
                view.WhenActivated(d => 
                {
                    d.Add(Disposable.Create(() => {})); // アクティベーション時の処理
                });
            }
        }
        
        /// <summary>
        /// 一方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="viewModelProperty">ビューモデルプロパティセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable OneWayBind<TView, TViewModel, TProperty>(
            this TView view,
            Expression<Func<TView, TProperty>> viewProperty,
            Expression<Func<TViewModel, TProperty?>> viewModelProperty)
            where TView : class, IViewFor<TViewModel>
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(viewProperty);
            ArgumentNullException.ThrowIfNull(viewModelProperty);
            
            if (view.ViewModel == null)
            {
                throw new InvalidOperationException($"ViewModel is not set for {view.GetType().Name}");
            }
            
            // ReactiveUIのOneWayBindメソッドを使用
            return ReactiveUI.PropertyBindingMixins.OneWayBind(
                view, 
                view.ViewModel, 
                viewModelProperty, 
                viewProperty);
        }
        
        /// <summary>
        /// 双方向バインディングを設定します
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewProperty">ビュープロパティセレクタ</param>
        /// <param name="viewModelProperty">ビューモデルプロパティセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable TwoWayBind<TView, TViewModel, TProperty>(
            this TView view,
            Expression<Func<TView, TProperty>> viewProperty,
            Expression<Func<TViewModel, TProperty?>> viewModelProperty)
            where TView : class, IViewFor<TViewModel>
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(viewProperty);
            ArgumentNullException.ThrowIfNull(viewModelProperty);
            
            if (view.ViewModel == null)
            {
                throw new InvalidOperationException($"ViewModel is not set for {view.GetType().Name}");
            }
            
            // ReactiveUIのBindメソッドを使用
            return ReactiveUI.PropertyBindingMixins.Bind(
                view, 
                view.ViewModel, 
                viewModelProperty, 
                viewProperty);
        }
        
        /// <summary>
        /// コマンドをバインドします
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TControl">コントロールの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="controlSelector">コントロールセレクタ</param>
        /// <param name="command">コマンドセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable BindCommand<TView, TViewModel, TControl>(
            this TView view,
            Expression<Func<TView, TControl>> controlSelector,
            Expression<Func<TViewModel, ReactiveCommand<Unit, Unit>>> command)
            where TView : class, IViewFor<TViewModel>
            where TViewModel : class
            where TControl : class, ICommand
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(controlSelector);
            ArgumentNullException.ThrowIfNull(command);
            
            if (view.ViewModel == null)
            {
                throw new InvalidOperationException($"ViewModel is not set for {view.GetType().Name}");
            }
            
            // ReactiveUIのBind系メソッドを使用
            var vmCommand = command.Compile().Invoke(view.ViewModel);
            var control = controlSelector.Compile().Invoke(view);
            
            // コマンドをコントロールに設定
            // リフレクションを使用してコマンドプロパティを設定
            var commandProperty = typeof(TControl).GetProperty("Command");
            if (commandProperty != null && commandProperty.CanWrite)
            {
                commandProperty.SetValue(control, vmCommand);
            }
            
            return Disposable.Create(() => 
            {
                if (commandProperty != null && commandProperty.CanWrite)
                {
                    commandProperty.SetValue(control, null);
                }
            });
        }
        
        /// <summary>
        /// バリデーションエラーをバインドします
        /// </summary>
        /// <typeparam name="TView">ビューの型</typeparam>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="view">ビューインスタンス</param>
        /// <param name="viewProperty">エラーテキストプロパティセレクタ</param>
        /// <param name="viewModelProperty">検証対象プロパティセレクタ</param>
        /// <returns>破棄可能なバインディング</returns>
        public static IDisposable BindValidation<TView, TViewModel, TProperty>(
            this TView view,
            Expression<Func<TView, string?>> viewProperty,
            Expression<Func<TViewModel, TProperty?>> viewModelProperty)
            where TView : class, IViewFor<TViewModel>
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(view);
            ArgumentNullException.ThrowIfNull(viewProperty);
            ArgumentNullException.ThrowIfNull(viewModelProperty);
            
            if (view.ViewModel == null)
            {
                throw new InvalidOperationException($"ViewModel is not set for {view.GetType().Name}");
            }
            
            // ReactiveUI.Validation拡張メソッドの利用
            if (view.ViewModel is ReactiveUI.Validation.Helpers.ReactiveValidationObject validationObject)
            {
                // シンプルな式でプロパティ変更を監視
                // ValidationContextの変更を監視して、検証エラーを取得する
                var observable = validationObject.WhenAnyValue(x => x.ValidationContext)
                    .Select(_ => SimpleValidationHelpers.GetErrorMessages(validationObject, viewModelProperty))
                    .Select(errors => string.Join(", ", errors));

                // エラー処理など必要に応じて追加
                return observable.ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(errorText => {
                        try {
                            // プロパティセッターを検索して適用
                            var propertyInfo = typeof(TView).GetProperty(((MemberExpression)viewProperty.Body).Member.Name);
                            if (propertyInfo != null && propertyInfo.CanWrite)
                            {
                                propertyInfo.SetValue(view, errorText);
                            }
                            else
                            {
                                Debug.WriteLine($"Property {((MemberExpression)viewProperty.Body).Member.Name} is not writable");
                            }
                        } catch (InvalidOperationException ex) {
                            // 無効な操作の例外
                            Debug.WriteLine($"BindValidation error: {ex.Message}");
                        } catch (ArgumentException ex) {
                            // 引数の例外
                            Debug.WriteLine($"BindValidation error: {ex.Message}");
                        } catch (MissingMemberException ex) {
                            // メンバが見つからない例外
                            Debug.WriteLine($"BindValidation error: {ex.Message}");
                        } catch (MethodAccessException ex) {
                            // メソッドアクセス例外
                            Debug.WriteLine($"BindValidation error: {ex.Message}");
                        }
                    });
            }

            // 非ReactiveValidationObjectの場合は空のバインディングを返す
            return Disposable.Empty;
        }
        
        /// <summary>
        /// プロパティ式からプロパティ名を取得します
        /// </summary>
        private static string GetPropertyName<TViewModel, TProperty>(
            Expression<Func<TViewModel, TProperty>> propertyExpression)
            where TViewModel : class
        {
            if (propertyExpression.Body is MemberExpression memberExpression)
            {
                return memberExpression.Member.Name;
            }

            throw new ArgumentException("Expression is not a property expression", nameof(propertyExpression));
        }
    }
}