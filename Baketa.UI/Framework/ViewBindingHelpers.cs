using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reflection;
using System.Windows.Input;
using Avalonia.ReactiveUI;
using DynamicData;
using ReactiveUI;
using ReactiveUI.Validation;
using ReactiveUI.Validation.Contexts;
using ReactiveUI.Validation.Abstractions;

namespace Baketa.UI.Framework;

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
            
            // ReactiveUIの標準APIを使用
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
            
            // ReactiveUIの標準APIを使用
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
            
            // コマンドをコントロールに設定
            var vmCommand = command.Compile().Invoke(view.ViewModel);
            var control = controlSelector.Compile().Invoke(view);
            
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
            if (view.ViewModel is IValidatableViewModel validationObject)
            {
                // プロパティ名を取得
                string propertyName = GetPropertyName(viewModelProperty);
                
                // シンプルな式でプロパティ変更を監視
                // ValidationContextの変更を監視して、検証エラーを取得する
                var observable = validationObject.WhenAnyValue(x => x.ValidationContext)
                    .Select(_ => GetValidationErrors(validationObject, propertyName))
                    .Select(errors => string.Join(", ", errors));

                // エラー処理など必要に応じて追加
                return observable.ObserveOn(RxApp.MainThreadScheduler)
                    .Subscribe(errorText => {
                        try {
                            // プロパティセッターを検索して適用
                                var propertyInfo = typeof(TView).GetProperty(((viewProperty.Body as MemberExpression)?.Member.Name) ?? "");
                                if (propertyInfo?.CanWrite == true)
                                {
                                    propertyInfo.SetValue(view, errorText);
                                }
                                else
                                {
                                    // プロパティが見つからない場合はプロパティ名を直接取得してログ出力
                                    string propertyName = viewProperty.Body is MemberExpression memberExpr 
                                        ? memberExpr.Member.Name 
                                        : "Unknown";
                                    Debug.WriteLine($"Property {propertyName} is not writable");
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

            // 非ValidationObjectの場合は空のバインディングを返す
            return Disposable.Empty;
        }
        
        /// <summary>
        /// バリデーションエラーメッセージを取得します
        /// </summary>
        private static List<string> GetValidationErrors(
            IValidatableViewModel validationObject, 
            string propertyName)
        {
            var errors = new List<string>();
            
            try
            {
                // ValidationContextからプロパティに対するバリデーションを取得
                var context = validationObject.ValidationContext;
                
                // ValidationContextのValidationsを空でないかチェック
                if (context?.Validations != null)
                {
                    // ValidationContextの実装に応じた処理
                    if (TryGetValidationMessages(context.Validations, propertyName, out var messages))
                    {
                        // メッセージがある場合は追加
                        errors.AddRange(messages);
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Error getting validation errors: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Error getting validation errors: {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine($"Error getting validation errors: {ex.Message}");
            }
            // 例外処理の簡素化のために削除
            // catch (Exception ex) when (false) // この条件は常にfalseのため、キャッチされない
            // {
            //    Debug.WriteLine($"Unexpected error getting validation errors: {ex.Message}");
            // }
            
            return errors;
        }
        
        /// <summary>
        /// ValidationContextからエラーメッセージを取得する試行
        /// </summary>
        private static bool TryGetValidationMessages(object validations, string propertyName, out IEnumerable<string> messages)
        {
            var result = new List<string>();
            bool success = false;
            
            try
            {
                // 方法1: Itemsプロパティを使用
                var itemsProperty = validations.GetType().GetProperty("Items");
                if (itemsProperty != null)
                {
                    var items = itemsProperty.GetValue(validations);
                    if (items != null)
                    {
                        // リフレクションでToList/ToArrayメソッドを呼び出し
                        var toListMethod = items.GetType().GetMethod("ToList", Type.EmptyTypes);
                        if (toListMethod != null)
                        {
                            if (toListMethod.Invoke(items, null) is IEnumerable<object> enumerable)
                            {
                                foreach (var item in enumerable)
                                {
                                    ProcessValidationItem(item, propertyName, result);
                                }
                                success = true;
                            }
                        }
                    }
                }
                
                // 方法2: ToListメソッドを直接呼び出し
                if (!success)
                {
                    var toListMethod = validations.GetType().GetMethod("ToList", Type.EmptyTypes);
                    if (toListMethod != null)
                    {
                        if (toListMethod.Invoke(validations, null) is IEnumerable<object> enumerable)
                        {
                            foreach (var item in enumerable)
                            {
                                ProcessValidationItem(item, propertyName, result);
                            }
                            success = true;
                        }
                    }
                }
                
                // 方法3: すべてのValidationを取得するメソッドやプロパティを探す
                if (!success)
                {
                    // validationsオブジェクトのすべてのプロパティを探索
                    var properties = validations.GetType().GetProperties();
                    foreach (var prop in properties)
                    {
                        if (prop.PropertyType.IsGenericType && 
                            prop.PropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        {
                            var value = prop.GetValue(validations);
                            if (value is IEnumerable<object> enumerable)
                            {
                                foreach (var item in enumerable)
                                {
                                    ProcessValidationItem(item, propertyName, result);
                                }
                                success = true;
                                break;
                            }
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Error getting validation messages: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Error getting validation messages: {ex.Message}");
            }
            catch (TargetInvocationException ex)
            {
                Debug.WriteLine($"Error getting validation messages: {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine($"Error getting validation messages: {ex.Message}");
            }
            
            messages = result;
            return success;
        }
        
        /// <summary>
        /// バリデーション項目を処理しエラーメッセージを収集
        /// </summary>
        private static void ProcessValidationItem(object item, string propertyName, List<string> messages)
        {
            try
            {
                // IsValidプロパティチェック
                var isValidProperty = item.GetType().GetProperty("IsValid");
                var messageProperty = item.GetType().GetProperty("Message");
                
                if (isValidProperty != null && messageProperty != null)
                {
                    bool isValid = isValidProperty.GetValue(item) is bool value && value;
                    
                    if (!isValid && IsPropertyRelated(item, propertyName))
                    {
                        var message = messageProperty.GetValue(item) as string;
                        if (!string.IsNullOrEmpty(message) && !messages.Contains(message))
                        {
                            messages.Add(message);
                        }
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Error processing validation item: {ex.Message}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Error processing validation item: {ex.Message}");
            }
            catch (TargetInvocationException ex)
            {
                Debug.WriteLine($"Error processing validation item: {ex.Message}");
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine($"Error processing validation item: {ex.Message}");
            }
        }
        
        /// <summary>
        /// バリデーションが特定のプロパティに関連しているかどうかを判定
        /// </summary>
        private static bool IsPropertyRelated(object validation, string propertyName)
        {
            try
            {
                // ContainsPropertyNameメソッドを探す
                var containsMethod = validation.GetType().GetMethod("ContainsPropertyName", [typeof(string)]);
                if (containsMethod != null)
                {
                    if (containsMethod.Invoke(validation, [propertyName]) is bool result)
                    {
                        return result;
                    }
                }
                
                // プロパティ一覧を取得する方法を探す
                var propertiesProperty = validation.GetType().GetProperty("Properties");
                if (propertiesProperty?.GetValue(validation) is IEnumerable<string> properties)
                {
                    return properties.Contains(propertyName);
                }
                
                // 判断できない場合はすべて含む
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"IsPropertyRelated error: {ex.Message}");
                return true; // エラー発生時は安全のため含める
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"IsPropertyRelated error: {ex.Message}");
                return true; // エラー発生時は安全のため含める
            }
            catch (NullReferenceException ex)
            {
                Debug.WriteLine($"IsPropertyRelated error: {ex.Message}");
                return true; // エラー発生時は安全のため含める
            }
            catch (TargetInvocationException ex)
            {
                Debug.WriteLine($"IsPropertyRelated error: {ex.Message}");
                return true; // エラー発生時は安全のため含める
            }
        }
        
        /// <summary>
        /// プロパティ式からプロパティ名を取得します
        /// </summary>
        private static string GetPropertyName<TViewModel, TProperty>(
            Expression<Func<TViewModel, TProperty>> propertyExpression)
            where TViewModel : class
        {
            return propertyExpression.Body is MemberExpression memberExpression
                ? memberExpression.Member.Name
                : throw new ArgumentException("Expression is not a property expression", nameof(propertyExpression));
        }
    }
