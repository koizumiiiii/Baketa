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
using Baketa.Core.Abstractions.Auth;
using Baketa.Core.Abstractions.Events;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;
using Baketa.UI.Views;
using Baketa.UI.Views.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using CoreEvents = Baketa.Core.Events;
using Baketa.UI.Framework.Events;

namespace Baketa.UI;

internal sealed partial class App : Avalonia.Application
{
    private ILogger<App>? _logger;
    private IEventAggregator? _eventAggregator;

    // アプリケーションアイコンのパス定数 (Issue #179)
    private const string BAKETA_ICON_PATH = "avares://Baketa/Assets/Icons/baketa.ico";

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

    /// <summary>
    /// [Issue #170] 早期ローディング画面表示用のウィンドウ参照
    /// App.Initialize()で作成し、OnFrameworkInitializationCompleted()で閉じる
    /// </summary>
    private LoadingWindow? _earlyLoadingWindow;

    public override void Initialize()
    {
        Console.WriteLine("🔥🔥🔥 [INIT_DEBUG] App.Initialize() 開始 - ServiceProvider状態確認 🔥🔥🔥");
        Console.WriteLine($"[INIT_DEBUG] Program.ServiceProvider == null: {Program.ServiceProvider == null}");

        AvaloniaXamlLoader.Load(this);

        // [Issue #170] 早期ローディング画面を即座に表示（ServiceProvider不要）
        try
        {
            Console.WriteLine("🚀 [EARLY_LOADING] 早期ローディング画面表示開始");

            _earlyLoadingWindow = new LoadingWindow();

            // アプリケーションアイコンを設定
            try
            {
                var iconUri = new Uri(BAKETA_ICON_PATH);
                _earlyLoadingWindow.Icon = new Avalonia.Controls.WindowIcon(
                    Avalonia.Platform.AssetLoader.Open(iconUri));
            }
            catch (Exception iconEx)
            {
                Console.WriteLine($"⚠️ 早期LoadingWindowアイコン設定失敗: {iconEx.Message}");
            }

            // ViewModelなしで表示（後でDataContextを設定）
            _earlyLoadingWindow.Show();
            Console.WriteLine("✅ [EARLY_LOADING] 早期ローディング画面表示完了");
        }
        catch (Exception earlyLoadingEx)
        {
            Console.WriteLine($"⚠️ [EARLY_LOADING] 早期ローディング画面表示失敗: {earlyLoadingEx.Message}");
            _earlyLoadingWindow = null;
        }

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

    public override void OnFrameworkInitializationCompleted()
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

#if DEBUG
            try
            {
                var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reactive_ui_startup.txt");
                File.WriteAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 🎆 ReactiveUIエラーハンドラー設定完了");
            }
            catch { /* ファイル出力失敗は無視 */ }
#endif

            try
            {
                Console.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");
                System.Diagnostics.Debug.WriteLine("🖥️ IClassicDesktopStyleApplicationLifetime取得成功");

                // サービスプロバイダーからサービスを取得
                LoadingWindow? loadingWindow = null;
                LoadingViewModel? loadingViewModel = null;
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

                // [Issue #170] UIスレッドで単一の非同期フローを実行（ローディング→初期化→メインUI表示）
                _ = Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    LoadingWindow? loadingWindow = null;
                    LoadingViewModel? loadingViewModel = null;

                    try
                    {
                        // 🔥 [ISSUE#167] デバッグ出力
                        Console.WriteLine("🔥🔥🔥 [AUTH_DEBUG] InvokeAsync開始 🔥🔥🔥");

                        // --- 1. ローディング画面の準備 ---
                        _logger?.LogInformation("ローディング画面初期化開始");
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 1: ローディング画面準備開始");

                        var loadingScreenInitializer = serviceProvider.GetRequiredService<Baketa.Core.Abstractions.Services.ILoadingScreenInitializer>();
                        loadingViewModel = serviceProvider.GetRequiredService<LoadingViewModel>();

                        if (_earlyLoadingWindow != null)
                        {
                            loadingWindow = _earlyLoadingWindow;
                            loadingWindow.DataContext = loadingViewModel;
                            _logger?.LogInformation("早期ローディング画面にLoadingViewModel設定完了");
                        }
                        else
                        {
                            Console.WriteLine("⚠️ 早期ローディング画面なし - 新規作成");
                            loadingWindow = new LoadingWindow { DataContext = loadingViewModel };
                            var iconUri = new Uri(BAKETA_ICON_PATH);
                            loadingWindow.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(iconUri));
                            loadingWindow.Show();
                            _logger?.LogInformation("LoadingViewModel設定完了（フォールバック）");
                        }

                        // --- 2. アプリケーション初期化 ---
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 2: アプリケーション初期化開始");
                        var loadingStartTime = System.Diagnostics.Stopwatch.StartNew();
                        await loadingScreenInitializer.InitializeAsync();
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 2: アプリケーション初期化完了");
                        _logger?.LogInformation("アプリケーション初期化完了");

                        // 最小表示時間（2秒）を確保
                        const int MinimumDisplayTimeMs = 2000;
                        var elapsedMs = (int)loadingStartTime.ElapsedMilliseconds;
                        if (elapsedMs < MinimumDisplayTimeMs)
                        {
                            var remainingMs = MinimumDisplayTimeMs - elapsedMs;
                            _logger?.LogInformation("ローディング画面最小表示時間確保: {RemainingMs}ms待機", remainingMs);
                            await Task.Delay(remainingMs);
                        }

                        // --- 3. ローディング画面を閉じる ---
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 3: ローディング画面クローズ開始");
                        await loadingWindow.CloseWithFadeOutAsync();
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 3: ローディング画面クローズ完了");
                        _logger?.LogInformation("ローディング画面クローズ完了");

                        // --- 4. 認証状態チェックとメインUI表示 ---
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 4: 認証状態チェック開始");
                        _logger?.LogInformation("認証状態をチェック中...");

                        var authService = serviceProvider.GetRequiredService<IAuthService>();
                        var tokenStorage = serviceProvider.GetRequiredService<ITokenStorage>();

                        // セッション復元を試みる
                        bool isAuthenticated = false;
                        try
                        {
                            // 保存されたトークンがあるか確認
                            var hasTokens = await tokenStorage.HasStoredTokensAsync().ConfigureAwait(true);
                            if (hasTokens)
                            {
                                _logger?.LogInformation("保存されたトークンを検出、セッション復元を試行中...");
                                await authService.RestoreSessionAsync().ConfigureAwait(true);

                                // セッション復元後に認証状態を確認
                                var session = await authService.GetCurrentSessionAsync().ConfigureAwait(true);
                                isAuthenticated = session != null;
                                _logger?.LogInformation("セッション復元結果: {IsAuthenticated}", isAuthenticated);
                            }
                            else
                            {
                                _logger?.LogInformation("保存されたトークンなし、未認証状態");
                            }
                        }
                        catch (Exception authEx)
                        {
                            _logger?.LogWarning(authEx, "セッション復元中にエラー発生、ログイン画面を表示します");
                            isAuthenticated = false;

                            // セキュリティ強化: 不正なトークンを削除
                            try
                            {
                                await tokenStorage.ClearTokensAsync().ConfigureAwait(true);
                                _logger?.LogInformation("セッション復元失敗に伴い、保存されたトークンをクリアしました");
                            }
                            catch (Exception clearEx)
                            {
                                _logger?.LogError(clearEx, "トークンクリア中にエラー発生");
                            }
                        }

                        Console.WriteLine($"📌 [AUTH_DEBUG] Step 4: 認証チェック完了 isAuthenticated={isAuthenticated}");

                        // 🔥 [ISSUE#167] 常にMainOverlayViewを最初に表示
                        // 認証前はExitボタンのみ有効、認証後は全ボタン有効
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 5: MainOverlayView表示開始");
                        _logger?.LogInformation("MainOverlayViewを表示します（認証状態: {IsAuthenticated}）", isAuthenticated);

                        var mainOverlayViewModel = serviceProvider.GetRequiredService<MainOverlayViewModel>();
                        if (Program.IsEventHandlerInitialized)
                        {
                            mainOverlayViewModel.IsEventHandlerInitialized = true;
                        }

                        // 認証状態に応じてモードを設定
                        mainOverlayViewModel.SetAuthenticationMode(!isAuthenticated);

                        var mainOverlayView = new MainOverlayView { DataContext = mainOverlayViewModel };
                        var mainIconUri = new Uri(BAKETA_ICON_PATH);
                        mainOverlayView.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(mainIconUri));

                        desktop.MainWindow = mainOverlayView;
                        mainOverlayView.Show();
                        Console.WriteLine("✅ MainOverlayView.Show()実行完了");

                        // --- 4.5 Patreon認証結果の通知表示（Issue #233） ---
                        await ShowPendingPatreonNotificationAsync(serviceProvider);

                        // --- 4.6 テストモード警告表示（Issue #110: 決済統合） ---
                        await ShowTestModeWarningIfNeededAsync(serviceProvider, mainOverlayView);

                        // 未認証の場合はLoginViewをダイアログとして表示
                        if (!isAuthenticated)
                        {
                            Console.WriteLine("📌 [AUTH_DEBUG] Step 6: LoginViewダイアログ表示（未認証）");
                            _logger?.LogInformation("未認証: LoginViewをダイアログとして表示します");

                            // 認証完了後にダイアログが閉じるよう、非同期で表示
                            _ = Task.Run(async () =>
                            {
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    var loginViewModel = serviceProvider.GetRequiredService<LoginViewModel>();
                                    var loginView = new LoginView(loginViewModel);
                                    var loginIconUri = new Uri(BAKETA_ICON_PATH);
                                    loginView.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(loginIconUri));

                                    await loginView.ShowDialog<bool?>(mainOverlayView);
                                    Console.WriteLine("✅ LoginViewダイアログ終了");
                                });
                            });
                        }

                        // --- 5. その他の初期化とイベントハンドラ登録 ---
                        _eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();

                        // Note: LocalizationManager.Initialize()はLocalizationServiceのコンストラクタで呼ばれるため、
                        // ここでの呼び出しは不要です (Issue #176, #177)

                        var translationFlowModule = new Baketa.UI.DI.Modules.TranslationFlowModule();
                        translationFlowModule.ConfigureEventAggregator(_eventAggregator, serviceProvider);

                        // --- 5.1 トークン有効期限切れハンドラー登録 (Issue #168) ---
                        SetupTokenExpirationHandler(serviceProvider, mainOverlayView);

                        _ = _eventAggregator?.PublishAsync(new ApplicationStartupEvent());
                        _logStartupCompleted(_logger, null);

                        desktop.ShutdownRequested += OnShutdownRequested;
                        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
                    }
                    catch (Exception ex)
                    {
                        // 🔥 [ISSUE#167] 起動時例外のデバッグ出力
                        Console.WriteLine($"❌❌❌ [AUTH_DEBUG] 起動時例外: {ex.GetType().Name}: {ex.Message}");
                        Console.WriteLine($"❌❌❌ [AUTH_DEBUG] スタックトレース: {ex.StackTrace}");
                        _logStartupError(_logger, ex);
                        loadingWindow?.Close();
                        desktop.Shutdown();
                    }
                    finally
                    {
                        if (loadingViewModel is IDisposable disposable)
                        {
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(500);
                                disposable.Dispose();
                            });
                        }
                    }
                }, Avalonia.Threading.DispatcherPriority.Normal);

                // [Issue #170] UIスレッド非同期フロー内でメインUI表示が完了するため、
                // この時点では追加の初期化は不要。AdWindowと診断システムは別途処理。

                // 📢 [Issue #174] 広告ウィンドウの起動（メインUIとは独立）
                _logger?.LogInformation("AdWindow起動開始（Issue #174: WebView統合）");
                try
                {
                    var adViewModel = serviceProvider.GetRequiredService<AdViewModel>();
                    var adWindow = new Views.AdWindow(adViewModel, serviceProvider.GetRequiredService<ILogger<Views.AdWindow>>());

                    // アプリケーションアイコンを設定
                    try
                    {
                        var iconUri = new Uri(BAKETA_ICON_PATH);
                        adWindow.Icon = new Avalonia.Controls.WindowIcon(
                            Avalonia.Platform.AssetLoader.Open(iconUri));
                    }
                    catch (Exception iconEx)
                    {
                        _logger?.LogWarning(iconEx, "AdWindowアイコン設定失敗");
                    }

                    // 広告表示が有効な場合のみ表示
                    if (adViewModel.ShouldShowAd)
                    {
                        adWindow.Show();
                        _logger?.LogInformation("AdWindow表示完了: 画面右下に配置");
                    }
                    else
                    {
                        _logger?.LogInformation("AdWindow非表示: Premiumプランまたは広告非表示設定");
                    }
                }
                catch (Exception adEx)
                {
                    _logger?.LogWarning(adEx, "AdWindow起動失敗: {Message}。アプリケーションは継続します", adEx.Message);
                }

                // 🩺 診断システム開始（メインUIとは独立）
                try
                {
                    var diagnosticCollectionService = serviceProvider.GetService<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService>();
                    if (diagnosticCollectionService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await diagnosticCollectionService.StartCollectionAsync().ConfigureAwait(false);
                                Console.WriteLine("✅ 診断データ収集開始完了");
                            }
                            catch (Exception diagEx)
                            {
                                _logger?.LogWarning(diagEx, "診断システム開始エラー");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "診断システム初期化エラー");
                }

                // 🔥 [ISSUE#163] SingleshotEventProcessor登録（メインUIとは独立）
                try
                {
                    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                    var singleshotProcessor = serviceProvider.GetRequiredService<IEventProcessor<ExecuteSingleshotRequestEvent>>();
                    eventAggregator.Subscribe<ExecuteSingleshotRequestEvent>(singleshotProcessor);
                    Console.WriteLine("✅ SingleshotEventProcessor登録完了");
                }
                catch (Exception singleshotEx)
                {
                    _logger?.LogWarning(singleshotEx, "SingleshotEventProcessor登録失敗");
                }

                // 🔔 [Issue #78 Phase 5] TokenUsageAlertService初期化
                // トークン使用量80%/90%/100%到達時のトースト通知サービス
                try
                {
                    _ = serviceProvider.GetRequiredService<Services.TokenUsageAlertService>();
                    Console.WriteLine("✅ TokenUsageAlertService初期化完了");
                }
                catch (Exception alertEx)
                {
                    _logger?.LogWarning(alertEx, "TokenUsageAlertService初期化失敗");
                }
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
    /// トークン有効期限切れハンドラーの設定 (Issue #168)
    /// TokenExpiredイベントをサブスクライブし、UI通知とナビゲーションを行う
    /// </summary>
    private void SetupTokenExpirationHandler(IServiceProvider serviceProvider, Avalonia.Controls.Window mainWindow)
    {
        try
        {
            var tokenExpirationHandler = serviceProvider.GetService<ITokenExpirationHandler>();
            if (tokenExpirationHandler == null)
            {
                _logger?.LogWarning("ITokenExpirationHandler が見つかりません。トークン有効期限切れ処理は無効です。");
                return;
            }

            var navigationService = serviceProvider.GetService<INavigationService>();
            var notificationService = serviceProvider.GetService<INotificationService>();

            tokenExpirationHandler.TokenExpired += async (sender, args) =>
            {
                _logger?.LogWarning("トークン有効期限切れイベント受信: {Reason} (ユーザー: {UserId})", args.Reason, args.UserId);

                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                {
                    try
                    {
                        // 1. ユーザーに通知
                        if (notificationService != null)
                        {
                            await notificationService.ShowWarningAsync(
                                "セッション期限切れ",
                                "セッションが期限切れになりました。再度ログインしてください。",
                                duration: 5000);
                        }

                        // 2. ログイン画面へナビゲーション
                        if (navigationService != null)
                        {
                            await navigationService.LogoutAndShowLoginAsync();
                            _logger?.LogInformation("トークン有効期限切れによりログイン画面へリダイレクトしました");
                        }
                        else
                        {
                            _logger?.LogWarning("INavigationService が見つかりません。ログイン画面へのリダイレクトをスキップします。");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "トークン有効期限切れイベント処理中にエラーが発生しました");
                    }
                });
            };

            _logger?.LogInformation("✅ TokenExpirationHandler イベントサブスクリプション完了");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "TokenExpirationHandler のセットアップに失敗しました");
        }
    }

    /// <summary>
    /// テストモード有効化に必要な環境変数名
    /// </summary>
    private const string TestModeEnvVar = "BAKETA_ALLOW_TEST_MODE";

    /// <summary>
    /// テストモード警告表示 (Issue #110: 決済統合)
    /// License.EnableMockMode と Payment.EnableMockMode が両方有効な場合に警告を表示
    /// </summary>
    private async Task ShowTestModeWarningIfNeededAsync(IServiceProvider serviceProvider, Avalonia.Controls.Window parentWindow)
    {
        try
        {
            var licenseSettings = serviceProvider.GetService<IOptions<LicenseSettings>>()?.Value;
            var paymentSettings = serviceProvider.GetService<IOptions<PaymentSettings>>()?.Value;

            // 両方のモックモードが有効かチェック
            bool isLicenseMockEnabled = licenseSettings?.EnableMockMode ?? false;
            bool isPaymentMockEnabled = paymentSettings?.EnableMockMode ?? false;

            if (!isLicenseMockEnabled || !isPaymentMockEnabled)
            {
                // テストモードではない
                return;
            }

            // 環境変数チェック
            var envValue = Environment.GetEnvironmentVariable(TestModeEnvVar);
            bool isEnvVarSet = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);

            _logger?.LogWarning(
                "🧪 テストモード設定検出: License.EnableMockMode={LicenseMock}, Payment.EnableMockMode={PaymentMock}, EnvVar={EnvVar}={EnvValue}",
                isLicenseMockEnabled, isPaymentMockEnabled, TestModeEnvVar, envValue ?? "(未設定)");

            // コンソールに警告出力（開発者向け）
            Console.WriteLine("⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️");
            Console.WriteLine("🧪 [TEST_MODE] 決済テストモード設定が検出されました");
            Console.WriteLine($"   License.EnableMockMode = {isLicenseMockEnabled}");
            Console.WriteLine($"   Payment.EnableMockMode = {isPaymentMockEnabled}");
            Console.WriteLine($"   {TestModeEnvVar} = {envValue ?? "(未設定)"}");

            // 警告メッセージを構築
            string warningTitle;
            string warningMessage;

            if (isEnvVarSet)
            {
                // 環境変数が設定されている場合：テストモードが完全に有効
                warningTitle = "🧪 テストモード有効";
                warningMessage = "決済処理をスキップし、プラン変更を即座に反映するテストモードが有効です。" +
                                "本番環境ではappsettings.jsonのEnableMockModeをfalseに設定してください。";

                Console.WriteLine("   ✅ テストモード完全有効（プラン即時変更可能）");
            }
            else
            {
                // 環境変数が設定されていない場合：設定は有効だがテストモードは無効
                warningTitle = "⚠️ 設定不整合";
                warningMessage = "モック設定が有効ですが環境変数が未設定のため、決済処理もプラン変更の反映も機能しません。" +
                                $"テストするには環境変数 {TestModeEnvVar}=true を設定してください。";

                Console.WriteLine("   ⚠️ 設定不整合（決済・プラン変更ともに機能しない状態）");
                Console.WriteLine($"   → 有効化するには: set {TestModeEnvVar}=true");
            }

            Console.WriteLine("⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️");

            // INotificationServiceを使用して警告を表示
            var notificationService = serviceProvider.GetService<INotificationService>();
            if (notificationService != null)
            {
                // 10秒間表示（重要な警告なので長めに）
                await notificationService.ShowWarningAsync(warningTitle, warningMessage, duration: 10000);
            }

            _logger?.LogInformation("テストモード警告表示完了");
        }
        catch (Exception ex)
        {
            // 警告表示の失敗はアプリケーション起動をブロックしない
            _logger?.LogWarning(ex, "テストモード警告表示中にエラー（継続）");
        }
    }

    /// <summary>
    /// Patreon認証結果の通知表示 (Issue #233)
    /// Program.PendingPatreonNotification にセットされた認証結果を表示
    /// </summary>
    private async Task ShowPendingPatreonNotificationAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var notification = Program.PendingPatreonNotification;
            if (notification == null)
            {
                return;
            }

            // 通知を消費（一度だけ表示）
            Program.PendingPatreonNotification = null;

            var notificationService = serviceProvider.GetService<INotificationService>();
            if (notificationService == null)
            {
                _logger?.LogWarning("INotificationService が見つかりません。Patreon認証結果の通知をスキップします。");
                return;
            }

            if (notification.IsSuccess)
            {
                await notificationService.ShowSuccessAsync(
                    "Patreon連携成功",
                    $"Patreonアカウントとの連携が完了しました。プラン: {notification.PlanName}",
                    duration: 5000);

                _logger?.LogInformation("Patreon認証成功通知を表示: Plan={Plan}", notification.PlanName);
            }
            else
            {
                await notificationService.ShowErrorAsync(
                    "Patreon連携失敗",
                    notification.ErrorMessage ?? "認証処理中にエラーが発生しました",
                    duration: 0); // エラーは手動で閉じるまで表示

                _logger?.LogWarning("Patreon認証失敗通知を表示: Error={Error}", notification.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Patreon認証結果通知表示中にエラー（継続）");
        }
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
