#pragma warning disable CS0618 // Type or member is obsolete
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Baketa.Core.Abstractions.Events;
using CoreEvents = Baketa.Core.Events;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.Infrastructure.Platform.Windows.Capture;
using ReactiveUI;

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
            LoggerMessage.Define(LogLevel.Information, new EventId(3, "OnShutdownRequested"),
                "アプリケーション終了中");
                
        private static readonly Action<ILogger, Exception> _logStartupError =
            LoggerMessage.Define(LogLevel.Error, new EventId(4, nameof(OnFrameworkInitializationCompleted)),
                "アプリケーション起動中にエラーが発生しました");
                
        private static readonly Action<ILogger, Exception> _logShutdownError =
            LoggerMessage.Define(LogLevel.Error, new EventId(5, "OnShutdownRequested"),
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
            // AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            // System.Threading.Tasks.TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
            
            // イベント集約器を取得
            _eventAggregator = Program.ServiceProvider?.GetService<IEventAggregator>();
        }

        public override void OnFrameworkInitializationCompleted()
        {
            Console.WriteLine("🚀 OnFrameworkInitializationCompleted開始");
            System.Diagnostics.Debug.WriteLine("🚀 OnFrameworkInitializationCompleted開始");
            
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // 未監視タスク例外のハンドラーを登録（早期登録）
                // TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                
                // ReactiveUIのエラーハンドラーを登録
                RxApp.DefaultExceptionHandler = new ReactiveUIExceptionHandler();
                
                // ReactiveUIログ出力
                Console.WriteLine("🎆 ReactiveUIエラーハンドラー設定完了");
                
                try
                {
                    var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reactive_ui_startup.txt");
                    File.WriteAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎆 ReactiveUIエラーハンドラー設定完了");
                }
                catch { /* ファイル出力失敗は無視 */ }

                try
                {
                    Console.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");
                    System.Diagnostics.Debug.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");
                    
                    // サービスプロバイダーからサービスを取得
                    Console.WriteLine("🔍 Program.ServiceProvider確認開始");
                    
                    // ログファイルにも確実に出力
                    try
                    {
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 Program.ServiceProvider確認開始");
                    }
                    catch { /* ファイル出力失敗は無視 */ }
                    
                    ServiceProvider? serviceProvider = null;
                    try 
                    {
                        Console.WriteLine("🔍 Program.ServiceProviderアクセス試行");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 Program.ServiceProviderアクセス試行");
                        
                        serviceProvider = Program.ServiceProvider;
                        
                        Console.WriteLine($"🔍 Program.ServiceProvider取得結果: {(serviceProvider == null ? "null" : "not null")}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"🔍 Program.ServiceProvider取得結果: {(serviceProvider == null ? "null" : "not null")}");
                    }
                    catch (Exception serviceProviderAccessEx)
                    {
                        Console.WriteLine($"💥 Program.ServiceProviderアクセスで例外: {serviceProviderAccessEx.GetType().Name}: {serviceProviderAccessEx.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 Program.ServiceProviderアクセスで例外: {serviceProviderAccessEx.GetType().Name}: {serviceProviderAccessEx.Message}");
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
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ Program.ServiceProvider確認成功");
                    
                    Console.WriteLine("🔍 IEventAggregator取得開始");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 IEventAggregator取得開始");
                    try
                    {
                        _eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                        Console.WriteLine($"✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}");
                        _logger?.LogInformation("✅ IEventAggregator取得成功: {AggregatorType}", _eventAggregator.GetType().Name);
                        
                        // EventHandlerInitializationServiceを取得して実行
                        Console.WriteLine("🔥 EventHandlerInitializationService実行開始");
                        var eventHandlerInitService = serviceProvider.GetRequiredService<Baketa.Application.Services.Events.EventHandlerInitializationService>();
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await eventHandlerInitService.InitializeAsync().ConfigureAwait(false);
                                Console.WriteLine("🔥 EventHandlerInitializationService実行完了");
                            }
                            catch (Exception initEx)
                            {
                                Console.WriteLine($"🔥 [ERROR] EventHandlerInitializationService実行エラー: {initEx.Message}");
                                _logger?.LogError(initEx, "EventHandlerInitializationService実行エラー");
                            }
                        });
                        Console.WriteLine("🔥 EventHandlerInitializationService非同期実行開始");
                    }
                    catch (Exception eventAggregatorEx)
                    {
                        Console.WriteLine($"💥 IEventAggregator取得失敗: {eventAggregatorEx.GetType().Name}: {eventAggregatorEx.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 IEventAggregator取得失敗: {eventAggregatorEx.GetType().Name}: {eventAggregatorEx.Message}");
                        _logger?.LogError(eventAggregatorEx, "💥 IEventAggregator取得失敗: {ErrorMessage}", eventAggregatorEx.Message);
                        throw; // 致命的なエラーなので再スロー
                    }
                    
                    // MainOverlayViewModelを取得
                    Console.WriteLine("🔍 MainOverlayViewModel取得開始");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 MainOverlayViewModel取得開始");
                    MainOverlayViewModel mainOverlayViewModel;
                    try
                    {
                        mainOverlayViewModel = serviceProvider.GetRequiredService<MainOverlayViewModel>();
                        Console.WriteLine($"✅ MainOverlayViewModel取得成功: {mainOverlayViewModel.GetType().Name}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ MainOverlayViewModel取得成功: {mainOverlayViewModel.GetType().Name}");
                        _logger?.LogInformation("✅ MainOverlayViewModel取得成功: {ViewModelType}", mainOverlayViewModel.GetType().Name);
                    }
                    catch (Exception mainViewModelEx)
                    {
                        Console.WriteLine($"💥 MainOverlayViewModel取得失敗: {mainViewModelEx.GetType().Name}: {mainViewModelEx.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 MainOverlayViewModel取得失敗: {mainViewModelEx.GetType().Name}: {mainViewModelEx.Message}");
                        _logger?.LogError(mainViewModelEx, "💥 MainOverlayViewModel取得失敗: {ErrorMessage}", mainViewModelEx.Message);
                        Console.WriteLine($"💥 内部例外: {mainViewModelEx.InnerException?.GetType().Name}: {mainViewModelEx.InnerException?.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 内部例外: {mainViewModelEx.InnerException?.GetType().Name}: {mainViewModelEx.InnerException?.Message}");
                        Console.WriteLine($"💥 スタックトレース: {mainViewModelEx.StackTrace}");
                        throw; // 致命的なエラーなので再スロー
                    }
                    
                    // MainOverlayViewを設定（透明オーバーレイとして）
                    Console.WriteLine("🖥️ MainOverlayView作成開始");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ MainOverlayView作成開始");
                    
                    var mainOverlayView = new MainOverlayView
                    {
                        DataContext = mainOverlayViewModel,
                    };
                    
                    Console.WriteLine("🖥️ MainOverlayView作成完了 - DataContext設定済み");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ MainOverlayView作成完了 - DataContext設定済み");
                    
                    desktop.MainWindow = mainOverlayView;
                    
                    Console.WriteLine("🖥️ desktop.MainWindowに設定完了");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🖥️ desktop.MainWindowに設定完了");
                    
                    // 明示的にウィンドウを表示
                    try
                    {
                        mainOverlayView.Show();
                        Console.WriteLine("✅ MainOverlayView.Show()実行完了");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ MainOverlayView.Show()実行完了");
                    }
                    catch (Exception showEx)
                    {
                        Console.WriteLine($"⚠️ MainOverlayView.Show()失敗: {showEx.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"⚠️ MainOverlayView.Show()失敗: {showEx.Message}");
                    }
                    
                    // インプレース翻訳オーバーレイマネージャーを初期化（優先）
                    Console.WriteLine("🎯 InPlaceTranslationOverlayManager初期化設定");
                    try
                    {
                        var inPlaceOverlayManager = serviceProvider.GetService<Baketa.Core.Abstractions.UI.IInPlaceTranslationOverlayManager>();
                        if (inPlaceOverlayManager != null)
                        {
                            // UIスレッドデッドロックを避けるため、遅延初期化に変更
                            Task.Run(async () =>
                            {
                                try
                                {
                                    Console.WriteLine("🎯 InPlaceTranslationOverlayManager非同期初期化開始");
                                    await inPlaceOverlayManager.InitializeAsync().ConfigureAwait(false);
                                    Console.WriteLine("✅ InPlaceTranslationOverlayManager初期化完了");
                                }
                                catch (Exception asyncEx)
                                {
                                    Console.WriteLine($"⚠️ InPlaceTranslationOverlayManager非同期初期化失敗: {asyncEx.Message}");
                                }
                            });
                            Console.WriteLine("✅ InPlaceTranslationOverlayManager遅延初期化設定完了");
                        }
                        else
                        {
                            Console.WriteLine("⚠️ InPlaceTranslationOverlayManagerが見つかりません");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ InPlaceTranslationOverlayManager初期化設定エラー: {ex.Message}");
                    }

                    // 旧TranslationResultOverlayManagerは削除済み - インプレースシステムが自動で管理
                    Console.WriteLine("🖥️ 旧オーバーレイシステムは削除済み - インプレースシステムが自動で管理");
                    
                    // TranslationFlowModuleを使用してイベント購読を設定
                    Console.WriteLine("🔧 TranslationFlowModuleのイベント購読を初期化中");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔧 TranslationFlowModuleのイベント購読を初期化中");
                    _logger?.LogInformation("🔧 TranslationFlowModuleのイベント購読を初期化中");
                    
                    try
                    {
                        var translationFlowModule = new Baketa.UI.DI.Modules.TranslationFlowModule();
                        Console.WriteLine("📦 TranslationFlowModuleインスタンス作成完了");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "📦 TranslationFlowModuleインスタンス作成完了");
                        _logger?.LogInformation("📦 TranslationFlowModuleインスタンス作成完了");
                        
                        translationFlowModule.ConfigureEventAggregator(_eventAggregator, serviceProvider);
                        
                        Console.WriteLine("✅ TranslationFlowModule初期化完了");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "✅ TranslationFlowModule初期化完了");
                        _logger?.LogInformation("✅ TranslationFlowModule初期化完了");
                        
                    }
                    catch (Exception moduleEx)
                    {
                        Console.WriteLine($"💥 TranslationFlowModule初期化エラー: {moduleEx.GetType().Name}: {moduleEx.Message}");
                        // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"💥 TranslationFlowModule初期化エラー: {moduleEx.GetType().Name}: {moduleEx.Message}");
                        _logger?.LogError(moduleEx, "💥 TranslationFlowModule初期化エラー: {ErrorMessage}", moduleEx.Message);
                        Console.WriteLine($"💥 スタックトレース: {moduleEx.StackTrace}");
                        _logger?.LogError("💥 スタックトレース: {StackTrace}", moduleEx.StackTrace);
                        // エラーが発生してもアプリケーションの起動は継続
                    }
                    
                    // 🔥【CRITICAL FIX】OPUS-MT事前起動サービスを開始 - TranslationFlowModule例外の影響を受けない独立実行
                    Console.WriteLine("🔥🔥🔥 OPUS-MT事前起動サービス処理開始 🔥🔥🔥");
                    try
                    {
                        Console.WriteLine("🔍 OpusMtPrewarmService取得開始");
                        var prewarmService = serviceProvider.GetRequiredService<Baketa.Core.Abstractions.Translation.IOpusMtPrewarmService>();
                        Console.WriteLine($"✅ OpusMtPrewarmService取得成功: {prewarmService.GetType().Name}");
                        Console.WriteLine("🚀 バックグラウンドタスク作成開始");
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                Console.WriteLine("🚀 prewarmService.StartPrewarmingAsync() 呼び出し開始");
                                await prewarmService.StartPrewarmingAsync().ConfigureAwait(false);
                                Console.WriteLine("✅ prewarmService.StartPrewarmingAsync() 完了");
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"⚠️ OpusMtPrewarmService開始エラー: {ex.Message}");
                                _logger?.LogWarning(ex, "⚠️ OpusMtPrewarmService開始エラー: {Error}", ex.Message);
                            }
                        });
                        Console.WriteLine("🚀 OpusMtPrewarmService開始要求完了");
                        _logger?.LogInformation("🚀 OpusMtPrewarmService開始要求完了");
                    }
                    catch (Exception prewarmEx)
                    {
                        Console.WriteLine($"💥💥💥 OpusMtPrewarmService取得エラー: {prewarmEx.GetType().Name}: {prewarmEx.Message}");
                        Console.WriteLine($"💥💥💥 スタックトレース: {prewarmEx.StackTrace}");
                        _logger?.LogWarning(prewarmEx, "⚠️ OpusMtPrewarmService取得エラー: {Error}", prewarmEx.Message);
                    }
                    
                    // 🚨 PythonServerHealthMonitor の直接開始
                    Console.WriteLine("🔧 PythonServerHealthMonitor直接開始開始");
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = serviceProvider.CreateScope();
                            
                            // PythonServerHealthMonitor を直接取得
                            var healthMonitor = scope.ServiceProvider.GetService<Baketa.Infrastructure.Translation.Services.PythonServerHealthMonitor>();
                            if (healthMonitor != null)
                            {
                                Console.WriteLine($"✅ [HEALTH_MONITOR] PythonServerHealthMonitor取得成功");
                                await healthMonitor.StartAsync(CancellationToken.None).ConfigureAwait(false);
                                Console.WriteLine($"🎯 [HEALTH_MONITOR] PythonServerHealthMonitor開始完了");
                            }
                            else
                            {
                                Console.WriteLine($"⚠️ [HEALTH_MONITOR] PythonServerHealthMonitor取得失敗");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ [HEALTH_MONITOR] PythonServerHealthMonitor開始エラー: {ex.Message}");
                            _logger?.LogWarning(ex, "⚠️ PythonServerHealthMonitor開始エラー: {Error}", ex.Message);
                        }
                    });
                    Console.WriteLine("🚀 PythonServerHealthMonitor直接開始要求完了");
                    
                    // アプリケーション起動完了イベントをパブリッシュ（非ブロッキング）
                    _ = _eventAggregator?.PublishAsync(new ApplicationStartupEvent());
                    
                    if (_logger != null)
                    {
                        _logStartupCompleted(_logger, null);
                    }
                    
                    // シャットダウンイベントハンドラーの登録
                    desktop.ShutdownRequested += OnShutdownRequested;
                    
                    // アプリケーション終了イベントハンドラーを追加
                    AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
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
        /// アプリケーションシャットダウン要求処理
        /// </summary>
        private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
        {
            try
            {
                _logger?.LogInformation("アプリケーションシャットダウン要求を受信");
                
                // ネイティブライブラリの強制終了を設定
                NativeWindowsCaptureWrapper.ForceShutdownOnApplicationExit();
                
                // シャットダウンイベントをパブリッシュ（非ブロッキング）
                _ = _eventAggregator?.PublishAsync(new ApplicationShutdownEvent());
                
                if (_logger != null)
                {
                    _logShuttingDown(_logger, null);
                }
            }
            catch (Exception ex)
            {
                if (_logger != null)
                {
                    _logShutdownError(_logger, ex);
                }
            }
        }

        /// <summary>
        /// プロセス終了時の処理
        /// </summary>
        private void OnProcessExit(object? sender, EventArgs e)
        {
            try
            {
                _logger?.LogInformation("プロセス終了処理開始");
                
                // ネイティブライブラリの強制終了
                NativeWindowsCaptureWrapper.ForceShutdownOnApplicationExit();
                
                _logger?.LogInformation("プロセス終了処理完了");
            }
            catch (Exception ex)
            {
                // プロセス終了時は例外を抑制
                try
                {
                    _logger?.LogError(ex, "プロセス終了処理中に例外が発生");
                }
                catch
                {
                    // ログ出力も失敗する場合は抑制
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
    
    /// <summary>
    /// ReactiveUI用エラーハンドラー
    /// </summary>
    internal sealed class ReactiveUIExceptionHandler : IObserver<Exception>
    {
        public void OnNext(Exception ex)
        {
            Console.WriteLine($"🚨 ReactiveUI例外: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"🚨 スタックトレース: {ex.StackTrace}");
            
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reactive_ui_errors.txt");
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 ReactiveUI例外: {ex.GetType().Name}: {ex.Message}");
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🚨 スタックトレース: {ex.StackTrace}");
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===== ReactiveUI例外終了 =====");
                Console.WriteLine($"📝 ReactiveUIエラーログ: {logPath}");
            }
            catch { /* ファイル出力失敗は無視 */ }
            
            // InvalidOperationExceptionのUIスレッド違反は吸収
            if (ex is InvalidOperationException invalidOp &&
                (invalidOp.Message.Contains("invalid thread", StringComparison.OrdinalIgnoreCase) ||
                 invalidOp.Message.Contains("VerifyAccess", StringComparison.OrdinalIgnoreCase) ||
                 invalidOp.StackTrace?.Contains("VerifyAccess") == true ||
                 invalidOp.StackTrace?.Contains("CheckAccess") == true ||
                 invalidOp.StackTrace?.Contains("ReactiveCommand") == true))
            {
                Console.WriteLine("🚨 ReactiveUI: UIスレッド違反を検出 - アプリケーションを継続");
                return; // 例外を吸収
            }
            
            // その他の例外は再スロー
            throw ex;
        }
        
        public void OnError(Exception error)
        {
            OnNext(error);
        }
        
        public void OnCompleted()
        {
            // 何もしない
        }
    }
