using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.ReactiveUI;
using Baketa.UI.ViewModels;
using ReactiveUI;
using System;
using System.Reactive;
using System.Reactive.Disposables;

namespace Baketa.UI.Views;

    /// <summary>
    /// 設定画面のビュー
    /// </summary>
    internal sealed partial class SettingsView : ReactiveUserControl<SettingsViewModel>
    {
        public SettingsView()
        {
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                // ListBoxItemの特定のイベントを処理
                if (this.FindControl<ListBox>("CategoryListBox") is ListBox listBox)
                {
                    // ListBoxItemのTappedイベントをハンドリング
                    listBox.AddHandler(InputElement.TappedEvent, 
                    new EventHandler<TappedEventArgs>(OnCategoryItemTapped), 
                    handledEventsToo: true);
                }
                
                // 個別のListBoxItemを取得してハンドラーを追加
                RegisterItemHandler("GeneralSettingItem", SettingsViewModel.SettingCategory.General);
                RegisterItemHandler("AppearanceSettingItem", SettingsViewModel.SettingCategory.Appearance);
                RegisterItemHandler("LanguageSettingItem", SettingsViewModel.SettingCategory.Language);
                RegisterItemHandler("HotkeysSettingItem", SettingsViewModel.SettingCategory.Hotkeys);
                RegisterItemHandler("AdvancedSettingItem", SettingsViewModel.SettingCategory.Advanced);
            });
        }
        
        /// <summary>
        /// ListBoxItemがタップされたときのイベントハンドラー
        /// </summary>
        private void OnCategoryItemTapped(object? sender, TappedEventArgs e)
        {
            if (e.Source is ListBoxItem item && ViewModel != null)
            {
                // アイテム名からカテゴリーを特定
                string itemName = item.Name ?? string.Empty;
                
                switch (itemName)
                {
                    case "GeneralSettingItem":
                        ViewModel.SelectCategoryCommand.Execute(SettingsViewModel.SettingCategory.General);
                        break;
                    case "AppearanceSettingItem":
                        ViewModel.SelectCategoryCommand.Execute(SettingsViewModel.SettingCategory.Appearance);
                        break;
                    case "LanguageSettingItem":
                        ViewModel.SelectCategoryCommand.Execute(SettingsViewModel.SettingCategory.Language);
                        break;
                    case "HotkeysSettingItem":
                        ViewModel.SelectCategoryCommand.Execute(SettingsViewModel.SettingCategory.Hotkeys);
                        break;
                    case "AdvancedSettingItem":
                        ViewModel.SelectCategoryCommand.Execute(SettingsViewModel.SettingCategory.Advanced);
                        break;
                }
                
                // イベントを処理済みとしてマーク
                e.Handled = true;
            }
        }
        
        /// <summary>
        /// 個別のListBoxItemにハンドラーを登録するヘルパーメソッド
        /// </summary>
        private void RegisterItemHandler(string itemName, SettingsViewModel.SettingCategory category)
        {
            if (this.FindControl<ListBoxItem>(itemName) is ListBoxItem item && ViewModel is not null)
            {
                item.Tapped += (s, e) => 
                {
                    ViewModel.SelectCategoryCommand.Execute(category);
                    e.Handled = true;
                };
            }
        }
    }
