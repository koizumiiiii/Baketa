using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using Avalonia;
using Avalonia.ReactiveUI;
// TODO: WebView統合完了後に有効化
// using Avalonia.WebView.Desktop; // 📢 Issue #174: WebView統合
using Baketa.Application.DI.Modules;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.Core.Performance;
using Baketa.Core.Settings;
using Baketa.Core.Utilities;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Platform.DI;
using Baketa.UI.DI.Modules;
using Baketa.UI.DI.Services;
using Baketa.UI.Extensions;
using Baketa.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ReactiveUI;

namespace Baketa.UI;

internal sealed class Program
{
    /// <summary>
    /// DIコンテナとサービスプロバイダー
    /// UltraPhase 4.3 FIX: volatile修飾子追加でマルチスレッド可視性問題解決
    /// </summary>
    public static volatile ServiceProvider? ServiceProvider;

    /// <summary>
    /// EventHandler初期化完了フラグ（UI安全性向上）
    /// </summary>
    public static bool IsEventHandlerInitialized { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static async Task Main(string[] args)
    {
        // 🚧 Single Instance Application Check - 重複翻訳表示問題根本解決
        const string mutexName = "Global\\BaketaTranslationOverlayApp_SingleInstance_v3";
        const string lockFileName = "baketa_instance.lock";
        var lockFilePath = Path.Combine(Path.GetTempPath(), lockFileName);

        System.Threading.Mutex mutex = null!;
        FileStream lockFile = null!;
        bool isOwnerOfMutex = false;
        bool isOwnerOfFileLock = false;

        try
        {
            // ステップ1: ファイルベースロック試行（即座に失敗）
            Console.WriteLine("🔍 [STEP1] Attempting file-based lock...");
            try
            {
                lockFile = new FileStream(lockFilePath, FileMode.Create, FileAccess.Write, FileShare.None);
                var processInfo = $"PID={Environment.ProcessId}|TIME={DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}|USER={Environment.UserName}";
                var processInfoBytes = System.Text.Encoding.UTF8.GetBytes(processInfo);
                lockFile.Write(processInfoBytes, 0, processInfoBytes.Length);
                lockFile.Flush();
                isOwnerOfFileLock = true;
                Console.WriteLine($"✅ [STEP1] File lock acquired: {lockFilePath}");
            }
            catch (IOException)
            {
                Console.WriteLine("⚠️ [STEP1] File lock failed - another instance is running");
                Console.WriteLine("🎯 This prevents duplicate translation overlay displays.");
                Console.WriteLine("🔄 Exiting duplicate instance immediately.");
                return;
            }
            catch (Exception fileEx)
            {
                Console.WriteLine($"⚠️ [STEP1] File lock error: {fileEx.Message}");
                // ファイルロックが失敗してもMutexロックを試行
            }

            // ステップ2: Mutexロック試行（2秒タイムアウト）
            Console.WriteLine("🔍 [STEP2] Attempting mutex lock...");
            try
            {
                mutex = new System.Threading.Mutex(false, mutexName);

                // 2秒以内にMutexが取得できない場合は他のインスタンスが実行中
                bool mutexAcquired = mutex.WaitOne(2000, false);
                if (mutexAcquired)
                {
                    isOwnerOfMutex = true;
                    Console.WriteLine("✅ [STEP2] Mutex lock acquired");
                }
                else
                {
                    Console.WriteLine("⚠️ [STEP2] Mutex timeout - another instance holding the lock");
                    if (isOwnerOfFileLock)
                    {
                        // ファイルロックは取得できたがMutexが取得できない = 異常状態
                        Console.WriteLine("🔄 [STEP2] File lock acquired but mutex failed - proceeding cautiously");
                    }
                    else
                    {
                        Console.WriteLine("🔄 Exiting duplicate instance immediately.");
                        return;
                    }
                }
            }
            catch (AbandonedMutexException)
            {
                // 前のインスタンスが異常終了した場合、Mutexを引き継ぎ
                Console.WriteLine("🔄 [STEP2] Previous instance terminated abnormally. Taking over mutex.");
                isOwnerOfMutex = true;
            }

            // ステップ3: プロセス検証
            Console.WriteLine("🔍 [STEP3] Process verification...");
            var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
            var allBaketaProcesses = System.Diagnostics.Process.GetProcessesByName("Baketa.UI");
            var allDotnetProcesses = System.Diagnostics.Process.GetProcessesByName("dotnet");

            Console.WriteLine($"🔍 Current process: PID {currentProcess.Id}, Name: {currentProcess.ProcessName}");
            Console.WriteLine($"🔍 Baketa.UI processes: {allBaketaProcesses.Length}");
            Console.WriteLine($"🔍 Dotnet processes: {allDotnetProcesses.Length}");

            // 複数のBaketa.UIプロセスが検出された場合の警告
            if (allBaketaProcesses.Length > 1)
            {
                var otherProcesses = allBaketaProcesses.Where(p => p.Id != currentProcess.Id);
                foreach (var proc in otherProcesses)
                {
                    Console.WriteLine($"⚠️ Other Baketa.UI process detected: PID {proc.Id}");
                }
            }

            // 両方のロックが成功した場合のみ継続
            if (isOwnerOfFileLock || isOwnerOfMutex)
            {
                Console.WriteLine("✅ Single instance check passed. Starting Baketa...");
            }
            else
            {
                Console.WriteLine("🚨 Failed to acquire any locks - aborting to prevent conflicts");
                return;
            }

            // 🎯 UltraThink修正: ProcessExitでのMutex解放は同期問題を引き起こすため削除
            // プロセス終了時のファイルロック解放のみ実行（Mutexは.NET GCが自動解放）
            AppDomain.CurrentDomain.ProcessExit += (sender, e) =>
            {
                Console.WriteLine("🔄 Baketa process terminating - releasing file locks.");
                try
                {
                    // ✅ ファイルロックのみ解放（同期問題なし）
                    if (isOwnerOfFileLock && lockFile != null)
                    {
                        lockFile.Close();
                        lockFile.Dispose();
                        File.Delete(lockFilePath);
                        Console.WriteLine("✅ File lock released successfully");
                    }
                    // 🚫 Mutex解放はメインスレッドでのみ実行（ProcessExitは別スレッドのため削除）
                }
                catch (Exception releaseEx)
                {
                    Console.WriteLine($"⚠️ Lock release error: {releaseEx.Message}");
                }
            };

            // Console.CancelKeyPress時の適切な終了処理
            Console.CancelKeyPress += (sender, e) =>
            {
                Console.WriteLine("🔄 Ctrl+C detected - gracefully terminating...");
                e.Cancel = false; // プロセス終了を許可
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 Single instance check FAILED: {ex.Message}");
            Console.WriteLine("🚨 Multiple instances may cause duplicate overlay displays!");
            Console.WriteLine("🛑 ABORTING startup to prevent system conflicts.");

            // クリーンアップ
            try
            {
                mutex?.Dispose();
                lockFile?.Close();
                lockFile?.Dispose();
                if (isOwnerOfFileLock && File.Exists(lockFilePath))
                {
                    File.Delete(lockFilePath);
                }
            }
            catch { /* クリーンアップエラーは無視 */ }

            return; // 例外発生時は起動を中止
        }

        // Single instance確保後、通常の初期化を続行
        // 🔧 [CRITICAL_ENCODING_FIX] Windows環境でUTF-8コンソール出力を強制設定
        try
        {
            // BOMなしUTF-8エンコーディングを使用してエンコーディング警告を回避
            var utf8NoBom = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            Console.OutputEncoding = utf8NoBom;
            Console.InputEncoding = utf8NoBom;

            // Windows環境でのUTF-8モード有効化（グローバル設定）
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "false");
            Environment.SetEnvironmentVariable("DOTNET_SYSTEM_TEXT_ENCODING_USEUTF8", "true");

            // コンソールコードページを65001 (UTF-8) に設定
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                try { Console.OutputEncoding = System.Text.Encoding.GetEncoding(65001); }
                catch { /* コードページ設定失敗は無視 */ }
            }

            Console.WriteLine("🔧 [ENCODING_INIT] UTF-8 console encoding configured successfully (BOM-less)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ [ENCODING_INIT] Failed to configure UTF-8 console: {ex.Message}");
        }

        // 統一パフォーマンス測定システムを初期化
        PerformanceLogger.Initialize();
        PerformanceLogger.LogSystemInfo();

        using var appStartMeasurement = new PerformanceMeasurement(
            MeasurementType.OverallProcessing, "アプリケーション起動全体");

        PerformanceLogger.LogPerformance("🚀 Baketa.UI.exe 起動開始");

        // 重要な初期化タイミングをログ
        appStartMeasurement.LogCheckpoint("統一ログシステム初期化完了");

        // 未処理例外の強制ログ出力
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            Console.WriteLine($"💥 FATAL: 未処理例外: {e.ExceptionObject}");
            System.Diagnostics.Debug.WriteLine($"💥 FATAL: 未処理例外: {e.ExceptionObject}");
            if (e.ExceptionObject is Exception ex)
            {
                Console.WriteLine($"💥 FATAL: Exception Type: {ex.GetType().Name}");
                Console.WriteLine($"💥 FATAL: Message: {ex.Message}");
                Console.WriteLine($"💥 FATAL: StackTrace: {ex.StackTrace}");
                System.Diagnostics.Debug.WriteLine($"💥 FATAL: Exception Type: {ex.GetType().Name}");
                System.Diagnostics.Debug.WriteLine($"💥 FATAL: Message: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"💥 FATAL: StackTrace: {ex.StackTrace}");

                // ファイルにも記録
                try
                {
                    var crashLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash_log.txt");
                    File.AppendAllText(crashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 FATAL: {ex.GetType().Name}: {ex.Message}\n");
                    File.AppendAllText(crashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 StackTrace: {ex.StackTrace}\n");
                    File.AppendAllText(crashLogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} 💥 IsTerminating: {e.IsTerminating}\n");
                    Console.WriteLine($"📝 クラッシュログ作成: {crashLogPath}");
                }
                catch { /* ファイル出力失敗は無視 */ }
            }
        };

        try
        {
            Console.WriteLine("🔧 DIコンテナの初期化開始");
            System.Diagnostics.Debug.WriteLine("🔧 DIコンテナの初期化開始");

            // DIコンテナの初期化
            await ConfigureServices();

            // 🩺 診断システム直接初期化 - OnFrameworkInitializationCompleted代替
            Console.WriteLine("🚨🚨🚨 [MAIN_DIAGNOSTIC] 診断システム直接初期化開始！ 🚨🚨🚨");
            InitializeDiagnosticSystemDirectly();
            Console.WriteLine("🚨🚨🚨 [MAIN_DIAGNOSTIC] 診断システム直接初期化完了！ 🚨🚨🚨");

            // OCRエンジン事前初期化（バックグラウンド）
            Console.WriteLine("🚀 OCRエンジン事前初期化開始（バックグラウンド）");
            System.Diagnostics.Debug.WriteLine("🚀 OCRエンジン事前初期化開始（バックグラウンド）");
            _ = Task.Run(PreInitializeOcrEngineAsync);

            // Phase4: 統合GPU最適化システム初期化
            Console.WriteLine("🎯 Phase4: 統合GPU最適化システム初期化開始");
            _ = Task.Run(InitializeUnifiedGpuSystemAsync);

            // OPUS-MT削除済み: NLLB-200統一により事前ウォームアップサービス不要

            appStartMeasurement.LogCheckpoint("Avalonia アプリケーション開始準備完了");
            PerformanceLogger.LogPerformance("🎯 Avalonia アプリケーション開始");

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

            // アプリケーション終了時の最終サマリー
            var startupResult = appStartMeasurement.Complete();
            PerformanceLogger.LogPerformance($"✅ アプリケーション起動完了 - 総時間: {startupResult.Duration.TotalSeconds:F2}秒");
            PerformanceLogger.FinalizeSession();
        }
        catch (Exception ex)
        {
            PerformanceLogger.LogPerformance($"💥 MAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            PerformanceLogger.FinalizeSession();

            Console.WriteLine($"💥 MAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"💥 MAIN STACK: {ex.StackTrace}");
            System.Diagnostics.Debug.WriteLine($"💥 MAIN EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"💥 MAIN STACK: {ex.StackTrace}");
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .UseReactiveUI();
            // TODO: WebView統合完了後に有効化
            // .UseDesktopWebView(); // 📢 Issue #174: WebView統合

    /// <summary>
    /// EventHandlerInitializationServiceを即座に実行（競合状態根本解決）
    /// </summary>
    private static void InitializeEventHandlersImmediately()
    {
        try
        {
            Console.WriteLine("🔥🔥🔥 [IMMEDIATE] EventHandlerInitializationService取得・実行開始 🔥🔥🔥");

            var eventHandlerInitService = ServiceProvider?.GetRequiredService<Baketa.Application.Services.Events.EventHandlerInitializationService>();
            if (eventHandlerInitService == null)
            {
                Console.WriteLine("🚨 [IMMEDIATE_ERROR] EventHandlerInitializationServiceが見つかりません！");
                return;
            }

            Console.WriteLine("✅ [IMMEDIATE] EventHandlerInitializationService取得成功 - 同期初期化実行");
            Console.WriteLine($"🔍 [IMMEDIATE_DEBUG] Service Type: {eventHandlerInitService.GetType().FullName}");
            Console.WriteLine($"🔍 [IMMEDIATE_DEBUG] Service Hash: {eventHandlerInitService.GetHashCode()}");
            Console.WriteLine($"🔍 [IMMEDIATE_DEBUG] Service Instance: {eventHandlerInitService}");

            // 🚨 強制詳細ログ出力を追加
            Console.WriteLine("🔍 [DEBUG_FORCE] InitializeAsync実行前");

            try
            {
                Console.WriteLine("🔍 [DEBUG_FORCE] InitializeAsync()呼び出し開始");
                var initTask = eventHandlerInitService.InitializeAsync();
                Console.WriteLine($"🔍 [DEBUG_FORCE] initTask作成完了: {initTask.GetType().FullName}");
                Console.WriteLine($"🔍 [DEBUG_FORCE] initTask.Status: {initTask.Status}");
                Console.WriteLine("🔍 [DEBUG_FORCE] initTask.Wait()実行前");
                initTask.Wait();
                Console.WriteLine("🔍 [DEBUG_FORCE] initTask.Wait()実行後");
                Console.WriteLine($"🔍 [DEBUG_FORCE] 完了後initTask.Status: {initTask.Status}");

                if (initTask.IsCompletedSuccessfully)
                {
                    Console.WriteLine("✅ [IMMEDIATE] EventHandlerInitializationService同期初期化成功！");
                }
                else if (initTask.IsFaulted)
                {
                    Console.WriteLine($"🚨 [IMMEDIATE] EventHandlerInitializationService失敗: {initTask.Exception?.Flatten().Message}");
                    throw (Exception)(initTask.Exception?.Flatten()!) ?? new InvalidOperationException("InitializeAsync failed");
                }
                else
                {
                    Console.WriteLine("⚠️ [IMMEDIATE] EventHandlerInitializationService未完了状態");
                }
            }
            catch (AggregateException aggEx)
            {
                Console.WriteLine($"🚨 [IMMEDIATE_AGGREGATE] AggregateException: {aggEx.Flatten().Message}");
                foreach (var innerEx in aggEx.Flatten().InnerExceptions)
                {
                    Console.WriteLine($"🚨 [IMMEDIATE_INNER] InnerException: {innerEx.GetType().Name}: {innerEx.Message}");
                }
                throw;
            }
            catch (Exception directEx)
            {
                Console.WriteLine($"🚨 [IMMEDIATE_DIRECT] DirectException: {directEx.GetType().Name}: {directEx.Message}");
                Console.WriteLine($"🚨 [IMMEDIATE_STACK] StackTrace: {directEx.StackTrace}");
                throw;
            }

            Console.WriteLine("✅ [IMMEDIATE] EventHandlerInitializationService同期初期化完了！");

            // 🚀 UI側にEventHandler初期化完了を通知
            NotifyUIEventHandlerInitialized();

            // デバッグログ記録
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→✅ [IMMEDIATE] EventHandlerInitializationService同期初期化完了！{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [IMMEDIATE_FATAL] EventHandler即座初期化エラー: {ex}");

            // デバッグログ記録
            try
            {
                var loggingSettings = LoggingSettings.CreateDevelopmentSettings();
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                System.IO.File.AppendAllText(loggingSettings.GetFullDebugLogPath(), $"{timestamp}→🚨 [IMMEDIATE_FATAL] EventHandler即座初期化エラー: {ex.Message}{Environment.NewLine}");
            }
            catch { /* ログファイル書き込み失敗は無視 */ }

            throw; // 致命的なので再スロー
        }
    }


    /// <summary>
    /// UI側にEventHandler初期化完了を通知（UI安全性向上）
    /// </summary>
    /// <summary>
    /// UI側にEventHandler初期化完了を通知（UI安全性向上）
    /// </summary>
    /// <summary>
    /// UI側にEventHandler初期化完了を通知（UI安全性向上）
    /// </summary>
    private static void NotifyUIEventHandlerInitialized()
    {
        try
        {
            Console.WriteLine("🚀 [UI_NOTIFY] EventHandler初期化完了フラグ設定");

            // 🔧 FIX: 静的フラグのみ設定、UIスレッドアクセスは避ける
            IsEventHandlerInitialized = true;

            Console.WriteLine("✅ [UI_NOTIFY] EventHandler初期化完了フラグ設定済み");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [UI_NOTIFY_ERROR] UI通知エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 診断システムを直接初期化します - OnFrameworkInitializationCompleted代替
    /// </summary>
    private static void InitializeDiagnosticSystemDirectly()
    {
        try
        {
            Console.WriteLine("🔍🔍🔍 [DIRECT_DEBUG] IDiagnosticCollectionService解決試行中... 🔍🔍🔍");
            var diagnosticCollectionService = ServiceProvider.GetService<Baketa.Core.Abstractions.Services.IDiagnosticCollectionService>();
            if (diagnosticCollectionService != null)
            {
                Console.WriteLine($"✅✅✅ [DIRECT_SUCCESS] IDiagnosticCollectionService解決成功: {diagnosticCollectionService.GetType().Name} ✅✅✅");

                // 診断システムを開始
                _ = Task.Run(async () =>
                {
                    try
                    {
                        Console.WriteLine("🩺 [DIRECT_DEBUG] 診断データ収集開始中...");
                        await diagnosticCollectionService.StartCollectionAsync().ConfigureAwait(false);
                        Console.WriteLine("✅ 診断データ収集開始完了");
                    }
                    catch (Exception diagEx)
                    {
                        Console.WriteLine($"⚠️ 診断システム開始エラー: {diagEx.Message}");
                        Console.WriteLine($"⚠️ エラーの詳細: {diagEx}");
                    }
                });

                // テストイベント発行
                var eventAggregator = ServiceProvider.GetService<Baketa.Core.Abstractions.Events.IEventAggregator>();
                if (eventAggregator != null)
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(3000).ConfigureAwait(false); // 3秒待機
                        try
                        {
                            var testEvent = new Baketa.Core.Events.Diagnostics.PipelineDiagnosticEvent
                            {
                                Stage = "DirectInitialization",
                                IsSuccess = true,
                                ProcessingTimeMs = 100,
                                Severity = Baketa.Core.Events.Diagnostics.DiagnosticSeverity.Information
                            };

                            await eventAggregator.PublishAsync(testEvent).ConfigureAwait(false);
                            Console.WriteLine("🧪 診断テストイベント発行完了（直接初期化版）");

                            // 手動レポート生成テスト
                            await Task.Delay(2000).ConfigureAwait(false);
                            var reportPath = await diagnosticCollectionService.GenerateReportAsync("direct_init_test").ConfigureAwait(false);
                            Console.WriteLine($"🧪 手動レポート生成完了（直接初期化版）: {reportPath}");
                        }
                        catch (Exception testEx)
                        {
                            Console.WriteLine($"🧪 診断テストエラー（直接初期化版）: {testEx.Message}");
                        }
                    });
                }

                Console.WriteLine("🩺 診断システム直接初期化非同期開始完了");
            }
            else
            {
                Console.WriteLine("🚨❌❌❌ [DIRECT_ERROR] IDiagnosticCollectionServiceが見つかりません！ ❌❌❌🚨");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"🚨 [DIRECT_ERROR] 診断システム直接初期化エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// DIコンテナを構成します。
    /// </summary>
    private static async Task ConfigureServices()
    {
        Console.WriteLine("🔍 ConfigureServices開始");
        System.Diagnostics.Debug.WriteLine("🔍 ConfigureServices開始");

        // 環境の検出（強制的にDevelopment環境を使用してOCR設定を確保）
        var debuggerAttached = Debugger.IsAttached;
        var environment = BaketaEnvironment.Development; // 🔧 OCR設定確保のためDevelopmentに固定

        Console.WriteLine($"🌍 Debugger.IsAttached: {debuggerAttached}");
        Console.WriteLine($"🌍 環境: {environment} (OCR設定確保のため強制Development)");
        System.Diagnostics.Debug.WriteLine($"🌍 環境: {environment}");

        // 設定ファイルの読み込み
        var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
        var currentDirectory = Directory.GetCurrentDirectory();

        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] Base Directory: {baseDirectory}");
        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] Current Directory: {currentDirectory}");
        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] appsettings.json exists in BaseDirectory: {File.Exists(Path.Combine(baseDirectory, "appsettings.json"))}");
        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] appsettings.json exists in CurrentDirectory: {File.Exists(Path.Combine(currentDirectory, "appsettings.json"))}");

        // 🔥 Phase 2.3修正: BaseDirectory（実行ファイルの場所）を優先して使用（環境非依存）
        // 理由: CurrentDirectory優先だと実行場所に依存してしまい、他環境で動作しない
        var configBasePath = File.Exists(Path.Combine(baseDirectory, "appsettings.json"))
            ? baseDirectory
            : currentDirectory;

        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] Selected config base path: {configBasePath}");

        var environmentConfigFile = $"appsettings.{(environment == BaketaEnvironment.Development ? "Development" : "Production")}.json";
        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] Environment config file: {environmentConfigFile}");
        Console.WriteLine($"🔍 [CONFIG_PATH_DEBUG] Environment config file exists: {File.Exists(Path.Combine(configBasePath, environmentConfigFile))}");

#if DEBUG
        // 🔥 [DEBUG] 設定ファイルパス診断ログ
        var diagLog = Path.Combine(baseDirectory, "config_diagnostic.log");
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] Config Base Path: {configBasePath}\n");
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] Environment Config: {environmentConfigFile}\n");
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] Env Config Exists: {File.Exists(Path.Combine(configBasePath, environmentConfigFile))}\n");
#endif

        var configuration = new ConfigurationBuilder()
            .SetBasePath(configBasePath)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile(environmentConfigFile, optional: true, reloadOnChange: true)
            .AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true) // シークレット用（.gitignore対象）
            .Build();

#if DEBUG
        // 🔥 [DEBUG] Translation設定の内容確認
        var translationSection = configuration.GetSection("Translation");
        var translationKeys = string.Join(", ", translationSection.GetChildren().Select(c => c.Key));
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] Translation Keys: {translationKeys}\n");
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] UseGrpcClient: {configuration["Translation:UseGrpcClient"] ?? "NULL"}\n");
        File.AppendAllText(diagLog, $"[{DateTime.Now:HH:mm:ss.fff}] GrpcServerAddress: {configuration["Translation:GrpcServerAddress"] ?? "NULL"}\n");
