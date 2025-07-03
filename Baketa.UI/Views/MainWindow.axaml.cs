using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using Baketa.UI.Helpers;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Logging;
using ReactiveUI;

using Baketa.Core.Abstractions.Events;
using CoreEvents = Baketa.Core.Events;
using EventTypes = Baketa.Core.Events.EventTypes;

namespace Baketa.UI.Views;

    /// <summary>
    /// メインウィンドウのコードビハインド
    /// </summary>
    internal sealed partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        // ロガー
        private readonly ILogger<MainWindow>? _logger;
        // イベント集約器
        private readonly IEventAggregator? _eventAggregator;
        
        /// <summary>
        /// 新しいメインウィンドウを初期化します
        /// </summary>
        /// <param name="logger">ロガー（オプション）</param>
        public MainWindow(ILogger<MainWindow>? logger = null)
        {
            _logger = logger;
            
            // サービスロケータからイベント集約器を取得
            _eventAggregator = Program.ServiceProvider?.GetService<IEventAggregator>();
            
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                _logger?.LogInformation("メインウィンドウがアクティブ化されました");
                
                // アクセシビリティ設定を適用
                ApplyAccessibility();
                
                // イベント購読（イベント集約器を直接使用）
                if (_eventAggregator != null)
                {
                    // アクセシビリティ設定変更イベントの購読
                    _eventAggregator.Subscribe<CoreEvents.AccessibilitySettingsChangedEvent>(
                        new AccessibilitySettingsProcessor(this));
                    
                    // 通知イベントの購読
                    _eventAggregator.Subscribe<EventTypes.NotificationEvent>(
                        new NotificationProcessor(this));
                }
                
                // ウィンドウのクローズイベントをサブスクライブ
                // AvaloniaのイベントAPIを使用
                this.Closing += OnWindowClosingHandler;
                Disposable.Create(() => this.Closing -= OnWindowClosingHandler);
            });
        }

        /// <summary>
        /// コンポーネントを初期化します
        /// </summary>
        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        /// <summary>
        /// アクセシビリティプロパティを設定します
        /// </summary>
        private void ApplyAccessibility()
        {
            try
            {
                _logger?.LogDebug("アクセシビリティプロパティを設定中");
                
                // ウィンドウ自体の設定
                this.WithAccessibility("Baketa メインウィンドウ", "Baketaアプリケーションのメインウィンドウです");
                
                // タブへのアクセシビリティ設定
                this.FindControl<TabItem>("HomeTab")?.WithAccessibility("ホーム画面", "アプリケーションのホーム画面を表示します");
                this.FindControl<TabItem>("CaptureTab")?.WithAccessibility("キャプチャ設定", "キャプチャ領域と設定の管理を行います");
                this.FindControl<TabItem>("TranslationTab")?.WithAccessibility("翻訳設定", "翻訳エンジンと言語設定を管理します");
                this.FindControl<TabItem>("OverlayTab")?.WithAccessibility("オーバーレイ設定", "翻訳結果表示のオーバーレイ設定を管理します");
                this.FindControl<TabItem>("HistoryTab")?.WithAccessibility("翻訳履歴", "過去の翻訳履歴を表示します");
                this.FindControl<TabItem>("SettingsTab")?.WithAccessibility("設定", "アプリケーション設定を管理します");
                
                // メニュー項目への設定
                this.FindControl<MenuItem>("FileMenuItem")?.WithLabel("ファイルメニュー");
                this.FindControl<MenuItem>("CaptureMenuItem")?.WithLabel("キャプチャメニュー");
                this.FindControl<MenuItem>("ToolsMenuItem")?.WithLabel("ツールメニュー");
                this.FindControl<MenuItem>("HelpMenuItem")?.WithLabel("ヘルプメニュー");
                
                // ステータスバー要素への設定
                this.FindControl<TextBlock>("StatusText")?.WithLabel("ステータスメッセージ");
                this.FindControl<ProgressBar>("ProgressIndicator")?.WithLabel("処理進捗状況");
                
                _logger?.LogDebug("アクセシビリティプロパティの設定完了");
            }
            catch (InvalidOperationException ex)
            {
                _logger?.LogError(ex, "アクセシビリティプロパティの設定中に操作エラーが発生しました");
            }
            catch (ArgumentException ex)
            {
                _logger?.LogError(ex, "アクセシビリティプロパティの設定中に引数エラーが発生しました");
            }
            catch (NullReferenceException ex)
            {
                _logger?.LogError(ex, "アクセシビリティプロパティの設定中にヌル参照エラーが発生しました");
            }
        }
        
        /// <summary>
        /// ウィンドウのクローズ時のハンドラー
        /// </summary>
        private void OnWindowClosingHandler(object? sender, WindowClosingEventArgs e)
        {
            OnWindowClosing();
        }
        
        /// <summary>
        /// ウィンドウのクローズ時の処理
        /// </summary>
        private void OnWindowClosing()
        {
            _logger?.LogInformation("メインウィンドウが閉じられています");
            
            // 必要に応じてクローズ前の処理を実行
            ViewModel?.OnWindowClosing();
        }
        
        /// <summary>
        /// アクセシビリティ設定変更イベントのハンドラー
        /// </summary>
        internal async Task OnAccessibilitySettingsChanged(CoreEvents.AccessibilitySettingsChangedEvent @event)
        {
            _logger?.LogInformation(
                "アクセシビリティ設定が変更されました - アニメーション無効: {DisableAnimations}, ハイコントラスト: {HighContrast}",
                @event.DisableAnimations, @event.HighContrastMode);
            
            // UIスレッドでUIの更新を行う
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                // アクセシビリティ設定の表示の更新はApp.axaml.csで行われるため、
                // ここでは必要に応じて追加のUI更新を実施
            });
        }
        
        /// <summary>
        /// 通知イベントのハンドラー
        /// </summary>
        internal async Task OnNotificationReceived(EventTypes.NotificationEvent @event)
        {
            _logger?.LogInformation("通知を受信: {Message}", @event.Message);
            
            // UIスレッドで通知を表示
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ViewModel != null)
                {
                    // 表示時間を計算
                    var duration = TimeSpan.FromMilliseconds(@event.DisplayTime > 0 ? @event.DisplayTime : 3000);
                    
                    // ViewModelの通知機能を使用
                    ViewModel.ShowNotification(@event.Message, duration);
                }
            });
        }
    }
    
    /// <summary>
    /// アクセシビリティ設定変更イベントプロセッサー
    /// </summary>
    /// <param name="mainWindow">メインウィンドウ</param>
    internal sealed class AccessibilitySettingsProcessor(MainWindow mainWindow) : IEventProcessor<CoreEvents.AccessibilitySettingsChangedEvent>
    {
        private readonly MainWindow _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;
        
        public Task HandleAsync(CoreEvents.AccessibilitySettingsChangedEvent eventData)
        {
            return _mainWindow.OnAccessibilitySettingsChanged(eventData);
        }
    }
    
    /// <summary>
    /// 通知イベントプロセッサー
    /// </summary>
    /// <param name="mainWindow">メインウィンドウ</param>
    internal sealed class NotificationProcessor(MainWindow mainWindow) : IEventProcessor<EventTypes.NotificationEvent>
    {
        private readonly MainWindow _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        
        /// <inheritdoc />
        public int Priority => 0;
        
        /// <inheritdoc />
        public bool SynchronousExecution => false;
        
        public Task HandleAsync(EventTypes.NotificationEvent eventData)
        {
            return _mainWindow.OnNotificationReceived(eventData);
        }
    }
