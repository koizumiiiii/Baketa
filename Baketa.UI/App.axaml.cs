using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Baketa.Core.Events;
using Baketa.Core.Events.EventTypes;
// using Baketa.UI.Services; // 必要なサービスは後で追加
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Baketa.UI
{
    internal partial class App : Avalonia.Application
    {
        private ILogger<App>? _logger;
        private IEventAggregator? _eventAggregator;
        
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
        
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            _logger = Program.ServiceProvider?.GetService<ILogger<App>>();
            if (_logger != null)
            {
                _logInitializing(_logger, null);
            }
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
                    
                    _eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                    
                    // MainViewModelを取得
                    var mainViewModel = serviceProvider.GetRequiredService<MainViewModel>();
                    
                    // MainWindowを設定
                    desktop.MainWindow = new MainWindow
                    {
                        DataContext = mainViewModel,
                    };
                    
                    // アプリケーション起動完了イベントをパブリッシュ
                    _eventAggregator.PublishAsync(new ApplicationStartupEvent());
                    
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
}