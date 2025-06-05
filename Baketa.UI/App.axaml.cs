using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

using CoreEvents = Baketa.Core.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;

namespace Baketa.UI;

    internal sealed partial class App : Avalonia.Application
    {
        private ILogger<App>? _logger;
        private CoreEvents.IEventAggregator? _eventAggregator;
        
        // LoggerMessageデリゲートの定義
        private static readonly Action<ILogger, Exception?> _logInitializing =
            LoggerMessage.Define(LogLevel.Information, new EventId(1, nameof(Initialize)),
                "Baketaアプリケーションを初期化中");
            
        private static readonly Action<ILogger, Exception?> _logStartupCompleted =
            LoggerMessage.Define(LogLevel.Information, new EventId(2, nameof(OnFrameworkInitializationCompleted)),
                "アプリケーション起動完了");
                
        private static readonly Action<ILogger, Exception?> _logShuttingDown =
            LoggerMessage.Define(LogLevel.Information, new EventId(3, nameof(OnShutdownRequested)),
                "アプリケーション終了中");
                
        private static readonly Action<ILogger, Exception> _logStartupError =
            LoggerMessage.Define(LogLevel.Error, new EventId(4, nameof(OnFrameworkInitializationCompleted)),
                "アプリケーション起動中にエラーが発生しました");
                
        private static readonly Action<ILogger, Exception> _logShutdownError =
            LoggerMessage.Define(LogLevel.Error, new EventId(5, nameof(OnShutdownRequested)),
                "シャットダウン中にエラーが発生しました");

        private static readonly Action<ILogger, bool, bool, double, bool, double, Exception?> _logAccessibilitySettingsChanged =
            LoggerMessage.Define<bool, bool, double, bool, double>(
                LogLevel.Information,
                new EventId(6, "AccessibilitySettingsChanged"),
                "アクセシビリティ設定が変更されました: アニメーション無効={DisableAnimations}, ハイコントラスト={HighContrastMode}, フォント倍率={FontScaleFactor}, フォーカス表示={AlwaysShowKeyboardFocus}, ナビゲーション速度={KeyboardNavigationSpeed}");
        
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            _logger = Program.ServiceProvider?.GetService<ILogger<App>>();
            if (_logger != null)
            {
                _logInitializing(_logger, null);
            }
            
            // イベント集約器を取得
            _eventAggregator = Program.ServiceProvider?.GetService<CoreEvents.IEventAggregator>();
            
            // アクセシビリティ設定変更イベントの購読
            _eventAggregator?.Subscribe<CoreEvents.AccessibilitySettingsChangedEvent>(
                new AccessibilitySettingsEventProcessor(this));
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    // サービスプロバイダーからサービスを取得
                    var serviceProvider = Program.ServiceProvider 
                        ?? throw new InvalidOperationException("サービスプロバイダーが初期化されていません");
                    
                    _eventAggregator = serviceProvider.GetRequiredService<CoreEvents.IEventAggregator>();
                    
                    // MainWindowViewModelを取得
                    var mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
                    
                    // MainWindowを設定
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = mainWindowViewModel,
                    };
                    
                    // アプリケーション起動完了イベントをパブリッシュ
                    _eventAggregator.PublishAsync(new ApplicationStartupEvent()).GetAwaiter().GetResult();
                    
                    if (_logger != null)
                    {
                        _logStartupCompleted(_logger, null);
                    }
                    
                    // シャットダウンイベントハンドラーの登録
                    desktop.ShutdownRequested += OnShutdownRequested;
                }
                catch (Exception ex)
                {
                    if (_logger != null)
                    {
                        _logStartupError(_logger, ex);
                    }
                    throw; // 致命的なエラーなので再スロー
                }
            }

            base.OnFrameworkInitializationCompleted();
        }
        
        /// <summary>
        /// アクセシビリティ設定変更イベントのハンドラー
        /// </summary>
        internal async Task OnAccessibilitySettingsChanged(CoreEvents.AccessibilitySettingsChangedEvent @event)
        {
            if (_logger != null)
            {
                _logAccessibilitySettingsChanged(
                    _logger,
                    @event.DisableAnimations,
                    @event.HighContrastMode,
                    @event.FontScaleFactor,
                    @event.AlwaysShowKeyboardFocus,
                    @event.KeyboardNavigationSpeed,
                    null);
            }
            
            // UIスレッドでリソースを更新
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    // アニメーション有効化/無効化
                    Resources["AnimationsEnabled"] = !@event.DisableAnimations;
                    
                    // ハイコントラストモード
                    bool isDarkTheme = RequestedThemeVariant?.Key == ThemeVariant.Dark.Key;
                    if (@event.HighContrastMode)
                    {
                        RequestedThemeVariant = ThemeVariant.Dark;
                        
                        // カスタムハイコントラスト設定を適用
                        // (Colors.axamlのHighContrastリソース辞書が自動的に選択される)
                    }
                    else if (isDarkTheme)
                    {
                        // 通常のダークテーマに戻す - HighContrastチェックを削除
                        RequestedThemeVariant = ThemeVariant.Dark;
                    }
                    
                    // フォントサイズ倍率適用
                    ApplyFontScaling(@event.FontScaleFactor);
                    
                    // キーボードフォーカス表示設定
                    ApplyKeyboardFocusSettings(@event.AlwaysShowKeyboardFocus);
                }
                catch (InvalidOperationException ex)
                {
                    _logger?.LogError(ex, "アクセシビリティ設定の適用中に操作エラーが発生しました");
                }
                catch (ArgumentException ex)
                {
                    _logger?.LogError(ex, "アクセシビリティ設定の適用中に引数エラーが発生しました");
                }
                catch (KeyNotFoundException ex)
                {
                    _logger?.LogError(ex, "アクセシビリティ設定の適用中にリソースが見つかりませんでした");
                }
                catch (NullReferenceException ex)
                {
                    _logger?.LogError(ex, "アクセシビリティ設定の適用中にヌル参照エラーが発生しました");
                }
            });
        }
        
        /// <summary>
        /// フォントサイズスケーリングを適用します
        /// </summary>
        private void ApplyFontScaling(double scaleFactor)
        {
            // リソースからベースサイズを取得
            if (Resources.TryGetResource("FontSizeNormal", ThemeVariant.Default, out var baseSizeObj) && 
                baseSizeObj is double baseSize)
            {
                // フォントサイズを倍率に応じて設定
                Resources["FontSizeSmall"] = baseSize * 0.85 * scaleFactor;
                Resources["FontSizeNormal"] = baseSize * scaleFactor;
                Resources["FontSizeLarge"] = baseSize * 1.2 * scaleFactor;
                Resources["FontSizeHeader"] = baseSize * 1.5 * scaleFactor;
            }
        }
        
        /// <summary>
        /// キーボードフォーカス表示設定を適用します
        /// </summary>
        private void ApplyKeyboardFocusSettings(bool alwaysShowFocus)
        {
            Resources["AlwaysShowKeyboardFocus"] = alwaysShowFocus;
            
            // キーボードフォーカス表示のスタイル設定（BasicStyles.axaml内で参照される）
            Resources["KeyboardFocusOpacity"] = alwaysShowFocus ? 1.0 : 0.0;
        }
        
        /// <summary>
        /// シャットダウンリクエスト時のハンドラー
        /// </summary>
        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            // アプリケーション終了イベントをパブリッシュ
            try
            {
                if (_logger != null)
                {
                    _logShuttingDown(_logger, null);
                }
                _eventAggregator?.PublishAsync(new ApplicationShutdownEvent()).GetAwaiter().GetResult();
            }
            catch (TaskCanceledException ex)
            {
                if (_logger != null)
                {
                    _logShutdownError(_logger, ex);
                }
            }
            catch (ObjectDisposedException ex)
            {
                if (_logger != null)
                {
                    _logShutdownError(_logger, ex);
                }
            }
            catch (InvalidOperationException ex)
            {
                if (_logger != null)
                {
                    _logShutdownError(_logger, ex);
                }
            }
        }
    }
    
    // イベントプロセッサの実装
    internal sealed class AccessibilitySettingsEventProcessor(App app) : CoreEvents.IEventProcessor<CoreEvents.AccessibilitySettingsChangedEvent>
    {
        private readonly App _app = app ?? throw new ArgumentNullException(nameof(app));
        
        public Task HandleAsync(CoreEvents.AccessibilitySettingsChangedEvent eventData)
        {
            return _app.OnAccessibilitySettingsChanged(eventData);
        }
    }
    
    // イベント定義
    internal sealed class ApplicationStartupEvent : CoreEvents.EventBase
    {
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "ApplicationStartup";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Application";
    }

    internal sealed class ApplicationShutdownEvent : CoreEvents.EventBase
    {
        /// <summary>
        /// イベント名
        /// </summary>
        public override string Name => "ApplicationShutdown";
        
        /// <summary>
        /// イベントカテゴリ
        /// </summary>
        public override string Category => "Application";
    }
