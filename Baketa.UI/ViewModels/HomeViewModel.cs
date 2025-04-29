using System;
using System.Reactive;
using System.Threading.Tasks;
using Baketa.UI.Framework;
using Baketa.UI.Framework.Events;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace Baketa.UI.ViewModels
{
    /// <summary>
    /// ホーム画面のビューモデル
    /// </summary>
    internal class HomeViewModel : Framework.ViewModelBase
    {
        // 履歴確認用
    private bool _hasHistory = true; // デモ用に初期値をtrueに設定
    public bool HasHistory
    {
        get => _hasHistory;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _hasHistory, value);
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
        
        // クイックアクセスコマンド
        public ReactiveCommand<Unit, Unit> StartCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenHelpCommand { get; }
        
        /// <summary>
        /// 新しいHomeViewModelを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="logger">ロガー</param>
        public HomeViewModel(IEventAggregator eventAggregator, ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // コマンドの初期化
            StartCaptureCommand = ReactiveCommandFactory.Create(ExecuteStartCaptureAsync);
            OpenSettingsCommand = ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync);
            OpenHelpCommand = ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            // イベント購読
            SubscribeToEvent<CaptureStatusChangedEvent>(OnCaptureStatusChanged);
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
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // ヘルプを開くコマンド実行
        private async Task ExecuteOpenHelpAsync()
        {
            //_logger?.LogInformation("ヘルプを開くコマンドが実行されました");
            // ヘルプ画面を開くロジック（まだ未実装）
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // キャプチャステータス変更イベントハンドラ
        private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent eventData)
        {
            if (eventData.IsActive)
            {
                StatusMessage = "キャプチャ中";
            }
            else
            {
                StatusMessage = "準備完了";
            }
            await Task.CompletedTask.ConfigureAwait(true);
        }
    }
    
    // イベント定義
    internal class StartCaptureRequestedEvent : IEvent
    {
    }
    
    internal class CaptureStatusChangedEvent(bool isActive) : IEvent
    {
        public bool IsActive { get; } = isActive;
    }
}