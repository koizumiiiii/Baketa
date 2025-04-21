using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Events.EventTypes;
// using Baketa.UI.Services; // 必要なサービスは後で追加
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;

namespace Baketa.UI
{
    public partial class App : Avalonia.Application
    {
        private ILogger<App>? _logger;
        private IEventAggregator? _eventAggregator;
        
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
            _logger = Program.ServiceProvider?.GetService<ILogger<App>>();
            _logger?.LogInformation("Baketaアプリケーションを初期化中");
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
                    
                    _logger?.LogInformation("アプリケーション起動完了");
                    
                    // シャットダウンイベントハンドラーの登録
                    desktop.ShutdownRequested += OnShutdownRequested;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "アプリケーション起動中にエラーが発生しました");
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
                _logger?.LogInformation("アプリケーション終了中");
                _eventAggregator?.PublishAsync(new ApplicationShutdownEvent()).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "シャットダウン中にエラーが発生しました");
            }
        }
    }
}