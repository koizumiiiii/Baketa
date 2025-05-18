using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using ReactiveUI.Validation.Helpers;
using ReactiveUI.Validation.Contexts;

namespace Baketa.UI.Framework.Validation;

    /// <summary>
    /// シンプルなバリデーションヘルパー
    /// 特定のインターフェース名に依存しない実装
    /// </summary>
    internal static class DetailedValidationHelpers
    {
        /// <summary>
        /// バリデーションオブジェクトから指定されたプロパティのエラーメッセージを取得します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="validationObject">バリデーションオブジェクト</param>
        /// <param name="propertyExpression">プロパティセレクタ</param>
        /// <returns>エラーメッセージのコレクション</returns>
        public static IEnumerable<string> GetDetailedErrorMessages<TViewModel, TProperty>(
            this ReactiveValidationObject validationObject,
            Expression<Func<TViewModel, TProperty>> propertyExpression)
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(validationObject);
            ArgumentNullException.ThrowIfNull(propertyExpression);

            // プロパティ名の取得
            string propertyName = GetPropertyName(propertyExpression);

            try
            {
                // ValidationContextを直接使用せずにエラーを取得
                // 内部実装の詳細に依存しない方法
                var context = validationObject.ValidationContext;
                var contextType = context.GetType();
                
                // リフレクションで必要なメソッドを探す
                var method = contextType.GetMethod("GetPropertyValidationStatuses", 
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                
                if (method != null)
                {
                // メソッドを呼び出して検証状態を取得
                var result = method.Invoke(context, new object[] { propertyName });
                
                // 戻り値から必要な情報を抽出
                List<string> errorList = new List<string>();
                if (result is IEnumerable<object> validationStates)
                {
                foreach (var state in validationStates)
                {
                // Text プロパティを取得
                var textProperty = state.GetType().GetProperty("Text");
                if (textProperty != null)
                {
                var text = textProperty.GetValue(state) as string;
                if (!string.IsNullOrEmpty(text))
                {
                errorList.Add(text);
                }
                }
                }
                }
                
                return errorList;
                }
                
                // 代替手段：すべてのエラーを取得してプロパティでフィルター
                // 初期値を設定するための変数
                List<string> errors = new List<string>();
                
                // IsValidプロパティを使って無効な場合のみエラーを収集
                var isValidProp = contextType.GetProperty("IsValid");
                if (isValidProp != null)
                {
                    var isValidValue = isValidProp.GetValue(context);
                    if (isValidValue != null && isValidValue is bool valid && !valid)
                    {
                        // ValidationTextプロパティを取得
                        var textMethod = contextType.GetMethod("GetErrors");
                        if (textMethod != null)
                        {
                            if (textMethod.Invoke(context, null) is IEnumerable<string> allErrors)
                            {
                                errors = new List<string>(allErrors);
                            }
                        }
                    }
                }
                
                return errors;
            }
            catch (TargetException ex)
            {
                // 対象が無効な場合のエラー
                System.Diagnostics.Debug.WriteLine($"検証エラーの取得に失敗: {ex.Message}");
                return Enumerable.Empty<string>();
            }
            catch (InvalidOperationException ex)
            {
                // 無効な操作の場合のエラー
                System.Diagnostics.Debug.WriteLine($"検証エラーの取得に失敗: {ex.Message}");
                return Enumerable.Empty<string>();
            }
            catch (MissingMethodException ex)
            {
                // メソッドが見つからない場合のエラー
                System.Diagnostics.Debug.WriteLine($"検証エラーの取得に失敗: {ex.Message}");
                return Enumerable.Empty<string>();
            }
            catch (MissingFieldException ex)
            {
                // フィールドが見つからない場合のエラー
                System.Diagnostics.Debug.WriteLine($"検証エラーの取得に失敗: {ex.Message}");
                return Enumerable.Empty<string>();
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
