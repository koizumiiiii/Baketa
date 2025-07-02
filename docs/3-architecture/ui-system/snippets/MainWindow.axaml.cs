using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.ReactiveUI;
using ReactiveUI;
using Baketa.Core.Events;
using Baketa.UI.Helpers;
using Baketa.UI.ViewModels;
using Microsoft.Extensions.Logging;

namespace Baketa.UI.Views
{
    /// <summary>
    /// メインウィンドウのコードビハインド
    /// </summary>
    public partial class MainWindow : ReactiveWindow<MainWindowViewModel>
    {
        // ロガー
        private readonly ILogger<MainWindow>? _logger;
        
        /// <summary>
        /// 新しいメインウィンドウを初期化します
        /// </summary>
        /// <param name="logger">ロガー（オプション）</param>
        public MainWindow(ILogger<MainWindow>? logger = null)
        {
            _logger = logger;
            InitializeComponent();
            
            this.WhenActivated(disposables => 
            {
                _logger?.LogInformation("メインウィンドウがアクティブ化されました");
                
                // アクセシビリティ設定を適用
                ApplyAccessibility();
                
                // イベント購読
                if (ViewModel != null)
                {
                    // アクセシビリティ設定変更イベントの購読
                    ViewModel.SubscribeToEvent<AccessibilitySettingsChangedEvent>(OnAccessibilitySettingsChanged)
                        .DisposeWith(disposables);
                        
                    // フォント設定変更イベントの購読
                    ViewModel.SubscribeToEvent<FontSettingsChangedEvent>(OnFontSettingsChanged)
                        .DisposeWith(disposables);
                        
                    // 通知イベントの購読
                    ViewModel.SubscribeToEvent<NotificationEvent>(OnNotificationReceived)
                        .DisposeWith(disposables);
                }
                
                // ウィンドウのクローズイベントをサブスクライブ
                this.Events().Closing.Subscribe(_ => OnWindowClosing())
                    .DisposeWith(disposables);
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
                this.FindControl<MenuItem>("HelpMenuItem")?.WithLabel("ヘルプメニュー");
                
                // ステータスバー要素への設定
                this.FindControl<TextBlock>("StatusText")?.WithLabel("ステータスメッセージ");
                this.FindControl<ProgressBar>("ProgressIndicator")?.WithLabel("処理進捗状況");
                
                _logger?.LogDebug("アクセシビリティプロパティの設定完了");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "アクセシビリティプロパティの設定中にエラーが発生しました");
            }
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
        private async Task OnAccessibilitySettingsChanged(AccessibilitySettingsChangedEvent @event)
        {
            _logger?.LogInformation(
                "アクセシビリティ設定が変更されました - アニメーション無効: {DisableAnimations}, ハイコントラスト: {HighContrast}",
                @event.DisableAnimations, @event.HighContrastMode);
            
            // UIスレッドでUIの更新を行う
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // アニメーション無効化の設定を適用
                    ApplyAnimationSettings(@event.DisableAnimations);
                    
                    // ハイコントラストモードの設定を適用
                    ApplyContrastSettings(@event.HighContrastMode);
                    
                    // フォントサイズの倍率を適用
                    ApplyFontScaling(@event.FontScaleFactor);
                    
                    // キーボードフォーカス表示設定を適用
                    ApplyKeyboardFocusSettings(@event.AlwaysShowKeyboardFocus);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "アクセシビリティ設定の適用中にエラーが発生しました");
                }
            });
        }
        
        /// <summary>
        /// アニメーション設定を適用します
        /// </summary>
        private void ApplyAnimationSettings(bool disableAnimations)
        {
            _logger?.LogDebug("アニメーション設定を適用: 無効={DisableAnimations}", disableAnimations);
            
            // アプリケーションのリソースにアニメーション設定を適用
            if (Application.Current?.Resources != null)
            {
                Application.Current.Resources["AnimationsEnabled"] = !disableAnimations;
            }
        }
        
        /// <summary>
        /// コントラスト設定を適用します
        /// </summary>
        private void ApplyContrastSettings(bool highContrastMode)
        {
            _logger?.LogDebug("コントラスト設定を適用: ハイコントラスト={HighContrastMode}", highContrastMode);
            
            if (Application.Current?.Styles != null)
            {
                // Avalonia.Themesの適切なテーマを選択
                // FluentThemeのRequestedThemeプロパティを更新
                var fluentTheme = Application.Current.Styles.OfType<Avalonia.Themes.Fluent.FluentTheme>().FirstOrDefault();
                if (fluentTheme != null)
                {
                    // ハイコントラストモードが有効な場合はカスタムディクショナリを使用
                    if (highContrastMode)
                    {
                        // ハイコントラストディクショナリをThemeDictionariesに追加
                        fluentTheme.TryGetResource("HighContrast", out _);
                    }
                    else
                    {
                        // 標準テーマを使用
                        fluentTheme.TryGetResource("Default", out _);
                    }
                }
            }
        }
        
        /// <summary>
        /// フォントサイズのスケーリングを適用します
        /// </summary>
        private void ApplyFontScaling(double scaleFactor)
        {
            _logger?.LogDebug("フォントスケーリングを適用: 倍率={ScaleFactor}", scaleFactor);
            
            if (Application.Current?.Resources != null)
            {
                // 基本フォントサイズを取得し、倍率を適用
                if (Application.Current.Resources.TryGetResource("FontSizeNormal", out var normalSizeObj) &&
                    normalSizeObj is double baseSize)
                {
                    // フォントサイズを更新
                    Application.Current.Resources["FontSizeSmall"] = baseSize * 0.85 * scaleFactor;
                    Application.Current.Resources["FontSizeNormal"] = baseSize * scaleFactor;
                    Application.Current.Resources["FontSizeLarge"] = baseSize * 1.2 * scaleFactor;
                    Application.Current.Resources["FontSizeHeader"] = baseSize * 1.5 * scaleFactor;
                }
            }
        }
        
        /// <summary>
        /// キーボードフォーカス表示設定を適用します
        /// </summary>
        private void ApplyKeyboardFocusSettings(bool alwaysShowFocus)
        {
            _logger?.LogDebug("キーボードフォーカス表示設定を適用: 常に表示={AlwaysShowFocus}", alwaysShowFocus);
            
            if (Application.Current?.Resources != null)
            {
                // キーボードフォーカス表示設定を適用
                Application.Current.Resources["AlwaysShowKeyboardFocus"] = alwaysShowFocus;
            }
        }
        
        /// <summary>
        /// フォント設定変更イベントのハンドラー
        /// </summary>
        private async Task OnFontSettingsChanged(FontSettingsChangedEvent @event)
        {
            _logger?.LogInformation("フォント設定が変更されました");
            
            // UIスレッドでフォント設定を適用
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // フォント設定の適用ロジック
                    // ...
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "フォント設定の適用中にエラーが発生しました");
                }
            });
        }
        
        /// <summary>
        /// 通知イベントのハンドラー
        /// </summary>
        private async Task OnNotificationReceived(NotificationEvent @event)
        {
            _logger?.LogInformation("通知を受信: {Message}", @event.Message);
            
            // UIスレッドで通知を表示
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ViewModel != null)
                {
                    // ViewModelの通知機能を使用
                    ViewModel.ShowNotification(@event.Message, @event.Duration);
                }
            });
        }
    }
    
    /// <summary>
    /// フォント設定変更イベント
    /// </summary>
    public class FontSettingsChangedEvent : EventBase
    {
        public string PrimaryFontFamily { get; set; } = string.Empty;
        public string JapaneseFontFamily { get; set; } = string.Empty;
        public string EnglishFontFamily { get; set; } = string.Empty;
        
        public override string EventId => "FontSettingsChanged";
        public override DateTime Timestamp => DateTime.UtcNow;
    }
    
    /// <summary>
    /// 通知イベント
    /// </summary>
    public class NotificationEvent : EventBase
    {
        public string Message { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(3);
        
        public override string EventId => "NotificationReceived";
        public override DateTime Timestamp => DateTime.UtcNow;
    }
}