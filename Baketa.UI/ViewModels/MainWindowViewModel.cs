using System;
using System.Reactive;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ReactiveUI;

using Baketa.Core.Abstractions.Events;
using CoreEvents = Baketa.Core.Events;
using UIEvents = Baketa.UI.Framework.Events;
using EventTypes = Baketa.Core.Events.EventTypes;
using TranslationEvents = Baketa.Core.Events.TranslationEvents;
using CaptureEvents = Baketa.Core.Events.CaptureEvents;
using Baketa.UI.Framework.ReactiveUI; 

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// メインウィンドウのビューモデル
    /// </summary>
    public sealed class MainWindowViewModel : Framework.ViewModelBase
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
        
        // 通知関連プロパティ
        private bool _isNotificationVisible;
        public bool IsNotificationVisible
        {
            get => _isNotificationVisible;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isNotificationVisible, value);
        }
        
        private string _notificationMessage = string.Empty;
        public string NotificationMessage
        {
            get => _notificationMessage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _notificationMessage, value);
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
        public SettingsViewModel SettingsViewModel { get; }
        public AccessibilitySettingsViewModel AccessibilitySettingsViewModel { get; }
        
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
        /// <param name="settingsViewModel">設定ビューモデル</param>
        /// <param name="accessibilityViewModel">アクセシビリティ設定ビューモデル</param>
        /// <param name="logger">ロガー</param>
        public MainWindowViewModel(
            IEventAggregator eventAggregator,
            HomeViewModel homeViewModel,
            CaptureViewModel captureViewModel,
            TranslationViewModel translationViewModel,
            OverlayViewModel overlayViewModel,
            HistoryViewModel historyViewModel,
            SettingsViewModel settingsViewModel,
            AccessibilitySettingsViewModel accessibilityViewModel,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            // 各タブのビューモデルを初期化
            HomeViewModel = homeViewModel;
            CaptureViewModel = captureViewModel;
            TranslationViewModel = translationViewModel;
            OverlayViewModel = overlayViewModel;
            HistoryViewModel = historyViewModel;
            SettingsViewModel = settingsViewModel;
            AccessibilitySettingsViewModel = accessibilityViewModel;
            
            // コマンドの実行可否条件
            var canStartCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => !isCapturing);
                
            var canStopCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => isCapturing);
            
            // コマンドの初期化
            OpenSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync);
            ExitCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteExitAsync);
            StartCaptureCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteStartCaptureAsync, canStartCapture);
            StopCaptureCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteStopCaptureAsync, canStopCapture);
            SelectRegionCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteSelectRegionAsync);
            OpenLogViewerCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenLogViewerAsync);
            OpenTranslationHistoryCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenTranslationHistoryAsync);
            OpenHelpCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
            OpenAboutCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenAboutAsync);
            MinimizeToTrayCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteMinimizeToTrayAsync);
            
            // ナビゲーションイベントの購読
            SubscribeToNavigationEvents();
        }
        
        /// <summary>
        /// アクティベーション時の処理
        /// </summary>
        protected override void HandleActivation()
        {
            // イベント購読
            SubscribeToEvent<TranslationEvents.TranslationCompletedEvent>(OnTranslationCompleted);
            SubscribeToEvent<CaptureEvents.CaptureStatusChangedEvent>(OnCaptureStatusChanged);
            SubscribeToEvent<TranslationEvents.TranslationSettingsChangedEvent>(OnTranslationSettingsChanged);
            SubscribeToEvent<TranslationEvents.TranslationErrorEvent>(OnTranslationError);
        }
        
        /// <summary>
        /// ナビゲーションイベントを購読します
        /// </summary>
        private void SubscribeToNavigationEvents()
        {
            // 各タブへの移動リクエストを購読 - 名前空間衝突があるため直接実装
            
            // OpenCaptureSettingsRequestedEvent
            SubscribeToOpenCaptureSettings();
            
            // OpenTranslationSettingsRequestedEvent 
            SubscribeToOpenTranslationSettings();
            
            // OpenHistoryViewRequestedEvent
            SubscribeToOpenHistoryView();
            
            // OpenAccessibilitySettingsRequestedEvent
            SubscribeToOpenAccessibilitySettings();
        }
        
        // 以下、イベント登録用ヘルパーメソッド
        private void SubscribeToOpenCaptureSettings()
        {
            // キャプチャ設定画面を開くイベント
            // 実際の実装に合わせて修正
            SubscribeToEvent<UIEvents.OpenCaptureSettingsRequestedEvent>(async _ => 
            {
                SelectedTabIndex = 1; // キャプチャ設定タブ
                await Task.CompletedTask.ConfigureAwait(false);
            });
        }
        
        private void SubscribeToOpenTranslationSettings()
        {
            // 翻訳設定画面を開くイベント
            // 実際の実装に合わせて修正
            SubscribeToEvent<UIEvents.OpenTranslationSettingsRequestedEvent>(async _ => 
            {
                SelectedTabIndex = 2; // 翻訳設定タブ
                await Task.CompletedTask.ConfigureAwait(false);
            });
        }
        
        private void SubscribeToOpenHistoryView()
        {
            // 履歴画面を開くイベント
            // 実際の実装に合わせて修正
            SubscribeToEvent<UIEvents.OpenHistoryViewRequestedEvent>(async _ => 
            {
                SelectedTabIndex = 4; // 履歴タブ
                await Task.CompletedTask.ConfigureAwait(false);
            });
        }
        
        private void SubscribeToOpenAccessibilitySettings()
        {
            // アクセシビリティ設定画面を開くイベント
            SubscribeToEvent<CoreEvents.AccessibilityEvents.OpenAccessibilitySettingsRequestedEvent>(async _ => 
            {
                // アクセシビリティ設定タブに切り替え
                SelectedTabIndex = 6; // AccessibilitySettingsViewModelタブ
                await Task.CompletedTask.ConfigureAwait(false);
            });
        }
        
        /// <summary>
        /// 通知を表示します
        /// </summary>
        /// <param name="message">通知メッセージ</param>
        /// <param name="duration">表示時間（秒）</param>
        public void ShowNotification(string message, TimeSpan duration = default)
        {
            if (duration == default)
            {
                duration = TimeSpan.FromSeconds(3);
            }
            
            NotificationMessage = message;
            IsNotificationVisible = true;
            
            // タイマーで通知を非表示にする
            Task.Delay(duration).ContinueWith(_ =>
            {
                IsNotificationVisible = false;
            }, TaskScheduler.Default);
        }
        
        /// <summary>
        /// ウィンドウが閉じられる前の処理
        /// </summary>
        public void OnWindowClosing()
        {
            Logger?.LogInformation("メインウィンドウのクローズが要求されました");
            // 必要に応じてクローズ前の処理を実行
        }
        
        // 設定画面を開くコマンド実行
        private async Task ExecuteOpenSettingsAsync()
        {
            Logger?.LogInformation("設定画面を開くコマンドが実行されました");
            
            // 設定タブに切り替え
            SelectedTabIndex = 5; // SettingsViewModelタブ
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // アプリケーション終了コマンド実行
        private async Task ExecuteExitAsync()
        {
            Logger?.LogInformation("アプリケーション終了コマンドが実行されました");
            
            // 終了前の確認（実際にはダイアログ表示）
            await PublishEventAsync(new UIEvents.ApplicationExitRequestedEvent()).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ開始コマンド実行
        private async Task ExecuteStartCaptureAsync()
        {
            Logger?.LogInformation("キャプチャ開始コマンドが実行されました");
            
            await PublishEventAsync(new UIEvents.StartCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = true;
            CaptureStatus = "キャプチャ中";
            StatusMessage = "キャプチャを開始しました";
            
            // 通知表示
            ShowNotification("キャプチャを開始しました");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ停止コマンド実行
        private async Task ExecuteStopCaptureAsync()
        {
            Logger?.LogInformation("キャプチャ停止コマンドが実行されました");
            
            await PublishEventAsync(new UIEvents.StopCaptureRequestedEvent()).ConfigureAwait(false);
            IsCapturing = false;
            CaptureStatus = "停止中";
            StatusMessage = "キャプチャを停止しました";
            
            // 通知表示
            ShowNotification("キャプチャを停止しました");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 領域選択コマンド実行
        private async Task ExecuteSelectRegionAsync()
        {
            Logger?.LogInformation("領域選択コマンドが実行されました");
            
            // CaptureViewModelタブに切り替え
            SelectedTabIndex = 1;
            
            // 領域選択コマンドを実行
            // Note: ReactiveCommandの.Executeは非同期メソッドではなく、直接awaitできない
            CaptureViewModel.SelectRegionCommand.Execute().Subscribe();
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // ログビューワーを開くコマンド実行
        private async Task ExecuteOpenLogViewerAsync()
        {
            Logger?.LogInformation("ログビューワーを開くコマンドが実行されました");
            
            // ログビューワーを開くロジック
            // (まだ実装されていません)
            
            // 通知表示
            ShowNotification("この機能はまだ実装されていません");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳履歴を開くコマンド実行
        private async Task ExecuteOpenTranslationHistoryAsync()
        {
            Logger?.LogInformation("翻訳履歴を開くコマンドが実行されました");
            
            // 履歴タブに切り替え
            SelectedTabIndex = 4; // HistoryViewModelタブ
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // ヘルプを開くコマンド実行
        private async Task ExecuteOpenHelpAsync()
        {
            Logger?.LogInformation("ヘルプを開くコマンドが実行されました");
            
            // ヘルプ画面を開くロジック
            // (まだ実装されていません)
            
            // 通知表示
            ShowNotification("この機能はまだ実装されていません");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // バージョン情報を開くコマンド実行
        private async Task ExecuteOpenAboutAsync()
        {
            Logger?.LogInformation("バージョン情報を開くコマンドが実行されました");
            
            // バージョン情報ダイアログを表示するロジック
            // (まだ実装されていません)
            
            // 通知表示
            ShowNotification("この機能はまだ実装されていません");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // トレイに最小化するコマンド実行
        private async Task ExecuteMinimizeToTrayAsync()
        {
            Logger?.LogInformation("トレイに最小化コマンドが実行されました");
            
            // トレイに最小化するロジック
            await PublishEventAsync(new UIEvents.MinimizeToTrayRequestedEvent()).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳完了イベントハンドラ
        private async Task OnTranslationCompleted(TranslationEvents.TranslationCompletedEvent eventData)
        {
            // ステータスメッセージを更新
            StatusMessage = $"翻訳完了: {eventData.SourceText[..Math.Min(20, eventData.SourceText.Length)]}...";
            IsProcessing = false;
            Progress = 0;
            
            // 通知表示
            ShowNotification("翻訳が完了しました");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // キャプチャ状態変更イベントハンドラ
        private async Task OnCaptureStatusChanged(CaptureEvents.CaptureStatusChangedEvent eventData)
        {
            // キャプチャ状態を更新
            IsCapturing = eventData.IsActive;
            CaptureStatus = eventData.IsActive ? "キャプチャ中" : "停止中";
            StatusMessage = eventData.IsActive ? "キャプチャを開始しました" : "キャプチャを停止しました";
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳設定変更イベントハンドラ
        private async Task OnTranslationSettingsChanged(TranslationEvents.TranslationSettingsChangedEvent eventData)
        {
            // 翻訳エンジンを更新
            TranslationEngine = eventData.Engine;
            StatusMessage = $"翻訳設定を更新しました: {eventData.Engine}, {eventData.TargetLanguage}";
            
            // 通知表示
            ShowNotification($"翻訳設定を更新しました: {eventData.Engine}");
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳エラーイベントハンドラ
        private async Task OnTranslationError(TranslationEvents.TranslationErrorEvent eventData)
        {
            // エラーメッセージを表示
            StatusMessage = $"翻訳エラー: {eventData.ErrorMessage}";
            ErrorMessage = eventData.ErrorMessage;
            IsProcessing = false;
            Progress = 0;
            
            // 通知表示
            ShowNotification($"翻訳エラー: {eventData.ErrorMessage}", TimeSpan.FromSeconds(5));
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
    }
