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
    /// メインウィンドウのビューモデル
    /// </summary>
    internal class MainWindowViewModel : Framework.ViewModelBase
    {
        // 選択中のタブインデックス
        private int _selectedTabIndex;
        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedTabIndex, value);
        }
        
        // ステータスメッセージ
        private string _statusMessage = "準備完了";
        public string StatusMessage
        {
            get => _statusMessage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _statusMessage, value);
        }
        
        // 翻訳エンジン
        private string _translationEngine = "Google";
        public string TranslationEngine
        {
            get => _translationEngine;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationEngine, value);
        }
        
        // キャプチャ状態
        private bool _isCapturing;
        public bool IsCapturing
        {
            get => _isCapturing;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isCapturing, value);
        }
        
        // キャプチャ状態表示用
        private string _captureStatus = "停止中";
        public string CaptureStatus
        {
            get => _captureStatus;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _captureStatus, value);
        }
        
        // 処理進捗
        private double _progress;
        public double Progress
        {
            get => _progress;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _progress, value);
        }
        
        // 処理中フラグ
        private bool _isProcessing;
        public bool IsProcessing
        {
            get => _isProcessing;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isProcessing, value);
        }
        
        // エラーメッセージ (newで基底クラスのプロパティを隠す)
        private string? _errorMessage;
        public new string? ErrorMessage
        {
        get => _errorMessage;
        set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _errorMessage, value);
        }
        
        // 各タブのビューモデル
        public HomeViewModel HomeViewModel { get; }
        public CaptureViewModel CaptureViewModel { get; }
        public TranslationViewModel TranslationViewModel { get; }
        public OverlayViewModel OverlayViewModel { get; }
        public HistoryViewModel HistoryViewModel { get; }
        
        // コマンド
        public ReactiveCommand<Unit, Unit> OpenSettingsCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> StartCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> StopCaptureCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectRegionCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenLogViewerCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTranslationHistoryCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenHelpCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenAboutCommand { get; }
        public ReactiveCommand<Unit, Unit> MinimizeToTrayCommand { get; }
        
        /// <summary>
        /// 新しいメインウィンドウビューモデルを初期化します
        /// </summary>
        /// <param name="eventAggregator">イベント集約器</param>
        /// <param name="homeViewModel">ホームビューモデル</param>
        /// <param name="captureViewModel">キャプチャビューモデル</param>
        /// <param name="translationViewModel">翻訳ビューモデル</param>
        /// <param name="overlayViewModel">オーバーレイビューモデル</param>
        /// <param name="historyViewModel">履歴ビューモデル</param>
        /// <param name="logger">ロガー</param>
        public MainWindowViewModel(
            IEventAggregator eventAggregator,
            HomeViewModel homeViewModel,
            CaptureViewModel captureViewModel,
            TranslationViewModel translationViewModel,
            OverlayViewModel overlayViewModel,
            HistoryViewModel historyViewModel,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // 各タブのビューモデルを初期化
            HomeViewModel = homeViewModel;
            CaptureViewModel = captureViewModel;
            TranslationViewModel = translationViewModel;
            OverlayViewModel = overlayViewModel;
            HistoryViewModel = historyViewModel;
            
            // コマンドの実行可否条件
            var canStartCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => !isCapturing);
                
            var canStopCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => isCapturing);
            
            // コマンドの初期化
            OpenSettingsCommand = ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync);
            ExitCommand = ReactiveCommandFactory.Create(ExecuteExitAsync);
            StartCaptureCommand = ReactiveCommandFactory.Create(ExecuteStartCaptureAsync, canStartCapture);
            StopCaptureCommand = ReactiveCommandFactory.Create(ExecuteStopCaptureAsync, canStopCapture);
            SelectRegionCommand = ReactiveCommandFactory.Create(ExecuteSelectRegionAsync);
            OpenLogViewerCommand = ReactiveCommandFactory.Create(ExecuteOpenLogViewerAsync);
            OpenTranslationHistoryCommand = ReactiveCommandFactory.Create(ExecuteOpenTranslationHistoryAsync);
            OpenHelpCommand = ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
            OpenAboutCommand = ReactiveCommandFactory.Create(ExecuteOpenAboutAsync);
            MinimizeToTrayCommand = ReactiveCommandFactory.Create(ExecuteMinimizeToTrayAsync);
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            // イベント購読
            SubscribeToEvent<TranslationCompletedEvent>(OnTranslationCompleted);
            SubscribeToEvent<CaptureStatusChangedEvent>(OnCaptureStatusChanged);
            SubscribeToEvent<TranslationSettingsChangedEvent>(OnTranslationSettingsChanged);
            SubscribeToEvent<TranslationErrorEvent>(OnTranslationError);
        }
        
        // 設定画面を開くコマンド実行
        private async Task ExecuteOpenSettingsAsync()
        {
            //_logger?.LogInformation("設定画面を開くコマンドが実行されました");
            
            // 設定タブに切り替え
            SelectedTabIndex = 1; // CaptureViewModelタブ
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // アプリケーション終了コマンド実行
        private async Task ExecuteExitAsync()
        {
            //_logger?.LogInformation("アプリケーション終了コマンドが実行されました");
            
            // 終了前の確認（実際にはダイアログ表示）
            await PublishEventAsync(new ApplicationExitRequestedEvent()).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ開始コマンド実行
        private async Task ExecuteStartCaptureAsync()
        {
            //_logger?.LogInformation("キャプチャ開始コマンドが実行されました");
            
            await PublishEventAsync(new StartCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = true;
            CaptureStatus = "キャプチャ中";
            StatusMessage = "キャプチャを開始しました";
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ停止コマンド実行
        private async Task ExecuteStopCaptureAsync()
        {
            //_logger?.LogInformation("キャプチャ停止コマンドが実行されました");
            
            await PublishEventAsync(new StopCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = false;
            CaptureStatus = "停止中";
            StatusMessage = "キャプチャを停止しました";
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 領域選択コマンド実行
        private async Task ExecuteSelectRegionAsync()
        {
            //_logger?.LogInformation("領域選択コマンドが実行されました");
            
            // CaptureViewModelタブに切り替え
            SelectedTabIndex = 1;
            
            // 領域選択コマンドを実行
            // Note: ReactiveCommandの.Executeは非同期メソッドではなく、直接awaitできない
            CaptureViewModel.SelectRegionCommand.Execute().Subscribe();
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // ログビューワーを開くコマンド実行
        private async Task ExecuteOpenLogViewerAsync()
        {
            //_logger?.LogInformation("ログビューワーを開くコマンドが実行されました");
            
            // ログビューワーを開くロジック
            // (まだ実装されていません)
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳履歴を開くコマンド実行
        private async Task ExecuteOpenTranslationHistoryAsync()
        {
            //_logger?.LogInformation("翻訳履歴を開くコマンドが実行されました");
            
            // 履歴タブに切り替え
            SelectedTabIndex = 4; // HistoryViewModelタブ
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // ヘルプを開くコマンド実行
        private async Task ExecuteOpenHelpAsync()
        {
            //_logger?.LogInformation("ヘルプを開くコマンドが実行されました");
            
            // ヘルプ画面を開くロジック
            // (まだ実装されていません)
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // バージョン情報を開くコマンド実行
        private async Task ExecuteOpenAboutAsync()
        {
            //_logger?.LogInformation("バージョン情報を開くコマンドが実行されました");
            
            // バージョン情報ダイアログを表示するロジック
            // (まだ実装されていません)
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // トレイに最小化するコマンド実行
        private async Task ExecuteMinimizeToTrayAsync()
        {
            //_logger?.LogInformation("トレイに最小化コマンドが実行されました");
            
            // トレイに最小化するロジック
            await PublishEventAsync(new MinimizeToTrayRequestedEvent()).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳完了イベントハンドラ
        private async Task OnTranslationCompleted(TranslationCompletedEvent eventData)
        {
            // ステータスメッセージを更新
            StatusMessage = $"翻訳完了: {eventData.SourceText[..Math.Min(20, eventData.SourceText.Length)]}...";
            IsProcessing = false;
            Progress = 0;
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // キャプチャ状態変更イベントハンドラ
        private async Task OnCaptureStatusChanged(CaptureStatusChangedEvent eventData)
        {
            // キャプチャ状態を更新
            IsCapturing = eventData.IsActive;
            CaptureStatus = eventData.IsActive ? "キャプチャ中" : "停止中";
            StatusMessage = eventData.IsActive ? "キャプチャを開始しました" : "キャプチャを停止しました";
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳設定変更イベントハンドラ
        private async Task OnTranslationSettingsChanged(TranslationSettingsChangedEvent eventData)
        {
            // 翻訳エンジンを更新
            TranslationEngine = eventData.Engine;
            StatusMessage = $"翻訳設定を更新しました: {eventData.Engine}, {eventData.TargetLanguage}";
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
        
        // 翻訳エラーイベントハンドラ
        private async Task OnTranslationError(TranslationErrorEvent eventData)
        {
            // エラーメッセージを表示
            StatusMessage = $"翻訳エラー: {eventData.ErrorMessage}";
            ErrorMessage = eventData.ErrorMessage;
            IsProcessing = false;
            Progress = 0;
            
            await Task.CompletedTask.ConfigureAwait(true);
        }
    }
    
    // イベント定義
    internal class ApplicationExitRequestedEvent : IEvent
    {
    }
    
    internal class MinimizeToTrayRequestedEvent : IEvent
    {
    }
    
    internal class TranslationSettingsChangedEvent(string engine, string targetLanguage) : IEvent
    {
        public string Engine { get; } = engine;
        public string TargetLanguage { get; } = targetLanguage;
    }
    
    internal class TranslationErrorEvent(string errorMessage) : IEvent
    {
        public string ErrorMessage { get; } = errorMessage;
    }
}