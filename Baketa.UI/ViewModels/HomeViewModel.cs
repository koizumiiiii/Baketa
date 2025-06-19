using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.Core.Abstractions.Events;
using Baketa.UI.Framework.Events;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.UI.Models;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// ホーム画面のビューモデル
    /// </summary>
    internal sealed class HomeViewModel : Framework.ViewModelBase
    {
        // 履歴確認用
        private bool _hasHistory = true; // デモ用に初期値をtrueに設定
        public bool HasHistory
        {
            get => _hasHistory;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _hasHistory, value);
        }
        
        // 最近の履歴アイテム
        private ObservableCollection<TranslationHistoryItem> _recentHistoryItems = [];
        public ObservableCollection<TranslationHistoryItem> RecentHistoryItems
        {
            get => _recentHistoryItems;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _recentHistoryItems, value);
        }
        
        // アプリケーションのステータスを表示するプロパティ
        private string _welcomeMessage = "Baketaへようこそ";
        public string WelcomeMessage
        {
            get => _welcomeMessage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _welcomeMessage, value);
        }
        
        private string _statusMessage = "準備完了";
        public string StatusMessage
        {
            get => _statusMessage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _statusMessage, value);
        }
        
        // アプリケーションの概要情報
        private string _appDescription = "Baketaは、ゲームプレイ中にリアルタイムでテキストを翻訳するWindows専用オーバーレイアプリケーションです。";
        public string AppDescription
        {
            get => _appDescription;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _appDescription, value);
        }
        
        // キャプチャステータス
        private string _captureStatus = "停止中";
        public string CaptureStatus
        {
            get => _captureStatus;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _captureStatus, value);
        }
        
        // キャプチャステータスの色
        private string _captureStatusColor = "#757575"; // 初期値は停止中の色
        public string CaptureStatusColor
        {
            get => _captureStatusColor;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _captureStatusColor, value);
        }
        
        // 翻訳エンジン
        private string _translationEngine = "Google";
        public string TranslationEngine
        {
            get => _translationEngine;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationEngine, value);
        }
        
        // 翻訳先言語
        private string _targetLanguage = "英語";
        public string TargetLanguage
        {
            get => _targetLanguage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _targetLanguage, value);
        }
        
        // メモリ使用量
        private string _memoryUsage = "45.8";
        public string MemoryUsage
        {
            get => _memoryUsage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _memoryUsage, value);
        }
        
        // 実行時間
        private string _runningTime = "0時間 25分";
        public string RunningTime
        {
            get => _runningTime;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _runningTime, value);
        }
        
        // クイックアクセスコマンド
        public ReactiveCommand<Unit, Unit> StartCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenCaptureSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTranslationSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenHelpCommand { get; }
        public ReactiveCommand<Unit, Unit> ViewAllHistoryCommand { get; }
        
        /// <summary>
        /// 新しいHomeViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public HomeViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの初期化
            StartCaptureCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteStartCaptureAsync);
            OpenSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync);
            OpenCaptureSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenCaptureSettingsAsync);
            OpenTranslationSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenTranslationSettingsAsync);
            OpenHelpCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
            ViewAllHistoryCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteViewAllHistoryAsync);
            
            // デモデータの初期化
            InitializeDemo();
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            // イベント購読
            SubscribeToEvent<CaptureStatusChangedEvent>(OnCaptureStatusChanged);
            SubscribeToEvent<TranslationSettingsChangedEvent>(OnTranslationSettingsChanged);
        }
        
        // デモデータの初期化
        private void InitializeDemo()
        {
            // 履歴のデモデータ
            if (_recentHistoryItems.Count == 0)
            {
                _recentHistoryItems.Add(new TranslationHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceText = "冒険の旅に出かけよう",
                    TranslatedText = "Let's go on an adventure",
                    SourceLanguage = "Japanese",
                    TargetLanguage = "English",
                    Engine = "Google",
                    Timestamp = DateTime.Now.AddMinutes(-5)
                });
                
                _recentHistoryItems.Add(new TranslationHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceText = "謎の古代遺跡を発見した",
                    TranslatedText = "Found a mysterious ancient ruin",
                    SourceLanguage = "Japanese",
                    TargetLanguage = "English",
                    Engine = "Google",
                    Timestamp = DateTime.Now.AddMinutes(-15)
                });
                
                _recentHistoryItems.Add(new TranslationHistoryItem
                {
                    Id = Guid.NewGuid().ToString(),
                    SourceText = "勇者の剣を手に入れた！",
                    TranslatedText = "Obtained the hero's sword!",
                    SourceLanguage = "Japanese",
                    TargetLanguage = "English",
                    Engine = "Google",
                    Timestamp = DateTime.Now.AddMinutes(-25)
                });
            }
        }
        
        // キャプチャ開始コマンド実行
        private async Task ExecuteStartCaptureAsync()
        {
            //_logger?.LogInformation("キャプチャ開始コマンドが実行されました");
            await PublishEventAsync(new StartCaptureRequestedEvent()).ConfigureAwait(false);
        }
        
        // 設定画面を開くコマンド実行
        private async Task ExecuteOpenSettingsAsync()
        {
            //_logger?.LogInformation("設定画面を開くコマンドが実行されました");
            // 設定画面を開くロジック（まだ未実装）
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ設定画面を開くコマンド実行
        private async Task ExecuteOpenCaptureSettingsAsync()
        {
            //_logger?.LogInformation("キャプチャ設定画面を開くコマンドが実行されました");
            await PublishEventAsync(new OpenCaptureSettingsRequestedEvent()).ConfigureAwait(false);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳設定画面を開くコマンド実行
        private async Task ExecuteOpenTranslationSettingsAsync()
        {
            //_logger?.LogInformation("翻訳設定画面を開くコマンドが実行されました");
            await PublishEventAsync(new OpenTranslationSettingsRequestedEvent()).ConfigureAwait(false);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // ヘルプを開くコマンド実行
        private async Task ExecuteOpenHelpAsync()
        {
            //_logger?.LogInformation("ヘルプを開くコマンドが実行されました");
            // ヘルプ画面を開くロジック（まだ未実装）
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 全履歴を表示するコマンド実行
        private async Task ExecuteViewAllHistoryAsync()
        {
            //_logger?.LogInformation("全履歴を表示するコマンドが実行されました");
            await PublishEventAsync(new OpenHistoryViewRequestedEvent()).ConfigureAwait(false);
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャステータス変更イベントハンドラ
        private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent eventData)
        {
            if (eventData.IsActive)
            {
                StatusMessage = "キャプチャ中";
                CaptureStatus = "キャプチャ中";
                CaptureStatusColor = "#4CAF50"; // SuccessColor
            }
            else
            {
                StatusMessage = "準備完了";
                CaptureStatus = "停止中";
                CaptureStatusColor = "#757575"; // TextSecondaryColor
            }
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳設定変更イベントハンドラ
        private async Task OnTranslationSettingsChanged(TranslationSettingsChangedEvent eventData)
        {
            TranslationEngine = eventData.Engine;
            TargetLanguage = eventData.TargetLanguage;
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
