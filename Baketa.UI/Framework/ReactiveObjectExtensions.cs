using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReactiveUI;

namespace Baketa.UI.Framework
{
    /// <summary>
    /// プロパティ変更通知を最適化するエクステンション
    /// </summary>
    internal static class ReactiveObjectExtensions
    {
        /// <summary>
        /// プロパティを設定し、変更があった場合のみ通知します
        /// </summary>
        /// <typeparam name="TObj">ReactiveObjectの型</typeparam>
        /// <typeparam name="TProperty">プロパティの型</typeparam>
        /// <param name="This">ReactiveObjectインスタンス</param>
        /// <param name="backingField">バッキングフィールドの参照</param>
        /// <param name="newValue">新しい値</param>
        /// <param name="propertyName">プロパティ名</param>
        /// <returns>値が変更されたかどうか</returns>
        public static bool RaiseAndSetIfChanged<TObj, TProperty>(
            this TObj This,
            ref TProperty backingField,
            TProperty newValue,
            [CallerMemberName] string? propertyName = null)
            where TObj : ReactiveObject
        {
            ArgumentNullException.ThrowIfNull(This);
            if (EqualityComparer<TProperty>.Default.Equals(backingField, newValue))
                return false;
                
            // nullの場合は空文字列を使用
            var safeName = propertyName ?? string.Empty;
            
            This.RaisePropertyChanging(safeName);
            backingField = newValue;
            This.RaisePropertyChanged(safeName);
            return true;
        }
        
        /// <summary>
        /// 複数のプロパティ変更を一度に通知します
        /// </summary>
        /// <param name="This">ReactiveObjectインスタンス</param>
        /// <param name="propertyNames">プロパティ名の配列</param>
        public static void RaisePropertyChanged(this ReactiveObject This, params string[] propertyNames)
        {
            ArgumentNullException.ThrowIfNull(This);
            
            // propertyNamesがnullの場合は何もしない (コンパイラの警告を防ぐため、パラメータのnull許容を削除)
            if (propertyNames is null || propertyNames.Length == 0)
            {
                return;
            }
            
            foreach (var propertyName in propertyNames)
            {
                // propertyNameがnullでないことを確認してから処理
                if (propertyName is not null)
                {
                    This.RaisePropertyChanged(propertyName);
                }
            }
        }
        
        /// <summary>
        /// すべての変更通知を一時的に遅延します
        /// </summary>
        /// <param name="This">ReactiveObjectインスタンス</param>
        /// <returns>破棄可能なディレイオブジェクト</returns>
        public static IDisposable DelayChangeNotifications(this ReactiveObject This)
        {
            ArgumentNullException.ThrowIfNull(This);
            return This.SuppressChangeNotifications();
        }
    }
}
