using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using ReactiveUI.Validation.Helpers;
using Baketa.UI.Framework.ReactiveUI;

namespace Baketa.UI.Framework.Validation;

    /// <summary>
    /// シンプルなバリデーションヘルパー
    /// </summary>
    public static class SimpleValidationHelpers
    {
        /// <summary>
        /// バリデーションオブジェクトからエラーメッセージを取得します
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
            // シンプルなリダイレクトとしてReactiveUI名前空間のValidationHelperクラスを使用
            return Baketa.UI.Framework.ReactiveUI.ValidationHelper.GetErrorMessages(
                validationObject, propertyExpression);
        }
    }
