using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using ReactiveUI;

namespace Baketa.UI.Framework;

    /// <summary>
    /// プロパティ変更通知を最適化するエクステンション
    /// </summary>
    internal static class ReactiveObjectExtensions
    {
        // 競合を回避するため、ReactiveUIの標準メソッドを使用します
        // カスタムのRaiseAndSetIfChangedメソッドは削除しました
        
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
