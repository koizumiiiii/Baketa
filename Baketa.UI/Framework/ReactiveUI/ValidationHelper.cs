using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using ReactiveUI;
using ReactiveUI.Validation.Helpers;
using ReactiveUI.Validation.Components;
using DynamicData;
using System.Reflection;

namespace Baketa.UI.Framework.ReactiveUI
{
    /// <summary>
    /// ReactiveUI.Validationのユーティリティクラス
    /// バージョン間の互換性を確保するために独自実装を提供
    /// </summary>
    /// <remarks>
    /// 以前は Validation という名前でしたが、CA1724警告に対応して
    /// 名前空間 Baketa.UI.Framework.Validation との競合を回避するために
    /// ValidationHelper に名前を変更しました。
    /// </remarks>
    public static class ValidationHelper
    {
        /// <summary>
        /// ValidationContextからエラーメッセージを取得します
        /// </summary>
        /// <typeparam name="TViewModel">ビューモデルの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="validationObject">ReactiveValidationObjectインスタンス</param>
        /// <param name="propertyExpression">プロパティセレクタ</param>
        /// <returns>エラーメッセージのコレクション</returns>
        public static IEnumerable<string> GetErrorMessages<TViewModel, TProperty>(
            ReactiveValidationObject validationObject,
            Expression<Func<TViewModel, TProperty>> propertyExpression)
            where TViewModel : class
        {
            ArgumentNullException.ThrowIfNull(validationObject);
            ArgumentNullException.ThrowIfNull(propertyExpression);

            // プロパティ名を取得
            string propertyName = GetPropertyName(propertyExpression);
            
            // ValidationContextからプロパティに対するバリデーションを取得
            var context = validationObject.ValidationContext;
            var allValidations = context.Validations.Items;
            
            // エラーメッセージを収集
            var errors = new List<string>();
            foreach (var validation in allValidations)
            {
                // プロパティ名に関連するバリデーションのみを処理
                bool isValid = false;
                string? message = null;
                bool containsProperty = false;
                
                // リフレクションで各プロパティやメソッドを取得・実行
                
                // IsValidプロパティを取得
                var isValidProp = validation.GetType().GetProperty("IsValid");
                if (isValidProp != null)
                {
                    if (isValidProp.GetValue(validation) is bool value)
                    {
                        isValid = value;
                    }
                }
                
                // プロパティ名に関連するかどうかをチェック
                // ContainsPropertyNameメソッドを試す
                var containsMethod = validation.GetType().GetMethod("ContainsPropertyName", [typeof(string)]);
                if (containsMethod != null)
                {
                    if (containsMethod.Invoke(validation, [propertyName]) is bool containsResult)
                    {
                        containsProperty = containsResult;
                    }
                }
                else
                {
                    // Propertiesプロパティを探す
                    var propertiesProp = validation.GetType().GetProperty("Properties");
                    if (propertiesProp != null && propertiesProp.GetValue(validation) is IEnumerable<string> properties)
                    {
                        containsProperty = properties.Contains(propertyName);
                    }
                    else
                    {
                        // 関連付けが不明な場合は含める
                        containsProperty = true;
                    }
                }
                
                // Messageプロパティを取得
                var messageProp = validation.GetType().GetProperty("Message");
                if (messageProp != null)
                {
                    message = messageProp.GetValue(validation) as string;
                }
                else
                {
                    // エラーテキストの代替プロパティを探す
                    var textProp = validation.GetType().GetProperty("Text");
                    if (textProp != null)
                    {
                        message = textProp.GetValue(validation) as string;
                    }
                }
                
                // 無効で、プロパティに関連し、メッセージがある場合のみ追加
                if (!isValid && containsProperty && !string.IsNullOrEmpty(message))
                {
                    errors.Add(message);
                }
            }
            
            return errors;
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