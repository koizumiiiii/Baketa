#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Baketa.Application.Services;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.ViewModels;
using Baketa.UI.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ReactiveUI;
using CoreEvents = Baketa.Core.Events;

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
        Console.WriteLine("🔥🔥🔥 [INIT_DEBUG] App.Initialize() 開始 - ServiceProvider状態確認 🔥🔥🔥");
        Console.WriteLine($"[INIT_DEBUG] Program.ServiceProvider == null: {Program.ServiceProvider == null}");

        AvaloniaXamlLoader.Load(this);

        // ServiceProviderが利用可能になってからサービスを取得
        if (Program.ServiceProvider != null)
        {
            Console.WriteLine("[INIT_DEBUG] ServiceProvider利用可能 - サービス取得中");
            _logger = Program.ServiceProvider.GetService<ILogger<App>>();
            _eventAggregator = Program.ServiceProvider.GetService<IEventAggregator>();

            if (_logger != null)
            {
                _logInitializing(_logger, null);
            }
        }
        else
        {
            Console.WriteLine("[INIT_DEBUG] ServiceProvider未利用可能 - 診断システム初期化は後で実行");
        }

        Console.WriteLine("🔥🔥🔥 [INIT_DEBUG] App.Initialize() 完了 🔥🔥🔥");
    }

    /// <summary>
    /// App.Initialize段階での診断システム初期化
    /// </summary>
    private void InitializeDiagnosticSystemInAppInitialize()
    {
        try
        {
            Console.WriteLine("🔍🔍🔍 [APP_INIT_DEBUG] Program.ServiceProvider確認中... 🔍🔍🔍");
            if (Program.ServiceProvider == null)
            {
                Console.WriteLine("🚨❌ [APP_INIT_ERROR] Program.ServiceProviderがnull！ ❌🚨");
                return;
            }

            Console.WriteLine("🔍🔍🔍 [APP_INIT_DEBUG] IDiagnosticCollectionService解決試行中... 🔍🔍🔍");
            var diagnosticCollectionService = Program.ServiceProvider.GetService<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService>();
            if (diagnosticCollectionService != null)
            {
                Console.WriteLine($"✅✅✅ [APP_INIT_SUCCESS] IDiagnosticCollectionService解決成功: {diagnosticCollectionService.GetType().Name} ✅✅✅");

                // 診断システムを即座に開始
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine("🩺 [APP_INIT_DEBUG] 診断データ収集開始中...");
                        await diagnosticCollectionService.StartCollectionAsync().ConfigureAwait(false);
                        Console.WriteLine("✅ [APP_INIT] 診断データ収集開始完了");
                    }
                    catch (Exception diagEx)
                    {
                        Console.WriteLine($"⚠️ [APP_INIT] 診断システム開始エラー: {diagEx.Message}");
                    }
                });

                // テストイベント発行（即座実行）
                if (_eventAggregator != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000).ConfigureAwait(false); // 2秒待機
                        try
                        {
                            var testEvent = new Baketa.Core.Events.Diagnostics.PipelineDiagnosticEvent
                            {
                                Stage = "AppInitialize",
                                IsSuccess = true,
                                ProcessingTimeMs = 50,
                                Severity = Baketa.Core.Events.Diagnostics.DiagnosticSeverity.Information
                            };

                            await _eventAggregator.PublishAsync(testEvent).ConfigureAwait(false);
                            Console.WriteLine("🧪 [APP_INIT] 診断テストイベント発行完了");

                            // 手動レポート生成テスト
                            await Task.Delay(1000).ConfigureAwait(false);
                            var reportPath = await diagnosticCollectionService.GenerateReportAsync("app_init_test").ConfigureAwait(false);
                            Console.WriteLine($"🧪 [APP_INIT] 手動レポート生成完了: {reportPath}");
                        }
                        catch (Exception testEx)
                        {
                            Console.WriteLine($"🧪 [APP_INIT] 診断テストエラー: {testEx.Message}");
                        }
                    });
                }

                // ✅ [FIXED] UltraPhase 14.6: TranslationInitializationService手動実行削除
                // HostedService登録復旧により自動実行されるため手動実行コードは不要

                Console.WriteLine("🩺 [APP_INIT] 診断システム初期化非同期開始完了");
            }
            else
            {
                Console.WriteLine("🚨❌❌❌ [APP_INIT_ERROR] IDiagnosticCollectionServiceが見つかりません！ ❌❌❌🚨");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [APP_INIT_ERROR] 診断システム初期化エラー: {ex.Message}");
            Console.WriteLine($"🚨 [APP_INIT_ERROR] スタックトレース: {ex.StackTrace}");
        }
    }

    public override async void OnFrameworkInitializationCompleted()
    {
        Console.WriteLine("🚨🚨🚨 [FRAMEWORK] OnFrameworkInitializationCompleted開始！ 🚨🚨🚨");
        Console.WriteLine("🚀 OnFrameworkInitializationCompleted開始");
        System.Diagnostics.Debug.WriteLine("🚀 OnFrameworkInitializationCompleted開始");

        // ログファイルにも確実に記録（デバッグ用）
        try
        {
            var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→🚨🚨🚨 [FRAMEWORK] OnFrameworkInitializationCompleted開始！ 🚨🚨🚨{Environment.NewLine}");
        }
        catch { /* ログファイル書き込み失敗は無視 */ }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Console.WriteLine("🚨🚨🚨 [DESKTOP] デスクトップアプリケーション初期化開始！ 🚨🚨🚨");

            // デバッグログ追加
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→🚨🚨🚨 [DESKTOP] デスクトップアプリケーション初期化開始！ 🚨🚨🚨{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
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

                    // デバッグログ追加
                    try
                    {
                        var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                        System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→🔍 Program.ServiceProviderアクセス試行{Environment.NewLine}");
                    }
                    catch { /* ログファイル書き込み失敗は無視 */ }

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

                // EventHandlerInitializationServiceを最優先で実行（Gemini分析に基づく修正）
                Console.WriteLine("🔥 EventHandlerInitializationService実行開始（最優先実行）");

                // デバッグログ追加
                try
                {
                    var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→🔥 EventHandlerInitializationService実行開始（最優先実行）{Environment.NewLine}");
                }
                catch { /* ログファイル書き込み失敗は無視 */ }

                // EventHandlerInitializationService は Program.cs で既に完了済み
                Console.WriteLine("✅ EventHandlerInitializationService は Program.cs で初期化済み - App.axaml.cs での重複実行をスキップ");

                // 🔥 [PHASE0_FIX] IHostedService重複起動削除 - Program.cs:677-722で既に起動済み
                // Event Storm問題（PythonServerStatusChangedEvent多重発行）の根本原因を解決
                // ServerManagerHostedServiceを含むすべてのIHostedServiceはProgram.csで起動完了
                Console.WriteLine("🔥 [PHASE0_FIX] IHostedService起動はProgram.csで完了済み - 重複実行を回避");

                Console.WriteLine("🔍 IEventAggregator取得開始");
                // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", "🔍 IEventAggregator取得開始");
                try
                {
                    _eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                    Console.WriteLine($"✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}");
                    // SafeFileLogger.AppendLogWithTimestamp("debug_app_logs.txt", $"✅ IEventAggregator取得成功: {_eventAggregator.GetType().Name}");
                    _logger?.LogInformation("✅ IEventAggregator取得成功: {AggregatorType}", _eventAggregator.GetType().Name);

                    // EventHandlerInitializationServiceは最優先実行済み（上部で処理完了）

                    // 🩺 診断システム開始 - 診断レポート機能を有効化
                    Console.WriteLine("🚨🚨🚨 [CRITICAL] 診断システム開始処理 - 重要ポイント！ 🚨🚨🚨");
                    try
                    {
                        Console.WriteLine("🔍🔍🔍 [CRITICAL_DEBUG] IDiagnosticCollectionService解決試行中... 🔍🔍🔍");
                        var diagnosticCollectionService = serviceProvider.GetService<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService>();
                        if (diagnosticCollectionService != null)
                        {
                            Console.WriteLine($"✅✅✅ [CRITICAL_SUCCESS] IDiagnosticCollectionService解決成功: {diagnosticCollectionService.GetType().Name} ✅✅✅");
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    Console.WriteLine("🩺 [DEBUG] 診断データ収集開始中...");
                                    await diagnosticCollectionService.StartCollectionAsync().ConfigureAwait(false);
                                    Console.WriteLine("✅ 診断データ収集開始完了");
                                }
                                catch (Exception diagEx)
                                {
                                    Console.WriteLine($"⚠️ 診断システム開始エラー: {diagEx.Message}");
                                    Console.WriteLine($"⚠️ エラーの詳細: {diagEx}");
                                    _logger?.LogWarning(diagEx, "診断システム開始エラー");
                                }
                            });
                            Console.WriteLine("🩺 診断システム非同期開始完了");
                        }
                        else
                        {
                            Console.WriteLine("🚨❌❌❌ [CRITICAL_ERROR] IDiagnosticCollectionServiceが見つかりません！ ❌❌❌🚨");
                            Console.WriteLine("🚨❌ [CRITICAL_DEBUG] DiagnosticModuleのDI登録に問題がある可能性があります ❌🚨");
                        }

                        // 🧪 診断システム動作テスト - テストイベント発行
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(5000).ConfigureAwait(false); // 5秒待機してから発行
                            try
                            {
                                var testEvent = new Baketa.Core.Events.Diagnostics.PipelineDiagnosticEvent
                                {
                                    Stage = "ApplicationStartup",
                                    IsSuccess = true,
                                    ProcessingTimeMs = 1000,
                                    Metrics = new Dictionary<string, object>
                                    {
                                        ["TestEventType"] = "StartupTest",
                                        ["Version"] = "1.0.0"
                                    },
                                    Severity = Baketa.Core.Events.Diagnostics.DiagnosticSeverity.Information
                                };

                                await _eventAggregator.PublishAsync(testEvent).ConfigureAwait(false);
                                Console.WriteLine("🧪 診断テストイベント発行完了");

                                // 追加のテストレポート生成 - 詳細デバッグ付き
                                Console.WriteLine("🔍 [診断レポート] 2秒待機開始");
                                await Task.Delay(2000).ConfigureAwait(false);
                                Console.WriteLine("🔍 [診断レポート] 2秒待機完了 - サービス取得開始");

                                try
                                {
                                    Console.WriteLine("🔍 [診断レポート] IDiagnosticCollectionService取得試行中...");
                                    var diagnosticCollectionService = serviceProvider.GetService<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService>();

                                    if (diagnosticCollectionService != null)
                                    {
                                        Console.WriteLine($"✅ [診断レポート] IDiagnosticCollectionService取得成功: {diagnosticCollectionService.GetType().Name}");
                                        Console.WriteLine("🧪 手動レポート生成テスト開始");

                                        var reportPath = await diagnosticCollectionService.GenerateReportAsync("manual_test").ConfigureAwait(false);
                                        Console.WriteLine($"🧪 手動レポート生成完了: {reportPath}");

                                        // Reports ディレクトリの内容確認
                                        var reportsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Baketa", "Reports");
                                        if (Directory.Exists(reportsDir))
                                        {
                                            var files = Directory.GetFiles(reportsDir, "*.json");
                                            Console.WriteLine($"📁 [診断レポート] Reports ディレクトリ内ファイル数: {files.Length}");
                                            foreach (var file in files.Take(3))
                                            {
                                                Console.WriteLine($"📄 [診断レポート] ファイル: {Path.GetFileName(file)}");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        Console.WriteLine("❌ [診断レポート] IDiagnosticCollectionServiceがnull - サービス登録を確認してください");
                                    }
                                }
                                catch (Exception manualEx)
                                {
                                    Console.WriteLine($"❌ [診断レポート] 手動レポート生成エラー: {manualEx.Message}");
                                    Console.WriteLine($"❌ [診断レポート] スタックトレース: {manualEx.StackTrace}");
                                }
                            }
                            catch (Exception testEx)
                            {
                                Console.WriteLine($"🧪 診断テストイベント発行エラー: {testEx.Message}");
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ 診断システム初期化エラー: {ex.Message}");
                        _logger?.LogError(ex, "診断システム初期化エラー");
                    }
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

                    // 🚀 EventHandler初期化完了をUI側に安全に通知（Gemini分析に基づく修正）
                    if (Program.IsEventHandlerInitialized)
                    {
                        Console.WriteLine("🚀 [UI_SAFE] EventHandler初期化済み - MainOverlayViewModel通知実行");
                        mainOverlayViewModel.IsEventHandlerInitialized = true;
                        Console.WriteLine("✅ [UI_SAFE] MainOverlayViewModel.IsEventHandlerInitialized = true 設定完了");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ [UI_SAFE] EventHandler初期化未完了 - UI表示時に手動設定が必要");
                    }
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

                // 🔧 [OVERLAY_UNIFICATION] オーバーレイマネージャー統合確認
                Console.WriteLine("🎯 IOverlayManager (Win32OverlayManager) 初期化確認");
                try
                {
                    var overlayManager = serviceProvider.GetService<Baketa.Core.Abstractions.UI.Overlays.IOverlayManager>();
                    if (overlayManager != null)
                    {
                        // 🔧 [OVERLAY_UNIFICATION] Win32OverlayManagerはDIコンテナで初期化済み
                        // InitializeAsync()メソッドは存在しないため、初期化不要
                        Console.WriteLine($"✅ IOverlayManager (Win32OverlayManager) DI解決成功: {overlayManager.GetType().Name}");
                    }
                    else
                    {
                        Console.WriteLine("⚠️ IOverlayManagerが見つかりません");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ IOverlayManager確認エラー: {ex.Message}");
                }

                // 旧TranslationResultOverlayManagerは削除済み - インプレースシステムが自動で管理
                Console.WriteLine("🖥️ 旧オーバーレイシステムは削除済み - インプレースシステムが自動で管理");

                // 🔥 [FIX] Pythonサーバー起動はPythonServerHostedServiceで自動実行される
                // IHostedServiceパターン（コミット 1b5a5d9）により、アプリ起動時に自動的にサーバーが起動される
                // 手動起動コードは重複のため削除（重複イベント発行によるStartボタン有効化問題を解決）
                Console.WriteLine("✅ Pythonサーバーは PythonServerHostedService により自動起動されます");

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

                // OPUS-MT削除済み: NLLB-200統一により事前起動サービス不要

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

        // 🚀 翻訳モデル事前ロード戦略 - Clean Architecture準拠実装
        Console.WriteLine("🚀 [APP_INIT] 翻訳エンジン事前ロード開始済み");
        try
        {
            // Clean Architecture準拠：DIコンテナから抽象化されたサービスを取得
            var serviceProvider = Program.ServiceProvider;
            if (serviceProvider != null)
            {
                var appInitializer = serviceProvider.GetService<IApplicationInitializer>();
                if (appInitializer != null)
                {
                    Console.WriteLine("🔥 [PRELOAD] TranslationModelLoader取得成功 - バックグラウンド実行開始");

                    // UIスレッドをブロックしないようにバックグラウンドで実行
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await appInitializer.InitializeAsync().ConfigureAwait(false);
                            Console.WriteLine("✅ [PRELOAD] 翻訳モデル事前ロード完了 - 初回翻訳は即座実行可能");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"⚠️ [PRELOAD] 事前ロード失敗 - 従来動作継続: {ex.Message}");
                            _logger?.LogWarning(ex, "翻訳モデル事前ロード失敗 - 従来の遅延初期化で継続");
                        }
                    });

                    Console.WriteLine("🎯 [PRELOAD] バックグラウンド事前ロード開始完了");
                }
                else
                {
                    Console.WriteLine("ℹ️ [PRELOAD] IApplicationInitializer未登録 - 従来動作で継続");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [PRELOAD] 事前ロードサービス取得失敗 - 従来動作継続: {ex.Message}");
            _logger?.LogWarning(ex, "事前ロードサービスの取得に失敗 - 従来動作を継続");
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// アプリケーションシャットダウン要求処理
    /// </summary>
    private void OnShutdownRequested(object? sender, ShutdownRequestedEventArgs e)
    {
        // 🔥 [SHUTDOWN_DEBUG] 診断ログ - ハンドラー実行確認
        Console.WriteLine("🚨 [SHUTDOWN_DEBUG] OnShutdownRequested呼び出し開始");
        System.IO.File.AppendAllText(
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
            $"[{DateTime.Now:HH:mm:ss.fff}] 🚨 [SHUTDOWN_DEBUG] OnShutdownRequested呼び出し開始\r\n");

        try
        {
            _logger?.LogInformation("アプリケーションシャットダウン要求を受信");

            // 🔥 [P0_GC_FIX] Win32ウィンドウクラスの完全クリーンアップ
            // WndProcDelegate参照を解放し、UnregisterClassでウィンドウクラス登録解除
            // これにより.NET Hostプロセス残存問題を解決
            try
            {
                Console.WriteLine("🔥 [SHUTDOWN_DEBUG] CleanupStaticResources呼び出し直前");
                Baketa.Infrastructure.Platform.Windows.Overlay.LayeredOverlayWindow.CleanupStaticResources();
                Console.WriteLine("✅ [SHUTDOWN_DEBUG] CleanupStaticResources呼び出し完了");
                _logger?.LogInformation("✅ [P0_GC_FIX] LayeredOverlayWindow静的リソースクリーンアップ完了");
            }
            catch (Exception cleanupEx)
            {
                Console.WriteLine($"❌ [SHUTDOWN_DEBUG] CleanupStaticResources例外: {cleanupEx.Message}");
                _logger?.LogWarning(cleanupEx, "⚠️ [P0_GC_FIX] LayeredOverlayWindowクリーンアップ中にエラー（継続）");
            }

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
        // 🔥 [P0_GC_FIX_CRITICAL] Win32ウィンドウクラスの完全クリーンアップ（最優先実行）
        // プロセス終了時は限られた時間しかないため、最優先でCleanupStaticResources()を実行
        // ログ出力などの二次的な処理は後回し
        try
        {
            Baketa.Infrastructure.Platform.Windows.Overlay.LayeredOverlayWindow.CleanupStaticResources();

            // クリーンアップ成功後に診断ログ出力（タイミング余裕があれば）
            try
            {
                Console.WriteLine("✅ [SHUTDOWN_DEBUG] CleanupStaticResources呼び出し完了（ProcessExit）");
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] ✅ [SHUTDOWN_DEBUG] CleanupStaticResources完了\r\n");
            }
            catch { /* 診断ログ失敗は無視 */ }
        }
        catch (Exception cleanupEx)
        {
            // クリーンアップエラーログ（可能な限り出力）
            try
            {
                Console.WriteLine($"❌ [SHUTDOWN_DEBUG] CleanupStaticResources例外: {cleanupEx.Message}");
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                    $"[{DateTime.Now:HH:mm:ss.fff}] ❌ [SHUTDOWN_DEBUG] Cleanupエラー: {cleanupEx.Message}\r\n");
            }
            catch { /* 診断ログ失敗は無視 */ }
        }

        // 二次的な処理（ネイティブライブラリ強制終了）
        try
        {
            _logger?.LogInformation("プロセス終了処理開始");

            // ネイティブライブラリの強制終了
            NativeWindowsCaptureWrapper.ForceShutdownOnApplicationExit();

            _logger?.LogInformation("プロセス終了処理完了");
        }
        catch (Exception ex)
        {
            // プロセス終了時のエラーは抑制
            try
            {
                _logger?.LogWarning(ex, "プロセス終了処理中にエラーが発生しましたが、継続します");
            }
            catch { /* ログ出力失敗も無視 */ }
        }
    }

    // 以下、削除された元のコードを残す（削除済み部分）
    private void OnProcessExit_Old(object? sender, EventArgs e)
    {
        // 🔥 [SHUTDOWN_DEBUG] 診断ログ - ハンドラー実行確認
        try
        {
            Console.WriteLine("🚨 [SHUTDOWN_DEBUG] OnProcessExit呼び出し開始");
            System.IO.File.AppendAllText(
                System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_app_logs.txt"),
                $"[{DateTime.Now:HH:mm:ss.fff}] 🚨 [SHUTDOWN_DEBUG] OnProcessExit呼び出し開始\r\n");
        }
        catch { }

        try
        {
            _logger?.LogInformation("プロセス終了処理開始");

            // 🔥 [P0_GC_FIX] Win32ウィンドウクラスの完全クリーンアップ（フェイルセーフ）
            // OnShutdownRequestedでクリーンアップ済みでも、クラッシュ時の保険として再実行
            // _windowClassAtom == 0 の場合は内部で安全にスキップされる
            try
            {
                Console.WriteLine("🔥 [SHUTDOWN_DEBUG] CleanupStaticResources呼び出し直前（ProcessExit）");
                Baketa.Infrastructure.Platform.Windows.Overlay.LayeredOverlayWindow.CleanupStaticResources();
                Console.WriteLine("✅ [SHUTDOWN_DEBUG] CleanupStaticResources呼び出し完了（ProcessExit）");
                _logger?.LogInformation("✅ [P0_GC_FIX] LayeredOverlayWindow静的リソースクリーンアップ完了（ProcessExit）");
            }
            catch (Exception cleanupEx)
            {
                // プロセス終了時のエラーは抑制
                try
                {
                    Console.WriteLine($"❌ [SHUTDOWN_DEBUG] CleanupStaticResources例外（ProcessExit）: {cleanupEx.Message}");
                    _logger?.LogWarning(cleanupEx, "⚠️ [P0_GC_FIX] LayeredOverlayWindowクリーンアップ中にエラー（ProcessExit・継続）");
                }
                catch
                {
                    // ログ出力も失敗する場合は抑制
                }
            }

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
