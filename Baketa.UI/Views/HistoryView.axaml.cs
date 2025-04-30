using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using Baketa.UI.Models;
using ReactiveUI;
using System;

namespace Baketa.UI.Views
{
    /// <summary>
    /// 翻訳履歴画面のビュー
    /// </summary>
    internal partial class HistoryView : ReactiveUserControl<HistoryViewModel>
    {
        public HistoryView()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // ビューモデルとのバインディングを設定
                
                // アイテムの削除ボタンクリックを処理
                // ListBoxのイベント委託パターンを使用
                HistoryListBox.AddHandler(Button.ClickEvent, new EventHandler<RoutedEventArgs>(OnRemoveButtonClick), handledEventsToo: true);
            });
        }

        /// <summary>
        /// 削除ボタンクリックのハンドラー
        /// </summary>
        private void OnRemoveButtonClick(object? sender, RoutedEventArgs e)
        {
            // クリックされたボタンを取得
            if (e.Source is Button button && button.Name == "RemoveButton")
            {
                // クリックされたボタンのCommandParameterを取得
                if (button.CommandParameter is TranslationHistoryItem item && ViewModel != null)
                {
                    // ViewModelのRemoveItemCommandを実行
                    ViewModel.RemoveItemCommand.Execute(item).Subscribe();
                }
                
                // イベント処理済みとしてマーク
                e.Handled = true;
            }
        }
    }
}