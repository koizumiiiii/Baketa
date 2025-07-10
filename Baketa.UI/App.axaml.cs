using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

using Baketa.Core.Abstractions.Events;
using CoreEvents = Baketa.Core.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Baketa.UI.Services;

namespace Baketa.UI;

    internal sealed partial class App : Avalonia.Application
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
            
            // 未処理例外ハンドラーを設定
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            
            // イベント集約器を取得
            _eventAggregator = Program.ServiceProvider?.GetService<IEventAggregator>();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Console.WriteLine("🚀 OnFrameworkInitializationCompleted開始");
            System.Diagnostics.Debug.WriteLine("🚀 OnFrameworkInitializationCompleted開始");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                try
                {
                    Console.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");
                    System.Diagnostics.Debug.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");
                    
                    // サービスプロバイダーからサービスを取得
                    Console.WriteLine("🔍 Program.ServiceProvider確認開始");
                    
                    // ログファイルにも確実に出力
                    try
                    {
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 Program.ServiceProvider確認開始{Environment.NewLine}");
                    }
                    catch { /* ファイル出力失敗は無視 */ }
                    
                    ServiceProvider? serviceProvider = null;
                    try 
                    {
                        Console.WriteLine("🔍 Program.ServiceProviderアクセス試行");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 Program.ServiceProviderアクセス試行{Environment.NewLine}");
                        
                        serviceProvider = Program.ServiceProvider;
                        
                        Console.WriteLine($"🔍 Program.ServiceProvider取得結果: {(serviceProvider == null ? "null" : "not null")}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 Program.ServiceProvider取得結果: {(serviceProvider == null ? "null" : "not null")}{Environment.NewLine}");
                    }
                    catch (Exception serviceProviderAccessEx)
                    {
                        Console.WriteLine($"💥 Program.ServiceProviderアクセスで例外: {serviceProviderAccessEx.GetType().Name}: {serviceProviderAccessEx.Message}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 Program.ServiceProviderアクセスで例外: {serviceProviderAccessEx.GetType().Name}: {serviceProviderAccessEx.Message}{Environment.NewLine}");
                        _logger?.LogError(serviceProviderAccessEx, "💥 Program.ServiceProviderアクセスで例外: {ErrorMessage}", serviceProviderAccessEx.Message);
                        throw;
                    }
                    
                    if (serviceProvider == null)
                    {
                        Console.WriteLine("💥 FATAL: Program.ServiceProviderがnullです！");
                        _logger?.LogError("💥 FATAL: Program.ServiceProviderがnullです！");
                        throw new InvalidOperationException("サービスプロバイダーが初期化されていません");
                    }
                    
                    Console.WriteLine("✅ Program.ServiceProvider確認成功");
                    File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ Program.ServiceProvider確認成功{Environment.NewLine}");
                    
                    Console.WriteLine("🔍 IEventAggregator取得開始");
                    File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 IEventAggregator取得開始{Environment.NewLine}");
                    try
                    {
                        _eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                        Console.WriteLine($"✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}{Environment.NewLine}");
                        _logger?.LogInformation("✅ IEventAggregator取得成功: {AggregatorType}", _eventAggregator.GetType().Name);
                    }
                    catch (Exception eventAggregatorEx)
                    {
                        Console.WriteLine($"💥 IEventAggregator取得失敗: {eventAggregatorEx.GetType().Name}: {eventAggregatorEx.Message}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 IEventAggregator取得失敗: {eventAggregatorEx.GetType().Name}: {eventAggregatorEx.Message}{Environment.NewLine}");
                        _logger?.LogError(eventAggregatorEx, "💥 IEventAggregator取得失敗: {ErrorMessage}", eventAggregatorEx.Message);
                        throw; // 致命的なエラーなので再スロー
                    }
                    
                    // MainOverlayViewModelを取得
                    Console.WriteLine("🔍 MainOverlayViewModel取得開始");
                    File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔍 MainOverlayViewModel取得開始{Environment.NewLine}");
                    MainOverlayViewModel mainOverlayViewModel;
                    try
                    {
                        mainOverlayViewModel = serviceProvider.GetRequiredService<MainOverlayViewModel>();
                        Console.WriteLine($"✅ MainOverlayViewModel取得成功: {mainOverlayViewModel.GetType().Name}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ MainOverlayViewModel取得成功: {mainOverlayViewModel.GetType().Name}{Environment.NewLine}");
                        _logger?.LogInformation("✅ MainOverlayViewModel取得成功: {ViewModelType}", mainOverlayViewModel.GetType().Name);
                    }
                    catch (Exception mainViewModelEx)
                    {
                        Console.WriteLine($"💥 MainOverlayViewModel取得失敗: {mainViewModelEx.GetType().Name}: {mainViewModelEx.Message}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 MainOverlayViewModel取得失敗: {mainViewModelEx.GetType().Name}: {mainViewModelEx.Message}{Environment.NewLine}");
                        _logger?.LogError(mainViewModelEx, "💥 MainOverlayViewModel取得失敗: {ErrorMessage}", mainViewModelEx.Message);
                        Console.WriteLine($"💥 内部例外: {mainViewModelEx.InnerException?.GetType().Name}: {mainViewModelEx.InnerException?.Message}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 内部例外: {mainViewModelEx.InnerException?.GetType().Name}: {mainViewModelEx.InnerException?.Message}{Environment.NewLine}");
                        Console.WriteLine($"💥 スタックトレース: {mainViewModelEx.StackTrace}");
                        throw; // 致命的なエラーなので再スロー
                    }
                    
                    // MainOverlayViewを設定（透明オーバーレイとして）
                    desktop.MainWindow = new MainOverlayView
                    {
                        DataContext = mainOverlayViewModel,
                    };
                    
                    // TranslationFlowModuleを使用してイベント購読を設定
                    Console.WriteLine("🔧 TranslationFlowModuleのイベント購読を初期化中");
                    File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🔧 TranslationFlowModuleのイベント購読を初期化中{Environment.NewLine}");
                    _logger?.LogInformation("🔧 TranslationFlowModuleのイベント購読を初期化中");
                    
                    try
                    {
                        var translationFlowModule = new Baketa.UI.DI.Modules.TranslationFlowModule();
                        Console.WriteLine("📦 TranslationFlowModuleインスタンス作成完了");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 📦 TranslationFlowModuleインスタンス作成完了{Environment.NewLine}");
                        _logger?.LogInformation("📦 TranslationFlowModuleインスタンス作成完了");
                        
                        translationFlowModule.ConfigureEventAggregator(_eventAggregator, serviceProvider);
                        
                        Console.WriteLine("✅ TranslationFlowModule初期化完了");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ✅ TranslationFlowModule初期化完了{Environment.NewLine}");
                        _logger?.LogInformation("✅ TranslationFlowModule初期化完了");
                    }
                    catch (Exception moduleEx)
                    {
                        Console.WriteLine($"💥 TranslationFlowModule初期化エラー: {moduleEx.GetType().Name}: {moduleEx.Message}");
                        File.AppendAllText("debug_app_logs.txt", $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 TranslationFlowModule初期化エラー: {moduleEx.GetType().Name}: {moduleEx.Message}{Environment.NewLine}");
                        _logger?.LogError(moduleEx, "💥 TranslationFlowModule初期化エラー: {ErrorMessage}", moduleEx.Message);
                        Console.WriteLine($"💥 スタックトレース: {moduleEx.StackTrace}");
                        _logger?.LogError("💥 スタックトレース: {StackTrace}", moduleEx.StackTrace);
                        // エラーが発生してもアプリケーションの起動は継続
                    }
                    
                    // アプリケーション起動完了イベントをパブリッシュ
                    _eventAggregator?.PublishAsync(new ApplicationStartupEvent()).GetAwaiter().GetResult();
                    
                    if (_logger != null)
                    {
                        _logStartupCompleted(_logger, null);
                    }
                    
                    // シャットダウンイベントハンドラーの登録
                    desktop.ShutdownRequested += OnShutdownRequested;
                }
                catch (InvalidOperationException ex)
                {
                    Console.WriteLine($"💥 InvalidOperationException: {ex.Message}");
                    Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
                    if (_logger != null)
                    {
                        _logStartupError(_logger, ex);
                    }
                    throw; // 致命的なエラーなので再スロー
                }
                catch (ArgumentNullException ex)
                {
                    if (_logger != null)
                    {
                        _logStartupError(_logger, ex);
                    }
                    throw; // 致命的なエラーなので再スロー
                }
                catch (TypeInitializationException ex)
                {
                    if (_logger != null)
                    {
                        _logStartupError(_logger, ex);
                    }
                    throw; // 致命的なエラーなので再スロー
                }
                catch (FileNotFoundException ex)
                {
                    if (_logger != null)
                    {
                        _logStartupError(_logger, ex);
                    }
                    throw; // 致命的なエラーなので再スロー
                }
                catch (TargetInvocationException ex)
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
                
                // オーバーレイマネージャーの破棄
                var overlayManager = Program.ServiceProvider?.GetService<TranslationResultOverlayManager>();
                overlayManager?.Dispose();
                
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
        
        /// <summary>
        /// 未処理例外ハンドラー
        /// </summary>
        private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                Console.WriteLine($"💥 未処理例外: {ex.GetType().Name}: {ex.Message}");
                Console.WriteLine($"💥 スタックトレース: {ex.StackTrace}");
                _logger?.LogError(ex, "💥 未処理例外が発生しました: {ExceptionType} - {Message}", 
                    ex.GetType().Name, ex.Message);
                
                if (ex is FormatException formatEx)
                {
                    Console.WriteLine($"🔍 FormatException詳細スタックトレース: {formatEx.StackTrace}");
                    _logger?.LogError("🔍 FormatException詳細: {StackTrace}", formatEx.StackTrace);
                    
                    // 内部例外もチェック
                    if (formatEx.InnerException != null)
                    {
                        Console.WriteLine($"🔍 FormatException内部例外: {formatEx.InnerException.GetType().Name}: {formatEx.InnerException.Message}");
                        _logger?.LogError("🔍 FormatException内部例外: {InnerExceptionType}: {InnerMessage}", 
                            formatEx.InnerException.GetType().Name, formatEx.InnerException.Message);
                    }
                }
            }
        }
        
        /// <summary>
        /// 未監視タスク例外ハンドラー
        /// </summary>
        private void OnUnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
        {
            _logger?.LogError(e.Exception, "💥 未監視タスク例外が発生しました: {Message}", e.Exception.Message);
            
            foreach (var ex in e.Exception.InnerExceptions)
            {
                if (ex is FormatException formatEx)
                {
                    _logger?.LogError("🔍 タスク内FormatException詳細: {StackTrace}", formatEx.StackTrace);
                }
            }
        }
    }
    
    // イベント定義
    /// <summary>
    /// アプリケーション開始イベント
    /// </summary>
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

    /// <summary>
    /// アプリケーション終了イベント
    /// </summary>
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