#endif

        // 設定内容の詳細デバッグ
        Console.WriteLine($"🔍 [CONFIG_DETAILED] All configuration keys:");
        foreach (var kvp in configuration.AsEnumerable())
        {
            if (kvp.Key.Contains("OCR") || kvp.Key.Contains("TimedAggregator") || kvp.Key.Contains("ProximityGrouping"))
                Console.WriteLine($"🔍 [CONFIG_DETAILED] {kvp.Key} = {kvp.Value}");
        }

        // 🔥🔥🔥 [CRITICAL_DEBUG] TimedAggregator.ProximityGrouping.VerticalDistanceFactor直接読み取り
        var verticalDistanceFactor = configuration["TimedAggregator:ProximityGrouping:VerticalDistanceFactor"];
        Console.WriteLine($"🔥🔥🔥 [CRITICAL_DEBUG] Program.cs ConfigurationBuilder直後 - VerticalDistanceFactor: {verticalDistanceFactor}");

        // すべての読み込まれた設定ファイルを列挙
        var providers = ((IConfigurationRoot)configuration).Providers;
        foreach (var provider in providers)
        {
            Console.WriteLine($"🔍 [CONFIG_PROVIDERS] Provider: {provider.GetType().Name}");
        }

        // DIコンテナの構成
        var services = new ServiceCollection();

        // Configurationを登録
        services.AddSingleton<IConfiguration>(configuration);

        // appsettings.jsonから設定を読み込み
        services.Configure<Baketa.Core.Settings.AppSettings>(configuration);

        // 📢 認証設定の登録（Issue #174: WebView統合）
        services.Configure<Baketa.Core.Settings.AuthSettings>(
            configuration.GetSection("Authentication"));

        services.Configure<Baketa.UI.Services.TranslationEngineStatusOptions>(
            configuration.GetSection("TranslationEngineStatus"));
        services.Configure<Baketa.Core.Settings.RoiDiagnosticsSettings>(
            configuration.GetSection("DiagnosticsSettings"));

        // OCR設定をappsettings.jsonから読み込み（DetectionThreshold統一化対応）
        services.Configure<Baketa.Core.Settings.OcrSettings>(
            configuration.GetSection("OCR"));

        // LoggingSettings設定をappsettings.jsonから読み込み（IOptionsパターン適用）
        services.Configure<Baketa.Core.Settings.LoggingSettings>(
            configuration.GetSection("Logging"));

        // 🎯 UltraThink Phase 60.4: ProcessingPipelineSettings設定をappsettings.jsonから読み込み（DI解決問題修正）
        services.Configure<Baketa.Core.Models.Processing.ProcessingPipelineSettings>(
            configuration.GetSection("SmartProcessingPipeline"));

        // 🔥 [CRITICAL_FIX] TimedAggregatorSettings設定をappsettings.jsonから読み込み（0.4問題修正）
        services.Configure<Baketa.Core.Settings.TimedAggregatorSettings>(
            configuration.GetSection("TimedAggregator"));
        Console.WriteLine("🔥 [CONFIG_FIX] TimedAggregatorSettings登録完了 - appsettings.jsonから読み込み");

        // ロギングの設定
        services.AddLogging(builder =>
        {
            // 🔥 [OPTION_C] appsettings.jsonのLogging設定を適用
            builder.AddConfiguration(configuration.GetSection("Logging"));

            // コンソール出力
            builder.AddConsole();

            // デバッグ出力（Visual Studio Output window）
            builder.AddDebug();

            // 🔥 [OPTION_C] カスタムファイルロガーの追加
            // debug_app_logs.txtにILoggerの出力を記録
            var debugLogPath = Path.Combine(baseDirectory, "debug_app_logs.txt");

            try
            {
                var customProvider = new Baketa.UI.Utils.CustomFileLoggerProvider(debugLogPath);
                builder.AddProvider(customProvider);
                Console.WriteLine($"✅ [LOGGING_FIX] CustomFileLoggerProvider登録完了 - Path: {debugLogPath}");

                // 診断: 即座にテストログを出力
                Baketa.UI.Utils.SafeFileLogger.AppendLog(debugLogPath, $"=== CustomFileLoggerProvider Test Log - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} ===");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ [LOGGING_FIX] CustomFileLoggerProvider登録失敗: {ex.Message}");
                Console.WriteLine($"❌ [LOGGING_FIX] StackTrace: {ex.StackTrace}");
            }

            // 環境に応じたログレベル設定
            if (environment == BaketaEnvironment.Development)
            {
                // 開発環境では詳細なログを有効化
                builder.SetMinimumLevel(LogLevel.Debug);
            }
            else
            {
                // 本番環境では必要最低限のログのみ
                builder.SetMinimumLevel(LogLevel.Information);
            }
        });

        // 🩺 DiagnosticModuleの最優先登録 - 診断レポートシステム即座有効化
        Console.WriteLine("🩺 [FIRST] DiagnosticModule最優先登録開始");
        var diagnosticModule = new Baketa.Infrastructure.DI.Modules.DiagnosticModule();
        diagnosticModule.RegisterServices(services);
        Console.WriteLine("✅ [FIRST] DiagnosticModule最優先登録完了");

        // 🚀 Phase 2-1: 段階的DI簡素化 - ステップ1: 基盤モジュール群の統合
        Console.WriteLine("🔧 Phase 2-1: 基盤モジュール群登録開始");
        RegisterFoundationModules(services);
        Console.WriteLine("✅ Phase 2-1: 基盤モジュール群登録完了");

        // 🚀 Phase 2-2: 段階的DI簡素化 - ステップ2: アプリケーション・特殊機能モジュール群の統合
        Console.WriteLine("🔧 Phase 2-2: アプリケーション・特殊機能モジュール群登録開始");
        RegisterApplicationAndSpecializedModules(services);
        Console.WriteLine("✅ Phase 2-2: アプリケーション・特殊機能モジュール群登録完了");

        // 🌐 [i18n] LocalizationService登録 - UI言語設定を保存/読み込みするサービス
        Console.WriteLine("🌐 [i18n] LocalizationService登録開始");
        services.AddTranslationSettingsUI(configuration);
        Console.WriteLine("✅ [i18n] LocalizationService登録完了");

        // DI登録デバッグ
        DebugServiceRegistration(services);

        // さらに詳細なDI診断
        DebugViewModelRegistration(services);

        // サービスプロバイダーの構築
        Console.WriteLine("🏗️ ServiceProvider構築開始");
        System.Diagnostics.Debug.WriteLine("🏗️ ServiceProvider構築開始");
        ServiceProvider = services.BuildServiceProvider();
        Console.WriteLine("✅ ServiceProvider構築完了");
        System.Diagnostics.Debug.WriteLine("✅ ServiceProvider構築完了");

        // 🌐 [i18n] ILocalizationService早期初期化 - 保存された言語設定をXAML読み込み前に適用
        Console.WriteLine("🌐 [i18n] ILocalizationService早期初期化開始");
        try
        {
            var localizationService = ServiceProvider.GetService<ILocalizationService>();
            if (localizationService != null)
            {
                Console.WriteLine($"🌐 [i18n] ILocalizationService初期化完了: Culture={localizationService.CurrentCulture.Name}");
            }
            else
            {
                Console.WriteLine("⚠️ [i18n] ILocalizationServiceが登録されていません");
            }
        }
        catch (Exception locEx)
        {
            Console.WriteLine($"❌ [i18n] ILocalizationService初期化エラー: {locEx.Message}");
            // ローカライゼーション初期化失敗してもアプリケーション起動は継続
        }

        // 🚨🚨🚨 [PHASE5.2G_VERIFY] 診断ログ - ビルド反映確認用
        var verifyMessage = "🚨🚨🚨 [PHASE5.2G_VERIFY] このログが出ればビルド反映成功！";
        Console.WriteLine(verifyMessage);

        // 🔥 [PHASE13.2.22_FIX] BaketaLogManager.LogSystemDebugをtry-catchで保護
        try
        {
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(verifyMessage);
        }
        catch (Exception baketaLogEx)
        {
            Console.WriteLine($"⚠️ [PHASE13.2.22_FIX] BaketaLogManager.LogSystemDebugエラー（処理は継続）: {baketaLogEx.Message}");
        }

        // 🚀 CRITICAL: EventHandlerInitializationServiceをDI完了直後に実行（競合状態根本解決）
        Console.WriteLine("🚀🚀🚀 [CRITICAL] EventHandlerInitializationService即座実行開始！ 🚀🚀🚀");
        try
        {
            InitializeEventHandlersImmediately();
            Console.WriteLine("🚀🚀🚀 [CRITICAL] EventHandlerInitializationService即座実行完了！ 🚀🚀🚀");
        }
        catch (Exception ex)
        {
            // InitializeEventHandlersImmediately()で例外が発生した場合
            Console.WriteLine($"❌ [CRITICAL] EventHandlerInitializationService失敗: {ex.GetType().Name}");
            Console.WriteLine($"❌ [CRITICAL] Message: {ex.Message}");
            Console.WriteLine($"❌ [CRITICAL] StackTrace: {ex.StackTrace}");
            // アプリケーション起動は継続（EventHandler初期化失敗しても機能する部分はある）
        }

        // 🔥 [PHASE5.2E_FIX] IHostedService手動起動 - Avalonia は Generic Host を使わないため手動起動が必須
        // Option A: WarmupHostedService等のバックグラウンドサービスを起動
        Console.WriteLine("🚀 [PHASE5.2E_FIX] IHostedService手動起動開始");
        try
        {
            await StartHostedServicesAsync(ServiceProvider).ConfigureAwait(false);
            Console.WriteLine("✅ [PHASE5.2E_FIX] IHostedService手動起動完了 - WarmupHostedServiceが起動しました");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE5.2E_FIX] IHostedService起動エラー: {ex.GetType().Name}");
            Console.WriteLine($"❌ [PHASE5.2E_FIX] Message: {ex.Message}");
            Console.WriteLine($"❌ [PHASE5.2E_FIX] StackTrace: {ex.StackTrace}");
            // ウォームアップ失敗してもアプリケーション起動は継続
        }

        // 🔥 UltraThink翻訳モデル事前ロード戦略 - Program.cs統合実装
        var startMessage = "🔥🔥🔥 [PRELOAD] 翻訳モデル事前ロード戦略実行開始！ 🔥🔥🔥";
        Console.WriteLine(startMessage);
        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(startMessage);
        _ = Task.Run(PreloadTranslationModelAsync);
        var completedMessage = "🔥🔥🔥 [PRELOAD] 翻訳モデル事前ロード戦略バックグラウンド開始完了！ 🔥🔥🔥";
        Console.WriteLine(completedMessage);
        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(completedMessage);

        // ReactiveUIスケジューラの設定
        ConfigureReactiveUI();

        // アプリケーション起動完了後にサービスを開始（App.axaml.csで実行）
    }

    /// <summary>
    /// 🔥 [PHASE5.2E_FIX] IHostedServiceを手動で起動します
    /// Avalonia は Generic Host を使わないため、WarmupHostedService等を手動起動
    /// </summary>
    /// <param name="serviceProvider">ServiceProvider</param>
    private static async Task StartHostedServicesAsync(IServiceProvider serviceProvider)
    {
        if (serviceProvider == null)
        {
            Console.WriteLine("⚠️ [PHASE5.2E_FIX] ServiceProviderがnull - IHostedService起動をスキップ");
            return;
        }

        try
        {
            Console.WriteLine("🚀 [PHASE5.2E_FIX] IHostedService検出中...");

            var hostedServices = serviceProvider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
            var serviceList = hostedServices.ToList();

            Console.WriteLine($"🔍 [PHASE5.2E_FIX] 検出されたIHostedService数: {serviceList.Count}");

            foreach (var service in serviceList)
            {
                var serviceName = service.GetType().Name;
                Console.WriteLine($"🚀 [PHASE5.2E_FIX] {serviceName} 起動開始...");

                try
                {
                    await service.StartAsync(CancellationToken.None).ConfigureAwait(false);
                    Console.WriteLine($"✅ [PHASE5.2E_FIX] {serviceName} 起動完了");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ [PHASE5.2E_FIX] {serviceName} 起動エラー: {ex.GetType().Name} - {ex.Message}");
                    // 1つのサービス起動失敗でも他のサービスは起動継続
                }
            }

            Console.WriteLine($"✅ [PHASE5.2E_FIX] IHostedService起動完了 - 起動済み: {serviceList.Count}個");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE5.2E_FIX] IHostedService起動エラー: {ex.GetType().Name}");
            Console.WriteLine($"❌ [PHASE5.2E_FIX] Message: {ex.Message}");
            Console.WriteLine($"❌ [PHASE5.2E_FIX] StackTrace: {ex.StackTrace}");
            throw;
        }
    }

    /// <summary>
    /// ReactiveUIの設定を行います
    /// </summary>
    private static void ConfigureReactiveUI()
    {
        try
        {
            Console.WriteLine("🔧 ReactiveUI設定開始");

            // デフォルトエラーハンドラを設定
            RxApp.DefaultExceptionHandler = Observer.Create<Exception>(ex =>
            {
                Console.WriteLine($"🚨 ReactiveUI例外: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"🚨 ReactiveUI例外: {ex.Message}");
                // UIスレッド違反例外は詳細ログを出力
                if (ex is InvalidOperationException && ex.Message.Contains("thread"))
                {
                    Console.WriteLine($"🧵 UIスレッド違反詳細: {ex.StackTrace}");
                }
            });

            Console.WriteLine("✅ ReactiveUI設定完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ ReactiveUI設定失敗: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"⚠️ ReactiveUI設定失敗: {ex.Message}");
        }
    }

    /// <summary>
    /// DI登録状況をデバッグします
    /// </summary>
    private static void DebugServiceRegistration(IServiceCollection services)
    {
        System.Console.WriteLine("=== DI Service Registration Debug ===");

        // ISettingsServiceの登録確認
        var settingsServices = services.Where(s => s.ServiceType == typeof(Baketa.Core.Services.ISettingsService));
        System.Console.WriteLine($"ISettingsService registrations count: {settingsServices.Count()}");

        foreach (var service in settingsServices)
        {
            System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
            System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "N/A"}");
            System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
            System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");
        }

        // ITranslationEngineの登録確認
        var translationEngines = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Translation.ITranslationEngine));
        System.Console.WriteLine($"ITranslationEngine registrations count: {translationEngines.Count()}");

        foreach (var service in translationEngines)
        {
            System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
            System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "N/A"}");
            System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
            System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");
        }

        // ITranslationServiceの登録確認
        var translationServices = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Translation.ITranslationService));
        System.Console.WriteLine($"ITranslationService registrations count: {translationServices.Count()}");

        foreach (var service in translationServices)
        {
            System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
            System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "N/A"}");
            System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
            System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");
        }

        // AccessibilitySettingsViewModelの登録確認
        var accessibilityVM = services.Where(s => s.ServiceType == typeof(Baketa.UI.ViewModels.AccessibilitySettingsViewModel));
        System.Console.WriteLine($"AccessibilitySettingsViewModel registrations count: {accessibilityVM.Count()}");

        // IOcrPreprocessingServiceの登録確認（Phase 3診断）
        var ocrPreprocessingServices = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.OCR.IOcrPreprocessingService));
        System.Console.WriteLine($"IOcrPreprocessingService registrations count: {ocrPreprocessingServices.Count()}");

        foreach (var service in ocrPreprocessingServices)
        {
            System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
            System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "Factory"}");
            System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
            System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");

            // ファクトリ関数がある場合は、実際の実装タイプを推定
            if (service.ImplementationFactory != null)
            {
                System.Console.WriteLine($"  - Factory details: Likely GameOptimizedPreprocessingService (Phase 3)");
            }
        }

        // 🩺 診断サービス登録確認
        System.Console.WriteLine("=== 🩺 Diagnostic Services Registration Debug ===");
        var diagnosticCollectionServices = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Services.IDiagnosticCollectionService));
        System.Console.WriteLine($"IDiagnosticCollectionService registrations count: {diagnosticCollectionServices.Count()}");

        foreach (var service in diagnosticCollectionServices)
        {
            System.Console.WriteLine($"  - ServiceType: {service.ServiceType.Name}");
            System.Console.WriteLine($"  - ImplementationType: {service.ImplementationType?.Name ?? "Factory"}");
            System.Console.WriteLine($"  - Lifetime: {service.Lifetime}");
            System.Console.WriteLine($"  - ImplementationFactory: {(service.ImplementationFactory != null ? "Yes" : "No")}");
        }

        var diagnosticReportGenerators = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Services.IDiagnosticReportGenerator));
        System.Console.WriteLine($"IDiagnosticReportGenerator registrations count: {diagnosticReportGenerators.Count()}");

        var backgroundTaskQueues = services.Where(s => s.ServiceType == typeof(Baketa.Core.Abstractions.Services.IBackgroundTaskQueue));
        System.Console.WriteLine($"IBackgroundTaskQueue registrations count: {backgroundTaskQueues.Count()}");

        var diagnosticEventProcessors = services.Where(s =>
            s.ServiceType == typeof(Baketa.Core.Abstractions.Events.IEventProcessor<Baketa.Core.Events.Diagnostics.PipelineDiagnosticEvent>));
        System.Console.WriteLine($"DiagnosticEventProcessor registrations count: {diagnosticEventProcessors.Count()}");

        System.Console.WriteLine("=== 🩺 End Diagnostic Services Debug ===");
    }

    /// <summary>
    /// ViewModelのDI登録詳細を確認します
    /// </summary>
    private static void DebugViewModelRegistration(IServiceCollection services)
    {
        System.Console.WriteLine("=== ViewModel Registration Debug ===");

        var viewModelTypes = new[]
        {
                typeof(Baketa.UI.ViewModels.AccessibilitySettingsViewModel),
                typeof(Baketa.UI.ViewModels.LanguagePairsViewModel)
                // 🔥 [PHASE2_PROBLEM2] MainWindowViewModel削除 - MainOverlayViewModelに統合完了
            };

        foreach (var vmType in viewModelTypes)
        {
            var registrations = services.Where(s => s.ServiceType == vmType);
            System.Console.WriteLine($"{vmType.Name}: {registrations.Count()} registration(s)");

            foreach (var reg in registrations)
            {
                System.Console.WriteLine($"  - Lifetime: {reg.Lifetime}");
                System.Console.WriteLine($"  - ImplementationType: {reg.ImplementationType?.Name ?? "Factory"}");
            }
        }
    }

    /// <summary>
    /// Phase4: 統合GPU最適化システムを初期化
    /// </summary>
    private static async Task InitializeUnifiedGpuSystemAsync()
    {
        try
        {
            Console.WriteLine("🎯 統合GPU最適化システム初期化開始");
            var timer = System.Diagnostics.Stopwatch.StartNew();

            // ServiceProviderが利用可能になるまで待機
            while (ServiceProvider == null)
            {
                await Task.Delay(100).ConfigureAwait(false);
                if (timer.ElapsedMilliseconds > 30000) // 30秒でタイムアウト
                {
                    Console.WriteLine("⚠️ ServiceProvider初期化タイムアウト - 統合GPU初期化を中止");
                    return;
                }
            }

            // UnifiedGpuInitializerサービスを取得して初期化
            var gpuInitializer = ServiceProvider.GetService<Baketa.Infrastructure.DI.UnifiedGpuInitializer>();
            if (gpuInitializer != null)
            {
                Console.WriteLine("🔧 UnifiedGpuInitializer取得成功 - 初期化開始");

                try
                {
                    await gpuInitializer.InitializeAsync().ConfigureAwait(false);
                    timer.Stop();

                    Console.WriteLine($"✅ 統合GPU最適化システム初期化完了 - 初期化時間: {timer.ElapsedMilliseconds}ms");
                    System.Diagnostics.Debug.WriteLine($"✅ 統合GPU最適化システム初期化完了 - 初期化時間: {timer.ElapsedMilliseconds}ms");
                }
                catch (Exception gpuEx)
                {
                    timer.Stop();
                    Console.WriteLine($"⚠️ 統合GPU最適化システム初期化部分的失敗（続行）: {gpuEx.Message} - 経過時間: {timer.ElapsedMilliseconds}ms");
                    System.Diagnostics.Debug.WriteLine($"⚠️ 統合GPU最適化システム初期化部分的失敗（続行）: {gpuEx.Message} - 経過時間: {timer.ElapsedMilliseconds}ms");
                }
            }
            else
            {
                timer.Stop();
                Console.WriteLine($"⚠️ UnifiedGpuInitializerサービスが見つかりません - 経過時間: {timer.ElapsedMilliseconds}ms");
                System.Diagnostics.Debug.WriteLine($"⚠️ UnifiedGpuInitializerサービスが見つかりません - 経過時間: {timer.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 統合GPU最適化システム初期化エラー: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"💥 統合GPU最適化システム初期化エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// OCRエンジンを事前初期化してメイン処理を高速化
    /// </summary>
    private static async Task PreInitializeOcrEngineAsync()
    {
        try
        {
            Console.WriteLine("🚀 OCRエンジン事前初期化開始");
            var timer = System.Diagnostics.Stopwatch.StartNew();

            // ServiceProviderが利用可能になるまで待機
            while (ServiceProvider == null)
            {
                await Task.Delay(100).ConfigureAwait(false);
                if (timer.ElapsedMilliseconds > 30000) // 30秒でタイムアウト
                {
                    Console.WriteLine("⚠️ ServiceProvider初期化タイムアウト - OCR事前初期化を中止");
                    return;
                }
            }

            // OCRエンジンサービスを取得して初期化
            var ocrService = ServiceProvider.GetService<Baketa.Core.Abstractions.OCR.IOcrEngine>();
            if (ocrService != null)
            {
                Console.WriteLine("🔧 OCRエンジンサービス取得成功 - 初期化開始");

                // OCRエンジンを事前初期化（appsettings.jsonから読み込んだ設定を使用）
                try
                {
                    // appsettings.jsonから読み込まれた設定を取得
                    var ocrSettings = ServiceProvider.GetService<Baketa.Core.Abstractions.OCR.OcrEngineSettings>();

                    // OCRエンジンの初期化（設定を明示的に渡す）
                    await ocrService.InitializeAsync(ocrSettings).ConfigureAwait(false);

                    Console.WriteLine($"🔧 OCR設定適用完了 - EnableHybridMode: {ocrSettings?.EnableHybridMode ?? false}");
                    timer.Stop();

                    Console.WriteLine($"✅ OCRエンジン事前初期化完了 - 初期化時間: {timer.ElapsedMilliseconds}ms");
                    System.Diagnostics.Debug.WriteLine($"✅ OCRエンジン事前初期化完了 - 初期化時間: {timer.ElapsedMilliseconds}ms");
                }
                catch (Exception ocrEx)
                {
                    timer.Stop();
                    Console.WriteLine($"⚠️ OCRエンジン初期化部分的失敗（続行）: {ocrEx.Message} - 経過時間: {timer.ElapsedMilliseconds}ms");
                    System.Diagnostics.Debug.WriteLine($"⚠️ OCRエンジン初期化部分的失敗（続行）: {ocrEx.Message} - 経過時間: {timer.ElapsedMilliseconds}ms");
                }
            }
            else
            {
                timer.Stop();
                Console.WriteLine($"⚠️ OCRエンジンサービスが見つかりません - 経過時間: {timer.ElapsedMilliseconds}ms");
                System.Diagnostics.Debug.WriteLine($"⚠️ OCRエンジンサービスが見つかりません - 経過時間: {timer.ElapsedMilliseconds}ms");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"💥 OCRエンジン事前初期化エラー: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"💥 OCRエンジン事前初期化エラー: {ex.Message}");
        }
    }

    /// <summary>
    /// 翻訳モデルを事前ロードして初回翻訳6秒待機問題を解決
    /// UltraThink翻訳モデル事前ロード戦略 - Program.cs統合実装
    /// </summary>
    private static async Task PreloadTranslationModelAsync()
    {
        try
        {
            var message = "🚀 [PRELOAD_START] 翻訳モデル事前ロード開始";
            Console.WriteLine(message);
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(message);
            var timer = System.Diagnostics.Stopwatch.StartNew();

            // ServiceProviderが利用可能になるまで待機
            while (ServiceProvider == null)
            {
                await Task.Delay(100).ConfigureAwait(false);
                if (timer.ElapsedMilliseconds > 30000) // 30秒でタイムアウト
                {
                    var timeoutMessage = "⚠️ [PRELOAD_TIMEOUT] ServiceProvider初期化タイムアウト - 翻訳モデル事前ロードを中止";
                    Console.WriteLine(timeoutMessage);
                    Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(timeoutMessage);
                    return;
                }
            }

            var initMessage = "🔄 [PRELOAD_INIT] ServiceProvider取得完了 - IApplicationInitializer解決開始";
            Console.WriteLine(initMessage);
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(initMessage);

            // IApplicationInitializerサービスを取得（Clean Architecture準拠）
            var appInitializer = ServiceProvider.GetService<Baketa.Application.Services.IApplicationInitializer>();
            if (appInitializer != null)
            {
                // 🔍 UltraPhase 9.1: appInitializer型の詳細確認
                var actualType = appInitializer.GetType().FullName;
                var typeInfoMessage = $"🔍 [TYPE_INFO] appInitializer実際の型: {actualType}";
                Console.WriteLine(typeInfoMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(typeInfoMessage);

                var isTranslationModelLoader = appInitializer is Baketa.Application.Services.TranslationModelLoader;
                var loaderCheckMessage = $"🔍 [TYPE_INFO] TranslationModelLoader型チェック: {isTranslationModelLoader}";
                Console.WriteLine(loaderCheckMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(loaderCheckMessage);

                var successMessage = "🔥 [PRELOAD] TranslationModelLoader取得成功 - バックグラウンド実行開始";
                Console.WriteLine(successMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(successMessage);

                // 🔥 UltraPhase 4 FIX: Task.Run実行追跡のため明示的デバッグログ追加
                var taskRunStartMessage = "🎯 [TASK_RUN_START] Task.Run呼び出し直前 - ラムダ式開始確認";
                Console.WriteLine(taskRunStartMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(taskRunStartMessage);

                // 🔥 Phase 2.2 FIX: メインスレッドブロック回避のため、Task.Runでバックグラウンド実行
                var preloadTask = Task.Run(async () =>
                {
                    try
                    {
                        var lambdaStartMessage = "🎯 [LAMBDA_START] Task.Runラムダ式内部実行開始";
                        Console.WriteLine(lambdaStartMessage);
                        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(lambdaStartMessage);

                        var initStartMessage = "🎯 [INIT_START] appInitializer.InitializeAsync()呼び出し直前";
                        Console.WriteLine(initStartMessage);
                        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(initStartMessage);

                        // 翻訳モデルの事前初期化実行
                        await appInitializer.InitializeAsync().ConfigureAwait(false);
                        timer.Stop();

                        var completedMessage = $"✅ [PRELOAD] 翻訳モデル事前ロード完了 - 初回翻訳は即座実行可能 (時間: {timer.ElapsedMilliseconds}ms)";
                        Console.WriteLine(completedMessage);
                        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(completedMessage);
                    }
                    catch (Exception preloadEx)
                    {
                        timer.Stop();
                        var failedMessage = $"⚠️ [PRELOAD] 事前ロード失敗 - 従来動作継続: {preloadEx.Message} (経過時間: {timer.ElapsedMilliseconds}ms)";
                        Console.WriteLine(failedMessage);
                        Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(failedMessage);
                    }
                });

                var taskRunEndMessage = "🎯 [TASK_RUN_END] Task.Run呼び出し完了 - ラムダ式実行中";
                Console.WriteLine(taskRunEndMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(taskRunEndMessage);
            }
            else
            {
                timer.Stop();
                var notRegisteredMessage = $"ℹ️ [PRELOAD] IApplicationInitializer未登録 - 従来動作で継続 (経過時間: {timer.ElapsedMilliseconds}ms)";
                Console.WriteLine(notRegisteredMessage);
                Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(notRegisteredMessage);
            }
        }
        catch (Exception ex)
        {
            var errorMessage = $"💥 [PRELOAD_ERROR] 翻訳モデル事前ロードエラー: {ex.Message}";
            Console.WriteLine(errorMessage);
            Baketa.Core.Logging.BaketaLogManager.LogSystemDebug(errorMessage);
        }
    }

    // OPUS-MT削除済み: StartOpusMtPrewarmingAsyncメソッドはNLLB-200統一により不要

    /// <summary>
    /// 基盤モジュール群（Core, Infrastructure, Platform）を登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterFoundationModules(IServiceCollection services)
    {
        // 依存関係トラッキング用の共通変数
        var registeredModules = new HashSet<Type>();
        var moduleStack = new Stack<Type>();

        // Coreモジュールの登録
        Console.WriteLine("🏗️ Core基盤モジュール登録開始");
        var coreModule = new CoreModule();
        coreModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ Core基盤モジュール登録完了");

        // 設定システムを登録（ISettingsServiceを提供）
        Console.WriteLine("⚙️ 設定システム登録開始");
        services.AddSettingsSystem();
        Console.WriteLine("✅ 設定システム登録完了");

        // InfrastructureModuleの登録（appsettings.json対応版）
        Console.WriteLine("🔧 Infrastructure基盤モジュール登録開始");
        var infrastructureModule = new InfrastructureModule();

        // Configuration オブジェクトを取得してappsettings.json設定を読み込み
        var configurationForInfrastructure = services.BuildServiceProvider().GetRequiredService<IConfiguration>();
        infrastructureModule.RegisterServices(services, configurationForInfrastructure);
        registeredModules.Add(typeof(InfrastructureModule));
        Console.WriteLine("✅ Infrastructure基盤モジュール登録完了 - appsettings.json設定読み込み済み");

        // 🎯 UltraThink Phase 21 修正: OCR処理パイプライン復旧のためのSmartProcessingPipelineService登録
        Console.WriteLine("🔧 ProcessingServices登録開始 - OCR処理パイプライン修復");
        services.AddProcessingServices();
        Console.WriteLine("✅ ProcessingServices登録完了 - SmartProcessingPipelineService + 戦略4種");

        // 🚀 NEW ARCHITECTURE: TimedAggregatorModule登録（完全自律型設定システム）
        Console.WriteLine("🔧 TimedAggregatorModule登録開始（新設計）");
        Console.WriteLine("🔧 [PHASE12.2_DIAG] TimedAggregatorModule登録開始 - Program.cs:1181");

        try
        {
            Console.WriteLine("🔧 [PHASE12.2_DIAG] new TimedAggregatorModule() 実行直前");
            var timedAggregatorModule = new Baketa.Infrastructure.DI.Modules.TimedAggregatorModule();
            Console.WriteLine($"✅ [PHASE12.2_DIAG] TimedAggregatorModule インスタンス作成完了: {timedAggregatorModule != null}");

            Console.WriteLine("🔧 [PHASE12.2_DIAG] RegisterServices() 実行直前");
            timedAggregatorModule.RegisterServices(services);
            Console.WriteLine("✅ [PHASE12.2_DIAG] RegisterServices() 実行完了");

            registeredModules.Add(typeof(Baketa.Infrastructure.DI.Modules.TimedAggregatorModule));
            Console.WriteLine("✅ TimedAggregatorModule登録完了 - 自律型設定システム統合済み");
            Console.WriteLine("✅ [PHASE12.2_DIAG] TimedAggregatorModule登録完全完了");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ [PHASE12.2_DIAG] TimedAggregatorModule登録失敗: {ex.GetType().Name}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] Exception Message: {ex.Message}");
            Console.WriteLine($"❌ [PHASE12.2_DIAG] StackTrace: {ex.StackTrace}");
            throw; // 例外を再スローして明確に失敗させる
        }

        // PlatformModuleの登録
        Console.WriteLine("🖥️ Platform基盤モジュール登録開始");
        var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
        platformModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ Platform基盤モジュール登録完了");

        // AdaptiveCaptureModuleの登録（ApplicationModuleのAdaptiveCaptureServiceに必要な依存関係を提供）
        Console.WriteLine("📷 AdaptiveCapture基盤モジュール登録開始");
        var adaptiveCaptureModule = new Baketa.Infrastructure.Platform.DI.Modules.AdaptiveCaptureModule();
        adaptiveCaptureModule.RegisterServices(services);
        Console.WriteLine("✅ AdaptiveCapture基盤モジュール登録完了");

        // AuthModuleの登録（InfrastructureレイヤーのAuthサービス）
        Console.WriteLine("🔐 Auth基盤モジュール登録開始");
        var authModule = new AuthModule();
        authModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ Auth基盤モジュール登録完了");

        Console.WriteLine($"📊 基盤モジュール登録済み数: {registeredModules.Count}");
    }

    /// <summary>
    /// アプリケーション・特殊機能モジュール群を登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterApplicationAndSpecializedModules(IServiceCollection services)
    {
        // 依存関係トラッキング用の共通変数
        var registeredModules = new HashSet<Type>();
        var moduleStack = new Stack<Type>();

        // ApplicationModuleの明示的登録
        Console.WriteLine("🚀 ApplicationModule登録開始");
        var applicationModule = new Baketa.Application.DI.Modules.ApplicationModule();
        applicationModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ ApplicationModule登録完了");

        // Gemini推奨モジュール群
        RegisterGeminiRecommendedModules(services, registeredModules, moduleStack);

        // UIモジュール群
        RegisterUIModules(services, registeredModules, moduleStack);

        // OCR最適化モジュール群
        RegisterOcrOptimizationModules(services);

        // アダプターサービスの登録
        Console.WriteLine("🔗 アダプターサービス登録開始");
        services.AddAdapterServices();
        Console.WriteLine("✅ アダプターサービス登録完了");

        Console.WriteLine($"📊 アプリケーション・特殊機能モジュール登録済み数: {registeredModules.Count}");
    }

    /// <summary>
    /// Gemini推奨モジュール群を登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="registeredModules">登録済みモジュール</param>
    /// <param name="moduleStack">モジュールスタック</param>
    private static void RegisterGeminiRecommendedModules(IServiceCollection services, HashSet<Type> registeredModules, Stack<Type> moduleStack)
    {
        // 🚀 Gemini推奨Step2: 段階的OCR戦略モジュール登録
        Console.WriteLine("🔍 [GEMINI] StagedOcrStrategyModule登録開始...");
        var stagedOcrModule = new Baketa.Application.DI.Modules.StagedOcrStrategyModule();
        stagedOcrModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ [GEMINI] StagedOcrStrategyModule登録完了！");

        // 🎯 Gemini推奨Step3: 高度キャッシング戦略モジュール登録
        Console.WriteLine("🔍 [GEMINI] AdvancedCachingModule登録開始...");
        var advancedCachingModule = new Baketa.Application.DI.Modules.AdvancedCachingModule();
        advancedCachingModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ [GEMINI] AdvancedCachingModule登録完了！");
    }

    /// <summary>
    /// UIモジュール群を登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    /// <param name="registeredModules">登録済みモジュール</param>
    /// <param name="moduleStack">モジュールスタック</param>
    private static void RegisterUIModules(IServiceCollection services, HashSet<Type> registeredModules, Stack<Type> moduleStack)
    {
        // UIモジュールの登録
        Console.WriteLine("🎨 UIModule登録開始");
        var uiModule = new UIModule();
        uiModule.RegisterWithDependencies(services, registeredModules, moduleStack);
        Console.WriteLine("✅ UIModule登録完了");

        // オーバーレイUIモジュールの登録
        Console.WriteLine("🖼️ OverlayUIModule登録開始");
        var overlayUIModule = new OverlayUIModule();
        overlayUIModule.RegisterServices(services);
        Console.WriteLine("✅ OverlayUIModule登録完了");

        // ✅ [Phase 1.4] Phase16UIOverlayModule完全削除完了 - OverlayUIModuleに統合済み
    }

    /// <summary>
    /// OCR最適化モジュール群を登録します
    /// </summary>
    /// <param name="services">サービスコレクション</param>
    private static void RegisterOcrOptimizationModules(IServiceCollection services)
    {
        // バッチOCRモジュールの登録
        Console.WriteLine("📦 BatchOcrModule登録開始");
        var batchOcrModule = new Baketa.Infrastructure.DI.BatchOcrModule();
        batchOcrModule.RegisterServices(services);
        Console.WriteLine("✅ BatchOcrModule登録完了");

        // OCRモジュールの登録（IOcrPreprocessingService提供）
        Console.WriteLine("🔍 OcrProcessingModule登録開始");
        var ocrProcessingModule = new Baketa.Infrastructure.DI.OcrProcessingModule();
        ocrProcessingModule.RegisterServices(services);
        Console.WriteLine("✅ OcrProcessingModule登録完了");

        // OpenCvProcessingModuleの登録（IOcrPreprocessingService上書き）
        Console.WriteLine("🎯 OpenCvProcessingModule登録開始");
        var openCvProcessingModule = new Baketa.Infrastructure.DI.Modules.OpenCvProcessingModule();
        openCvProcessingModule.RegisterServices(services);
        Console.WriteLine("✅ OpenCvProcessingModule登録完了");

        // PaddleOCRモジュールの登録
        Console.WriteLine("🚀 PaddleOcrModule登録開始");
        var paddleOcrModule = new Baketa.Infrastructure.DI.PaddleOcrModule();
        paddleOcrModule.RegisterServices(services);
        Console.WriteLine("✅ PaddleOcrModule登録完了");

        // Phase 4: 統合GPU最適化モジュールの登録
        Console.WriteLine("🎯 Phase4: UnifiedGpuModule登録開始");
        var unifiedGpuModule = new Baketa.Infrastructure.DI.UnifiedGpuModule();
        unifiedGpuModule.RegisterServices(services);
        Console.WriteLine("✅ Phase4: UnifiedGpuModule登録完了");
    }
}
