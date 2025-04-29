using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels
{
    /// <summary>
    /// 翻訳履歴画面のビューモデル
    /// </summary>
    internal class HistoryViewModel : Framework.ViewModelBase
    {
        // 検索関連
        private string _searchText = string.Empty;
        public string SearchText
        {
            get => _searchText;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _searchText, value);
        }
        
        // 並び替え関連
        public string[] AvailableSortOrders { get; } = ["新しい順", "古い順", "原文 A-Z", "翻訳文 A-Z"];
        
        private string _sortOrder = "新しい順";
        public string SortOrder
        {
            get => _sortOrder;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _sortOrder, value);
        }
        
        // 詳細表示関連
        public string DetailSourceText => SelectedItem?.SourceText ?? string.Empty;
        public string DetailTranslatedText => SelectedItem?.TranslatedText ?? string.Empty;
        public DateTime DetailTimestamp => SelectedItem?.Timestamp ?? DateTime.Now;
        public string DetailSourceLanguage => SelectedItem?.SourceLanguage ?? string.Empty;
        public string DetailTargetLanguage => SelectedItem?.TargetLanguage ?? string.Empty;
        public string DetailEngine => SelectedItem?.Engine ?? string.Empty;
        // 翻訳履歴コレクション
        private ObservableCollection<TranslationHistoryItem> _historyItems = [];
        public ObservableCollection<TranslationHistoryItem> HistoryItems
        {
            get => _historyItems;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _historyItems, value);
        }
        
        // 選択中のアイテム
        private TranslationHistoryItem? _selectedItem;
        public TranslationHistoryItem? SelectedItem
        {
            get => _selectedItem;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedItem, value);
        }
        
        // コマンド
        public ReactiveCommand<Unit, Unit> ClearHistoryCommand { get; }
        public ReactiveCommand<Unit, Unit> ExportHistoryCommand { get; }
        public ReactiveCommand<TranslationHistoryItem, Unit> CopySourceCommand { get; }
        public ReactiveCommand<Unit, Unit> SearchCommand { get; }
        public ReactiveCommand<TranslationHistoryItem, Unit> RemoveItemCommand { get; }
        public ReactiveCommand<TranslationHistoryItem, Unit> CopyTranslationCommand { get; }
        
        /// <summary>
        /// 新しいHistoryViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public HistoryViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの初期化
            ClearHistoryCommand = ReactiveCommandFactory.Create(ExecuteClearHistoryAsync);
            ExportHistoryCommand = ReactiveCommandFactory.Create(ExecuteExportHistoryAsync);
            CopySourceCommand = ReactiveCommandFactory.Create<TranslationHistoryItem>(ExecuteCopySourceAsync);
            CopyTranslationCommand = ReactiveCommandFactory.Create<TranslationHistoryItem>(ExecuteCopyTranslationAsync);
            
            SearchCommand = ReactiveCommandFactory.Create(ExecuteSearchAsync);
            RemoveItemCommand = ReactiveCommandFactory.Create<TranslationHistoryItem>(ExecuteRemoveItemAsync);
            
            // デモ用のサンプルデータ
            LoadSampleData();
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
        // イベント購読
        SubscribeToEvent<TranslationCompletedEvent>(OnTranslationCompleted);
        }
        
        // 履歴クリアコマンド実行
        private async Task ExecuteClearHistoryAsync()
        {
            // 確認を表示
            // 実際の実装では確認ダイアログを表示
            
            // 履歴をクリア
            HistoryItems.Clear();
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 履歴エクスポートコマンド実行
        private async Task ExecuteExportHistoryAsync()
        {
            // エクスポート処理
            // 実際の実装ではファイル保存ダイアログを表示
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 原文コピーコマンド実行
        private async Task ExecuteCopySourceAsync(TranslationHistoryItem item)
        {
            // クリップボードにコピー処理
            // 実際の実装ではクリップボードAPIを使用
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳文コピーコマンド実行
        private async Task ExecuteCopyTranslationAsync(TranslationHistoryItem item)
        {
            // クリップボードにコピー処理
            // 実際の実装ではクリップボードAPIを使用
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 検索実行
        private async Task ExecuteSearchAsync()
        {
            // 検索処理の実装
            // 実際の実装では、_searchTextを使用してフィルタリング
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 項目削除実行
        private async Task ExecuteRemoveItemAsync(TranslationHistoryItem item)
        {
            if (item != null)
            {
                HistoryItems.Remove(item);
                
                // 選択されている項目が削除された場合、選択を解除
                if (SelectedItem == item)
                {
                    SelectedItem = null;
                }
            }
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳完了イベントハンドラ
        private async Task OnTranslationCompleted(TranslationCompletedEvent eventData)
        {
            // 履歴に追加
            var newItem = new TranslationHistoryItem
            {
                SourceText = eventData.SourceText,
                TranslatedText = eventData.TranslatedText,
                Timestamp = DateTime.Now
            };
            
            HistoryItems.Insert(0, newItem);
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // サンプルデータ読み込み（デモ用）
        private void LoadSampleData()
        {
            HistoryItems.Add(new TranslationHistoryItem
            {
                SourceText = "こんにちは、世界",
                TranslatedText = "Hello, world",
                Timestamp = DateTime.Now.AddMinutes(-5)
            });
            
            HistoryItems.Add(new TranslationHistoryItem
            {
                SourceText = "ゲームの世界を冒険しよう",
                TranslatedText = "Let's explore the game world",
                Timestamp = DateTime.Now.AddMinutes(-10)
            });
            
            HistoryItems.Add(new TranslationHistoryItem
            {
                SourceText = "攻撃ボタンを押して敵を倒せ",
                TranslatedText = "Press the attack button to defeat enemies",
                Timestamp = DateTime.Now.AddMinutes(-15)
            });
        }
    }
    
    /// <summary>
    /// 翻訳履歴アイテム
    /// </summary>
    internal class TranslationHistoryItem()
    {
        public string SourceText { get; set; } = string.Empty;
        public string TranslatedText { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string SourceLanguage { get; set; } = "日本語";
        public string TargetLanguage { get; set; } = "英語";
        public string Engine { get; set; } = "Google";
    }
    
    internal class TranslationCompletedEvent(string sourceText, string translatedText) : IEvent
    {
        public string SourceText { get; } = sourceText;
        public string TranslatedText { get; } = translatedText;
    }
}