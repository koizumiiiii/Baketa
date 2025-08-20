using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.ReactiveUI;
using Baketa.Application.DI.Modules;
using Baketa.Core.DI;
using Baketa.Core.DI.Modules;
using Baketa.Core.Performance;
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.DI;
using Baketa.Infrastructure.Platform.DI;
using Baketa.UI.DI.Services;
using Baketa.UI.DI.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using ReactiveUI;
using System.Reactive;

namespace Baketa.UI;

    internal sealed class Program
    {
        /// <summary>
        /// DIコンテナとサービスプロバイダー
        /// </summary>
        public static ServiceProvider? ServiceProvider { get; private set; }
        
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // 🔧 [CRITICAL_ENCODING_FIX] Windows環境でUTF-8コンソール出力を強制設定
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
                Console.InputEncoding = System.Text.Encoding.UTF8;
                
                // Windows環境でのUTF-8モード有効化
                Environment.SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "false");
                Environment.SetEnvironmentVariable("DOTNET_SYSTEM_TEXT_ENCODING_USEUTF8", "true");
                
                Console.WriteLine("🔧 [ENCODING_INIT] UTF-8 console encoding configured successfully");
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
                ConfigureServices();
                
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
                
        /// <summary>
        /// DIコンテナを構成します。
        /// </summary>
        private static void ConfigureServices()
        {
            Console.WriteLine("🔍 ConfigureServices開始");
            System.Diagnostics.Debug.WriteLine("🔍 ConfigureServices開始");
            
            // 環境の検出
            var environment = Debugger.IsAttached 
                ? BaketaEnvironment.Development 
                : BaketaEnvironment.Production;
            
            Console.WriteLine($"🌍 環境: {environment}");
            System.Diagnostics.Debug.WriteLine($"🌍 環境: {environment}");
            
            // 設定ファイルの読み込み
            var configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{(environment == BaketaEnvironment.Development ? "Development" : "Production")}.json", optional: true, reloadOnChange: true)
                .Build();
            
            // DIコンテナの構成
            var services = new ServiceCollection();
            
            // Configurationを登録
            services.AddSingleton<IConfiguration>(configuration);
            
            // appsettings.jsonから設定を読み込み
            services.Configure<Baketa.Core.Settings.AppSettings>(configuration);
            services.Configure<Baketa.UI.Services.TranslationEngineStatusOptions>(
                configuration.GetSection("TranslationEngineStatus"));
            
            // ロギングの設定
            services.AddLogging(builder => 
            {
                builder.AddConsole();
                
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
            
            // 🚀 Phase 2-1: 段階的DI簡素化 - ステップ1: 基盤モジュール群の統合
            Console.WriteLine("🔧 Phase 2-1: 基盤モジュール群登録開始");
            RegisterFoundationModules(services);
            Console.WriteLine("✅ Phase 2-1: 基盤モジュール群登録完了");
            
            // 🚀 Phase 2-2: 段階的DI簡素化 - ステップ2: アプリケーション・特殊機能モジュール群の統合
            Console.WriteLine("🔧 Phase 2-2: アプリケーション・特殊機能モジュール群登録開始");
            RegisterApplicationAndSpecializedModules(services);
            Console.WriteLine("✅ Phase 2-2: アプリケーション・特殊機能モジュール群登録完了");
            
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
            
            // ReactiveUIスケジューラの設定
            ConfigureReactiveUI();
            
            // アプリケーション起動完了後にサービスを開始（App.axaml.csで実行）
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
                // typeof(Baketa.UI.ViewModels.MainWindowViewModel) // MainWindowは使用されていないため無効化
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
                    
                    // OCRエンジンを事前初期化（初期化処理のみ実行）
                    try
                    {
                        // OCRエンジンの初期化のみ実行（ダミー画像処理は省略してシンプルに）
                        await ocrService.InitializeAsync().ConfigureAwait(false);
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
            
            // InfrastructureModuleの登録
            Console.WriteLine("🔧 Infrastructure基盤モジュール登録開始");
            var infrastructureModule = new InfrastructureModule();
            infrastructureModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            Console.WriteLine("✅ Infrastructure基盤モジュール登録完了");
            
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
