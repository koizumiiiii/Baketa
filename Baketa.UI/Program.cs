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
using Baketa.Infrastructure.DI.Modules;
using Baketa.Infrastructure.Platform.DI;
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
            // ファイル出力で確実にログを残す（絶対パスで保存）
            try
            {
                var startupLogPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_startup.txt");
                File.WriteAllText(startupLogPath, $"🚀 Baketa.UI.exe 起動開始 - {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\n");
                File.AppendAllText(startupLogPath, $"📁 BaseDirectory: {AppDomain.CurrentDomain.BaseDirectory}\n");
                File.AppendAllText(startupLogPath, $"📁 CurrentDirectory: {Environment.CurrentDirectory}\n");
                Console.WriteLine($"📝 起動ログ作成: {startupLogPath}");
            }
            catch (Exception fileEx) 
            { 
                Console.WriteLine($"❌ 起動ログ作成失敗: {fileEx.Message}");
            }
            
            Console.WriteLine("🚀 Baketa.UI.exe 起動開始");
            System.Diagnostics.Debug.WriteLine("🚀 Baketa.UI.exe 起動開始");
            
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
                
                Console.WriteLine("🎯 Avalonia アプリケーション開始");
                System.Diagnostics.Debug.WriteLine("🎯 Avalonia アプリケーション開始");
                
                BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            }
            catch (Exception ex)
            {
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
            
            // Baketaの標準モジュールを登録
            // Coreモジュールの登録
            var coreModule = new CoreModule();
            var registeredModules = new HashSet<Type>();
            var moduleStack = new Stack<Type>();
            coreModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // 設定システムを登録（ISettingsServiceを提供）
            services.AddSettingsSystem();
            
            // InfrastructureModuleの登録
            var infrastructureModule = new InfrastructureModule();
            infrastructureModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // PlatformModuleの登録
            var platformModule = new Baketa.Infrastructure.Platform.DI.Modules.PlatformModule();
            platformModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // AuthModuleの登録（InfrastructureレイヤーのAuthサービス）
            var authModule = new AuthModule();
            authModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // ApplicationModuleの明示的登録
            var applicationModule = new Baketa.Application.DI.Modules.ApplicationModule();
            applicationModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // UIモジュールの登録
            var uiModule = new UIModule();
            uiModule.RegisterWithDependencies(services, registeredModules, moduleStack);
            
            // PaddleOcrModuleの登録
            var paddleOcrModule = new Baketa.Infrastructure.DI.PaddleOcrModule();
            paddleOcrModule.RegisterServices(services);
            
            // アダプターサービスの登録
            services.AddAdapterServices();
            
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
                typeof(Baketa.UI.ViewModels.SettingsViewModel),
                typeof(Baketa.UI.ViewModels.LanguagePairsViewModel),
                typeof(Baketa.UI.ViewModels.MainWindowViewModel)
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
    }
