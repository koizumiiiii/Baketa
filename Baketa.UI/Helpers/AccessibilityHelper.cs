using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using System;

namespace Baketa.UI.Helpers;

    /// <summary>
    /// アクセシビリティ関連のヘルパーメソッドを提供します。
    /// </summary>
    public static class AccessibilityHelper
    {
        /// <summary>
        /// コントロールにスクリーンリーダー用の名前を設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="name">設定する名前</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithLabel<T>(this T element, string name) where T : Control
        {
            ArgumentNullException.ThrowIfNull(element);
            
            AutomationProperties.SetName(element, name);
            return element;
        }
        
        /// <summary>
        /// コントロールにスクリーンリーダー用のヘルプテキストを設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="helpText">設定するヘルプテキスト</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithHelpText<T>(this T element, string helpText) where T : Control
        {
            ArgumentNullException.ThrowIfNull(element);
            
            AutomationProperties.SetHelpText(element, helpText);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティプロパティを一括設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="name">設定する名前</param>
        /// <param name="helpText">設定するヘルプテキスト</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithAccessibility<T>(this T element, string name, string helpText) where T : Control
        {
            ArgumentNullException.ThrowIfNull(element);
            
            AutomationProperties.SetName(element, name);
            AutomationProperties.SetHelpText(element, helpText);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティラベルを設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="labelElement">ラベル要素</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithLabeledBy<T>(this T element, Control labelElement) where T : Control
        {
            ArgumentNullException.ThrowIfNull(element);
            ArgumentNullException.ThrowIfNull(labelElement);
            
            // AvaloniaではLabeledByプロパティが名前が異なります
            AutomationProperties.SetLabeledBy(element, labelElement);
            return element;
        }
        
        /// <summary>
        /// コントロールにアクセシビリティフレームワークでのコントロール種類を設定します。
        /// </summary>
        /// <typeparam name="T">コントロール型</typeparam>
        /// <param name="element">対象コントロール</param>
        /// <param name="controlType">コントロール種類</param>
        /// <returns>設定後のコントロール</returns>
        public static T WithAutomationControlType<T>(this T element, AutomationControlType controlType) where T : Control
        {
            // nullチェックを追加
            ArgumentNullException.ThrowIfNull(element);
            
            // AvaloniaのAutomationPropertiesにSetAutomationControlTypeメソッドがない場合の代替実装
            // 新しいバージョンでは異なる設定方法を使用
            // 注：AvaloniaUIのバージョンに対応する必要あり
            
            // 代替実装: AutomationProperties.AutomationIdを使用してマーキング
            string controlTypeName = controlType.ToString();
            AutomationProperties.SetAutomationId(element, $"{element.GetType().Name}_{controlTypeName}");
            
            // 必要に応じて追加のアクセシビリティ情報を設定
            switch (controlType) {
                case AutomationControlType.Button:
                    // ボタン特有の設定
                    if (string.IsNullOrEmpty(AutomationProperties.GetName(element)))
                    {
                        AutomationProperties.SetName(element, "Button");
                    }
                    break;
                case AutomationControlType.CheckBox:
                    // チェックボックス特有の設定
                    if (string.IsNullOrEmpty(AutomationProperties.GetName(element)))
                    {
                        AutomationProperties.SetName(element, "CheckBox");
                    }
                    break;
                // 他のケースも同様に実装可能
            }
            return element;
        }
    }
