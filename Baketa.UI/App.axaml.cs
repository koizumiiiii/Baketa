#pragma warning disable CS0618 // Type or member is obsolete
using System;
using System.Collections.Generic;
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
using Baketa.Core.Abstractions.Services;
using Baketa.Core.Abstractions.Settings;
using Baketa.Core.Abstractions.Translation;
using Baketa.Core.Settings;
using Baketa.Infrastructure.Platform.Windows.Capture;
using Baketa.UI.Services;
using Baketa.UI.Utils;
using Baketa.UI.ViewModels;
using Baketa.UI.ViewModels.Auth;
using Baketa.UI.Views;
using Baketa.UI.Views.Auth;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReactiveUI;
using CoreEvents = Baketa.Core.Events;
using Baketa.UI.Framework.Events;

namespace Baketa.UI;

internal sealed partial class App : Avalonia.Application, IDisposable
{
    private ILogger<App>? _logger;
    private IEventAggregator? _eventAggregator;
    private IUsageAnalyticsService? _usageAnalyticsService;  // Issue #269
    private bool _disposed;

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

    /// <summary>
    /// [Issue #249] 自動アップデートサービス
    /// NetSparkleを使用してGitHub Releasesからアップデートを確認・適用
    /// </summary>
    private UpdateService? _updateService;

