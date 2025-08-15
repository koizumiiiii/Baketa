using System;
using System.Reactive;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;

using Baketa.Core.Abstractions.Events;
using CoreEvents = Baketa.Core.Events;
using UIEvents = Baketa.UI.Framework.Events;
using EventTypes = Baketa.Core.Events.EventTypes;
using TranslationEvents = Baketa.Core.Events.TranslationEvents;
using CaptureEvents = Baketa.Core.Events.CaptureEvents;
using Baketa.UI.Framework.ReactiveUI;
using Baketa.Core.Translation.Models;
using Baketa.UI.Services; 

namespace Baketa.UI.ViewModels;

    /// <summary>
    /// メインウィンドウのビューモデル
    /// </summary>
    public sealed class MainWindowViewModel : Framework.ViewModelBase
    {
        private readonly INavigationService _navigationService;
        private readonly Baketa.Application.Services.Translation.TranslationOrchestrationService? _translationOrchestrationService;
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
        
        // 翻訳エンジン初期化状態
        private bool _isTranslationEngineInitializing;
        public bool IsTranslationEngineInitializing
        {
            get => _isTranslationEngineInitializing;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _isTranslationEngineInitializing, value);
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
        
        // 翻訳テスト関連プロパティ
        private string _translationTestInput = "こんにちは";
        public string TranslationTestInput
        {
            get => _translationTestInput;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationTestInput, value);
        }
        
        private string _translationTestOutput = string.Empty;
        public string TranslationTestOutput
        {
            get => _translationTestOutput;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationTestOutput, value);
        }
        
        private string _translationTestStatus = "翻訳テスト準備完了";
        public string TranslationTestStatus
        {
            get => _translationTestStatus;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _translationTestStatus, value);
        }
        
        private string _selectedTranslationEngine = "Local";
        public string SelectedTranslationEngine
        {
            get => _selectedTranslationEngine;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedTranslationEngine, value);
        }
        
        private string _selectedLanguagePair = "ja-en";
        public string SelectedLanguagePair
        {
            get => _selectedLanguagePair;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _selectedLanguagePair, value);
        }
        
        // エラーメッセージ (newで基底クラスのプロパティを隠す)
        private string? _errorMessage;
        public new string? ErrorMessage
        {
            get => _errorMessage;
            set => ReactiveUI.IReactiveObjectExtensions.RaiseAndSetIfChanged(this, ref _errorMessage, value);
        }
        
        // 翻訳状態監視プロパティ
        public bool IsTranslationInProgress => _translationOrchestrationService?.IsAnyTranslationActive ?? false;
        public bool CanOpenSettings => !IsTranslationInProgress;
        public string SettingsLockedMessage => "翻訳処理中は設定画面を開けません";
        
        // 各タブのビューモデル
        public HomeViewModel HomeViewModel { get; }
        public CaptureViewModel CaptureViewModel { get; }
        public TranslationViewModel TranslationViewModel { get; }
        public OverlayViewModel OverlayViewModel { get; }
        public HistoryViewModel HistoryViewModel { get; }
        public SimpleSettingsViewModel SimpleSettingsViewModel { get; }
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
        public ReactiveCommand<Unit, Unit> TestTranslationCommand { get; }
        
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
        /// <param name="navigationService">ナビゲーションサービス</param>
        /// <param name="translationOrchestrationService">翻訳オーケストレーションサービス（オプション）</param>
        /// <param name="logger">ロガー</param>
        public MainWindowViewModel(
            IEventAggregator eventAggregator,
            HomeViewModel homeViewModel,
            CaptureViewModel captureViewModel,
            TranslationViewModel translationViewModel,
            OverlayViewModel overlayViewModel,
            HistoryViewModel historyViewModel,
            SimpleSettingsViewModel simpleSettingsViewModel,
            AccessibilitySettingsViewModel accessibilityViewModel,
            INavigationService navigationService,
            Baketa.Application.Services.Translation.TranslationOrchestrationService? translationOrchestrationService = null,
            ILogger? logger = null)
            : base(eventAggregator, logger)
        {
            _navigationService = navigationService ?? throw new ArgumentNullException(nameof(navigationService));
            _translationOrchestrationService = translationOrchestrationService;
            
            // 各タブのビューモデルを初期化
            HomeViewModel = homeViewModel;
            CaptureViewModel = captureViewModel;
            TranslationViewModel = translationViewModel;
            OverlayViewModel = overlayViewModel;
            HistoryViewModel = historyViewModel;
            SimpleSettingsViewModel = simpleSettingsViewModel;
            AccessibilitySettingsViewModel = accessibilityViewModel;
            
            // コマンドの実行可否条件
            var canStartCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool, bool>(
                x => x.IsCapturing,
                x => x.IsTranslationEngineInitializing,
                (isCapturing, isInitializing) => !isCapturing && !isInitializing);
                
            var canStopCapture = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsCapturing,
                isCapturing => isCapturing);
                
            var canOpenSettings = this.WhenAnyValue<MainWindowViewModel, bool, bool>(
                x => x.IsTranslationInProgress,
                isTranslating => !isTranslating);
            
            // コマンドの初期化
            OpenSettingsCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenSettingsAsync, canOpenSettings);
            ExitCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteExitAsync);
            StartCaptureCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteStartCaptureAsync, canStartCapture);
            StopCaptureCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteStopCaptureAsync, canStopCapture);
            SelectRegionCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteSelectRegionAsync);
            OpenLogViewerCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenLogViewerAsync);
            OpenTranslationHistoryCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenTranslationHistoryAsync);
            OpenHelpCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenHelpAsync);
            OpenAboutCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteOpenAboutAsync);
            MinimizeToTrayCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteMinimizeToTrayAsync);
            TestTranslationCommand = Framework.ReactiveUI.ReactiveCommandFactory.Create(ExecuteTestTranslationAsync);
            
            // 翻訳状態監視の初期化
            InitializeTranslationStateMonitoring();
            
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
            
            // CloseSettingsRequestedEvent
            SubscribeToCloseSettings();
        }

        
        /// <summary>
        /// 翻訳状態監視を初期化します
        /// </summary>
        private void InitializeTranslationStateMonitoring()
        {
            Console.WriteLine($"🔧 [INIT_TRANSLATION_MONITORING] TranslationOrchestrationService: {_translationOrchestrationService?.GetType().Name ?? "NULL"}");
            
            if (_translationOrchestrationService == null) 
            {
                Console.WriteLine("⚠️ [INIT_TRANSLATION_MONITORING] TranslationOrchestrationServiceがnullです - 翻訳状態監視を無効化");
                return;
            }
            
            Console.WriteLine("🔧 [INIT_TRANSLATION_MONITORING] 翻訳状態監視を開始");
            
            // TranslationOrchestrationServiceのIsAnyTranslationActive変更を監視
            _translationOrchestrationService.WhenAnyValue(x => x.IsAnyTranslationActive)
                .Subscribe(isActive =>
                {
                    this.RaisePropertyChanged(nameof(IsTranslationInProgress));
                    this.RaisePropertyChanged(nameof(CanOpenSettings));
                    Console.WriteLine($"🔒 [MAIN_TRANSLATION_STATE] 翻訳状態変更: IsActive={isActive}, CanOpenSettings={CanOpenSettings}");
                    
                    // 詳細デバッグログ
                    try
                    {
                        System.IO.File.AppendAllText("E:\\dev\\Baketa\\debug_app_logs.txt", 
                            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔒 [MAIN_TRANSLATION_STATE] IsActive={isActive}, CanOpenSettings={CanOpenSettings}, IsTranslationInProgress={IsTranslationInProgress}{Environment.NewLine}");
                    }
                    catch { }
                });
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

        
        private void SubscribeToCloseSettings()
        {
            // 設定画面を閉じるイベント
            SubscribeToEvent<UIEvents.CloseSettingsRequestedEvent>(async _ => 
            {
                try
                {
                    // 設定画面を閉じる（ナビゲーションサービスを使用）
                    // TODO: 実際の設定画面クローズ処理を実装
                    Console.WriteLine("🔧 [CLOSE_SETTINGS] 設定画面を閉じる要求を受信");
                    
                    // 通知表示
                    ShowNotification("設定を保存しました", TimeSpan.FromSeconds(2));
                }
                catch (Exception ex)
                {
                    Logger?.LogError(ex, "設定画面のクローズ中にエラーが発生しました");
                    ShowNotification("設定画面のクローズに失敗しました", TimeSpan.FromSeconds(3));
                }
                
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
            
            try
            {
                // ナビゲーションサービスを使って設定画面を表示
                await _navigationService.ShowSettingsAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "設定画面の表示中にエラーが発生しました");
                StatusMessage = "設定画面の表示に失敗しました";
            }
        }
        
        // アプリケーション終了コマンド実行
        private async Task ExecuteExitAsync()
        {
            Logger?.LogInformation("アプリケーション終了コマンドが実行されました");
            
            // 終了前の確認（実際にはダイアログ表示）
            await PublishEventAsync(new UIEvents.ApplicationExitRequestedEvent()).ConfigureAwait(false);
            
            await Task.CompletedTask.ConfigureAwait(false);
        }
        
        // 翻訳テストコマンド実行
        private async Task ExecuteTestTranslationAsync()
        {
            Logger?.LogInformation("翻訳テストコマンドが実行されました");
            
            try
            {
                TranslationTestStatus = "翻訳中...";
                TranslationTestOutput = "";
                
                if (string.IsNullOrWhiteSpace(TranslationTestInput))
                {
                    TranslationTestStatus = "入力テキストが空です";
                    return;
                }
                
                // 言語ペアの解析
                var parts = SelectedLanguagePair.Split('-');
                if (parts.Length != 2)
                {
                    TranslationTestStatus = "無効な言語ペア形式です";
                    return;
                }
                
                var sourceLanguage = parts[0] == "ja" ? Language.Japanese : Language.English;
                var targetLanguage = parts[1] == "ja" ? Language.Japanese : Language.English;
                
                // 翻訳リクエストの作成
                var request = TranslationRequest.Create(
                    TranslationTestInput,
                    sourceLanguage,
                    targetLanguage);
                
                // 実際の翻訳サービスを使用
                var translationService = Program.ServiceProvider?.GetService<Baketa.Core.Abstractions.Translation.ITranslationService>();
                if (translationService != null)
                {
                    var response = await translationService.TranslateAsync(
                        TranslationTestInput,
                        sourceLanguage,
                        targetLanguage,
                        null).ConfigureAwait(false);
                    
                    if (response.IsSuccess)
                    {
                        TranslationTestOutput = response.TranslatedText ?? string.Empty;
                        TranslationTestStatus = $"翻訳完了 (エンジン: {response.EngineName})";
                    }
                    else
                    {
                        TranslationTestOutput = response.Error?.Message ?? "翻訳に失敗しました";
                        TranslationTestStatus = "翻訳エラー";
                    }
                }
                else
                {
                    // フォールバック：翻訳サービスが取得できない場合はダミー処理
                    TranslationTestOutput = GenerateTestTranslation(TranslationTestInput, SelectedLanguagePair);
                    TranslationTestStatus = $"テスト翻訳完了 (フォールバック)";
                }
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "翻訳テスト実行中にエラーが発生しました");
                TranslationTestStatus = $"翻訳テストエラー: {ex.Message}";
            }
        }
        
        // テスト翻訳の生成
        private string GenerateTestTranslation(string input, string languagePair)
        {
            // 簡易的なテスト翻訳
            return languagePair switch
            {
                "ja-en" => input switch
                {
                    "こんにちは" => "Hello",
                    "ありがとう" => "Thank you",
                    "さようなら" => "Goodbye",
                    "はい" => "Yes",
                    "いいえ" => "No",
                    _ => $"[Test JA→EN] {input}"
                },
                "en-ja" => input.ToLowerInvariant() switch
                {
                    "hello" => "こんにちは",
                    "thank you" => "ありがとう",
                    "goodbye" => "さようなら",
                    "yes" => "はい",
                    "no" => "いいえ",
                    _ => $"[Test EN→JA] {input}"
                },
                _ => $"[Test {languagePair}] {input}"
            };
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
            
            // 翻訳テスト結果の表示
            if (!string.IsNullOrEmpty(TranslationTestInput) && 
                eventData.SourceText == TranslationTestInput)
            {
                TranslationTestOutput = eventData.TranslatedText;
                TranslationTestStatus = $"翻訳完了 (テストモード)";
            }
            
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