    /// <summary>
    /// [Issue #245] 保存されたテーマを適用
    /// 設定ファイルを直接読み込んでテーマを設定
    /// </summary>
    private void ApplyStoredTheme()
    {
        Console.WriteLine("[Theme] ApplyStoredTheme() 開始");
        try
        {
            // [Issue #252] BaketaSettingsPaths を使用してパスを一元管理
            var settingsFilePath = BaketaSettingsPaths.MainSettingsPath;

            Console.WriteLine($"[Theme] 設定ファイルパス: {settingsFilePath}");
            Console.WriteLine($"[Theme] ファイル存在: {File.Exists(settingsFilePath)}");

            if (File.Exists(settingsFilePath))
            {
                var json = File.ReadAllText(settingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                // AppSettings.General.Theme を読み取る
                if (doc.RootElement.TryGetProperty("General", out var generalElement) &&
                    generalElement.TryGetProperty("Theme", out var themeElement))
                {
                    var themeValue = themeElement.GetInt32();
                    var theme = (UiTheme)themeValue;

                    Console.WriteLine($"[Theme] 読み込んだテーマ値: {themeValue} -> {theme}");

                    RequestedThemeVariant = theme switch
                    {
                        UiTheme.Light => Avalonia.Styling.ThemeVariant.Light,
                        UiTheme.Dark => Avalonia.Styling.ThemeVariant.Dark,
                        UiTheme.Auto => Avalonia.Styling.ThemeVariant.Default,
                        _ => Avalonia.Styling.ThemeVariant.Default
                    };

                    Console.WriteLine($"[Theme] ✅ 保存されたテーマを適用: {theme}, RequestedThemeVariant={RequestedThemeVariant}");
                }
                else
                {
                    Console.WriteLine("[Theme] 設定ファイルにGeneral.Themeが見つからない");
                }
            }
            else
            {
                Console.WriteLine("[Theme] 設定ファイルなし - デフォルトテーマを使用");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Theme] テーマ適用エラー（継続）: {ex.Message}");
            Console.WriteLine($"[Theme] スタックトレース: {ex.StackTrace}");
        }
        Console.WriteLine("[Theme] ApplyStoredTheme() 終了");
    }

    public override void Initialize()
    {
        Console.WriteLine("🔥🔥🔥 [INIT_DEBUG] App.Initialize() 開始 - ServiceProvider状態確認 🔥🔥🔥");
        Console.WriteLine($"[INIT_DEBUG] Program.ServiceProvider == null: {Program.ServiceProvider == null}");

        AvaloniaXamlLoader.Load(this);

        // [Issue #245] 保存されたテーマを起動時に適用（XAML読み込み直後、UIウィンドウ表示前）
        ApplyStoredTheme();

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

            // [Issue #287] 静的API Key削除 - Phase 8でJWT認証へ完全移行予定

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
            // 🔥 [Issue #243] ShutdownModeをOnExplicitShutdownに設定
            // デフォルトのOnLastWindowCloseだと、Loading Window閉じた時に
            // MainWindowがまだ設定されていないため、アプリが早期終了する問題を修正
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Console.WriteLine("🔧 ShutdownMode set to OnExplicitShutdown");

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

            // [Issue #252] ReactiveUIのエラーハンドラーはGlobalExceptionHandlerに統合済み
            // GlobalExceptionHandler.Initialize()でRxApp.DefaultExceptionHandlerが設定される
            Console.WriteLine("🎆 ReactiveUIエラーハンドラーはGlobalExceptionHandlerで統合管理");

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

                // [Issue #245] テーマはInitialize()で既に適用済み

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

                        // --- 3.5 [Issue #252] クラッシュレポート検出・ダイアログ表示 ---
                        await CheckAndShowCrashReportDialogAsync(serviceProvider);

                        // --- 3.6 [Issue #261] プライバシーポリシー同意確認 ---
                        var consentAccepted = await CheckAndShowConsentDialogAsync(serviceProvider);
                        if (!consentAccepted)
                        {
                            _logger?.LogInformation("[Issue #261] 同意が得られなかったため、アプリケーションを終了します");
                            desktop.Shutdown();
                            return;
                        }

                        // --- 4. 認証状態チェックとメインUI表示 ---
                        Console.WriteLine("📌 [AUTH_DEBUG] Step 4: 認証状態チェック開始");
                        _logger?.LogInformation("認証状態をチェック中...");

                        var authService = serviceProvider.GetRequiredService<IAuthService>();
                        var tokenStorage = serviceProvider.GetRequiredService<ITokenStorage>();

                        // [Issue #299] AuthInitializationService.StartAsync()で既にセッション復元済み
                        // ここでは現在のセッションを取得するだけでよい（重複呼び出し防止）
                        bool isAuthenticated = false;
                        try
                        {
                            var session = await authService.GetCurrentSessionAsync().ConfigureAwait(true);
                            isAuthenticated = session != null;
                            if (isAuthenticated && session?.User?.Id != null && !string.IsNullOrEmpty(session.AccessToken))
                            {
                                _logger?.LogInformation("[Issue #299] 既存セッション検出（AuthInitializationServiceで復元済み）");

                                // [Issue #261] 認証成功時にローカル同意をDBに同期
                                var consentService = serviceProvider.GetService<Baketa.Core.Abstractions.Settings.IConsentService>();
                                if (consentService != null)
                                {
                                    await consentService.SyncLocalConsentToServerAsync(session.User.Id, session.AccessToken).ConfigureAwait(true);
                                }
                            }
                            else
                            {
                                _logger?.LogInformation("[Issue #299] セッションなし、未認証状態");
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

                        // --- 4.9 [Issue #275] トークン使用量の起動時同期 ---
                        // 設定画面を開く前にLicenseManagerに実際のトークン使用量を同期
                        await SyncTokenUsageAtStartupAsync(serviceProvider).ConfigureAwait(true);

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

                        // --- 4.4 自動アップデートチェック（Issue #249） ---
                        await InitializeUpdateServiceAsync(serviceProvider);

                        // --- 4.5 Patreon認証結果の通知表示（Issue #233） ---
                        await ShowPendingPatreonNotificationAsync(serviceProvider);

                        // 未認証の場合はSignupViewをダイアログとして表示（初回起動時はサインアップを推奨）
                        if (!isAuthenticated)
                        {
                            Console.WriteLine("📌 [AUTH_DEBUG] Step 6: SignupViewダイアログ表示（未認証）");
                            _logger?.LogInformation("未認証: SignupViewをダイアログとして表示します");

                            // 認証完了後にダイアログが閉じるよう、非同期で表示
                            _ = Task.Run(async () =>
                            {
                                await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(async () =>
                                {
                                    var signupViewModel = serviceProvider.GetRequiredService<SignupViewModel>();
                                    var signupView = new SignupView(signupViewModel);
                                    var signupIconUri = new Uri(BAKETA_ICON_PATH);
                                    signupView.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(signupIconUri));

                                    await signupView.ShowDialog<bool?>(mainOverlayView);
                                    Console.WriteLine("✅ SignupViewダイアログ終了");
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

                        // --- 5.2 [Issue #269] 使用統計 session_start イベント記録 ---
                        try
                        {
                            _usageAnalyticsService = serviceProvider.GetService<IUsageAnalyticsService>();
                            if (_usageAnalyticsService?.IsEnabled == true)
                            {
                                // [Issue #307] MinVerはAssemblyInformationalVersionAttributeに設定
                                var appAssembly = Assembly.GetEntryAssembly();
                                var infoVersion = appAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                                var appVersion = !string.IsNullOrEmpty(infoVersion)
                                    ? infoVersion.Split(['-', '+'])[0]  // "0.2.26-alpha.0.2+hash" → "0.2.26"
                                    : appAssembly?.GetName().Version?.ToString(3) ?? "0.0.0";

                                var sessionData = new Dictionary<string, object>
                                {
                                    ["os_version"] = Environment.OSVersion.VersionString,
                                    ["runtime_version"] = Environment.Version.ToString(),
                                    ["app_version"] = appVersion
                                };
                                _usageAnalyticsService.TrackEvent("session_start", sessionData);
                                _logger?.LogDebug("[Issue #307] session_start イベント記録完了: app_version={AppVersion}", appVersion);
                            }
                        }
                        catch (Exception analyticsEx)
                        {
                            _logger?.LogWarning(analyticsEx, "[Issue #269] session_start イベント記録失敗（継続）");
                        }

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
                // この時点では追加の初期化は不要。診断システムは別途処理。

                // Issue #125: 広告機能は廃止（AdWindow削除済み）

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

                // 📊 [Issue #269] AnalyticsEventProcessor登録 - 翻訳イベントの使用統計記録
                // [Issue #297] 名前空間修正: Core.Events.TranslationEvents → Core.Translation.Events
                // [Issue #307] 両方の名前空間のTranslationCompletedEventに対応
                try
                {
                    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();

                    // Core.Translation.Events.TranslationCompletedEvent 購読（StandardTranslationPipeline用）
                    var analyticsProcessor1 = serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Translation.Events.TranslationCompletedEvent>>();
                    eventAggregator.Subscribe<Baketa.Core.Translation.Events.TranslationCompletedEvent>(analyticsProcessor1);

                    // Core.Events.EventTypes.TranslationCompletedEvent 購読（TranslationPipelineService用）
                    var analyticsProcessor2 = serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.EventTypes.TranslationCompletedEvent>>();
                    eventAggregator.Subscribe<Baketa.Core.Events.EventTypes.TranslationCompletedEvent>(analyticsProcessor2);

                    Console.WriteLine("✅ AnalyticsEventProcessor登録完了（両イベントタイプ対応）");
                }
                catch (Exception analyticsEx)
                {
                    _logger?.LogWarning(analyticsEx, "[Issue #307] AnalyticsEventProcessor登録失敗（継続）");
                }

                // 🔧 [Issue #300] OcrRecoveryEventProcessor登録 - OCRサーバー復旧時のユーザー通知
                try
                {
                    var eventAggregator = serviceProvider.GetRequiredService<IEventAggregator>();
                    var ocrRecoveryProcessor = serviceProvider.GetRequiredService<IEventProcessor<Baketa.Core.Events.OcrServerRecoveryEvent>>();
                    eventAggregator.Subscribe<Baketa.Core.Events.OcrServerRecoveryEvent>(ocrRecoveryProcessor);
                    Console.WriteLine("✅ OcrRecoveryEventProcessor登録完了");
                }
                catch (Exception ocrRecoveryEx)
                {
                    _logger?.LogWarning(ocrRecoveryEx, "[Issue #300] OcrRecoveryEventProcessor登録失敗（継続）");
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
    /// テストモード警告表示
    /// License.EnableMockMode が有効な場合に警告を表示
    /// </summary>
    private async Task ShowTestModeWarningIfNeededAsync(IServiceProvider serviceProvider, Avalonia.Controls.Window parentWindow)
    {
        try
        {
            var licenseSettings = serviceProvider.GetService<IOptions<LicenseSettings>>()?.Value;

            // ライセンスモックモードが有効かチェック
            bool isLicenseMockEnabled = licenseSettings?.EnableMockMode ?? false;

            if (!isLicenseMockEnabled)
            {
                // テストモードではない
                return;
            }

            _logger?.LogWarning(
                "🧪 テストモード設定検出: License.EnableMockMode={LicenseMock}",
                isLicenseMockEnabled);

            // コンソールに警告出力（開発者向け）
            Console.WriteLine("⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️");
            Console.WriteLine("🧪 [TEST_MODE] ライセンステストモードが有効です");
            Console.WriteLine($"   License.EnableMockMode = {isLicenseMockEnabled}");
            Console.WriteLine("   本番環境では appsettings.json の EnableMockMode を false に設定してください");
            Console.WriteLine("⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️⚠️");

            // INotificationServiceを使用して警告を表示
            var notificationService = serviceProvider.GetService<INotificationService>();
            if (notificationService != null)
            {
                await notificationService.ShowWarningAsync(
                    "🧪 テストモード有効",
                    "ライセンスモックモードが有効です。本番環境では appsettings.json の License.EnableMockMode を false に設定してください。",
                    duration: 10000);
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
    /// [Issue #249] 自動アップデートサービスの初期化
    /// アプリ起動時にサイレントで更新チェックを実行
    /// </summary>
    private async Task InitializeUpdateServiceAsync(IServiceProvider serviceProvider)
    {
        try
        {
            _logger?.LogInformation("[Issue #249] UpdateService初期化開始...");

            // UpdateServiceをDI経由ではなく直接作成（現時点ではシンプルな実装）
            var pythonServerManager = serviceProvider.GetService<IPythonServerManager>();
            var updateLogger = serviceProvider.GetService<ILogger<UpdateService>>();

            _updateService = new UpdateService(pythonServerManager, updateLogger);
            _updateService.Initialize();

            // バックグラウンドでサイレント更新チェック
            _ = Task.Run(async () =>
            {
                try
                {
                    // 起動直後の負荷を避けるため少し待機
                    await Task.Delay(5000).ConfigureAwait(false);
                    await _updateService.CheckForUpdatesInBackgroundAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[Issue #249] サイレント更新チェック失敗（継続）");
                }
            });

            _logger?.LogInformation("[Issue #249] UpdateService初期化完了");
        }
        catch (Exception ex)
        {
            // 更新チェック失敗はアプリ起動をブロックしない
            _logger?.LogWarning(ex, "[Issue #249] UpdateService初期化失敗（継続）");
        }
    }

    /// <summary>
    /// [Issue #252 Phase 4] クラッシュレポート設定を取得
    /// 設定ファイルから直接読み込み（DIコンテナに依存しない）
    /// </summary>
    private static CrashReportSettings GetCrashReportSettings()
    {
        try
        {
            // [Issue #252] BaketaSettingsPaths を使用してパスを一元管理
            var settingsFilePath = BaketaSettingsPaths.MainSettingsPath;

            if (File.Exists(settingsFilePath))
            {
                var json = File.ReadAllText(settingsFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("CrashReport", out var crashReportElement))
                {
                    var settings = new CrashReportSettings();

                    if (crashReportElement.TryGetProperty("AutoSendCrashReports", out var autoSendElement))
                    {
                        settings.AutoSendCrashReports = autoSendElement.GetBoolean();
                    }

                    if (crashReportElement.TryGetProperty("IncludeSystemInfo", out var systemInfoElement))
                    {
                        settings.IncludeSystemInfo = systemInfoElement.GetBoolean();
                    }

                    if (crashReportElement.TryGetProperty("IncludeLogs", out var logsElement))
                    {
                        settings.IncludeLogs = logsElement.GetBoolean();
                    }

                    return settings;
                }
            }
        }
        catch
        {
            // 設定読み込みエラー時はデフォルト設定を使用
        }

        // デフォルト設定を返す
        return new CrashReportSettings();
    }

    /// <summary>
    /// [Issue #252] クラッシュレポート検出・ダイアログ表示
    /// .crash_pendingフラグが存在する場合、設定に応じて自動送信またはダイアログを表示
    /// </summary>
    private async Task CheckAndShowCrashReportDialogAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var crashReportService = serviceProvider.GetService<Core.Abstractions.CrashReporting.ICrashReportService>();
            if (crashReportService == null)
            {
                return;
            }

            // .crash_pendingフラグの存在チェック
            if (!crashReportService.HasPendingCrashReport())
            {
                return;
            }

            _logger?.LogInformation("[Issue #252] 前回クラッシュを検出");

            // 未送信のクラッシュレポートを取得
            var crashReports = await crashReportService.GetPendingCrashReportsAsync().ConfigureAwait(true);
            if (crashReports.Count == 0)
            {
                await crashReportService.ClearCrashPendingFlagAsync().ConfigureAwait(true);
                return;
            }

            // [Phase 4] 自動送信設定を確認
            var crashReportSettings = GetCrashReportSettings();

            if (crashReportSettings.AutoSendCrashReports)
            {
                // 自動送信モード：ダイアログなしで送信
                _logger?.LogInformation("[Issue #252] 自動送信モード - ダイアログなしでクラッシュレポートを送信");

                await SendCrashReportsAsync(
                    crashReportService,
                    crashReports,
                    crashReportSettings.IncludeSystemInfo,
                    crashReportSettings.IncludeLogs).ConfigureAwait(false);
            }
            else
            {
                // ダイアログ表示モード：ユーザーに確認
                _logger?.LogInformation("[Issue #252] ダイアログを表示してユーザーに確認");

                var viewModel = new ViewModels.CrashReportDialogViewModel(crashReports);
                var dialog = new Views.CrashReportDialogWindow(viewModel);

                // ダイアログアイコンを設定
                try
                {
                    var iconUri = new Uri(BAKETA_ICON_PATH);
                    dialog.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(iconUri));
                }
                catch (Exception iconEx)
                {
                    Console.WriteLine($"⚠️ クラッシュダイアログアイコン設定失敗: {iconEx.Message}");
                }

                // クラッシュダイアログは起動時に表示されるため、親ウィンドウは存在しない
                // ShowDialogの代わりにShowを使用し、ユーザー操作完了をイベントで待機
                dialog.Show();
                var tcs = new System.Threading.Tasks.TaskCompletionSource<ViewModels.CrashReportDialogResult>();
                dialog.Closed += (_, _) => tcs.TrySetResult(viewModel.Result);
                var result = await tcs.Task.ConfigureAwait(true);

                if (result == ViewModels.CrashReportDialogResult.Send)
                {
                    _logger?.LogInformation("[Issue #252] ユーザーがクラッシュレポート送信を選択");

                    await SendCrashReportsAsync(
                        crashReportService,
                        crashReports,
                        viewModel.IncludeSystemInfo,
                        viewModel.IncludeLogs).ConfigureAwait(false);
                }
                else
                {
                    _logger?.LogInformation("[Issue #252] ユーザーがクラッシュレポート送信をスキップ");

                    // 送信しない場合もレポートを削除（次回表示されないように）
                    foreach (var summary in crashReports)
                    {
                        await crashReportService.DeleteCrashReportAsync(summary.ReportId).ConfigureAwait(false);
                    }
                }
            }

            // フラグをクリア
            await crashReportService.ClearCrashPendingFlagAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // クラッシュレポート処理の失敗はアプリ起動をブロックしない
            _logger?.LogWarning(ex, "[Issue #252] クラッシュレポート処理中にエラー（継続）");
        }
    }

    /// <summary>
    /// [Issue #261] プライバシーポリシー同意確認・ダイアログ表示
    /// 初回起動時にプライバシーポリシーへの同意を確認し、同意しない場合はアプリを終了
    /// [Issue #277] 認証済みの場合は先にサーバーから同期してから判定
    /// </summary>
    /// <returns>同意された場合はtrue、拒否された場合はfalse（アプリ終了が必要）</returns>
    private async Task<bool> CheckAndShowConsentDialogAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var consentService = serviceProvider.GetService<Baketa.Core.Abstractions.Settings.IConsentService>();
            if (consentService == null)
            {
                _logger?.LogWarning("[Issue #261] IConsentServiceが登録されていません");
                return true; // サービスがない場合は続行
            }

            var localizationService = serviceProvider.GetService<Services.ILocalizationService>();
            if (localizationService == null)
            {
                _logger?.LogWarning("[Issue #261] ILocalizationServiceが登録されていません");
                return true; // サービスがない場合は続行
            }

            // [Issue #277] 認証済みの場合、サーバーから同期してからローカル状態をチェック
            // AuthInitializationService.StartAsync()はIHostedServiceとして非同期で実行されるため、
            // ここで明示的に同期を待つ必要がある
            await TrySyncConsentFromServerAsync(serviceProvider, consentService).ConfigureAwait(true);

            // [Gemini Review] 非同期化: UIスレッドブロック回避
            // 初回起動時の同意が必要か確認
            var needsConsent = await consentService.NeedsInitialConsentAsync().ConfigureAwait(true);
            if (!needsConsent)
            {
                _logger?.LogDebug("[Issue #261] プライバシーポリシー同意済み、スキップ");
                return true;
            }

            _logger?.LogInformation("[Issue #261] プライバシーポリシー同意が必要、ダイアログを表示");

            // [Gemini Review] 非同期ファクトリメソッドでViewModelを初期化
            var viewModel = await ViewModels.ConsentDialogViewModel.CreateAsync(
                consentService,
                localizationService,
                ViewModels.ConsentDialogMode.InitialLaunch).ConfigureAwait(true);
            var dialog = new Views.ConsentDialogWindow(viewModel);

            // ダイアログアイコンを設定
            try
            {
                var iconUri = new Uri(BAKETA_ICON_PATH);
                dialog.Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(iconUri));
            }
            catch (Exception iconEx)
            {
                Console.WriteLine($"⚠️ 同意ダイアログアイコン設定失敗: {iconEx.Message}");
            }

            // ダイアログを表示
            dialog.Show();
            var tcs = new TaskCompletionSource<ViewModels.ConsentDialogResult>();
            dialog.Closed += (_, _) => tcs.TrySetResult(viewModel.Result);
            var result = await tcs.Task.ConfigureAwait(true);

            if (result == ViewModels.ConsentDialogResult.Accepted)
            {
                _logger?.LogInformation("[Issue #261] ユーザーが利用規約・プライバシーポリシーに同意");

                // 両方の同意を記録
                await consentService.AcceptAllAsync().ConfigureAwait(true);

                // [Issue #277] 認証済みの場合、同意をサーバーに即時同期
                // これによりPC移行時にDBから同意状態を復元可能になる
                await TrySyncConsentToServerAsync(serviceProvider, consentService).ConfigureAwait(true);

                return true;
            }
            else
            {
                _logger?.LogInformation("[Issue #261] ユーザーが利用規約に同意しなかった、アプリを終了");
                return false;
            }
        }
        catch (Exception ex)
        {
            // 同意確認処理の失敗はアプリ起動をブロックしない（セキュリティ考慮で要検討）
            _logger?.LogWarning(ex, "[Issue #261] 同意確認処理中にエラー（継続）");
            return true;
        }
    }

    /// <summary>
    /// [Issue #277] 認証済みの場合、サーバーから同意状態を同期
    /// ローカル設定ファイル削除時にDBから復元するために必要
    /// </summary>
    private async Task TrySyncConsentFromServerAsync(
        IServiceProvider serviceProvider,
        Baketa.Core.Abstractions.Settings.IConsentService consentService)
    {
        try
        {
            var tokenStorage = serviceProvider.GetService<Baketa.Core.Abstractions.Auth.ITokenStorage>();
            var authService = serviceProvider.GetService<Baketa.Core.Abstractions.Auth.IAuthService>();

            if (tokenStorage == null || authService == null)
            {
                _logger?.LogDebug("[Issue #277] Auth services not available, skipping server sync");
                return;
            }

            // [Issue #299] 既に起動時にRestoreSessionAsyncが呼ばれているため、
            // ここでは現在のセッションを取得するだけでよい（重複呼び出し防止）
            var session = await authService.GetCurrentSessionAsync().ConfigureAwait(true);

            if (session?.AccessToken == null)
            {
                _logger?.LogDebug("[Issue #277] No valid session, skipping server sync");
                return;
            }

            // サーバーから同期
            _logger?.LogInformation("[Issue #277] 認証済み - サーバーから同意状態を同期中...");
            var syncResult = await consentService.SyncFromServerAsync(session.AccessToken).ConfigureAwait(true);
            _logger?.LogInformation("[Issue #277] 同意状態の同期完了: {Result}", syncResult);
        }
        catch (Exception ex)
        {
            // 同期失敗はアプリ起動をブロックしない
            _logger?.LogWarning(ex, "[Issue #277] サーバーからの同意状態同期に失敗（ローカル設定を使用）");
        }
    }

    /// <summary>
    /// [Issue #277] 認証済みの場合、ローカルの同意状態をサーバーに同期
    /// 同意ダイアログで承認後に呼び出され、DBに同意を記録する
    /// </summary>
    private async Task TrySyncConsentToServerAsync(
        IServiceProvider serviceProvider,
        Baketa.Core.Abstractions.Settings.IConsentService consentService)
    {
        try
        {
            var tokenStorage = serviceProvider.GetService<Baketa.Core.Abstractions.Auth.ITokenStorage>();
            var authService = serviceProvider.GetService<Baketa.Core.Abstractions.Auth.IAuthService>();

            if (tokenStorage == null || authService == null)
            {
                _logger?.LogDebug("[Issue #277] Auth services not available, skipping server sync");
                return;
            }

            // 保存されたトークンがあるか確認
            var hasTokens = await tokenStorage.HasStoredTokensAsync().ConfigureAwait(true);
            if (!hasTokens)
            {
                _logger?.LogDebug("[Issue #277] No stored tokens, skipping server sync (consent saved locally only)");
                return;
            }

            // セッションを取得
            var session = await authService.GetCurrentSessionAsync().ConfigureAwait(true);

            if (session?.AccessToken == null || session.User?.Id == null)
            {
                _logger?.LogDebug("[Issue #277] No valid session, skipping server sync (consent saved locally only)");
                return;
            }

            // ローカル同意をサーバーに同期
            _logger?.LogInformation("[Issue #277] 認証済み - 同意状態をサーバーに同期中...");
            await consentService.SyncLocalConsentToServerAsync(session.User.Id, session.AccessToken).ConfigureAwait(true);
            _logger?.LogInformation("[Issue #277] 同意状態のサーバー同期完了");
        }
        catch (Exception ex)
        {
            // 同期失敗はアプリ起動をブロックしない（ローカルには保存済み）
            _logger?.LogWarning(ex, "[Issue #277] サーバーへの同意状態同期に失敗（ローカル設定は保存済み）");
        }
    }

    /// <summary>
    /// [Issue #252 Phase 4] クラッシュレポートを送信
    /// 自動送信・ダイアログ送信の共通処理
    /// </summary>
    private async Task SendCrashReportsAsync(
        Core.Abstractions.CrashReporting.ICrashReportService crashReportService,
        System.Collections.Generic.IReadOnlyList<Core.Abstractions.CrashReporting.CrashReportSummary> crashReports,
        bool includeSystemInfo,
        bool includeLogs)
    {
        var sentCount = 0;
        var failedCount = 0;

        // [Issue #252] レート制限対策: 最新5件のみ送信、古いものは削除のみ
        const int maxSendCount = 5;
        var reportsToSend = crashReports.Take(maxSendCount).ToList();
        var reportsToDeleteOnly = crashReports.Skip(maxSendCount).ToList();

        // 古いレポートは送信せず削除のみ
        foreach (var summary in reportsToDeleteOnly)
        {
            await crashReportService.DeleteCrashReportAsync(summary.ReportId).ConfigureAwait(false);
        }

        foreach (var summary in reportsToSend)
        {
            // レポート詳細を読み込み
            var fullReport = await crashReportService.LoadCrashReportAsync(summary.ReportId).ConfigureAwait(false);
            if (fullReport == null)
            {
                continue;
            }

            // サーバーに送信
            var success = await crashReportService.SendCrashReportAsync(
                fullReport,
                includeSystemInfo,
                includeLogs).ConfigureAwait(false);

            if (success)
            {
                // 送信成功したレポートは削除
                await crashReportService.DeleteCrashReportAsync(summary.ReportId).ConfigureAwait(false);
                sentCount++;
            }
            else
            {
                failedCount++;
            }

            // [Issue #252] レート制限回避のため送信間に遅延（10件/分制限 → 7秒間隔）
            await Task.Delay(7000).ConfigureAwait(false);
        }

        _logger?.LogInformation("[Issue #252] クラッシュレポート送信完了: 成功={SentCount}, 失敗={FailedCount}", sentCount, failedCount);
    }

    /// <summary>
    /// [Issue #275] 起動時のトークン使用量同期
    /// TokenUsageRepositoryから実際の使用量を読み込み、LicenseManagerに同期する
    /// これにより、設定画面を最初に開いた時から正しい値が表示される
    /// [Issue #298] サーバーから既に同期済みの場合はローカルファイルで上書きしない
    /// </summary>
    private async Task SyncTokenUsageAtStartupAsync(IServiceProvider serviceProvider)
    {
        try
        {
            var tokenTracker = serviceProvider.GetService<Core.Translation.Abstractions.ITokenConsumptionTracker>();
            var licenseManager = serviceProvider.GetService<Core.Abstractions.License.ILicenseManager>();

            if (tokenTracker == null || licenseManager == null)
            {
                _logger?.LogDebug("[Issue #275] トークン同期スキップ: サービスが利用不可");
                return;
            }

            // [Issue #298] サーバーから既にトークン使用量が同期されている場合はスキップ
            // サーバーの値（token_usage DB）が正しい値であり、ローカルファイルは
            // 前ユーザーのデータが残っている可能性がある
            var serverTokensUsed = licenseManager.CurrentState.CloudAiTokensUsed;
            if (serverTokensUsed > 0)
            {
                _logger?.LogDebug("[Issue #298] サーバー同期済みのためローカル同期スキップ: ServerTokens={ServerTokens}", serverTokensUsed);
                Console.WriteLine($"✅ [Issue #298] サーバー同期済み({serverTokensUsed})、ローカル同期スキップ");
                return;
            }

            var usage = await tokenTracker.GetMonthlyUsageAsync().ConfigureAwait(false);

            if (usage.TotalTokensUsed > 0)
            {
                licenseManager.SyncTokenUsage(usage.TotalTokensUsed);
                _logger?.LogDebug("[Issue #275] 起動時トークン同期完了: {TokensUsed}", usage.TotalTokensUsed);
                Console.WriteLine($"✅ [Issue #275] 起動時トークン同期完了: {usage.TotalTokensUsed}");
            }
        }
        catch (Exception ex)
        {
            // トークン同期の失敗はアプリ起動をブロックしない
            _logger?.LogWarning(ex, "[Issue #275] 起動時トークン同期失敗（継続）");
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

            // [Issue #269] 使用統計 session_end イベント記録とフラッシュ
            // [Gemini Review] UIスレッドデッドロック回避のため Task.Run で実行
            try
            {
                if (_usageAnalyticsService?.IsEnabled == true)
                {
                    _usageAnalyticsService.TrackEvent("session_end");
                    // バックグラウンドスレッドで同期待機（UIスレッドブロック回避）
                    Task.Run(async () =>
                    {
                        try
                        {
                            await _usageAnalyticsService.FlushAsync().ConfigureAwait(false);
                        }
                        catch (Exception flushEx)
                        {
                            _logger?.LogWarning(flushEx, "[Issue #269] FlushAsync失敗（継続）");
                        }
                    }).Wait(TimeSpan.FromSeconds(5));  // 最大5秒待機
                    _logger?.LogDebug("[Issue #269] session_end イベント記録・フラッシュ完了");
                }
            }
            catch (Exception analyticsEx)
            {
                _logger?.LogWarning(analyticsEx, "[Issue #269] session_end 処理失敗（継続）");
            }

            // [Issue #249] UpdateServiceの破棄
            try
            {
                _updateService?.Dispose();
                _updateService = null;
                Console.WriteLine("✅ [SHUTDOWN_DEBUG] UpdateService破棄完了");
            }
            catch (Exception updateEx)
            {
                Console.WriteLine($"⚠️ [SHUTDOWN_DEBUG] UpdateService破棄失敗: {updateEx.Message}");
            }

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

    /// <summary>
    /// IDisposable実装 - CA1001対応
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _updateService?.Dispose();
        _updateService = null;

        _disposed = true;
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

// [Issue #252] ReactiveUIExceptionHandlerはGlobalExceptionHandlerに統合されました
// 詳細: Baketa.UI/Services/GlobalExceptionHandler.cs の OnReactiveUIException メソッド
